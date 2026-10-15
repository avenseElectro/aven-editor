using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using Sprocket.Core.Commands;
using Sprocket.Core.Model;
using Sprocket.Core.Timing;
using Sprocket.Playback;
using Sprocket.Render;
using MovePreset = Sprocket.App.SegmentMovement.MovePreset;

namespace Sprocket.App;

/// <summary>
/// The «Editar recorte e movimento» window (Aven Editor approved spec): a per-segment framing + entry/exit
/// movement editor over one video clip, opened from the timeline clip context menu. Everything is staged
/// against a throwaway one-clip preview project (the Source-monitor pattern, <see cref="Monitors"/>), so
/// <see cref="Show"/> never mutates the real project: «Cancelar» (or closing the window) discards the preview
/// project outright, and «Aplicar» returns a single composite command — remove the clip's existing Transform
/// (if any) + add the freshly built one — that the shell runs through <see cref="EditHistory"/>, so one
/// «Anular» restores exactly the prior state. Movements are keyframes on the existing Transform effect
/// (Scale/PositionX/PositionY — keyframe times are absolute timeline time, so the built instance anchors to
/// the clip's real span and placement commands keep shifting it afterwards). The exit movement departs from
/// the entry movement's resulting framing, so the two compose without a jump.
/// </summary>
internal sealed class SegmentEditWindow : Window
{
    private readonly record struct Snapshot(
        double Scale, double X, double Y, MovePreset Entry, MovePreset Exit,
        double EntryIntensity, double ExitIntensity, double EntrySeconds, double ExitSeconds,
        double EntryCustomScale, double EntryCustomX, double EntryCustomY,
        double ExitCustomScale, double ExitCustomX, double ExitCustomY, int EaseIndex);

    private readonly Project _project;
    private readonly Clip _clip;
    private readonly MediaRef _media;
    private readonly EffectInstance? _committed;   // the clip's existing Transform, replaced by «Aplicar»
    private readonly long _durationTicks;          // the clip's real timeline span
    private readonly Project _previewProject;
    private readonly Clip _previewClip;
    private readonly int _frameWidth;
    private readonly int _frameHeight;
    private readonly Snapshot _initial;

    private PlaybackEngine? _engine;
    private EffectInstance? _staged;               // the staged Transform on the preview clip
    private bool _suppress;                        // programmatic control updates must not re-enter

    // Staged state.
    private double _scale = 1, _posX, _posY;
    private MovePreset _entryPreset, _exitPreset;
    private double _entryIntensity = 0.5, _exitIntensity = 0.5;
    private double _entrySeconds = 1, _exitSeconds = 1;
    private double _entryCustomScale = 1, _entryCustomX, _entryCustomY;
    private double _exitCustomScale = 1, _exitCustomX, _exitCustomY;
    private Interpolation _ease = Interpolation.EaseInOut;

    // Controls referenced after build.
    private readonly PreviewPane _pane = new();
    private readonly PreviewSurface _surface = new();
    private readonly Slider _scrub = new() { Minimum = 0 };
    private readonly TextBlock _timeLabel = new() { Foreground = Palette.MutedTextBrush };
    private readonly TextBlock _previewNote = new()
    {
        Text = "Não foi possível abrir o vídeo desta fonte.", Foreground = Palette.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap, IsVisible = false,
    };
    private Slider _zoomSlider = null!, _xSlider = null!, _ySlider = null!;
    private TextBlock _zoomValue = null!;
    private ComboBox _entryPresetBox = null!, _exitPresetBox = null!;
    private Slider _entryIntensitySlider = null!, _exitIntensitySlider = null!;
    private TextBox _entryDurBox = null!, _exitDurBox = null!;
    private StackPanel _entryCustomPanel = null!, _exitCustomPanel = null!;
    private Control _entryIntensityRow = null!, _exitIntensityRow = null!;
    private TextBox _entryCustomScaleBox = null!, _entryCustomXBox = null!, _entryCustomYBox = null!;
    private TextBox _exitCustomScaleBox = null!, _exitCustomXBox = null!, _exitCustomYBox = null!;
    private ComboBox _easeBox = null!;
    private Button _playButton = null!;

