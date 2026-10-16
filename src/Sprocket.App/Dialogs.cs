using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Sprocket.Core.Model;
using Sprocket.Core.Timing;
using Sprocket.Export;
using Sprocket.Render;

namespace Sprocket.App;

// Small modal dialogs for the menu surface (PLAN.md step 16c): an About box and a discard-unsaved-changes
// confirmation. Built in code (like Timeline.TimelineControl / the panels) against the shell's dark palette —
// the shared Palette in Palette.cs, so there is no extra XAML and no per-dialog color copies. Dialog
// look/behaviour rests on manual verification (the App is a UI-bound WinExe); the logic that decides *whether*
// to show them lives in testable helpers.

/// <summary>The app icon, loaded once from the embedded avares resource and shared by the About box and any
/// code-built dialog windows. (MainWindow / its taskbar icon are wired directly in MainWindow.axaml.)</summary>
internal static class AppIcon
{
    public static readonly Bitmap Bitmap =
        new(AssetLoader.Open(new Uri("avares://Sprocket/Assets/sprocket.png")));

    public static WindowIcon Window => new(Bitmap);
}

/// <summary>The "About Sprocket" box. Deliberately carries no framework / runtime text (UI.md §3.7) — just the
/// product name, the app's own version, and a one-line description.</summary>
internal static class AboutDialog
{
    /// <summary>The public project site, shown in the About box and opened by Help ▸ Sprocket Website.</summary>
    public const string WebsiteUrl = "https://sprocketvideo.org";

    /// <summary>The user documentation site, opened by Help ▸ Documentation.</summary>
    public const string DocsUrl = "https://sprocketvideo.org/docs/";

    /// <summary>The GitHub new-issue chooser, opened by Help ▸ Report an Issue.</summary>
    public const string ReportIssueUrl = "https://github.com/SprocketVideo/Sprocket/issues/new/choose";

    /// <summary>The funding page, opened by Help ▸ Support Sprocket. The sponsor profile also links
    /// the secondary Ko-fi channel, so the app carries a single canonical URL.</summary>
    public const string SupportUrl = "https://github.com/sponsors/drittich";

    /// <summary>Documented exception to the <see cref="Typography"/> scale (STYLE_GUIDE.md): the About
    /// box's "Sprocket" wordmark is brand presentation, not UI chrome, so it sits above the token scale
    /// — the same carve-out the style guide gives component-local colors.</summary>
    private const double AboutWordmarkSize = 20;

    public static Task Show(Window owner)
    {
        // The bundled media engine version — a user-facing credit for the core dependency (not framework
        // chrome, UI.md §3.7). Degrades gracefully if FFmpeg can't be probed.
        string ffmpeg;
        try { ffmpeg = Sprocket.Media.FFmpegDiagnostics.DisplayVersion(); }
        catch { ffmpeg = "FFmpeg indisponível"; }

        var logo = new Image
        {
            Width = 48,
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            Source = AppIcon.Bitmap,
        };

        var openLogs = new Button
        {
            Content = "Abrir pasta de registos",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(12, 4),
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            CornerRadius = new CornerRadius(5),
        };

        var close = new Button
        {
            Content = "Fechar",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(18, 5),
            Foreground = Brushes.White,
            Background = Palette.AccentBrush,
            CornerRadius = new CornerRadius(5),
        };

        // The public site, as a link-styled line (opened via the TopLevel launcher, like Open Logs Folder).
        var website = new TextBlock
        {
            Text = "sprocketvideo.org",
            FontSize = Typography.Body,
            Foreground = Palette.AccentBrush,
            TextDecorations = TextDecorations.Underline,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var dialog = new Window
        {
            Title = "Acerca do Sprocket",
            Icon = AppIcon.Window,
            Width = 420,
            Height = 380,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.WindowBgBrush,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    logo,
                    Centered("Sprocket", AboutWordmarkSize, FontWeight.SemiBold, Palette.TextBrush),
                    Centered($"Versão {Program.AppVersion}", Typography.Body, FontWeight.Normal, Palette.MutedTextBrush),
                    Centered($"Motor de multimédia: {ffmpeg}", Typography.Body, FontWeight.Normal, Palette.MutedTextBrush),
                    Centered("Um editor de vídeo multiplataforma e não destrutivo. Gratuito e de código aberto.", Typography.Body, FontWeight.Normal, Palette.MutedTextBrush),
                    website,
                    // Where crash / exception logs are written (CrashLog), so a user hitting a problem can find the
                    // log without knowing the per-OS convention. The path is selectable; the button opens the folder.
                    Centered("Os registos são escritos em:", Typography.Caption, FontWeight.Normal, Palette.MutedTextBrush),
                    Selectable(CrashLog.LogDirectory),
                    openLogs,
                    close,
                },
            },
        };

        website.PointerPressed += async (_, _) =>
        {
            try
            {
                if (TopLevel.GetTopLevel(dialog)?.Launcher is { } launcher)
                    await launcher.LaunchUriAsync(new Uri(WebsiteUrl));
            }
            catch
            {
                // Best-effort, like Open Logs Folder below.
            }
        };
        openLogs.Click += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(CrashLog.LogDirectory); // may not exist yet if nothing has been logged
                if (TopLevel.GetTopLevel(dialog)?.Launcher is { } launcher)
                    await launcher.LaunchUriAsync(new Uri(CrashLog.LogDirectory));
            }
            catch
            {
                // Best-effort: on a headless / unusual environment the launcher may be unavailable.
            }
        };
        close.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog(owner);
    }

    private static TextBlock Centered(string text, double size, FontWeight weight, IBrush brush) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = brush,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    // A wrapped, user-selectable path so it can be copied out of the About box.
    private static SelectableTextBlock Selectable(string text) => new()
    {
        Text = text,
        FontSize = Typography.Caption,
        Foreground = Palette.TextBrush,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
}

/// <summary>The Help ▸ Third-Party Notices box (PLAN.md step 36a): the repo-root
/// <c>THIRD-PARTY-NOTICES.md</c> (copied next to the exe by Sprocket.App.csproj so it ships in every release
/// bundle, read by path the same way <see cref="MediaBootstrap"/> resolves the bundled sample clip), rendered
/// in-app by <see cref="MarkdownView"/> — headings, tables, and clickable links on the native inline model, no
/// markdown/HTML NuGet or WebView. Degrades to an explanatory message rather than failing if the file is ever
/// missing (e.g. a dev build that hasn't copied Content items).</summary>
internal static class ThirdPartyNoticesDialog
{
    public static Task Show(Window owner)
    {
        Control body;
        try
        {
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md"));
            body = MarkdownView.Build(text, AppContext.BaseDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            body = new SelectableTextBlock
            {
                Text = "O THIRD-PARTY-NOTICES.md não foi encontrado junto do executável.",
                FontSize = Typography.Emphasis,
                Foreground = Palette.TextBrush,
                TextWrapping = TextWrapping.Wrap,
            };
        }
        body.Margin = new Thickness(0, 0, 12, 0);

        var close = new Button
        {
            Content = "Fechar",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(18, 5),
            Foreground = Brushes.White,
            Background = Palette.AccentBrush,
            CornerRadius = new CornerRadius(5),
        };

        var dialog = new Window
        {
            Title = "Avisos de terceiros",
            Icon = AppIcon.Window,
            Width = 760,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.WindowBgBrush,
            Content = new DockPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new StackPanel
                    {
                        [DockPanel.DockProperty] = Dock.Bottom,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 14, 0, 0),
                        Children = { close },
                    },
                    new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        Content = body,
                    },
                },
            },
        };

        close.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog(owner);
    }
}

/// <summary>A two-button confirmation (e.g. discard unsaved changes before New/Open). Returns <c>true</c> when
/// the user accepts, <c>false</c> on cancel / close.</summary>
internal static class ConfirmDialog
{
    public static Task<bool> Show(Window owner, string title, string message, string confirmText, string cancelText) =>
        Build(title, message, confirmText, cancelText).ShowDialog<bool>(owner);

    /// <summary>Like <see cref="Show"/>, but tells a title-bar dismissal apart from the cancel button: <c>true</c>
    /// = confirm, <c>false</c> = cancel button, <c>null</c> = closed without choosing. For prompts whose cancel
    /// button is itself a destructive choice (e.g. discarding an autosave), so closing the window stays a no-op.</summary>
    public static Task<bool?> ShowOrDismiss(Window owner, string title, string message, string confirmText, string cancelText) =>
        Build(title, message, confirmText, cancelText).ShowDialog<bool?>(owner);

