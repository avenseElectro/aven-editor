using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Sprocket.Core.Commands;
using Sprocket.Core.Model;
using Sprocket.Core.Timing;
using Sprocket.Render;
using ShapesPath = Avalonia.Controls.Shapes.Path; // aliased so it doesn't clash with System.IO.Path

namespace Sprocket.App.MediaBrowser;

/// <summary>
/// The Project panel's tabbed browser (PLAN.md step 15, UI.md §3.3): a <b>Media</b> bin of poster-frame /
/// waveform thumbnails with metadata badges and a search filter, an <b>Effects</b> browser over the
/// <see cref="EffectCatalog"/> (double-click to add the effect to the selected clip, through the step-10
/// command stack), a <b>Looks</b> browser of one-click creative grades (<see cref="LooksLibrary"/>: the curated
/// <see cref="LooksCatalog"/> plus the user's saved looks and imported creative LUTs — plan/features/looks-browser.md),
/// a <b>Transitions</b> browser over the <see cref="TransitionCatalog"/> (drag onto a cut, or
/// double-click to apply to the selected clip's cut — PLAN.md step 25), and an <b>Audio</b> tab listing the
/// bin's audio sources as waveforms. Built entirely in code like <see cref="TimelineControl"/> /
/// <see cref="PreviewSurface"/>; thumbnails are produced off-thread by <see cref="ThumbnailService"/>.
/// </summary>
public sealed class MediaBrowserPanel : UserControl
{
    // Core tokens come from the shared Palette (Palette.cs) so this code control can't drift from the shell.
    private static readonly IBrush PanelBg = Palette.PanelBgBrush;
    private static readonly IBrush RaisedBg = Palette.RaisedBgBrush;
    private static readonly IBrush PosterBg = Palette.WindowBgBrush;
    private static readonly IBrush Edge = Palette.EdgeBrush;
    private static readonly IBrush TextBrush = Palette.TextBrush;
    private static readonly IBrush MutedText = Palette.MutedTextBrush;
    private static readonly IBrush FaintText = Palette.FaintTextBrush;
    private static readonly IBrush Accent = Palette.AccentBrush;
    private static readonly IBrush BadgeBg = Hex("#2E2E38"); // media-badge chip — component-specific, stays local

    private const double TileWidth = 116;
    private const int PosterW = 104;
    private const int PosterH = 58;

    private Project? _project;
    private EditHistory? _history;
    private ThumbnailService? _thumbs;
    private Clip? _selectedClip;

    private string _search = string.Empty;
    private string _lookSearch = string.Empty; // the Looks tab keeps its own filter; the box shows the active tab's
    private LooksLibrary? _looks;
    private Func<Timecode>? _playhead;
    private Tab _activeTab = Tab.Media;
    private Control? _mixer; // the audio mixer installed by the shell (PLAN.md step 30); null → the audio-media list
    private bool _audioGridStale = true; // the audio-media list needs (re)building before it is next shown

    // Built-once chrome.
    private readonly TextBox _searchBox;
    private readonly WrapPanel _mediaGrid;
    private readonly WrapPanel _audioGrid;
    private readonly StackPanel _effectsList;
    private readonly StackPanel _transitionsList;
    private readonly StackPanel _looksList;
    private readonly WrapPanel _stickersGrid;
    private readonly Decorator _content;            // hosts the active tab's body
    private readonly ScrollViewer _mediaView, _audioView, _effectsView, _transitionsView, _looksView, _stickersView;
    private readonly Dictionary<Tab, Button> _tabButtons = new();

    /// <summary>Raised with a short message for the status strip (effect applied / select-a-clip hint).</summary>
    public event Action<string>? Status;

    /// <summary>Raised with the media-bin item count when the bin is (re)populated, for the pane header.</summary>
    public event Action<int>? ItemCountChanged;

    /// <summary>Raised when OS files are dropped on the bin (PLAN.md step 16b); the shell imports them.</summary>
    public event Action<IReadOnlyList<string>>? FilesDropped;

    /// <summary>Raised when a transition is double-clicked (PLAN.md step 25); the shell applies it to the cut at the
    /// selected clip. Dragging a transition onto a cut is handled by the timeline directly.</summary>
    public event Action<string>? TransitionActivated;

    /// <summary>Raised when an Action VFX preset is double-clicked in the Effects browser (plan/features/
    /// special-effects.md, phase 3); the shell inserts it at the timeline playhead.</summary>
    public event Action<ActionVfxDescriptor>? ActionVfxActivated;

    /// <summary>Raised when the media-bin tile's "Interpret Footage…" is chosen (PLAN.md step 42); the shell opens
    /// the frame-rate dialog and runs the reinterpret command for the source.</summary>
    public event Action<MediaRef>? InterpretFootageRequested;

    /// <summary>Raised when the media-bin tile's "Analyze for Stabilization" is chosen (plan/features/stabilization.md
    /// phase 6, FCP's browser analyse); the shell pre-warms the analysis cache for the whole source.</summary>
    public event Action<MediaRef>? AnalyzeForStabilizationRequested;

    /// <summary>Raised when a bin item is double-clicked (Premiere/Resolve gesture); the shell loads it into the
    /// Source monitor. Fires for tiles in both the Media and Audio tabs.</summary>
    public event Action<MediaRef>? MediaActivated;

    /// <summary>Raised when a sticker is clicked in the <b>Autocolantes</b> tab (Etapa C), with the bundled PNG's
    /// filesystem path; the shell imports it as Still media and places its clip at the playhead.</summary>
    public event Action<string>? StickerActivated;

    private enum Tab { Media, Effects, Looks, Transitions, Stickers, Audio }