    /// <summary>Opens the window over one video clip. Returns the composite command to run through the edit
    /// history when the user picks «Aplicar», or <see langword="null"/> when nothing should change (cancel,
    /// close, or an identity staging with no prior Transform).</summary>
    public static async Task<IEditCommand?> Show(Window owner, Project project, Clip clip)
    {
        if (project.MediaPool.Get(clip.MediaRefId) is not { Info.HasVideo: true } media)
            return null;
        var dialog = new SegmentEditWindow(project, clip, media);
        return await dialog.ShowDialog<IEditCommand?>(owner);
    }

    private SegmentEditWindow(Project project, Clip clip, MediaRef media)
    {
        _project = project;
        _clip = clip;
        _media = media;
        _committed = clip.Effects.FirstOrDefault(e => e.EffectTypeId == EffectTypeIds.Transform);
        _durationTicks = (clip.TimelineEnd - clip.TimelineStart).Ticks;

        // Load the committed static framing (an animated Transform can't round-trip into this editor — staging
        // starts from the identity and «Aplicar» replaces it).
        if (_committed is not null)
        {
            _scale = ConstantOf(_committed, EffectParamNames.Scale, 1);
            _posX = ConstantOf(_committed, EffectParamNames.PositionX, 0);
            _posY = ConstantOf(_committed, EffectParamNames.PositionY, 0);
        }

        // The throwaway preview project mirrors the sequence's format; the clip is cloned to start at zero and
        // its other effects ride along (keyframes shifted to the preview span) so the preview composites like
        // the program will. Only this project is ever touched — never the real one.
        Timeline format = project.ActiveSequence.Timeline;
        _frameWidth = format.Resolution.Width;
        _frameHeight = format.Resolution.Height;
        var timeline = new Timeline(format.FrameRate, format.Resolution, format.SampleRate);
        _previewProject = new Project(timeline);
        _previewProject.MediaPool.Add(media);
        var track = new VideoTrack { Name = "V1" };
        _previewClip = clip.CloneContentForSpan(clip.SourceIn, clip.SourceOut, Timecode.Zero);
        Timecode shift = Timecode.Zero - clip.TimelineStart;
        foreach (EffectInstance fx in clip.Effects)
            if (fx.EffectTypeId != EffectTypeIds.Transform)
                _previewClip.Effects.Add(fx.CloneShifted(shift));
        track.Clips.Add(_previewClip);
        timeline.Tracks.Add(track);

        _initial = Capture();
        BuildUi();
        RebuildStaged();
    }

    private static double ConstantOf(EffectInstance fx, string name, double fallback) =>
        SegmentMovement.Constant(fx, name, fallback);

    // ── UI ───────────────────────────────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        Title = "Editar recorte e movimento";
        Icon = AppIcon.Window;
        Width = 820;
        Height = 760;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.WindowBgBrush;

        _pane.Children.Add(_surface);
        _pane.DragDelta = OnPreviewDrag;
        _pane.Wheel = OnPreviewWheel;