    private static Window Build(string title, string message, string confirmText, string cancelText)
    {
        var confirm = new Button
        {
            Content = confirmText,
            Padding = new Thickness(16, 5),
            Foreground = Brushes.White,
            Background = Palette.AccentBrush,
            CornerRadius = new CornerRadius(5),
        };
        var cancel = new Button
        {
            Content = cancelText,
            Padding = new Thickness(16, 5),
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            CornerRadius = new CornerRadius(5),
        };

        var dialog = new Window
        {
            Title = title,
            Icon = AppIcon.Window,
            Width = 400,
            MinHeight = 170,
            // Grow to fit a longer message (e.g. the Export Complete diagnostics block) instead of clipping it.
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.WindowBgBrush,
            Content = new DockPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new StackPanel
                    {
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 16, 0, 0),
                        Children = { cancel, confirm },
                    },
                    new TextBlock
                    {
                        Text = message,
                        Foreground = Palette.TextBrush,
                        FontSize = Typography.Emphasis,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };

        confirm.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        return dialog;
    }
}

/// <summary>What the user chose in the unsaved-changes prompt (<see cref="SaveChangesDialog"/>).</summary>
internal enum SaveChangesChoice
{
    /// <summary>Abandon the action; the document stays open and dirty. Deliberately declared first so it is
    /// <c>default</c> — that is what <c>ShowDialog&lt;T&gt;</c> hands back when the prompt is dismissed by its
    /// title-bar close button, and "do nothing" is the only safe reading of that gesture.</summary>
    Cancel,

    /// <summary>Save the document first, then continue with the action.</summary>
    Save,

    /// <summary>Continue and lose the changes.</summary>
    Discard,
}

/// <summary>
/// The three-button unsaved-changes prompt shown before anything that would discard the document — closing,
/// quitting, or replacing it via File ▸ New / Open. Save · Don't Save · Cancel is what every leading editor
/// offers here (Premiere, Resolve, and the platform save-on-close alerts themselves): a two-button
/// discard-or-cancel would make the user back out, save by hand, and start the action over. Mirrors
/// <see cref="ConfirmDialog"/>'s look, with the accented default (Save) rightmost.
/// </summary>
internal static class SaveChangesDialog
{
    public static Task<SaveChangesChoice> Show(Window owner, string message)
    {
        var save = new Button
        {
            Content = "Guardar",
            Padding = new Thickness(16, 5),
            Foreground = Brushes.White,
            Background = Palette.AccentBrush,
            CornerRadius = new CornerRadius(5),
            IsDefault = true, // Enter saves…
        };
        var cancel = new Button
        {
            Content = "Cancelar",
            Padding = new Thickness(16, 5),
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            CornerRadius = new CornerRadius(5),
            IsCancel = true,  // …and Esc backs out, so this modal is never a keyboard dead end
        };
        var discard = new Button
        {
            Content = "Não guardar",
            Padding = new Thickness(16, 5),
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            CornerRadius = new CornerRadius(5),
        };

        var dialog = new Window
        {
            Title = "Alterações não guardadas",
            Icon = AppIcon.Window,
            Width = 440,
            Height = 200,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.WindowBgBrush,
            Content = new DockPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new StackPanel
                    {
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 16, 0, 0),
                        // Destructive answer furthest from the default, as the platform alerts place it.
                        Children = { discard, cancel, save },
                    },
                    new TextBlock
                    {
                        Text = message,
                        Foreground = Palette.TextBrush,
                        FontSize = Typography.Emphasis,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };

        save.Click += (_, _) => dialog.Close(SaveChangesChoice.Save);
        discard.Click += (_, _) => dialog.Close(SaveChangesChoice.Discard);
        cancel.Click += (_, _) => dialog.Close(SaveChangesChoice.Cancel);
        // Focus Save rather than let the panel's first child (Don't Save) take it: a reflexive Enter on a
        // prompt the user did not expect must not be the keystroke that throws their work away.
        dialog.Opened += (_, _) => save.Focus();

        return dialog.ShowDialog<SaveChangesChoice>(owner);
    }
}

/// <summary>
/// A tiny modal that prompts for a single name (an export preset, a saved look); returns the trimmed name, or
/// <see langword="null"/> on cancel / empty. Mirrors the shell's other code-built dialogs against the shared dark
/// palette.
/// </summary>
internal static class NamePromptDialog
{
    /// <summary>Shows the prompt titled <paramref name="title"/> over <paramref name="owner"/>, prefilled with
    /// <paramref name="initial"/> (selected, so typing replaces it).</summary>
    public static Task<string?> Show(Window owner, string title, string label, string placeholder, string? initial = null)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = placeholder,
            Text = initial,
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var ok = new Button
        {
            Content = "Guardar",
            Padding = new Thickness(16, 5),
            Foreground = Brushes.White,
            Background = Palette.AccentBrush,
            CornerRadius = new CornerRadius(5),
        };
        var cancel = new Button
        {
            Content = "Cancelar",
            Padding = new Thickness(16, 5),
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            CornerRadius = new CornerRadius(5),
        };
        var dlg = new Window
        {
            Title = title,
            Icon = AppIcon.Window,
            Width = 340,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.WindowBgBrush,
            FontSize = Typography.Body,
            Content = new DockPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new StackPanel
                    {
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 16, 0, 0),
                        Children = { cancel, ok },
                    },
                    new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock { Text = label, Foreground = Palette.MutedTextBrush, FontSize = Typography.Body },
                            nameBox,
                        },
                    },
                },
            },
        };

        void Accept()
        {
            string trimmed = (nameBox.Text ?? string.Empty).Trim();
            dlg.Close(string.IsNullOrEmpty(trimmed) ? null : trimmed);
        }
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => dlg.Close((string?)null);
        nameBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Accept(); };

        dlg.Opened += (_, _) =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        };
        return dlg.ShowDialog<string?>(owner);
    }
}

/// <summary>A single-button information dialog (e.g. "export complete / failed"). Mirrors
/// <see cref="ConfirmDialog"/>'s look but has nothing to decide — it just acknowledges a message.</summary>
internal static class MessageDialog
{
    public static Task Show(Window owner, string title, string message, string buttonText = "OK")
    {
        var ok = new Button
        {
            Content = buttonText,
            Padding = new Thickness(18, 5),
            Foreground = Brushes.White,
            Background = Palette.AccentBrush,
            CornerRadius = new CornerRadius(5),
        };

        var dialog = new Window
        {
            Title = title,
            Icon = AppIcon.Window,
            Width = 420,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.WindowBgBrush,
            Content = new DockPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new StackPanel
                    {
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 16, 0, 0),
                        Children = { ok },
                    },
                    new TextBlock
                    {
                        Text = message,
                        Foreground = Palette.TextBrush,
                        FontSize = Typography.Emphasis,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };

        ok.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog(owner);
    }
}

/// <summary>The Speed / Duration dialog's result: the constant speed ratio plus the playback direction.</summary>
internal sealed record SpeedDialogResult(Rational Speed, bool Reverse);

/// <summary>
/// The Clip ▸ Speed / Duration dialog (PLAN.md step 21): edits a clip's playback speed as a percentage
/// (100% = normal), with quick presets, and a <b>Reverse Speed</b> toggle (the naming leading editors use; the
/// percentage stays positive and the direction is a separate flag). Returns the chosen speed + direction, or
/// <see langword="null"/> on cancel. Freeze (0%) is the Frame Hold feature, so the input is clamped to a
/// positive percentage; keyframed speed ramps are authored in the Inspector's Speed lane.
/// </summary>
internal static class SpeedDialog
{
    public static Task<SpeedDialogResult?> Show(Window owner, Rational current, bool currentReverse, bool hasRamp = false, bool canReverse = true)
    {
        var reverse = new CheckBox
        {
            Content = "Inverter velocidade",
            IsChecked = currentReverse,
            IsEnabled = canReverse,
            Foreground = Palette.TextBrush,
            Margin = new Thickness(0, 8, 0, 0),
        };
        ToolTip.SetTip(reverse, canReverse
            ? "Reproduz o clipe ao contrário à velocidade escolhida (a duração não é alterada)"
            : "Uma sequência aninhada não pode ser invertida");
        // A ramped clip's constant speed is dormant; say so, since applying a *different* speed removes the ramp.
        var rampNote = new TextBlock
        {
            Text = "Este clipe tem uma rampa de velocidade. Aplicar uma velocidade diferente substitui a rampa.",
            Foreground = Palette.MutedTextBrush,
            FontSize = Typography.Caption,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = hasRamp,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var box = new TextBox
        {
            Text = SpeedFormat.ToPercentString(current),
            Width = 90,
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 10, 0, 0) };
        foreach (int pct in new[] { 25, 50, 100, 200, 400 })
        {
            int p = pct;
            var b = new Button
            {
                Content = $"{p}%",
                Padding = new Thickness(10, 4),
                Foreground = Palette.TextBrush,
                Background = Palette.PanelBgBrush,
                CornerRadius = new CornerRadius(4),
            };
            b.Click += (_, _) => box.Text = p.ToString(System.Globalization.CultureInfo.InvariantCulture);
            presets.Children.Add(b);
        }

        var ok = new Button
        {
            Content = "Aplicar",
            Padding = new Thickness(16, 5),
            Foreground = Brushes.White,
            Background = Palette.AccentBrush,
            CornerRadius = new CornerRadius(5),
        };
        var cancel = new Button
        {
            Content = "Cancelar",
            Padding = new Thickness(16, 5),
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            CornerRadius = new CornerRadius(5),
        };

        var dialog = new Window
        {
            Title = "Velocidade / Duração",
            Icon = AppIcon.Window,
            Width = 360,
            Height = hasRamp ? 290 : 250,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.WindowBgBrush,
            Content = new DockPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new StackPanel
                    {
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 16, 0, 0),
                        Children = { cancel, ok },
                    },
                    new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock { Text = "Velocidade", Foreground = Palette.MutedTextBrush, FontSize = Typography.Body },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 6,
                                Children =
                                {
                                    box,
                                    new TextBlock { Text = "%", Foreground = Palette.TextBrush, VerticalAlignment = VerticalAlignment.Center },
                                },
                            },
                            presets,
                            reverse,
                            rampNote,
                        },
                    },
                },
            },
        };

        void Accept()
        {
            if (SpeedFormat.TryParsePercent(box.Text, out Rational speed))
                dialog.Close(new SpeedDialogResult(speed, reverse.IsChecked == true));
        }
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => dialog.Close(null);
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Accept(); };

        return dialog.ShowDialog<SpeedDialogResult?>(owner);
    }
}