    // The visible PT label for a tab (the enum names stay as internal identifiers).
    private static string TabLabel(Tab tab) => tab switch
    {
        Tab.Media => "Multimédia",
        Tab.Effects => "Efeitos",
        Tab.Looks => "Estilos",
        Tab.Transitions => "Transições",
        Tab.Stickers => "Autocolantes",
        Tab.Audio => "Áudio",
        _ => tab.ToString(),
    };

    public MediaBrowserPanel()
    {
        _searchBox = new TextBox
        {
            PlaceholderText = "Pesquisar multimédia…",
            FontSize = Typography.Body,
            Margin = new Avalonia.Thickness(8, 6),
            Background = PanelBg,
            BorderBrush = Edge,
        };
        _searchBox.TextChanged += (_, _) =>
        {
            string text = _searchBox.Text ?? string.Empty;
            if (_activeTab == Tab.Looks)
            {
                if (text == _lookSearch)
                    return;
                _lookSearch = text;
                BuildLooks();
                return;
            }
            if (text == _search)
                return; // a tab switch restoring the media filter must not re-request every thumbnail
            _search = text;
            RebuildGrids();
        };

        _mediaGrid = new WrapPanel { Margin = new Avalonia.Thickness(6) };
        _audioGrid = new WrapPanel { Margin = new Avalonia.Thickness(6) };
        _effectsList = new StackPanel { Margin = new Avalonia.Thickness(8), Spacing = 6 };
        _transitionsList = new StackPanel { Margin = new Avalonia.Thickness(8), Spacing = 6 };
        _looksList = new StackPanel { Margin = new Avalonia.Thickness(8), Spacing = 6 };
        _stickersGrid = new WrapPanel { Margin = new Avalonia.Thickness(6) };

        _mediaView = Scroll(_mediaGrid);
        _audioView = Scroll(_audioGrid);
        _effectsView = Scroll(_effectsList);
        _transitionsView = Scroll(_transitionsList);
        _looksView = Scroll(_looksList);
        _stickersView = Scroll(_stickersGrid);
        BuildStickers(); // the bundled set is fixed — no project needed, so build once here

        _content = new Decorator();

        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Margin = new Avalonia.Thickness(8, 6),
        };
        foreach (Tab t in Enum.GetValues<Tab>())
        {
            Button button = TabButton(t);
            _tabButtons[t] = button;
            tabs.Children.Add(button);
        }

        var root = new DockPanel();
        var tabsBar = new Border
        {
            Background = PanelBg,
            BorderBrush = Edge,
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Child = tabs,
        };
        DockPanel.SetDock(tabsBar, Dock.Top);
        DockPanel.SetDock(_searchBox, Dock.Top);
        root.Children.Add(tabsBar);
        root.Children.Add(_searchBox);
        root.Children.Add(_content);
        Content = root;

        // OS file-drop onto the bin imports media (PLAN.md step 16b); the shell does the probe + add.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnFilesDragOver);
        AddHandler(DragDrop.DropEvent, OnFilesDrop);