        var previewColumn = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new Border
                {
                    Height = 290,
                    CornerRadius = new CornerRadius(6),
                    Child = _pane,
                },
                _scrub,
                _timeLabel,
                _previewNote,
            },
        };

        var controls = new ScrollViewer { Content = BuildControls() };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,310") };
        Grid.SetColumn(previewColumn, 0);
        Grid.SetColumn(controls, 1);
        body.Children.Add(previewColumn);
        body.Children.Add(controls);

        _playButton = Btn("Pré-visualizar");
        _playButton.Click += (_, _) => TogglePreview();
        Button reset = Btn("Repor alterações");
        reset.Click += (_, _) => Restore(_initial);
        Button cancel = Btn("Cancelar");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close(null);
        Button apply = Btn("Aplicar", primary: true);
        apply.IsDefault = true;
        apply.Click += (_, _) => Close(BuildApplyCommand());

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _playButton, reset, cancel, apply },
        };

        Content = new DockPanel
        {
            Margin = new Thickness(18),
            Children =
            {
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 14, 0, 0),
                    Children = { buttons },
                },
                body,
            },
        };

        Closed += (_, _) => Teardown();
        Opened += (_, _) => StartPreview();
    }

    private Control BuildControls()
    {
        var panel = new StackPanel { Spacing = 4 };

        panel.Children.Add(Section("Enquadramento"));
        _zoomValue = new TextBlock { Foreground = Palette.TextBrush, MinWidth = 44 };
        _zoomSlider = Range(25, 400, _scale * 100, 5);
        _zoomSlider.ValueChanged += (_, e) => { if (!_suppress) { _scale = e.NewValue / 100; SyncZoomValue(); RebuildStaged(); } };
        panel.Children.Add(Row("Zoom", _zoomSlider, _zoomValue));
        _xSlider = Range(-100, 100, _posX * 100, 1);
        _xSlider.ValueChanged += (_, e) => { if (!_suppress) { _posX = e.NewValue / 100; RebuildStaged(); } };
        panel.Children.Add(Row("Posição horizontal", _xSlider, null));
        _ySlider = Range(-100, 100, _posY * 100, 1);
        _ySlider.ValueChanged += (_, e) => { if (!_suppress) { _posY = e.NewValue / 100; RebuildStaged(); } };
        panel.Children.Add(Row("Posição vertical", _ySlider, null));

        Button fit = Btn("Ajustar");
        fit.Click += (_, _) => SetFraming(1, 0, 0);
        Button fill = Btn("Preencher");
        fill.Click += (_, _) =>
        {
            if (FramingOps.FillHeightScale(_project.ActiveSequence.Timeline.Resolution,
                    _media.Info.Width, _media.Info.Height, _clip.ConformMode) is { } s)
                SetFraming(s, _posX, _posY);
        };
        Button resetFraming = Btn("Repor");
        resetFraming.Click += (_, _) => SetFraming(_initial.Scale, _initial.X, _initial.Y);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { fit, fill, resetFraming },
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Arraste a imagem para posicionar; use a roda do rato sobre a pré-visualização para ampliar.",
            Foreground = Palette.MutedTextBrush,
            FontSize = Typography.Micro,
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(Section("Movimento de entrada"));
        StackPanel entryAll;
        (_entryPresetBox, _entryIntensitySlider, _entryDurBox, _entryCustomPanel, entryAll) =
            BuildMovement(_entryPreset, _entryIntensity, _entrySeconds,
                _entryCustomScale, _entryCustomX, _entryCustomY,
                (p, i, s, cs, cx, cy) =>
                {
                    _entryPreset = p; _entryIntensity = i; _entrySeconds = s;
                    _entryCustomScale = cs; _entryCustomX = cx; _entryCustomY = cy;
                },
                out _entryCustomScaleBox, out _entryCustomXBox, out _entryCustomYBox);
        _entryIntensityRow = entryAll.Children[1];
        panel.Children.Add(entryAll);
        panel.Children.Add(_entryCustomPanel);

        panel.Children.Add(Section("Movimento de saída"));
        StackPanel exitAll;
        (_exitPresetBox, _exitIntensitySlider, _exitDurBox, _exitCustomPanel, exitAll) =
            BuildMovement(_exitPreset, _exitIntensity, _exitSeconds,
                _exitCustomScale, _exitCustomX, _exitCustomY,
                (p, i, s, cs, cx, cy) =>
                {
                    _exitPreset = p; _exitIntensity = i; _exitSeconds = s;
                    _exitCustomScale = cs; _exitCustomX = cx; _exitCustomY = cy;
                },
                out _exitCustomScaleBox, out _exitCustomXBox, out _exitCustomYBox);
        _exitIntensityRow = exitAll.Children[1];
        panel.Children.Add(exitAll);
        panel.Children.Add(_exitCustomPanel);

        panel.Children.Add(Section("Suavidade"));
        var advancedToggle = new ToggleButton
        {
            Content = "Opções avançadas",
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            Padding = new Thickness(10, 4),
            CornerRadius = new CornerRadius(4),
        };
        var advanced = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0), IsVisible = false };
        _easeBox = new ComboBox
        {
            ItemsSource = new[] { "Linear", "Suave", "Suave à entrada", "Suave à saída" },
            SelectedIndex = 1,
            MinWidth = 150,
            Background = Palette.PanelBgBrush,
        };
        _easeBox.SelectionChanged += (_, _) =>
        {
            if (_suppress) return;
            _ease = _easeBox.SelectedIndex switch
            {
                0 => Interpolation.Linear,
                2 => Interpolation.EaseIn,
                3 => Interpolation.EaseOut,
                _ => Interpolation.EaseInOut,
            };
            RebuildStaged();
        };
        advanced.Children.Add(_easeBox);
        advanced.Children.Add(new TextBlock
        {
            Text = "O ritmo aplica-se aos movimentos de entrada e de saída. «Pré-visualizar» reproduz o segmento "
                + "completo com os valores em ensaio.",
            Foreground = Palette.MutedTextBrush,
            FontSize = Typography.Micro,
            TextWrapping = TextWrapping.Wrap,
        });
        advancedToggle.Click += (_, _) => advanced.IsVisible = advancedToggle.IsChecked == true;
        panel.Children.Add(advancedToggle);
        panel.Children.Add(advanced);

        return panel;
    }

    /// <summary>Builds one movement section: the preset picker, the intensity + duration rows, and the collapsed
    /// «Personalizado» value rows. The setter applies control values back into the staged fields.</summary>
    private (ComboBox Presets, Slider Intensity, TextBox Duration, StackPanel Custom, StackPanel All)
        BuildMovement(
            MovePreset preset, double intensity, double seconds,
            double customScale, double customX, double customY,
            Action<MovePreset, double, double, double, double, double> setter,
            out TextBox scaleBox, out TextBox xBox, out TextBox yBox)
    {
        var all = new StackPanel { Spacing = 4 };

        var presets = new ComboBox
        {
            ItemsSource = new[]
            {
                "Sem movimento", "Zoom in", "Zoom out", "Esquerda", "Direita", "Cima", "Baixo", "Personalizado",
            },
            SelectedIndex = (int)preset,
            MinWidth = 160,
            Background = Palette.PanelBgBrush,
        };

        var intensitySlider = Range(0, 100, intensity * 100, 5);
        var intensityRow = Row("Intensidade", intensitySlider, null);
        intensitySlider.ValueChanged += (_, e) => { if (!_suppress) { setter(preset, e.NewValue / 100, seconds, customScale, customX, customY); RebuildStaged(); } };

        var durBox = new TextBox
        {
            Text = seconds.ToString("0.0", CultureInfo.InvariantCulture),
            Width = 70,
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
        };
        var durRow = Row("Duração (s)", durBox, null);
        durBox.TextChanged += (_, _) =>
        {
            if (!_suppress && TrySeconds(durBox.Text, out double s))
            {
                setter(preset, intensity, s, customScale, customX, customY);
                RebuildStaged();
            }
        };

        var custom = new StackPanel { Spacing = 4, IsVisible = preset == MovePreset.Custom };
        TextBox scaleLocal = PercentBox(customScale * 100);
        TextBox xLocal = PercentBox(customX * 100);
        TextBox yLocal = PercentBox(customY * 100);
        scaleBox = scaleLocal;
        xBox = xLocal;
        yBox = yLocal;
        scaleLocal.TextChanged += (_, _) => { if (!_suppress && TryPercent(scaleLocal.Text, out double v)) { setter(preset, intensity, seconds, v / 100, customX, customY); RebuildStaged(); } };
        xLocal.TextChanged += (_, _) => { if (!_suppress && TryPercent(xLocal.Text, out double v)) { setter(preset, intensity, seconds, customScale, v / 100, customY); RebuildStaged(); } };
        yLocal.TextChanged += (_, _) => { if (!_suppress && TryPercent(yLocal.Text, out double v)) { setter(preset, intensity, seconds, customScale, customX, v / 100); RebuildStaged(); } };
        custom.Children.Add(Row("Escala inicial (%)", scaleLocal, null));
        custom.Children.Add(Row("Desvio X inicial (%)", xLocal, null));
        custom.Children.Add(Row("Desvio Y inicial (%)", yLocal, null));

        presets.SelectionChanged += (_, _) =>
        {
            if (_suppress) return;
            MovePreset picked = (MovePreset)Math.Max(0, presets.SelectedIndex);
            bool customOn = picked == MovePreset.Custom;
            bool off = picked == MovePreset.None;
            custom.IsVisible = customOn;
            intensityRow.IsVisible = !customOn && !off;
            setter(picked, intensity, seconds, customScale, customX, customY);
            RebuildStaged();
        };
        if (preset == MovePreset.None)
            intensityRow.IsVisible = false;
        if (preset != MovePreset.Custom)
            custom.IsVisible = false;

        all.Children.Add(presets);
        all.Children.Add(intensityRow);
        all.Children.Add(durRow);
        return (presets, intensitySlider, durBox, custom, all);
    }

    private static TextBox PercentBox(double value) => new()
    {
        Text = value.ToString("0", CultureInfo.InvariantCulture),
        Width = 70,
        Foreground = Palette.TextBrush,
        Background = Palette.PanelBgBrush,
    };

    private static TextBlock Section(string title) => new()
    {
        Text = title,
        Foreground = Palette.TextBrush,
        FontSize = Typography.Title,
        Margin = new Thickness(0, 16, 0, 6),
    };

    private static Control Row(string label, Control control, TextBlock? value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Palette.MutedTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 132,
            TextWrapping = TextWrapping.Wrap,
        });
        control.MinWidth = 120;
        row.Children.Add(control);
        if (value is not null)
        {
            value.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(value);
        }
        return row;
    }

    private static Slider Range(double min, double max, double value, double step) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        SmallChange = step,
        LargeChange = step * 10,
    };

    private static Button Btn(string text, bool primary = false) => new()
    {
        Content = text,
        Padding = new Thickness(14, 5),
        Foreground = primary ? Brushes.White : Palette.TextBrush,
        Background = primary ? Palette.AccentBrush : Palette.PanelBgBrush,
        CornerRadius = new CornerRadius(5),
    };

    // ── Preview engine ───────────────────────────────────────────────────────────────────────────

    private void StartPreview()
    {
        if (MediaBootstrap.OpenVideoFeed(_media) is not { } feed)
        {
            _previewNote.IsVisible = true;
            return;
        }
        _engine = new PlaybackEngine(_previewProject, feed, null); // software clock: video-only staging preview
        _engine.PositionChanged += t => Dispatcher.UIThread.Post(() => OnPreviewPosition(t));
        _engine.PlaybackEnded += () => Dispatcher.UIThread.Post(() => _playButton.Content = "Pré-visualizar");
        _surface.SetFrameSize(_frameWidth, _frameHeight);
        _surface.Attach(_engine);
        _engine.Start();
        _engine.SeekTo(Timecode.Zero); // present the first frame of the staged clip immediately
        _scrub.Maximum = Math.Max(1, _previewClip.Duration.Ticks);
        OnPreviewPosition(_engine.Position);
    }

    private void Teardown()
    {
        _surface.Detach();
        if (_engine is { } engine)
        {
            _engine = null;
            _ = engine.DisposeAsync(); // fire-and-forget teardown, the Source-monitor pattern
        }
    }

    private void OnPreviewPosition(Timecode t)
    {
        _suppress = true;
        _scrub.Value = Math.Min(t.Ticks, (long)_scrub.Maximum);
        _suppress = false;
        _timeLabel.Text = $"{t.ToSeconds():0.0} s / {_previewClip.Duration.ToSeconds():0.0} s";
    }

    private void TogglePreview()
    {
        if (_engine is not { } engine)
            return;
        if (engine.State == PlaybackState.Playing)
        {
            engine.Pause();
            _playButton.Content = "Pré-visualizar";
        }
        else
        {
            engine.SeekTo(Timecode.Zero);
            engine.Play(); // the one-clip project's end-stop halts playback at the segment's end
            _playButton.Content = "Parar";
        }
    }

    /// <summary>Rebuilds the staged Transform on the preview clip from the current fields and re-presents the
    /// paused frame so the change is visible immediately (playing previews pick it up on the next frame).</summary>
    private void RebuildStaged()
    {
        if (_staged is not null)
            _previewClip.Effects.Remove(_staged);
        _staged = BuildStagedEffect(0, _previewClip.Duration.Ticks); // preview-clip-relative times
        _previewClip.Effects.Add(_staged);
        if (_engine is { State: not PlaybackState.Playing } engine)
            engine.SeekTo(engine.Position);
    }

    private void OnPreviewDrag(Point last, Point current)
    {
        var bounds = SKRect.Create((float)_pane.Bounds.Width, (float)_pane.Bounds.Height);
        SKRect frame = FramePresenter.ComputeFitRect(bounds, _frameWidth, _frameHeight);
        if (frame.Width <= 0 || frame.Height <= 0)
            return;
        _posX = Math.Clamp(_posX + (current.X - last.X) / frame.Width, -1, 1);
        _posY = Math.Clamp(_posY + (current.Y - last.Y) / frame.Height, -1, 1);
        SyncFramingControls();
        RebuildStaged();
    }

    private bool OnPreviewWheel(Point position, double delta)
    {
        _scale = Math.Clamp(_scale * (delta > 0 ? 1.1 : 1 / 1.1), 0.25, 4);
        SyncFramingControls();
        RebuildStaged();
        return true;
    }

    private void SetFraming(double scale, double x, double y)
    {
        _scale = Math.Clamp(scale, 0.25, 4);
        _posX = Math.Clamp(x, -1, 1);
        _posY = Math.Clamp(y, -1, 1);
        SyncFramingControls();
        RebuildStaged();
    }

    private void SyncFramingControls()
    {
        _suppress = true;
        _zoomSlider.Value = _scale * 100;
        _xSlider.Value = _posX * 100;
        _ySlider.Value = _posY * 100;
        _suppress = false;
        SyncZoomValue();
    }

    private void SyncZoomValue() => _zoomValue.Text = $"{_scale * 100:0}%";

    // ── Staging → effect → command ───────────────────────────────────────────────────────────────

    /// <summary>Builds the staged Transform effect anchored at <paramref name="startTicks"/> (the preview clip's
    /// zero, or the real clip's timeline start for «Aplicar»).</summary>
    private EffectInstance BuildStagedEffect(long startTicks, long durationTicks)
    {
        (long de, long dx) = SegmentMovement.Durations(
            _entrySeconds, _exitSeconds, durationTicks,
            _entryPreset != MovePreset.None, _exitPreset != MovePreset.None);

        var fx = new EffectInstance(EffectTypeIds.Transform);
        foreach (string name in new[] { EffectParamNames.Scale, EffectParamNames.PositionX, EffectParamNames.PositionY })
        {
            double b = name == EffectParamNames.Scale ? _scale : name == EffectParamNames.PositionX ? _posX : _posY;
            double? entry = SegmentMovement.EntryStart(
                _entryPreset, name, _scale, _posX, _posY, _entryIntensity,
                _entryCustomScale, _entryCustomX, _entryCustomY);
            double? exit = SegmentMovement.ExitEnd(
                _exitPreset, name, _scale, _posX, _posY, _exitIntensity,
                _exitCustomScale, _exitCustomX, _exitCustomY);
            fx.Set(name, SegmentMovement.Parameter(b, entry, exit, startTicks, durationTicks, de, dx, _ease));
        }
        return fx;
    }

    /// <summary>The one «Aplicar» command: remove the clip's existing Transform (if any) and add the staged one
    /// anchored to the clip's real span — a single history entry, so one «Anular» restores the exact prior state.
    /// Identity staging with nothing to remove returns null (nothing to change).</summary>
    private IEditCommand? BuildApplyCommand()
    {
        EffectInstance fx = BuildStagedEffect(_clip.TimelineStart.Ticks, _durationTicks);
        bool identity = SegmentMovement.IsIdentity(fx);
        if (identity && _committed is null)
            return null;

        var commands = new List<IEditCommand>();
        if (_committed is not null)
            commands.Add(new RemoveEffectCommand(_clip, _committed));
        if (!identity)
            commands.Add(new AddEffectCommand(_clip, fx));
        return commands.Count == 0
            ? null
            : commands.Count == 1 ? commands[0] : new CompositeCommand("Editar recorte e movimento", commands);
    }

    // ── Repor alterações ─────────────────────────────────────────────────────────────────────────

    private Snapshot Capture() => new(
        _scale, _posX, _posY, _entryPreset, _exitPreset,
        _entryIntensity, _exitIntensity, _entrySeconds, _exitSeconds,
        _entryCustomScale, _entryCustomX, _entryCustomY,
        _exitCustomScale, _exitCustomX, _exitCustomY,
        _easeBox is null ? 1 : _easeBox.SelectedIndex);

    private void Restore(Snapshot s)
    {
        _suppress = true;
        _scale = s.Scale; _posX = s.X; _posY = s.Y;
        _entryPreset = s.Entry; _exitPreset = s.Exit;
        _entryIntensity = s.EntryIntensity; _exitIntensity = s.ExitIntensity;
        _entrySeconds = s.EntrySeconds; _exitSeconds = s.ExitSeconds;
        _entryCustomScale = s.EntryCustomScale; _entryCustomX = s.EntryCustomX; _entryCustomY = s.EntryCustomY;
        _exitCustomScale = s.ExitCustomScale; _exitCustomX = s.ExitCustomX; _exitCustomY = s.ExitCustomY;
        _ease = s.EaseIndex switch
        {
            0 => Interpolation.Linear,
            2 => Interpolation.EaseIn,
            3 => Interpolation.EaseOut,
            _ => Interpolation.EaseInOut,
        };

        _zoomSlider.Value = _scale * 100;
        _xSlider.Value = _posX * 100;
        _ySlider.Value = _posY * 100;
        _entryPresetBox.SelectedIndex = (int)_entryPreset;
        _exitPresetBox.SelectedIndex = (int)_exitPreset;
        _entryIntensitySlider.Value = _entryIntensity * 100;
        _exitIntensitySlider.Value = _exitIntensity * 100;
        _entryDurBox.Text = _entrySeconds.ToString("0.0", CultureInfo.InvariantCulture);
        _exitDurBox.Text = _exitSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        _entryCustomScaleBox.Text = (_entryCustomScale * 100).ToString("0", CultureInfo.InvariantCulture);
        _entryCustomXBox.Text = (_entryCustomX * 100).ToString("0", CultureInfo.InvariantCulture);
        _entryCustomYBox.Text = (_entryCustomY * 100).ToString("0", CultureInfo.InvariantCulture);
        _exitCustomScaleBox.Text = (_exitCustomScale * 100).ToString("0", CultureInfo.InvariantCulture);
        _exitCustomXBox.Text = (_exitCustomX * 100).ToString("0", CultureInfo.InvariantCulture);
        _exitCustomYBox.Text = (_exitCustomY * 100).ToString("0", CultureInfo.InvariantCulture);
        _easeBox.SelectedIndex = s.EaseIndex;
        SetMovementVisibility(_entryIntensityRow, _entryCustomPanel, _entryPreset);
        SetMovementVisibility(_exitIntensityRow, _exitCustomPanel, _exitPreset);
        SyncZoomValue();
        _suppress = false;
        RebuildStaged();
    }

    private static void SetMovementVisibility(Control intensityRow, StackPanel custom, MovePreset preset)
    {
        bool customOn = preset == MovePreset.Custom;
        bool off = preset == MovePreset.None;
        custom.IsVisible = customOn;
        intensityRow.IsVisible = !customOn && !off;
    }

    private static bool TrySeconds(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static bool TryPercent(string? text, out double value) => TrySeconds(text, out value);

    /// <summary>The preview area: hosts the <see cref="PreviewSurface"/> and routes pointer drags (position) and
    /// the wheel (zoom) to the window, mapping them through the surface's fit rectangle.</summary>
    private sealed class PreviewPane : Panel
    {
        private Point _last;
        public Action<Point, Point>? DragDelta { get; set; }
        public Func<Point, double, bool>? Wheel { get; set; }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _last = e.GetPosition(this);
                e.Pointer.Capture(this);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Pointer.Captured == this)
            {
                Point current = e.GetPosition(this);
                DragDelta?.Invoke(_last, current);
                _last = current;
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (e.Pointer.Captured == this)
                e.Pointer.Capture(null);
            e.Handled = true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (Wheel?.Invoke(e.GetPosition(this), e.Delta.Y) == true)
                e.Handled = true;
        }
    }
}