/// <summary>
/// The «Aplicar transições aleatórias» options window (Etapa C): the transition duration, which types may be
/// drawn (a toggle per catalog entry — the emoji types included), and the RNG seed with a «Gerar de novo»
/// re-roll, over a live preview listing the planned cut → transition assignment. The planning itself is
/// <see cref="RandomTransitions"/>, recomputed on every option change; the window closes with the final
/// assignment list (null on cancel), which the caller applies as one composite command.
/// </summary>
internal static class RandomTransitionsDialog
{
    public static Task<IReadOnlyList<RandomTransitionAssignment>?> Show(Window owner, Timeline timeline, long frameTicks)
    {
        var durationBox = new TextBox
        {
            Text = "0.5",
            Width = 70,
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
        };
        var seedBox = new TextBox
        {
            Width = 130,
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
        };
        var reroll = new Button
        {
            Content = "Gerar de novo",
            Padding = new Thickness(10, 4),
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            CornerRadius = new CornerRadius(4),
        };

        // One toggle per catalog type, all on by default; Avalonia 12 ToggleButtons expose state via Click + IsChecked.
        var toggles = new List<(ToggleButton Button, string Id)>();
        var typeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (TransitionDescriptor descriptor in TransitionCatalog.BuiltIns)
        {
            var toggle = new ToggleButton
            {
                Content = descriptor.DisplayName,
                IsChecked = true,
                Padding = new Thickness(10, 4),
                Foreground = Palette.TextBrush,
                Background = Palette.PanelBgBrush,
                CornerRadius = new CornerRadius(4),
            };
            toggles.Add((toggle, descriptor.Id));
            typeRow.Children.Add(toggle);
        }

        var summary = new TextBlock { Foreground = Palette.MutedTextBrush, FontSize = Typography.Caption, TextWrapping = TextWrapping.Wrap };
        var previewList = new StackPanel { Spacing = 2 };
        var ok = new Button
        {
            Content = "Aplicar",
            Padding = new Thickness(16, 5),
            Foreground = Brushes.White,
            Background = Palette.AccentBrush,
            CornerRadius = new CornerRadius(5),
        };
        var cancel = new Button
        {
            Content = "Cancelar",
            Padding = new Thickness(16, 5),
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            CornerRadius = new CornerRadius(5),
        };

        var dialog = new Window
        {
            Title = "Aplicar transições aleatórias",
            Icon = AppIcon.Window,
            Width = 480,
            Height = 540,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.WindowBgBrush,
            Content = new DockPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new StackPanel
                    {
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 16, 0, 0),
                        Children = { cancel, ok },
                    },
                    new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock { Text = "Duração de cada transição", Foreground = Palette.MutedTextBrush, FontSize = Typography.Body },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 6,
                                Children =
                                {
                                    durationBox,
                                    new TextBlock { Text = "segundos", Foreground = Palette.TextBrush, VerticalAlignment = VerticalAlignment.Center },
                                },
                            },
                            new TextBlock
                            {
                                Text = "Tipos a sortear",
                                Foreground = Palette.MutedTextBrush,
                                FontSize = Typography.Body,
                                Margin = new Thickness(0, 8, 0, 0),
                            },
                            typeRow,
                            new TextBlock
                            {
                                Text = "Semente",
                                Foreground = Palette.MutedTextBrush,
                                FontSize = Typography.Body,
                                Margin = new Thickness(0, 8, 0, 0),
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 6,
                                Children = { seedBox, reroll },
                            },
                            new TextBlock
                            {
                                Text = "Pré-visualização",
                                Foreground = Palette.MutedTextBrush,
                                FontSize = Typography.Body,
                                Margin = new Thickness(0, 8, 0, 0),
                            },
                            summary,
                            new ScrollViewer { MaxHeight = 210, Content = previewList },
                        },
                    },
                },
            },
        };

        ulong seed = (ulong)Random.Shared.NextInt64();
        IReadOnlyList<RandomTransitionAssignment> current = [];

        void Refresh(bool rewriteSeed)
        {
            if (rewriteSeed)
                seedBox.Text = seed.ToString(CultureInfo.InvariantCulture);
            previewList.Children.Clear();
            if (!double.TryParse(durationBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                || seconds <= 0)
            {
                summary.Text = "Duração inválida — introduza os segundos (ex.: 0.5).";
                ok.IsEnabled = false;
                return;
            }
            List<string> allowed = toggles.Where(t => t.Button.IsChecked == true).Select(t => t.Id).ToList();
            if (allowed.Count == 0)
            {
                summary.Text = "Escolha pelo menos um tipo de transição.";
                ok.IsEnabled = false;
                return;
            }

            current = RandomTransitions.Build(
                timeline, Timecode.FromSeconds(seconds), allowed, seed, frameTicks, EmojiAssets.Bundled);
            summary.Text = current.Count == 0
                ? "Sem cortes elegíveis — só cortes entre clips adjacentes e ainda sem transição recebem uma."
                : $"{current.Count} transições a aplicar:";
            foreach (RandomTransitionAssignment a in current)
            {
                string track = string.IsNullOrEmpty(a.Track.Name) ? "pista" : a.Track.Name;
                previewList.Children.Add(new TextBlock
                {
                    Text = $"{FormatCut(a.CutPoint)} · {track} — {TransitionCatalog.DisplayName(a.TransitionTypeId)}"
                        + (a.Emoji is null ? "" : $" {a.Emoji}"),
                    Foreground = Palette.TextBrush,
                    FontSize = Typography.Body,
                });
            }
            ok.IsEnabled = current.Count > 0;
        }

        reroll.Click += (_, _) => { seed = (ulong)Random.Shared.NextInt64(); Refresh(rewriteSeed: true); };
        foreach ((ToggleButton button, _) in toggles)
            button.Click += (_, _) => Refresh(rewriteSeed: false);
        durationBox.TextChanged += (_, _) => Refresh(rewriteSeed: false);
        // Typing a seed re-plans with it (reproducing a liked draw); an unparseable seed just disables Aplicar.
        seedBox.TextChanged += (_, _) =>
        {
            if (ulong.TryParse(seedBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong typed))
            {
                seed = typed;
                Refresh(rewriteSeed: false);
            }
            else
            {
                previewList.Children.Clear();
                summary.Text = "Semente inválida.";
                ok.IsEnabled = false;
            }
        };

        ok.Click += (_, _) => dialog.Close(current);
        cancel.Click += (_, _) => dialog.Close(null);

        Refresh(rewriteSeed: true);
        return dialog.ShowDialog<IReadOnlyList<RandomTransitionAssignment>?>(owner);
    }

    private static string FormatCut(Timecode t) =>
        TimeSpan.FromTicks((long)(t.Ticks * 1e7 / Timecode.TicksPerSecond))
            .ToString("m\\:ss\\.f", CultureInfo.InvariantCulture);
}