        SelectTab(Tab.Media);
    }

    /// <summary>Binds the browser to the project, the shared edit history, and the thumbnail service. Call once.</summary>
    public void Attach(Project project, EditHistory history, ThumbnailService thumbs)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(thumbs);
        _project = project;
        _history = history;
        _thumbs = thumbs;
        BuildEffects();
        BuildTransitions();
        RebuildGrids();
    }

    /// <summary>
    /// Binds the Looks tab to the looks library and a playhead source (Save Look… snapshots keyframed values at the
    /// playhead). Call once; the tab re-lists whenever the library changes.
    /// </summary>
    internal void AttachLooks(LooksLibrary library, Func<Timecode> playhead)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(playhead);
        _looks = library;
        _playhead = playhead;
        library.Changed += BuildLooks;
        BuildLooks();
    }

    /// <summary>Sets the clip the Effects browser will apply effects to (driven by the timeline selection).</summary>
    public void SetSelectedClip(Clip? clip) => _selectedClip = clip;

    /// <summary>Re-reads the <see cref="MediaPool"/> into the bin (after an import or undo, PLAN.md step 16b).</summary>
    public void Refresh() => RebuildGrids();

    // ── OS file drop (import) ─────────────────────────────────────────────────────────────────────────

    private void OnFilesDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnFilesDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File))
            return;
        var paths = new List<string>();
        foreach (IStorageItem item in e.DataTransfer.TryGetFiles() ?? [])
            if (item is IStorageFile file && file.TryGetLocalPath() is { } path)
                paths.Add(path);
        if (paths.Count > 0)
            FilesDropped?.Invoke(paths);
    }

    // ── Tabs ────────────────────────────────────────────────────────────────────────────────────────

    private Button TabButton(Tab tab)
    {
        var button = new Button
        {
            Content = TabLabel(tab),
            FontSize = Typography.Body,
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = new Avalonia.Thickness(2, 2),
            Foreground = FaintText,
        };
        button.Click += (_, _) => SelectTab(tab);
        return button;
    }

    private void SelectTab(Tab tab)
    {
        _activeTab = tab;
        foreach ((Tab t, Button b) in _tabButtons)
        {
            b.Foreground = t == tab ? TextBrush : FaintText;
            b.FontWeight = t == tab ? FontWeight.SemiBold : FontWeight.Normal;
        }

        _searchBox.IsVisible = tab is Tab.Media or Tab.Looks || (tab == Tab.Audio && _mixer is null);
        _searchBox.PlaceholderText = tab == Tab.Looks ? "Pesquisar estilos…" : "Pesquisar multimédia…";
        _searchBox.Text = tab == Tab.Looks ? _lookSearch : _search;
        _content.Child = tab switch
        {
            Tab.Media => _mediaView,
            Tab.Effects => _effectsView,
            Tab.Looks => _looksView,
            Tab.Transitions => _transitionsView,
            Tab.Stickers => _stickersView,
            Tab.Audio => (Control?)_mixer ?? _audioView,
            _ => _mediaView,
        };
        EnsureAudioGrid(); // the audio-media list is built lazily, on first show (no mixer installed)
    }

    /// <summary>Installs the audio <b>mixer</b> as the Audio tab's body (PLAN.md step 30, UI.md §3.3), replacing the
    /// audio-media list. Null-safe to call before/after tab selection; swaps in immediately if the Audio tab is open.</summary>
    public void SetMixer(Control mixer)
    {
        _mixer = mixer;
        if (_activeTab == Tab.Audio)
        {
            _content.Child = mixer;
            _searchBox.IsVisible = false;
        }
    }

    // ── Media / Audio grids ───────────────────────────────────────────────────────────────────────────

    private void RebuildGrids()
    {
        _mediaGrid.Children.Clear();
        _audioGrid.Children.Clear();
        _audioGridStale = true;
        if (_project is null || _thumbs is null)
            return;

        List<MediaRef> items = SortedItems();
        ItemCountChanged?.Invoke(items.Count);

        foreach (MediaRef media in items)
        {
            string name = Path.GetFileName(media.AbsolutePath);
            if (MediaSearch.Matches(name, _search))
                _mediaGrid.Children.Add(BuildTile(media, name));
        }

        if (_mediaGrid.Children.Count == 0)
            _mediaGrid.Children.Add(EmptyNote(_search.Length > 0 ? "Nenhuma multimédia corresponde à pesquisa." : "Sem multimédia importada."));

        EnsureAudioGrid();
    }

    /// <summary>Builds the Audio-tab waveform list, but only when it is actually on screen: once the shell installs
    /// the mixer (<see cref="SetMixer"/>) the list is never shown, so building it eagerly would decode a waveform
    /// per audio source for nothing on every project open. Stale after each <see cref="RebuildGrids"/>.</summary>
    private void EnsureAudioGrid()
    {
        if (!_audioGridStale || _activeTab != Tab.Audio || _mixer is not null || _project is null || _thumbs is null)
            return;
        _audioGridStale = false;

        _audioGrid.Children.Clear();
        foreach (MediaRef media in SortedItems())
        {
            string name = Path.GetFileName(media.AbsolutePath);
            if (media.Info.HasAudio && MediaSearch.Matches(name, _search))
                _audioGrid.Children.Add(BuildTile(media, name, audioView: true));
        }
        if (_audioGrid.Children.Count == 0)
            _audioGrid.Children.Add(EmptyNote("Sem fontes de áudio."));
    }

    private List<MediaRef> SortedItems() => _project!.MediaPool.Items
        .OrderBy(m => Path.GetFileName(m.AbsolutePath), StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Builds one bin tile: a poster (video) or waveform (audio) thumbnail, the filename, and badges.</summary>
    private Control BuildTile(MediaRef media, string name, bool audioView = false)
    {
        // In the Audio tab, or for audio-only sources, the thumbnail is the waveform; otherwise the poster.
        bool useWaveform = audioView || !media.Info.HasVideo;

        var poster = new Border
        {
            Width = PosterW,
            Height = PosterH,
            Background = PosterBg,
            CornerRadius = new Avalonia.CornerRadius(3),
            ClipToBounds = true,
        };
        var fallback = new ShapesPath
        {
            Data = useWaveform ? Icons.Music : Icons.Film,
            Stroke = FaintText,
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = IconSizes.Placeholder,
            Height = IconSizes.Placeholder,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // The thumbnail Image sits over the fallback glyph; a 1-px accent line overlays it during hover-scrub.
        var image = new Image { Stretch = Stretch.UniformToFill, IsVisible = false };
        var scrubLine = new Border
        {
            Width = 1,
            Background = Accent,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsVisible = false,
        };
        poster.Child = new Panel { Children = { fallback, image, scrubLine } };

        Task<Bitmap?> task = useWaveform
            ? _thumbs!.GetWaveformAsync(media, PosterW, PosterH)
            : _thumbs!.GetPosterAsync(media, PosterW, PosterH);
        LoadThumb(image, task);

        // Hover-scrub: video tiles with a bounded duration in the Media tab show a filmstrip when hovered
        // (Premiere hover-scrub / Resolve live preview). Stills, audio, and the Audio-tab waveform are skipped.
        if (!useWaveform && media.Info.HasVideo && !media.HasUnboundedDuration)
            WireHoverScrub(poster, image, scrubLine, media);

        var nameText = new TextBlock
        {
            Text = name,
            FontSize = Typography.Caption,
            Foreground = TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = PosterW,
            Margin = new Avalonia.Thickness(0, 4, 0, 2),
        };

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (string badge in MediaBadges.Describe(media))
            badges.Children.Add(Badge(badge));

        var stack = new StackPanel { Width = TileWidth - 12 };
        stack.Children.Add(poster);
        stack.Children.Add(nameText);
        stack.Children.Add(badges);

        var tile = new Border
        {
            Width = TileWidth,
            Margin = new Avalonia.Thickness(4),
            Padding = new Avalonia.Thickness(6),
            Background = RaisedBg,
            CornerRadius = new Avalonia.CornerRadius(5),
            Child = stack,
        };
        ToolTip.SetTip(tile, "Faça duplo clique para pré-visualizar no monitor Origem · Arraste para uma pista da linha do tempo para colocar um clipe.");
        // Double-click loads the source into the Source monitor (Premiere/Resolve gesture) — see the Effects/
        // Transitions rows for the same pattern.
        tile.DoubleTapped += (_, _) => MediaActivated?.Invoke(media);
        // Drag the source onto the timeline to place a clip (PLAN.md step 16b).
        EnableDrag(tile, DragFormats.MediaRefId, () => media.Id.Value.ToString());

        // Interpret Footage — reassign the source's frame rate (PLAN.md step 42). Offered for any video-bearing
        // source (it re-times image sequences and is the whole-clip "shoot on twos" lever); audio-only has no rate.
        if (media.Info.HasVideo)
        {
            var interpret = new MenuItem { Header = "Interpretar metragem…" };
            interpret.Click += (_, _) => InterpretFootageRequested?.Invoke(media);
            // Analyze for Stabilization (FCP's browser "Analyze for stabilization"): pre-warm the per-user analysis
            // cache from the bin, before the effect is applied, so it's ready the moment the user adds Stabilization.
            var analyze = new MenuItem { Header = "Analisar para estabilização" };
            analyze.Click += (_, _) => AnalyzeForStabilizationRequested?.Invoke(media);
            tile.ContextMenu = new ContextMenu { ItemsSource = new MenuItem[] { interpret, analyze } };
        }
        return tile;
    }

    private async void LoadThumb(Image image, Task<Bitmap?> task)
    {
        try
        {
            Bitmap? bitmap = await task; // resumes on the UI thread (Avalonia sync context)
            if (bitmap is not null)
            {
                image.Source = bitmap;
                image.IsVisible = true;
            }
        }
        catch
        {
            // Leave the fallback glyph in place on failure (§15).
        }
    }

    /// <summary>Wires filmstrip hover-scrub on a video tile's poster: the strip is decoded once on first hover
    /// (cached thereafter), and moving the pointer over the poster swaps the shown frame and slides an accent
    /// line. Left-drag is left to <see cref="EnableDrag"/> so the drag-to-timeline gesture is untouched.</summary>
    private void WireHoverScrub(Border poster, Image image, Border scrubLine, MediaRef media)
    {
        const int frames = FilmstripMath.DefaultFrames;
        Bitmap? strip = null;
        var slots = new CroppedBitmap?[frames]; // per-slot views into the strip, built lazily and reused
        IImage? poster0 = null;                 // the original poster frame, restored on exit

        poster.PointerEntered += async (_, _) =>
        {
            poster0 = image.Source;
            if (strip is null)
            {
                try { strip = await _thumbs!.GetFilmstripAsync(media, PosterW, PosterH, frames); }
                catch { strip = null; } // decode failure just leaves the poster showing
            }
        };

        poster.PointerMoved += (_, e) =>
        {
            // Ignore while the left button is down so the tile's drag gesture (EnableDrag) owns the move.
            if (strip is null || e.GetCurrentPoint(poster).Properties.IsLeftButtonPressed)
                return;

            double x = e.GetPosition(poster).X;
            int slot = FilmstripMath.SlotAt(x, poster.Bounds.Width, frames);
            if (slot < 0)
                return;

            poster0 ??= image.Source;
            image.Source = slots[slot] ??= new CroppedBitmap(strip, new PixelRect(slot * PosterW, 0, PosterW, PosterH));
            image.IsVisible = true;
            scrubLine.Margin = new Avalonia.Thickness(x, 0, 0, 0);
            scrubLine.IsVisible = true;
        };

        poster.PointerExited += (_, _) =>
        {
            if (poster0 is not null)
                image.Source = poster0;
            scrubLine.IsVisible = false;
        };
    }

    private static Border Badge(string text) => new()
    {
        Background = BadgeBg,
        CornerRadius = new Avalonia.CornerRadius(3),
        Padding = new Avalonia.Thickness(5, 1),
        Child = new TextBlock { Text = text, FontSize = Typography.Micro, Foreground = MutedText },
    };

    // ── Effects browser ───────────────────────────────────────────────────────────────────────────────

    private void BuildEffects()
    {
        _effectsList.Children.Clear();
        _effectsList.Children.Add(new TextBlock
        {
            Text = "Faça duplo clique num efeito para o adicionar ao clipe selecionado.",
            FontSize = Typography.Caption,
            Foreground = FaintText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 4),
        });

        foreach (EffectDescriptor effect in EffectCatalog.All)
            _effectsList.Children.Add(EffectRow(effect));

        // Action VFX presets (plan/features/special-effects.md, phase 3) — a visible group so users need not
        // assemble fire/explosion stacks from the primitives. They insert at the playhead rather than applying
        // to the selection, because each is a stack of new layers above the shot.
        _effectsList.Children.Add(new TextBlock
        {
            Text = "VFX DE AÇÃO",
            FontSize = Typography.Micro,
            Foreground = MutedText,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 10, 0, 0),
        });
        _effectsList.Children.Add(new TextBlock
        {
            Text = "Faça duplo clique para inserir no indicador de reprodução, estacionado no fotograma de impacto. Cada camada mantém-se editável.",
            FontSize = Typography.Caption,
            Foreground = FaintText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 4),
        });
        foreach (ActionVfxDescriptor preset in ActionVfxCatalog.BuiltIns)
            _effectsList.Children.Add(ActionVfxRow(preset));

        // Day for Night looks (plan/features/special-effects.md, phase 4) — one-click entry points for the
        // Day for Night effect's presets. Unlike the action VFX they grade the selection, like any effect; a
        // whole scene is graded by selecting an adjustment layer over it.
        if (EffectCatalog.Find(EffectTypeIds.DayForNight) is { } dayForNight)
        {
            _effectsList.Children.Add(new TextBlock
            {
                Text = "DIA PARA NOITE",
                FontSize = Typography.Micro,
                Foreground = MutedText,
                FontWeight = FontWeight.SemiBold,
                Margin = new Avalonia.Thickness(0, 10, 0, 0),
            });
            _effectsList.Children.Add(new TextBlock
            {
                Text = "Faça duplo clique para gradar o clipe selecionado (ou uma camada de ajuste, para uma cena inteira). Afine no inspetor.",
                FontSize = Typography.Caption,
                Foreground = FaintText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 4),
            });
            foreach (EffectPreset look in dayForNight.Presets)
                _effectsList.Children.Add(EffectPresetRow(dayForNight, look));
        }

        // One-tap preset stacks (plan/features/toy-cassette-camera.md, phase 5) — each adds several ordinary effects
        // across the picture and the linked sound in one undo step; applying again swaps the look in place.
        foreach ((string header, IReadOnlyList<PresetStack> stacks) in PresetStackBrowserModel.Groups(PresetStackCatalog.All))
        {
            _effectsList.Children.Add(new TextBlock
            {
                Text = header,
                FontSize = Typography.Micro,
                Foreground = MutedText,
                FontWeight = FontWeight.SemiBold,
                Margin = new Avalonia.Thickness(0, 10, 0, 0),
            });
            _effectsList.Children.Add(new TextBlock
            {
                Text = "Faça duplo clique para aplicar ao clipe selecionado e ao respetivo áudio ligado, ou arraste para um clipe. Cada parte mantém-se editável no inspetor.",
                FontSize = Typography.Caption,
                Foreground = FaintText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 4),
            });
            foreach (PresetStack stack in stacks)
                _effectsList.Children.Add(PresetStackRow(stack));
        }
    }

    private Control PresetStackRow(PresetStack stack)
    {
        var title = new TextBlock { Text = stack.Name, FontSize = Typography.Body, Foreground = TextBrush, FontWeight = FontWeight.SemiBold };
        var badge = new TextBlock { Text = PresetStackBrowserModel.Badge(stack), FontSize = Typography.Micro, Foreground = Accent };
        var header = new DockPanel();
        DockPanel.SetDock(badge, Dock.Right);
        header.Children.Add(badge);
        header.Children.Add(title);

        var desc = new TextBlock
        {
            Text = stack.Description,
            FontSize = Typography.Caption,
            Foreground = MutedText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 2, 0, 0),
        };

        var row = new Border
        {
            Background = RaisedBg,
            CornerRadius = new Avalonia.CornerRadius(5),
            Padding = new Avalonia.Thickness(8, 6),
            Child = new StackPanel { Children = { header, desc } },
        };
        row.DoubleTapped += (_, _) => ApplyPresetStack(stack);
        EnableDrag(row, DragFormats.PresetStackName, () => PresetStackBrowserModel.DragPayload(stack));
        ToolTip.SetTip(row, PresetStackBrowserModel.Tooltip(stack));
        return row;
    }

    private void ApplyPresetStack(PresetStack stack)
    {
        if (_selectedClip is null || _history is null || _project is null)
        {
            Status?.Invoke($"Selecione um clipe na linha do tempo para aplicar {stack.Title}.");
            return;
        }
        Status?.Invoke(PresetStackBrowserModel.ApplyToClip(stack, _selectedClip, _project, _history));
    }

    private Control EffectPresetRow(EffectDescriptor effect, EffectPreset preset)
    {
        var title = new TextBlock { Text = preset.Name, FontSize = Typography.Body, Foreground = TextBrush, FontWeight = FontWeight.SemiBold };
        var kind = new TextBlock { Text = effect.DisplayName, FontSize = Typography.Micro, Foreground = Accent };
        var header = new DockPanel();
        DockPanel.SetDock(kind, Dock.Right);
        header.Children.Add(kind);
        header.Children.Add(title);

        var desc = new TextBlock
        {
            Text = preset.Description ?? effect.Description,
            FontSize = Typography.Caption,
            Foreground = MutedText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 2, 0, 0),
        };

        var row = new Border
        {
            Background = RaisedBg,
            CornerRadius = new Avalonia.CornerRadius(5),
            Padding = new Avalonia.Thickness(8, 6),
            Child = new StackPanel { Children = { header, desc } },
        };
        row.DoubleTapped += (_, _) => ApplyEffect(effect, preset);
        EnableDrag(row, data =>
        {
            data.Add(DataTransferItem.Create(DragFormats.EffectId, effect.Id));
            data.Add(DataTransferItem.Create(DragFormats.EffectPresetName, preset.Name));
        });
        ToolTip.SetTip(row, $"Faça duplo clique para adicionar {effect.DisplayName} ({preset.Name}) ao clipe selecionado, ou arraste-o para um clipe.");
        return row;
    }

    private Control ActionVfxRow(ActionVfxDescriptor preset)
    {
        var title = new TextBlock { Text = preset.DisplayName, FontSize = Typography.Body, Foreground = TextBrush, FontWeight = FontWeight.SemiBold };
        var layers = new TextBlock { Text = $"{preset.Layers.Count} camada{(preset.Layers.Count == 1 ? "" : "s")}", FontSize = Typography.Micro, Foreground = Accent };
        var header = new DockPanel();
        DockPanel.SetDock(layers, Dock.Right);
        header.Children.Add(layers);
        header.Children.Add(title);

        var desc = new TextBlock
        {
            Text = preset.Description,
            FontSize = Typography.Caption,
            Foreground = MutedText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 2, 0, 0),
        };

        var row = new Border
        {
            Background = RaisedBg,
            CornerRadius = new Avalonia.CornerRadius(5),
            Padding = new Avalonia.Thickness(8, 6),
            Child = new StackPanel { Children = { header, desc } },
        };
        row.DoubleTapped += (_, _) => ActionVfxActivated?.Invoke(preset);
        ToolTip.SetTip(row, $"Faça duplo clique para inserir no indicador de reprodução. {preset.PlacementHint}");
        return row;
    }

    private Control EffectRow(EffectDescriptor effect)
    {
        var title = new TextBlock { Text = effect.DisplayName, FontSize = Typography.Body, Foreground = TextBrush, FontWeight = FontWeight.SemiBold };
        var category = new TextBlock { Text = effect.Category.ToString(), FontSize = Typography.Micro, Foreground = Accent };
        var header = new DockPanel();
        DockPanel.SetDock(category, Dock.Right);
        header.Children.Add(category);
        header.Children.Add(title);

        var desc = new TextBlock
        {
            Text = effect.Description,
            FontSize = Typography.Caption,
            Foreground = MutedText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 2, 0, 0),
        };

        var row = new Border
        {
            Background = RaisedBg,
            CornerRadius = new Avalonia.CornerRadius(5),
            Padding = new Avalonia.Thickness(8, 6),
            Child = new StackPanel { Children = { header, desc } },
        };
        row.DoubleTapped += (_, _) => ApplyEffect(effect);
        ToolTip.SetTip(row, "Faça duplo clique para adicionar ao clipe selecionado, ou arraste para um clipe da linha do tempo.");
        // Drag the effect onto a timeline clip to append it (PLAN.md step 16b), complementing double-click.
        EnableDrag(row, DragFormats.EffectId, () => effect.Id);
        return row;
    }

    // ── Looks browser (plan/features/looks-browser.md) ────────────────────────────────────────────────

    private void BuildLooks()
    {
        _looksList.Children.Clear();
        if (_looks is null)
            return;

        var save = ToolbarButton("Guardar estilo…", "Guardar a gradação do clipe selecionado (os seus efeitos de cor, não a transformação de entrada) como um estilo.");
        save.Click += (_, _) => _ = SaveLookAsync();
        var import = ToolbarButton("Importar LUT…", "Adicionar uma LUT .cube criativa (feita para metragem Rec.709) como um estilo.");
        import.Click += (_, _) => _ = ImportLutAsync();
        _looksList.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { save, import } });
        _looksList.Children.Add(new TextBlock
        {
            Text = "Faça duplo clique num estilo para gradar o clipe selecionado, ou arraste-o para um clipe. Cada estilo adiciona efeitos normais que pode afinar no inspetor.",
            FontSize = Typography.Caption,
            Foreground = FaintText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 2, 0, 4),
        });

        bool any = false;
        foreach ((string group, IReadOnlyList<Look> looks) in LooksBrowserModel.Grouped(_looks.All, _lookSearch))
        {
            bool userGroup = group == Look.UserGroup;
            if (looks.Count == 0 && _lookSearch.Length > 0)
                continue; // a filtered-out user group needs no empty-state hint
            _looksList.Children.Add(GroupHeader(group.ToUpperInvariant()));
            if (looks.Count == 0 && userGroup)
                _looksList.Children.Add(new TextBlock
                {
                    Text = "Ainda não há estilos guardados. Grade um clipe e escolha Guardar estilo…, ou importe uma LUT criativa.",
                    FontSize = Typography.Caption,
                    Foreground = FaintText,
                    TextWrapping = TextWrapping.Wrap,
                });
            foreach (Look look in looks)
            {
                _looksList.Children.Add(LookRow(look));
                any = true;
            }
        }
        if (!any && _lookSearch.Length > 0)
            _looksList.Children.Add(EmptyNote("Nenhum estilo corresponde à pesquisa."));
    }

    private Control LookRow(Look look)
    {
        var title = new TextBlock { Text = look.Name, FontSize = Typography.Body, Foreground = TextBrush, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var badge = new TextBlock { Text = LooksBrowserModel.Badge(look), FontSize = Typography.Micro, Foreground = Accent, Margin = new Avalonia.Thickness(6, 0, 0, 0) };
        var header = new DockPanel();
        DockPanel.SetDock(badge, Dock.Right);
        header.Children.Add(badge);
        header.Children.Add(title);

        // A saved look has no curated description; list what it stacks instead, so rows stay distinguishable.
        string description = look.Description
            ?? string.Join(" · ", look.Entries.Select(e => EffectCatalog.DisplayName(e.EffectTypeId)));
        var desc = new TextBlock
        {
            Text = description,
            FontSize = Typography.Caption,
            Foreground = MutedText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 2, 0, 0),
        };

        var row = new Border
        {
            Background = RaisedBg,
            CornerRadius = new Avalonia.CornerRadius(5),
            Padding = new Avalonia.Thickness(8, 6),
            Child = new StackPanel { Children = { header, desc } },
        };
        row.DoubleTapped += (_, _) => ApplyLook(look);
        EnableDrag(row, DragFormats.LookId, () => look.Id);
        ToolTip.SetTip(row, $"Faça duplo clique para aplicar {look.Name} ao clipe selecionado, ou arraste-o para um clipe.");

        if (!look.IsBuiltIn)
        {
            var rename = new MenuItem { Header = "Renomear…" };
            rename.Click += (_, _) => _ = RenameLookAsync(look);
            var delete = new MenuItem { Header = "Eliminar" };
            delete.Click += (_, _) => _ = DeleteLookAsync(look);
            row.ContextMenu = new ContextMenu { ItemsSource = new MenuItem[] { rename, delete } };
        }
        return row;
    }

    private void ApplyLook(Look look)
    {
        if (_selectedClip is null || _history is null || _project is null)
        {
            Status?.Invoke("Selecione um clipe na linha do tempo para aplicar um estilo.");
            return;
        }
        Status?.Invoke(LooksBrowserModel.ApplyToClip(look, _selectedClip, _project, _history));
    }

    /// <summary>Save Look… — snapshots the selected clip's enabled grading effects (at the playhead, for keyframed
    /// values) as a named user look.</summary>
    private async Task SaveLookAsync()
    {
        try
        {
            if (_looks is null || _selectedClip is not { } clip)
            {
                Status?.Invoke("Selecione um clipe gradado na linha do tempo para guardar o seu estilo.");
                return;
            }
            Timecode at = _playhead?.Invoke() ?? clip.TimelineStart;
            if (at < clip.TimelineStart || at >= clip.TimelineEnd)
                at = clip.TimelineStart; // keyframes are sampled on the clip, never off either end
            if (LookApplication.Capture(clip, "Look", at) is not { } captured)
            {
                Status?.Invoke("O clipe selecionado não tem efeitos de cor para guardar como estilo.");
                return;
            }
            if (TopLevel.GetTopLevel(this) is not Window owner
                || await NamePromptDialog.Show(owner, "Guardar estilo", "Nome do estilo", "ex.: Entrevista quente",
                    _looks.UniqueName("My Look")) is not { } name)
                return;
            Look stored = _looks.Add(captured with { Name = name });
            Status?.Invoke(_looks.LastSaveFailed
                ? $"Estilo {stored.Name} guardado para esta sessão, mas não foi possível escrever o ficheiro de estilos."
                : $"Estilo {stored.Name} guardado ({LooksBrowserModel.Badge(stored)}).");
        }
        catch (Exception ex)
        {
            // Fire-and-forget from a click handler: never let a dialog failure escape.
            System.Diagnostics.Debug.WriteLine($"save look failed: {ex}");
        }
    }

    /// <summary>Import LUT… — adds each chosen creative <c>.cube</c> as a one-effect look, after checking it loads.</summary>
    private async Task ImportLutAsync()
    {
        try
        {
            if (_looks is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
                return;
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importar LUT criativa",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("LUT Cube") { Patterns = ["*.cube"] }, FilePickerFileTypes.All],
            });
            var added = new List<string>();
            foreach (IStorageFile file in files)
            {
                if (file.TryGetLocalPath() is not { } path)
                    continue;
                CreativeLuts.Invalidate(path); // a re-import must re-read the file, not replay a cached verdict
                bool ok = await Task.Run(() => CreativeLuts.TryGet(path, out _, out _));
                if (!ok)
                {
                    Status?.Invoke($"Não foi possível importar {Path.GetFileName(path)}: {CreativeLuts.Error(path) ?? "não é uma LUT 3D .cube legível"}");
                    continue;
                }
                added.Add(_looks.Add(LooksBrowserModel.FromLutFile(path)).Name);
            }
            if (added.Count > 0)
                Status?.Invoke((added.Count == 1 ? $"Estilo de LUT {added[0]} importado." : $"{added.Count} estilos de LUT importados.") + UnsavedNote());
        }
        catch (Exception ex)
        {
            // Some Linux portals throw on cancel; a picker failure must never take the process down.
            System.Diagnostics.Debug.WriteLine($"import LUT failed: {ex}");
        }
    }

    private async Task RenameLookAsync(Look look)
    {
        try
        {
            if (_looks is null || TopLevel.GetTopLevel(this) is not Window owner)
                return;
            if (await NamePromptDialog.Show(owner, "Renomear estilo", "Nome do estilo", look.Name, look.Name) is { } name
                && _looks.Rename(look.Id, name) is { } renamed)
                Status?.Invoke($"Estilo renomeado para {renamed.Name}.{UnsavedNote()}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"rename look failed: {ex}");
        }
    }

    private async Task DeleteLookAsync(Look look)
    {
        try
        {
            if (_looks is null || TopLevel.GetTopLevel(this) is not Window owner)
                return;
            // Saved looks live outside the project's undo history, so deleting one is confirmed.
            if (await ConfirmDialog.Show(owner, "Eliminar estilo",
                    $"Tem a certeza de que pretende eliminar o estilo “{look.Name}”? Não é possível anular. Os clips a que foi aplicado mantêm os seus efeitos.",
                    "Eliminar", "Cancelar")
                && _looks.Remove(look.Id))
                Status?.Invoke($"Estilo {look.Name} eliminado.{UnsavedNote()}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"delete look failed: {ex}");
        }
    }

    /// <summary>The suffix for a library edit whose file write failed (the change holds for this session only).</summary>
    private string UnsavedNote() => _looks?.LastSaveFailed == true
        ? " Não foi possível escrever o ficheiro de estilos, pelo que esta alteração durará apenas esta sessão."
        : string.Empty;

    private static Button ToolbarButton(string text, string tip)
    {
        var button = new Button
        {
            Content = text,
            FontSize = Typography.Caption,
            Padding = new Avalonia.Thickness(8, 3),
            MinHeight = 24,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static TextBlock GroupHeader(string text) => new()
    {
        Text = text,
        FontSize = Typography.Micro,
        Foreground = MutedText,
        FontWeight = FontWeight.SemiBold,
        Margin = new Avalonia.Thickness(0, 10, 0, 0),
    };

    // ── Transitions browser ─────────────────────────────────────────────────────────────────────────

    private void BuildTransitions()
    {
        _transitionsList.Children.Clear();
        _transitionsList.Children.Add(new TextBlock
        {
            Text = "Arraste uma transição para um corte entre dois clips, ou faça duplo clique para a adicionar ao corte do clipe selecionado.",
            FontSize = Typography.Caption,
            Foreground = FaintText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 4),
        });

        foreach (TransitionDescriptor transition in TransitionCatalog.BuiltIns)
            _transitionsList.Children.Add(TransitionRow(transition));
    }

    private Control TransitionRow(TransitionDescriptor transition)
    {
        var title = new TextBlock { Text = transition.DisplayName, FontSize = Typography.Body, Foreground = TextBrush, FontWeight = FontWeight.SemiBold };
        var desc = new TextBlock
        {
            Text = transition.Description,
            FontSize = Typography.Caption,
            Foreground = MutedText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 2, 0, 0),
        };

        var row = new Border
        {
            Background = RaisedBg,
            CornerRadius = new Avalonia.CornerRadius(5),
            Padding = new Avalonia.Thickness(8, 6),
            Child = new StackPanel { Children = { title, desc } },
        };
        row.DoubleTapped += (_, _) => TransitionActivated?.Invoke(transition.Id);
        ToolTip.SetTip(row, "Arraste para um corte entre dois clips, ou faça duplo clique para a adicionar ao corte do clipe selecionado.");
        // Drag the transition onto a timeline cut to apply it (PLAN.md step 25), complementing double-click.
        EnableDrag(row, DragFormats.TransitionId, () => transition.Id);
        return row;
    }

    // ── Stickers browser (Etapa C) ─────────────────────────────────────────────────────────────────────

    // The PT tile names, parallel to EmojiAssets.Bundled's palette order.
    private static readonly string[] StickerNames =
    [
        "Fogo", "Coração", "Riso", "Surpresa", "Festa", "Polegar", "Óculos", "Susto",
        "Brilho", "Choro", "Bocejo", "Caveira", "Olhos", "Encanto", "Sono", "Clapeta",
    ];

    /// <summary>Builds the <b>Autocolantes</b> tiles once: the bundled Twemoji PNGs (the same glyphs the emoji
    /// transitions render, copied next to the exe under <c>Samples/Stickers/</c>) — a single click places the
    /// sticker at the playhead. No project needed, so this runs from the constructor.</summary>
    private void BuildStickers()
    {
        _stickersGrid.Children.Clear();
        string dir = Path.Combine(AppContext.BaseDirectory, "Samples", "Stickers");
        IReadOnlyList<string> bundled = EmojiAssets.Bundled;
        int placed = 0;
        for (int i = 0; i < bundled.Count && i < StickerNames.Length; i++)
        {
            string path = Path.Combine(dir, $"{EmojiAssets.AssetName(bundled[i])}.png");
            if (!File.Exists(path))
                continue;
            _stickersGrid.Children.Add(StickerTile(path, StickerNames[i]));
            placed++;
        }
        if (placed == 0)
            _stickersGrid.Children.Add(EmptyNote("Autocolantes indisponíveis — reinstale a aplicação."));
    }

    /// <summary>One sticker tile: the glyph PNG over its PT name. A single left click (no double-click — the
    /// palette gesture is one-tap-one-sticker) raises <see cref="StickerActivated"/> with the PNG path.</summary>
    private Control StickerTile(string pngPath, string name)
    {
        Bitmap image;
        try
        {
            image = new Bitmap(pngPath);
        }
        catch (Exception)
        {
            return EmptyNote($"Não foi possível ler {name}.");
        }

        var tile = new Border
        {
            Background = RaisedBg,
            CornerRadius = new Avalonia.CornerRadius(5),
            Padding = new Avalonia.Thickness(8, 6),
            Margin = new Avalonia.Thickness(0, 0, 6, 6),
            Child = new StackPanel
            {
                Children =
                {
                    new Image { Source = image, Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock
                    {
                        Text = name,
                        FontSize = Typography.Caption,
                        Foreground = MutedText,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 3, 0, 0),
                    },
                },
            },
        };
        tile.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
                StickerActivated?.Invoke(pngPath);
        };
        ToolTip.SetTip(tile, "Clique para colocar o autocolante no indicador de reprodução, sobre o vídeo.");
        return tile;
    }

    // ── Drag source ─────────────────────────────────────────────────────────────────────────────────

    // Pending-drag state: a press arms a drag that only begins once the pointer moves past a small threshold,
    // so a plain click (double-click to apply an effect, selecting a tile) still works. Avalonia 12's
    // DoDragDropAsync needs the originating PointerPressedEventArgs, so we hold it until the move fires.
    private Point _dragStart;
    private PointerPressedEventArgs? _pressedArgs;

    /// <summary>Makes <paramref name="source"/> a drag source carrying <paramref name="payloadFactory"/>'s string
    /// under <paramref name="format"/> once the pointer moves past a threshold.</summary>
    private void EnableDrag(Control source, DataFormat<string> format, Func<string> payloadFactory) =>
        EnableDrag(source, data => data.Add(DataTransferItem.Create(format, payloadFactory())));

    /// <summary>Makes <paramref name="source"/> a drag source whose payload <paramref name="fill"/> writes (for a drag
    /// carrying more than one format) once the pointer moves past a threshold.</summary>
    private void EnableDrag(Control source, Action<DataTransfer> fill)
    {
        source.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(source).Properties.IsLeftButtonPressed)
            {
                _pressedArgs = e;
                _dragStart = e.GetPosition(this);
            }
        };
        source.PointerMoved += (_, e) =>
        {
            if (_pressedArgs is not { } pressed || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed)
                return;
            Point p = e.GetPosition(this);
            if (Math.Abs(p.X - _dragStart.X) < 4 && Math.Abs(p.Y - _dragStart.Y) < 4)
                return;

            _pressedArgs = null;
            var data = new DataTransfer();
            fill(data);
            _ = DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Copy); // fire-and-forget; result not needed
        };
        source.PointerReleased += (_, _) => _pressedArgs = null;
    }

    private void ApplyEffect(EffectDescriptor effect, EffectPreset? preset = null)
    {
        if (_selectedClip is null || _history is null || _project is null)
        {
            Status?.Invoke("Selecione um clipe na linha do tempo para aplicar um efeito.");
            return;
        }

        EffectInstance instance = preset is null ? effect.CreateInstance() : effect.CreateInstance(preset);
        _history.Execute(new AddEffectCommand(_selectedClip, instance));
        string clip = Path.GetFileName(_project.MediaPool.Get(_selectedClip.MediaRefId)?.AbsolutePath ?? "clip");
        string name = preset is null ? effect.DisplayName : $"{effect.DisplayName} ({preset.Name})";
        Status?.Invoke($"{name} adicionado a {clip}.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static ScrollViewer Scroll(Control content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
    };

    private static Control EmptyNote(string text) => new TextBlock
    {
        Text = text,
        FontSize = Typography.Caption,
        Foreground = FaintText,
        Margin = new Avalonia.Thickness(8, 12),
    };

    private static IBrush Hex(string hex) => new SolidColorBrush(Color.Parse(hex));
}