/// <summary>
/// The sequence-format dialog (PLAN.md step 23): name plus frame size, the latter picked from the preset list in
/// <see cref="SequenceFormatPresets"/> (landscape + portrait/square social formats) or entered as a custom
/// width × height — the Sequence ▸ Settings surface (<see cref="ShowSettings"/>) and the New Sequence format
/// picker (<see cref="ShowNew"/>) share the same form, like the shared settings panel behind Premiere's New
/// Sequence / Sequence Settings dialogs. Frame rate and sample rate remain read-only (changing them re-times
/// every clip; deferred). Returns the chosen name + resolution on accept, or <see langword="null"/> on cancel;
/// the undoable application is the caller's job (via <see cref="SequenceSettingsOps"/>).
/// </summary>
internal static class SequenceSettingsDialog
{
    /// <summary>What the dialog returns: the (trimmed, non-empty) sequence name and the chosen frame size.</summary>
    internal sealed record Result(string Name, Resolution Resolution);

    /// <summary>Sequence ▸ Settings: edit the active sequence's name + frame size.</summary>
    public static Task<Result?> ShowSettings(Window owner, Sequence sequence) =>
        Show(owner, "Definições de sequência", "Aplicar", sequence.Name, sequence.Timeline);

    /// <summary>Sequence ▸ New Sequence: pick the new sequence's name + format, seeded from the active one.</summary>
    public static Task<Result?> ShowNew(Window owner, string suggestedName, Timeline seedFormat) =>
        Show(owner, "Nova sequência", "Criar", suggestedName, seedFormat);

    private static Task<Result?> Show(Window owner, string title, string acceptLabel, string name, Timeline format)
    {
        TextBox MakeBox(string text) => new()
        {
            Text = text,
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var nameBox = MakeBox(name);
        var widthBox = MakeBox(format.Resolution.Width.ToString());
        var heightBox = MakeBox(format.Resolution.Height.ToString());

        var presetBox = new ComboBox
        {
            ItemsSource = SequenceFormatPresets.Presets.Select(p => p.Label).ToList(),
            SelectedIndex = SequenceFormatPresets.IndexOf(format.Resolution),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            FontSize = Typography.Body,
        };

        // Preset ↔ width/height boxes, two-way: picking a preset fills the boxes; hand-editing a box snaps the
        // preset to Custom (the export dialog's preset-box convention). The guard stops the programmatic fill
        // from bouncing the combo back to Custom.
        bool applyingPreset = false;
        presetBox.SelectionChanged += (_, _) =>
        {
            if (SequenceFormatPresets.Presets[Math.Max(0, presetBox.SelectedIndex)].Value is not { } preset)
                return;
            applyingPreset = true;
            widthBox.Text = preset.Width.ToString();
            heightBox.Text = preset.Height.ToString();
            applyingPreset = false;
        };
        void SnapToCustom(object? _, EventArgs __)
        {
            if (!applyingPreset
                && SequenceFormatPresets.TryParse(widthBox.Text, heightBox.Text, out Resolution typed))
                presetBox.SelectedIndex = SequenceFormatPresets.IndexOf(typed);
            else if (!applyingPreset)
                presetBox.SelectedIndex = SequenceFormatPresets.CustomIndex;
        }
        widthBox.TextChanged += SnapToCustom;
        heightBox.TextChanged += SnapToCustom;

        double fps = format.FrameRate.Den > 0 ? (double)format.FrameRate.Num / format.FrameRate.Den : 0;
        string fixedFormat = string.Format(
            CultureInfo.InvariantCulture, "{0:0.##} fps  ·  {1:0.#} kHz  (fixa)", fps, format.SampleRate / 1000.0);

        var apply = new Button
        {
            Content = acceptLabel,
            Padding = new Thickness(16, 5),
            Foreground = Brushes.White,
            Background = Palette.AccentBrush,
            CornerRadius = new CornerRadius(5),
        };
        var cancel = new Button
        {
            Content = "Cancelar",
            Padding = new Thickness(16, 5),
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            CornerRadius = new CornerRadius(5),
        };

        var sizeGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var widthCol = new StackPanel
        {
            Spacing = 3,
            Children = { new TextBlock { Text = "Largura", Foreground = Palette.MutedTextBrush, FontSize = Typography.Body }, widthBox },
        };
        var heightCol = new StackPanel
        {
            Spacing = 3,
            Children = { new TextBlock { Text = "Altura", Foreground = Palette.MutedTextBrush, FontSize = Typography.Body }, heightBox },
        };
        widthCol.SetValue(Grid.ColumnProperty, 0);
        heightCol.SetValue(Grid.ColumnProperty, 2);
        sizeGrid.Children.Add(widthCol);
        sizeGrid.Children.Add(heightCol);

        var dialog = new Window
        {
            Title = title,
            Icon = AppIcon.Window,
            Width = 380,
            Height = 330,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.WindowBgBrush,
            Content = new DockPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new StackPanel
                    {
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 16, 0, 0),
                        Children = { cancel, apply },
                    },
                    new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock { Text = "Nome", Foreground = Palette.MutedTextBrush, FontSize = Typography.Body },
                            nameBox,
                            new TextBlock { Text = "Dimensão do fotograma", Foreground = Palette.MutedTextBrush, FontSize = Typography.Body, Margin = new Thickness(0, 10, 0, 0) },
                            presetBox,
                            sizeGrid,
                            new TextBlock { Text = fixedFormat, Foreground = Palette.MutedTextBrush, FontSize = Typography.Body, Margin = new Thickness(0, 10, 0, 0) },
                        },
                    },
                },
            },
        };

        void Accept()
        {
            string trimmed = (nameBox.Text ?? string.Empty).Trim();
            if (trimmed.Length == 0
                || !SequenceFormatPresets.TryParse(widthBox.Text, heightBox.Text, out Resolution resolution))
                return; // incomplete/invalid — keep the dialog open, like the export dialog's guarded Export button
            dialog.Close(new Result(trimmed, resolution));
        }
        apply.Click += (_, _) => Accept();
        cancel.Click += (_, _) => dialog.Close(null);
        void AcceptOnEnter(object? _, Avalonia.Input.KeyEventArgs e) { if (e.Key == Avalonia.Input.Key.Enter) Accept(); }
        nameBox.KeyDown += AcceptOnEnter;
        widthBox.KeyDown += AcceptOnEnter;
        heightBox.KeyDown += AcceptOnEnter;

        return dialog.ShowDialog<Result?>(owner);
    }
}

/// <summary>
/// The Export settings dialog (PLAN.md step 27 + 29): a cascading container → video-codec → audio-codec picker, a
/// quality tier, and output resolution / frame-rate overrides, so the user can deliver into the whole format/codec
/// matrix rather than a fixed MP4. The video / audio dropdowns are repopulated with only the codecs valid in the
/// chosen container, so every selection is a valid combination. A <b>preset</b> dropdown applies a saved selection
/// over that matrix (the curated built-ins plus the user's own, persisted by <see cref="UserExportPresets"/>), and
/// <b>Save Preset…</b> captures the current selection as a new user preset (PLAN.md step 29). Returns the chosen
/// settings on Export, or <see langword="null"/> on cancel.
/// </summary>
internal static class ExportSettingsDialog
{
    /// <summary>
    /// What the Export settings dialog returns: the delivery <see cref="Options"/> plus whether the export should
    /// cover only the timeline's in/out-marked range (<see cref="UseInOutRange"/>) rather than the whole sequence —
    /// the Range selector of leading editors' export dialogs (Premiere's Entire Sequence / Sequence In-Out,
    /// Resolve's Entire Timeline / In-Out Range). The caller resolves the actual <c>ExportRange</c> from the marks.
    /// </summary>
    internal sealed record Result(ExportOptions Options, bool UseInOutRange);

    // The resolution / frame-rate override choices, each paired with its value (null = keep the sequence's own).
    // The set is closed: a user preset can only capture a value that exists here, so every saved preset round-trips
    // back to a dropdown entry.
    private static readonly (string Label, Resolution? Value)[] Resolutions =
    [
        ("Igual à sequência", null),
        ("3840 × 2160 (4K UHD)", new Resolution(3840, 2160)),
        ("1920 × 1080 (1080p)", new Resolution(1920, 1080)),
        ("1280 × 720 (720p)", new Resolution(1280, 720)),
        ("854 × 480 (480p)", new Resolution(854, 480)),
        ("2160 × 3840 (4K Vertical)", new Resolution(2160, 3840)),
        ("1080 × 1920 (1080p Vertical)", new Resolution(1080, 1920)),
        ("720 × 1280 (720p Vertical)", new Resolution(720, 1280)),
        ("1080 × 1350 (4:5 Vertical)", new Resolution(1080, 1350)),
        ("1080 × 1080 (Quadrada)", new Resolution(1080, 1080)),
    ];

    private static readonly (string Label, Rational? Value)[] FrameRates =
    [
        ("Igual à sequência", null),
        ("23.976", new Rational(24000, 1001)),
        ("24", new Rational(24, 1)),
        ("25 (PAL)", new Rational(25, 1)),
        ("29.97 (NTSC)", new Rational(30000, 1001)),
        ("30", new Rational(30, 1)),
        ("50", new Rational(50, 1)),
        ("59.94", new Rational(60000, 1001)),
        ("60", new Rational(60, 1)),
    ];

    public static Task<Result?> Show(Window owner, int sequenceWidth, int sequenceHeight, string projectName = "", bool hasMarkedRange = false)
    {
        // Range selector (the Premiere / Resolve export-dialog convention): Entire sequence vs. the timeline's
        // in/out-marked range, defaulting to the marked range when marks are set. Kept out of presets (it is
        // per-export scope, not delivery format) and never snaps the preset box to Custom. Applies to audio-only
        // deliveries too — the master mix can be exported in-to-out. Choosing the range option with no marks set
        // degrades to the whole sequence (the caller resolves the marks).
        ComboBox rangeBox = MakeCombo(["Sequência inteira", "Intervalo entrada/saída"]);
        rangeBox.SelectedIndex = hasMarkedRange ? 1 : 0;

        // The Format list holds the video containers first, then the audio-only delivery targets (PLAN.md step 44):
        // selecting one of the latter switches the dialog into audio-only mode (the video-side controls hide).
        ExportContainer[] containers = Enum.GetValues<ExportContainer>();
        ExportAudioFormat[] audioFormats = Enum.GetValues<ExportAudioFormat>();
        int videoFormatCount = containers.Length;
        ComboBox containerBox = MakeCombo(
        [
            .. containers.Select(c => ExportCodecs.Container(c).DisplayName),
            .. audioFormats.Select(a => "Apenas áudio · " + ExportCodecs.AudioFormat(a).DisplayName),
        ]);
        bool IsAudioOnly() => containerBox.SelectedIndex >= videoFormatCount;
        ExportAudioFormat SelectedAudioFormat() => audioFormats[containerBox.SelectedIndex - videoFormatCount];
        ComboBox videoBox = MakeCombo([]);
        ComboBox audioBox = MakeCombo([]);
        // Rate control (the two-mode trade-off leading NLEs expose — Resolve's Quality vs "Restrict to", Premiere's
        // VBR target): constant quality holds the picture steady via a CRF slider (size floats with content);
        // target bitrate aims at a Mbps figure (size predictable, quality floats). Part of the delivery format,
        // so presets capture it.
        ComboBox rateControlBox = MakeCombo(["Qualidade constante", "Bitrate de destino"]);
        rateControlBox.SelectedIndex = 0;
        // Encoding: software (deterministic, best compatibility — the default) or hardware (GPU encoder with
        // automatic software fallback, PLAN.md step 29; it honours the same rate control via each vendor's own
        // quality knob). It swaps only the encoder — the label says so, so it never implies the whole export runs on
        // the GPU (that is Fast Export's job). Presets capture it.
        ComboBox encodingBox = MakeCombo(["Software", "Codificador por hardware (se disponível)"]);
        encodingBox.SelectedIndex = 0;
        // Mode (export-speed phase 3): Final Export — the deterministic reference render — or Fast Export, which
        // always probes the GPU encoders and falls back to a speed-first software preset (the reference-vs-draft split
        // of leading editors). Fast locks Encoding to hardware, restoring the user's choice when switched back.
        // Presets capture it.
        ComboBox modeBox = MakeCombo(["Exportação final", "Exportação rápida"]);
        modeBox.SelectedIndex = 0;
        var modeText = new TextBlock { Foreground = Palette.MutedTextBrush, FontSize = Typography.Body, TextWrapping = TextWrapping.Wrap };
        int encodingBeforeFast = 0;
        void UpdateModeText() => modeText.Text = modeBox.SelectedIndex == 1
            ? "Codifica na GPU quando disponível, caso contrário com uma predefinição de software mais rápida. Os ficheiros podem ser um pouco maiores e diferir entre máquinas — não se destina à entrega final."
            : "Qualidade de referência: saída reprodutível, para entrega final.";
        modeBox.SelectionChanged += (_, _) =>
        {
            bool fast = modeBox.SelectedIndex == 1;
            if (fast && encodingBox.IsEnabled)
            {
                encodingBeforeFast = encodingBox.SelectedIndex;
                encodingBox.SelectedIndex = 1;
            }
            else if (!fast && !encodingBox.IsEnabled)
            {
                encodingBox.SelectedIndex = encodingBeforeFast;
            }
            encodingBox.IsEnabled = !fast;
            UpdateModeText();
        };
        UpdateModeText();
        ComboBox resolutionBox = MakeCombo(Resolutions.Select(r => r.Label));
        resolutionBox.SelectedIndex = 0;
        ComboBox fpsBox = MakeCombo(FrameRates.Select(f => f.Label));
        fpsBox.SelectedIndex = 0;

        // Constant-quality sub-panel: a CRF slider on the selected codec's own scale with a live plain-language
        // readout, so the number's visual meaning is never a mystery. Seeded to the High tier's CRF.
        var crfSlider = new Slider
        {
            Minimum = 1,
            Maximum = ExportCodecs.MaxCrfFor(ExportVideoCodec.H264),
            Value = ExportCodecs.CrfFor(ExportVideoCodec.H264, ExportQuality.High),
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var crfText = new TextBlock { Foreground = Palette.MutedTextBrush, FontSize = Typography.Body };

        // Target-bitrate sub-panel: Mbps target + optional max (VBR ceiling). An empty target uses the
        // resolution-scaled default shown as its watermark; an empty max leaves the rate uncapped.
        TextBox MakeMbpsBox() => new()
        {
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        TextBox bitrateBox = MakeMbpsBox();
        TextBox maxRateBox = MakeMbpsBox();
        maxRateBox.PlaceholderText = "nenhum";

        var resText = new TextBlock { Foreground = Palette.MutedTextBrush, FontSize = Typography.Body };
        void UpdateResText()
        {
            Resolution? sel = Resolutions[Math.Max(0, resolutionBox.SelectedIndex)].Value;
            (int sw, int sh) = sel is { } r ? (r.Width, r.Height) : (sequenceWidth, sequenceHeight);
            (int ow, int oh) = VideoExporter.ComputeExportResolution(sw, sh);
            resText.Text = ow == sw && oh == sh
                ? $"Resolução de saída: {ow}×{oh}"
                : $"Resolução de saída: {ow}×{oh}  (ampliada de {sw}×{sh} para o limite de exportação 4K)";
            // Reseed the bitrate watermark to the output size's recommended default ("Same as sequence" frame rate
            // seeds at the 30 fps baseline; the exporter recomputes with the actual rate when the box is left empty).
            Rational fps = FrameRates[Math.Max(0, fpsBox.SelectedIndex)].Value ?? new Rational(30, 1);
            bitrateBox.PlaceholderText = string.Format(
                CultureInfo.InvariantCulture, "{0:0.#} (recomendado)", ExportCodecs.DefaultTargetBitrate(ow, oh, fps) / 1_000_000.0);
        }
        UpdateResText();

        // Preset dropdown: "Custom" + the curated built-ins + the user's persisted presets. `allPresets` is the
        // combined list behind indices 1.. of the box (index 0 = Custom). It is reassigned when a preset is saved.
        var allPresets = ExportPresetStore.BuiltInAndUser(UserExportPresets.Load());
        ComboBox presetBox = MakeCombo(PresetLabels(allPresets));
        presetBox.SelectedIndex = 0; // Custom (the historical MP4/H.264 defaults below)

        // The codecs valid in the currently-selected container, mirrored so a selection index maps back to an enum.
        var videoCodecs = new List<ExportVideoCodec>();
        var audioCodecs = new List<ExportAudioCodec>();

        ExportVideoCodec SelectedVideoCodec() =>
            !IsAudioOnly() && videoBox.SelectedIndex >= 0 && videoBox.SelectedIndex < videoCodecs.Count
                ? videoCodecs[videoBox.SelectedIndex]
                : ExportVideoCodec.H264;

        void UpdateCrfText()
        {
            int crf = (int)crfSlider.Value;
            crfText.Text = $"CRF {crf} — {ExportCodecs.QualityLabel(SelectedVideoCodec(), crf)}  (menor = melhor qualidade, ficheiro maior)";
        }

        // Keeps the CRF slider on the selected codec's own scale (x264/x265 0–51, AV1/VP9 0–63). When a codec
        // change switches scale families, the old number would mean something different, so re-seed to the new
        // codec's High-tier CRF rather than keep a misleading value; a same-scale change keeps the user's setting.
        void UpdateCrfSliderScale()
        {
            int max = ExportCodecs.MaxCrfFor(SelectedVideoCodec());
            if ((int)crfSlider.Maximum != max)
            {
                crfSlider.Maximum = max;
                crfSlider.Value = ExportCodecs.CrfFor(SelectedVideoCodec(), ExportQuality.High);
            }
            UpdateCrfText();
        }

        // The video-side rows (assigned once the controls below are built); audio-only mode (PLAN.md step 44) hides
        // them. Declared here so the preset / selection handlers defined below can call UpdateAudioOnlyMode. The
        // rate-control rows are swappable within the video side (only the active mode's row shows), so audio-only
        // restore re-applies the mode split after the blanket un-hide.
        Control[]? videoOnlyControls = null;
        Control? crfRow = null, bitrateRow = null;
        void UpdateRateControlMode()
        {
            if (crfRow is null || bitrateRow is null)
                return;
            bool bitrate = rateControlBox.SelectedIndex == 1;
            crfRow.IsVisible = !bitrate && !IsAudioOnly();
            bitrateRow.IsVisible = bitrate && !IsAudioOnly();
        }
        void UpdateAudioOnlyMode()
        {
            if (videoOnlyControls is null)
                return;
            bool audio = IsAudioOnly();
            foreach (Control c in videoOnlyControls)
                c.IsVisible = !audio;
            UpdateRateControlMode();
        }

        void RepopulateCodecs()
        {
            if (IsAudioOnly())
            {
                // Audio-only: the codec is fixed by the chosen audio format, so the video/audio codec pickers are
                // cleared (and hidden by UpdateAudioOnlyMode).
                videoCodecs = [];
                audioCodecs = [];
                videoBox.ItemsSource = new List<string>();
                audioBox.ItemsSource = new List<string>();
                videoBox.SelectedIndex = -1;
                audioBox.SelectedIndex = -1;
                return;
            }
            ExportContainer container = containers[Math.Max(0, containerBox.SelectedIndex)];
            videoCodecs = [.. ExportCodecs.VideoCodecsFor(container)];
            audioCodecs = [.. ExportCodecs.AudioCodecsFor(container)];
            videoBox.ItemsSource = videoCodecs.Select(c => ExportCodecs.Video(c).DisplayName).ToList();
            audioBox.ItemsSource = audioCodecs.Select(c => ExportCodecs.Audio(c).DisplayName).ToList();
            videoBox.SelectedIndex = videoCodecs.Count > 0 ? 0 : -1;
            audioBox.SelectedIndex = audioCodecs.Count > 0 ? 0 : -1;
        }

        containerBox.SelectionChanged += (_, _) => RepopulateCodecs();
        containerBox.SelectedIndex = 0;
        RepopulateCodecs(); // ensure populated even though setting index 0 (already 0) fires no change
        UpdateCrfSliderScale(); // reflect the initial codec (H.264) in the slider's range + readout

        // Applying a preset drives the controls programmatically; the guard stops that from snapping the preset box
        // back to "Custom" (which any *manual* control change does, below).
        bool applyingPreset = false;

        void ApplyPreset(ExportPreset p)
        {
            applyingPreset = true;
            if (p.AudioFormat is { } af)
            {
                // Audio-only preset (PLAN.md step 44): select its Format-list entry; the video controls hide.
                containerBox.SelectedIndex = videoFormatCount + (int)af;
                RepopulateCodecs();
            }
            else
            {
                containerBox.SelectedIndex = Math.Max(0, Array.IndexOf(containers, p.Format.Container));
                RepopulateCodecs(); // container may not have changed (no event), so refresh the codec lists explicitly
                int vi = videoCodecs.IndexOf(p.Format.VideoCodec);
                if (vi >= 0) videoBox.SelectedIndex = vi;
                int ai = audioCodecs.IndexOf(p.Format.AudioCodec);
                if (ai >= 0) audioBox.SelectedIndex = ai;
                rateControlBox.SelectedIndex = p.RateControl == ExportRateControl.Bitrate ? 1 : 0;
                UpdateCrfSliderScale(); // range for the preset's codec before seeding the value
                crfSlider.Value = p.Crf > 0
                    ? Math.Clamp(p.Crf, (int)crfSlider.Minimum, (int)crfSlider.Maximum)
                    : ExportCodecs.CrfFor(p.Format.VideoCodec, p.Quality);
                bitrateBox.Text = p.VideoBitRate > 0
                    ? (p.VideoBitRate / 1_000_000.0).ToString("0.##", CultureInfo.InvariantCulture) : "";
                maxRateBox.Text = p.MaxBitRate > 0
                    ? (p.MaxBitRate / 1_000_000.0).ToString("0.##", CultureInfo.InvariantCulture) : "";
                resolutionBox.SelectedIndex = Math.Max(0, IndexOfResolution(p.Resolution));
                fpsBox.SelectedIndex = Math.Max(0, IndexOfFrameRate(p.FrameRate));
                modeBox.SelectedIndex = 0; // unlock Encoding before applying the preset's choice
                encodingBox.SelectedIndex = p.Acceleration == ExportAcceleration.Hardware ? 1 : 0;
                modeBox.SelectedIndex = p.Mode == ExportMode.Fast ? 1 : 0;
            }
            applyingPreset = false;
            UpdateAudioOnlyMode();
            UpdateResText();
        }

        presetBox.SelectionChanged += (_, _) =>
        {
            if (applyingPreset) return;
            int idx = presetBox.SelectedIndex;
            if (idx >= 1 && idx - 1 < allPresets.Count)
                ApplyPreset(allPresets[idx - 1]);
        };

        // Any manual edit means the selection no longer matches the chosen preset — fall back to "Custom".
        void SnapToCustom(object? _, EventArgs __) { if (!applyingPreset) presetBox.SelectedIndex = 0; }
        containerBox.SelectionChanged += SnapToCustom;
        videoBox.SelectionChanged += SnapToCustom;
        audioBox.SelectionChanged += SnapToCustom;
        rateControlBox.SelectionChanged += SnapToCustom;
        crfSlider.ValueChanged += SnapToCustom;
        bitrateBox.TextChanged += SnapToCustom;
        maxRateBox.TextChanged += SnapToCustom;
        resolutionBox.SelectionChanged += SnapToCustom;
        fpsBox.SelectionChanged += SnapToCustom;
        encodingBox.SelectionChanged += SnapToCustom;
        modeBox.SelectionChanged += SnapToCustom;
        rateControlBox.SelectionChanged += (_, _) => UpdateRateControlMode();
        crfSlider.ValueChanged += (_, _) => UpdateCrfText();
        videoBox.SelectionChanged += (_, _) => UpdateCrfSliderScale();
        resolutionBox.SelectionChanged += (_, _) => UpdateResText();
        fpsBox.SelectionChanged += (_, _) => UpdateResText(); // the frame rate feeds the recommended-bitrate seed

        // Burn-ins & handles (PLAN.md step 29). Burn-ins are opt-in overlays baked onto the export (timecode /
        // clip name / watermark) with a nine-point position each; handles add extra frames around an in-out range
        // for review / conform outputs. Defaults keep the pre-step-29 behaviour (no burn-ins, no handles).
        BurnInPosition[] positions = Enum.GetValues<BurnInPosition>();
        var tcCheck = MakeCheck("Código de tempo");
        ComboBox tcPos = MakePositionCombo((int)BurnInPosition.BottomCenter);
        var nameCheck = MakeCheck("Nome do clipe");
        ComboBox namePos = MakePositionCombo((int)BurnInPosition.TopLeft);
        var watermarkBox = new TextBox
        {
            PlaceholderText = "Texto da marca de água…",
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ComboBox watermarkPos = MakePositionCombo((int)BurnInPosition.BottomRight);
        var handlesBox = new TextBox
        {
            Text = "0",
            Width = 70,
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
        };

        // Log-media color handling (PLAN.md step 37): baked (default — deliverables match the preview) or
        // pass-through so a log source stays log-encoded for downstream grading. Per-export, not preset state.
        var bakeColorCheck = MakeCheck("Incorporar transformação de cor de entrada (log → Rec.709)");
        bakeColorCheck.IsChecked = true;

        // Container metadata tags (PLAN.md step 38), prefilled from the user-settings defaults set in
        // Edit ▸ Preferences. Per-export values, not preset state (they describe the work, not the format).
        // The stored defaults may carry {token} placeholders (© {year} {username}, {project}); resolve them
        // to concrete text against the live user/year/project so the boxes show final, editable values.
        UserSettings metaDefaults = UserSettingsFile.Load();
        var tokenCtx = new MetadataTokenContext(
            Environment.UserName,
            DateTime.Now.Year,
            projectName,
            DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        TextBox metaTitle = MakeMetaBox(MetadataTokens.Resolve(metaDefaults.ExportTitle, tokenCtx));
        TextBox metaAuthor = MakeMetaBox(MetadataTokens.Resolve(metaDefaults.ExportAuthor, tokenCtx));
        TextBox metaCopyright = MakeMetaBox(MetadataTokens.Resolve(metaDefaults.ExportCopyright, tokenCtx));
        TextBox metaComment = MakeMetaBox(MetadataTokens.Resolve(metaDefaults.ExportComment, tokenCtx));

        var savePreset = new Button
        {
            Content = "Guardar predefinição…",
            Padding = new Thickness(10, 4),
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var export = new Button
        {
            Content = "Exportar…",
            Padding = new Thickness(16, 5),
            Foreground = Brushes.White,
            Background = Palette.AccentBrush,
            CornerRadius = new CornerRadius(5),
        };
        var cancel = new Button
        {
            Content = "Cancelar",
            Padding = new Thickness(16, 5),
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            CornerRadius = new CornerRadius(5),
        };

        // Video-side rows, captured so audio-only mode (PLAN.md step 44) can hide them (the master mix has no video
        // codec, resolution, frame rate, burn-ins, or color-transform choice).
        Control modeRow = LabeledRow("Modo", new StackPanel { Spacing = 2, Children = { modeBox, modeText } });
        Control codecRow = TwoColumnRow("Codec de vídeo", videoBox, "Codec de áudio", audioBox);
        Control rateRow = TwoColumnRow("Controlo de taxa", rateControlBox, "Codificação", encodingBox);
        Control crfRowControl = LabeledRow("Qualidade", new StackPanel { Spacing = 2, Children = { crfSlider, crfText } });
        Control bitrateRowControl = TwoColumnRow("Destino (Mbps)", bitrateBox, "Máx. (Mbps)", maxRateBox);
        crfRow = crfRowControl;
        bitrateRow = bitrateRowControl;
        Control resFpsRow = TwoColumnRow("Resolução", resolutionBox, "Cadência de fotogramas", fpsBox);
        Control burnHeader = new TextBlock { Text = "Sobreposições fixas", Foreground = Palette.MutedTextBrush, FontSize = Typography.Body, Margin = new Thickness(0, 8, 0, 0) };
        Control tcRow = BurnInRow(tcCheck, tcPos);
        Control nameRow = BurnInRow(nameCheck, namePos);
        Control watermarkRow = BurnInRow(watermarkBox, watermarkPos);
        Control handlesRow = LabeledRow("Margens (fotogramas antes / depois do intervalo)", handlesBox);
        Control colorHeader = new TextBlock { Text = "Cor", Foreground = Palette.MutedTextBrush, FontSize = Typography.Body, Margin = new Thickness(0, 8, 0, 0) };
        videoOnlyControls =
            [modeRow, codecRow, rateRow, crfRowControl, bitrateRowControl, resFpsRow, resText, burnHeader, tcRow, nameRow, watermarkRow, handlesRow, colorHeader, bakeColorCheck];

        var settings = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                LabeledRow("Predefinição", BurnInRow(presetBox, savePreset)),
                LabeledRow("Formato", containerBox),
                LabeledRow("Intervalo", rangeBox),
                modeRow,
                codecRow,
                rateRow,
                crfRowControl,
                bitrateRowControl,
                resFpsRow,
                resText,
                burnHeader,
                tcRow,
                nameRow,
                watermarkRow,
                handlesRow,
                colorHeader,
                bakeColorCheck,
                new TextBlock { Text = "Metadados", Foreground = Palette.MutedTextBrush, FontSize = Typography.Body, Margin = new Thickness(0, 8, 0, 0) },
                TwoColumnRow("Título", metaTitle, "Autor", metaAuthor),
                TwoColumnRow("Direitos de autor", metaCopyright, "Comentário", metaComment),
            },
        };
        UpdateAudioOnlyMode(); // reflect the initial (video) selection
        containerBox.SelectionChanged += (_, _) => UpdateAudioOnlyMode();

        var dialog = new Window
        {
            Title = "Definições de exportação",
            Icon = AppIcon.Window,
            Width = 500,
            Height = 680,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.WindowBgBrush,
            FontSize = Typography.Body,
            Content = new DockPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new StackPanel
                    {
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 16, 0, 0),
                        Children = { cancel, export },
                    },
                    new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        // Fluent's ScrollViewer overlays the vertical scrollbar on top of the content column
                        // (AllowAutoHide's default) rather than reserving space for it, so the full-width
                        // hover/drag thumb would otherwise sit directly on top of the right-column controls
                        // below (audio codec, encoding, frame rate, burn-in position pickers) — this padding
                        // reserves it a lane of its own, clear of everything the form draws.
                        Padding = new Thickness(0, 0, 12, 0),
                        Content = settings,
                    },
                },
            },
        };

        // Whether the selection is complete enough to export / save: an audio-only format is complete on its own;
        // a video container needs a valid video + audio codec triple.
        bool SelectionComplete() =>
            IsAudioOnly() || (containerBox.SelectedIndex >= 0 && videoBox.SelectedIndex >= 0 && audioBox.SelectedIndex >= 0);

        ExportMode SelectedMode() => modeBox.SelectedIndex == 1 ? ExportMode.Fast : ExportMode.Final;

        // The user's own Encoding choice — the one Fast Export's hardware lock hides — so a Fast preset or export
        // records what Final Export should use when switched back (the exporter forces hardware for Fast anyway).
        ExportAcceleration SelectedAcceleration() =>
            (SelectedMode() == ExportMode.Fast ? encodingBeforeFast : encodingBox.SelectedIndex) == 1
                ? ExportAcceleration.Hardware
                : ExportAcceleration.Software;

        ExportFormat BuildFormat() => new(
            containers[containerBox.SelectedIndex],
            videoCodecs[videoBox.SelectedIndex],
            audioCodecs[audioBox.SelectedIndex]);

        // The rate-control state as (mode, crf, target, max) in the model's units. An unparsable / empty Mbps box
        // means "use the recommended default" (0), matching its watermark.
        (ExportRateControl Mode, int Crf, long BitRate, long MaxRate) BuildRateControl()
        {
            static long ParseMbps(TextBox box) =>
                double.TryParse((box.Text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double mbps) && mbps > 0
                    ? (long)Math.Round(mbps * 1_000_000)
                    : 0;
            return rateControlBox.SelectedIndex == 1
                ? (ExportRateControl.Bitrate, 0, ParseMbps(bitrateBox), ParseMbps(maxRateBox))
                : (ExportRateControl.Quality, (int)crfSlider.Value, 0, 0);
        }

        export.Click += (_, _) =>
        {
            if (!SelectionComplete())
                return;

            bool useInOut = rangeBox.SelectedIndex == 1;

            // Audio-only delivery (PLAN.md step 44): no video-side options, just the audio format + metadata.
            if (IsAudioOnly())
            {
                dialog.Close(new Result(new ExportOptions(
                    AudioFormat: SelectedAudioFormat(),
                    MetaTitle: metaTitle.Text,
                    MetaAuthor: metaAuthor.Text,
                    MetaCopyright: metaCopyright.Text,
                    MetaComment: metaComment.Text), useInOut));
                return;
            }

            var burnIns = new List<BurnIn>();
            if (tcCheck.IsChecked == true)
                burnIns.Add(new BurnIn(BurnInField.Timecode, positions[Math.Max(0, tcPos.SelectedIndex)]));
            if (nameCheck.IsChecked == true)
                burnIns.Add(new BurnIn(BurnInField.ClipName, positions[Math.Max(0, namePos.SelectedIndex)]));
            string watermark = (watermarkBox.Text ?? string.Empty).Trim();
            if (watermark.Length > 0)
                burnIns.Add(new BurnIn(BurnInField.Text, positions[Math.Max(0, watermarkPos.SelectedIndex)], watermark));

            int handles = 0;
            if (int.TryParse((handlesBox.Text ?? string.Empty).Trim(), out int parsed))
                handles = Math.Max(0, parsed);

            (ExportRateControl rateMode, int crf, long bitRate, long maxRate) = BuildRateControl();
            dialog.Close(new Result(new ExportOptions(
                Format: BuildFormat(),
                RateControl: rateMode,
                Crf: crf,
                VideoBitRate: bitRate,
                MaxBitRate: maxRate,
                HandleFrames: handles,
                BurnIns: burnIns.Count > 0 ? burnIns : null,
                Resolution: Resolutions[Math.Max(0, resolutionBox.SelectedIndex)].Value,
                FrameRate: FrameRates[Math.Max(0, fpsBox.SelectedIndex)].Value,
                Acceleration: SelectedAcceleration(),
                BakeColorTransform: bakeColorCheck.IsChecked != false,
                MetaTitle: metaTitle.Text,
                MetaAuthor: metaAuthor.Text,
                MetaCopyright: metaCopyright.Text,
                MetaComment: metaComment.Text,
                Mode: SelectedMode()), useInOut));
        };
        cancel.Click += (_, _) => dialog.Close((Result?)null);

        // Save Preset… captures the current format / quality / resolution / frame-rate under a user-given name and
        // persists it (burn-ins and handles are per-export review options, not part of a delivery preset). A repeated
        // name replaces the existing user preset. The dropdown then refreshes with the new preset selected.
        savePreset.Click += async (_, _) =>
        {
            if (!SelectionComplete())
                return;
            if (await PromptForPresetName(dialog) is not { } name)
                return;

            (ExportRateControl rateMode, int crf, long bitRate, long maxRate) = BuildRateControl();
            ExportPreset preset = IsAudioOnly()
                ? new ExportPreset(name, default, ExportQuality.High, AudioFormat: SelectedAudioFormat())
                : new ExportPreset(
                    name,
                    BuildFormat(),
                    ExportQuality.High, // the explicit Crf / bit rate below carries the quality; the tier is unused
                    Resolutions[Math.Max(0, resolutionBox.SelectedIndex)].Value,
                    FrameRates[Math.Max(0, fpsBox.SelectedIndex)].Value,
                    RateControl: rateMode,
                    Crf: crf,
                    VideoBitRate: bitRate,
                    MaxBitRate: maxRate,
                    Acceleration: SelectedAcceleration(),
                    Mode: SelectedMode());

            var user = UserExportPresets.Load()
                .Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            user.Add(preset);
            UserExportPresets.Save(user);

            allPresets = ExportPresetStore.BuiltInAndUser(user);
            presetBox.ItemsSource = PresetLabels(allPresets);
            applyingPreset = true;
            presetBox.SelectedIndex = allPresets.Count; // the just-saved preset is last (index 0 is Custom)
            applyingPreset = false;
        };

        return dialog.ShowDialog<Result?>(owner);
    }

    private static ComboBox MakeCombo(IEnumerable<string> items) => new()
    {
        ItemsSource = items.ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Foreground = Palette.TextBrush,
        Background = Palette.PanelBgBrush,
        FontSize = Typography.Body,
    };

    /// <summary>The preset-dropdown labels: "Custom" followed by each preset's name.</summary>
    private static List<string> PresetLabels(IReadOnlyList<ExportPreset> presets) =>
        ["Personalizado", .. presets.Select(p => p.Name)];

    /// <summary>Index of <paramref name="value"/> in the resolution dropdown (0 = "Same as sequence"); -1 if a
    /// hand-edited preset carries a resolution not offered here.</summary>
    private static int IndexOfResolution(Resolution? value) =>
        Array.FindIndex(Resolutions, r => r.Value.Equals(value));

    /// <summary>Index of <paramref name="value"/> in the frame-rate dropdown (0 = "Same as sequence"); -1 if a
    /// hand-edited preset carries a rate not offered here.</summary>
    private static int IndexOfFrameRate(Rational? value) =>
        Array.FindIndex(FrameRates, f => f.Value.Equals(value));

    /// <summary>Prompts for an export preset name (see <see cref="NamePromptDialog"/>).</summary>
    private static Task<string?> PromptForPresetName(Window owner) =>
        NamePromptDialog.Show(owner, "Guardar predefinição de exportação", "Nome da predefinição", "ex.: YouTube 1080p");

    /// <summary>A nine-point burn-in position picker, preselected to <paramref name="defaultIndex"/>.</summary>
    private static ComboBox MakePositionCombo(int defaultIndex)
    {
        ComboBox combo = MakeCombo(
        [
            "Topo esquerdo", "Topo centro", "Topo direito",
            "Meio esquerdo", "Centro", "Meio direito",
            "Base esquerda", "Base centro", "Base direita",
        ]);
        combo.Width = 130;
        combo.HorizontalAlignment = HorizontalAlignment.Right;
        combo.SelectedIndex = defaultIndex;
        return combo;
    }

    /// <summary>A metadata text field prefilled with its user-settings default (PLAN.md step 38).</summary>
    private static TextBox MakeMetaBox(string value) => new()
    {
        Text = value,
        Foreground = Palette.TextBrush,
        Background = Palette.PanelBgBrush,
        FontSize = Typography.Body,
    };

    private static CheckBox MakeCheck(string label) => new()
    {
        Content = label,
        Foreground = Palette.TextBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A burn-in row: the enable control (checkbox or watermark textbox) on the left, its position picker
    /// pinned right.</summary>
    private static Grid BurnInRow(Control enable, Control position)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        enable.SetValue(Grid.ColumnProperty, 0);
        position.SetValue(Grid.ColumnProperty, 1);
        enable.Margin = new Thickness(0, 0, 8, 0);
        grid.Children.Add(enable);
        grid.Children.Add(position);
        return grid;
    }

    private static StackPanel LabeledRow(string label, Control control) => new()
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, Foreground = Palette.MutedTextBrush, FontSize = Typography.Body },
            control,
        },
    };

    /// <summary>Two labeled controls side by side, each taking half the width with a gap between — for naturally
    /// paired fields (video/audio codec, resolution/frame-rate) so the form stays compact and organised.</summary>
    private static Grid TwoColumnRow(string leftLabel, Control leftControl, string rightLabel, Control rightControl)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        StackPanel left = LabeledRow(leftLabel, leftControl);
        StackPanel right = LabeledRow(rightLabel, rightControl);
        left.SetValue(Grid.ColumnProperty, 0);
        right.SetValue(Grid.ColumnProperty, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }
}

/// <summary>
/// A modal progress dialog for an in-flight export: a determinate bar driven from the export's
/// <c>IProgress&lt;double&gt;</c> and a Cancel button that signals the supplied
/// <see cref="CancellationTokenSource"/>. While it is shown the shell is input-blocked, so no second
/// libav* pipeline can start. The dialog stays up (showing "Cancelling…") until the export actually
/// stops; only <see cref="CompleteAndClose"/> dismisses it — the window-chrome close button is treated
/// as a cancel so it can never orphan a running export.
/// </summary>
internal sealed class ExportProgressDialog : Window
{
    private readonly ProgressBar _bar;
    private readonly TextBlock _percent;
    private readonly Button _cancel;
    private readonly CancellationTokenSource _cts;
    private bool _allowClose;

    public ExportProgressDialog(string fileName, CancellationTokenSource cts)
    {
        _cts = cts;

        Title = "A exportar";
        Icon = AppIcon.Window;
        Width = 440;
        Height = 160;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.WindowBgBrush;
        FontSize = Typography.Body;

        _bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 16 };
        _percent = new TextBlock
        {
            Text = "0%",
            Foreground = Palette.MutedTextBrush,
            FontSize = Typography.Body,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _cancel = new Button
        {
            Content = "Cancelar",
            Padding = new Thickness(16, 5),
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            CornerRadius = new CornerRadius(5),
        };
        _cancel.Click += (_, _) => RequestCancel();

        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        _cancel.SetValue(DockPanel.DockProperty, Dock.Right);
        bottom.Children.Add(_cancel);
        bottom.Children.Add(_percent);

        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = $"A exportar {fileName}…",
                    Foreground = Palette.TextBrush,
                    FontSize = Typography.Body,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                _bar,
                bottom,
            },
        };
    }

    /// <summary>Updates the bar from an export progress fraction (0–1). Called on the UI thread by the
    /// caller's <c>Progress&lt;double&gt;</c>, which captured this thread's context at construction.</summary>
    public void SetProgress(double fraction)
    {
        int pct = (int)Math.Clamp(fraction * 100, 0, 100);
        _bar.Value = pct;
        if (!_cts.IsCancellationRequested)
            _percent.Text = $"{pct}%";
    }

    /// <summary>Dismisses the dialog once the export has finished — the only sanctioned way to close it.</summary>
    public void CompleteAndClose()
    {
        _allowClose = true;
        Close();
    }

    private void RequestCancel()
    {
        _cancel.IsEnabled = false;
        _percent.Text = "A cancelar…";
        _cts.Cancel();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The title-bar close button must not orphan a running export: treat it as Cancel and keep the
        // dialog up until the export actually stops (CompleteAndClose then dismisses it).
        if (!_allowClose)
        {
            e.Cancel = true;
            if (!_cts.IsCancellationRequested)
                RequestCancel();
        }
        base.OnClosing(e);
    }
}
