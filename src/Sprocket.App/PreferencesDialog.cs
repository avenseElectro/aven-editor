using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace Sprocket.App;

/// <summary>
/// The application Preferences dialog (PLAN.md step 38) — the user-scoped settings surface, distinct from
/// per-sequence <c>SequenceSettingsDialog</c> and the per-project <c>ProjectSettings</c>. Code-built against
/// the shared dark <see cref="Palette"/> like the other modals in <c>Dialogs.cs</c> (its own file — Dialogs.cs
/// is already large). Look/behaviour rests on manual verification; the decision logic (parsing, clamping,
/// byte formatting, token resolution, the MCP setup command) lives in the tested <see cref="PreferencesFormat"/>
/// and <see cref="UserSettingsStore"/>.
///
/// Cache "Clear" buttons act immediately (after a confirm) rather than on OK — the standard NLE behavior for
/// cache management — so Cancel does not undo them.
/// </summary>
internal static class PreferencesDialog
{
    /// <summary>
    /// Shows the dialog over <paramref name="current"/>. The cache callbacks report/clear the proxy cache
    /// (global) and the render cache (the current session's store — render files live beside the project).
    /// Returns the clamped new settings on OK, or <see langword="null"/> on cancel.
    /// </summary>
    public static Task<UserSettings?> Show(Window owner, UserSettings current,
        Func<long> proxyCacheSize, Func<int> clearProxyCache,
        Func<long> renderCacheSize, Action clearRenderCache,
        Func<long> analysisCacheSize, Action clearAnalysisCache,
        IReadOnlyList<string> audioDevices)
    {
        // ── Audio output device ─────────────────────────────────────────────────────────────────
        // "System Default" ("" specifier) plus the enumerated devices. Keep the persisted device selectable even
        // when it isn't currently enumerated (unplugged) so leaving the dialog doesn't silently discard the choice.
        var deviceLabels = new List<string> { "Predefinição do sistema" };
        var deviceSpecs = new List<string> { "" };
        foreach (string d in audioDevices)
        {
            deviceLabels.Add(d);
            deviceSpecs.Add(d);
        }
        if (current.AudioOutputDevice.Length > 0 && !deviceSpecs.Contains(current.AudioOutputDevice))
        {
            deviceLabels.Add($"{current.AudioOutputDevice} (não ligado)");
            deviceSpecs.Add(current.AudioOutputDevice);
        }
        var audioDevice = new ComboBox
        {
            ItemsSource = deviceLabels,
            SelectedIndex = Math.Max(0, deviceSpecs.IndexOf(current.AudioOutputDevice)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            FontSize = Typography.Body,
        };

        // ── Caches ──────────────────────────────────────────────────────────────────────────────
        var proxySizeText = Muted(PreferencesFormat.Bytes(proxyCacheSize()));
        var renderSizeText = Muted(PreferencesFormat.Bytes(renderCacheSize()));
        var analysisSizeText = Muted(PreferencesFormat.Bytes(analysisCacheSize()));
        Button clearProxy = SmallButton("Limpar");
        Button clearRender = SmallButton("Limpar");
        Button clearAnalysis = SmallButton("Limpar");

        // ── Export metadata defaults ────────────────────────────────────────────────────────────
        TextBox title = MakeText(current.ExportTitle);
        TextBox author = MakeText(current.ExportAuthor);
        TextBox copyright = MakeText(current.ExportCopyright);
        TextBox comment = MakeText(current.ExportComment);

        // ── Autosave ────────────────────────────────────────────────────────────────────────────
        TextBox autosave = MakeText(current.AutosaveIntervalSeconds.ToString());
        autosave.Width = 80;
        autosave.HorizontalAlignment = HorizontalAlignment.Left;

        // ── Import (PLAN.md step 42) ──────────────────────────────────────────────────────────────
        TextBox stillDuration = MakeText(
            current.StillImageDefaultSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        stillDuration.Width = 80;
        stillDuration.HorizontalAlignment = HorizontalAlignment.Left;

        // ── Updates (PLAN.md steps 36 + 45) ─────────────────────────────────────────────────────
        var updatesEnabled = new CheckBox
        {
            Content = "Procurar atualizações no arranque",
            IsChecked = current.UpdateCheckEnabled,
            Foreground = Palette.TextBrush,
        };
        var updatesNote = new TextBlock
        {
            Text = "O Sprocket avisa-o quando é publicada uma nova versão; nada é transferido nem instalado " +
                   "sem a sua autorização. As versões portáteis são encaminhadas para a página de lançamentos.",
            Foreground = Palette.MutedTextBrush,
            FontSize = Typography.Caption,
            TextWrapping = TextWrapping.Wrap,
        };

        // ── AI control (MCP) ────────────────────────────────────────────────────────────────────
        string token = current.McpToken;
        var mcpEnabled = new CheckBox
        {
            Content = "Ativar servidor MCP (apenas loopback)",
            IsChecked = current.McpEnabled,
            Foreground = Palette.TextBrush,
        };
        TextBox port = MakeText(current.McpPort.ToString());
        port.Width = 80;
        port.HorizontalAlignment = HorizontalAlignment.Left;
        var requireToken = new CheckBox
        {
            Content = "Exigir token bearer",
            IsChecked = current.McpRequireToken,
            Foreground = Palette.TextBrush,
        };
        var tokenText = new TextBox
        {
            Text = token,
            IsReadOnly = true,
            FontFamily = Typography.Mono,
            FontSize = Typography.Caption,
            Foreground = Palette.MutedTextBrush,
            Background = Palette.PanelBgBrush,
        };
        Button copySetup = SmallButton("Copiar comando de configuração");
        var mcpWarning = new TextBlock
        {
            Text = "Enquanto ativado, um cliente de IA local pode inspecionar e editar o projeto aberto.",
            Foreground = Palette.MutedTextBrush,
            FontSize = Typography.Caption,
            TextWrapping = TextWrapping.Wrap,
        };

        void RefreshMcpControls()
        {
            bool on = mcpEnabled.IsChecked == true;
            bool tokenOn = requireToken.IsChecked == true;
            port.IsEnabled = on;
            requireToken.IsEnabled = on;
            tokenText.IsVisible = on && tokenOn;
            copySetup.IsEnabled = on;
        }

        requireToken.IsCheckedChanged += (_, _) =>
        {
            token = PreferencesFormat.ResolveToken(requireToken.IsChecked == true, token, UserSettingsStore.NewToken);
            tokenText.Text = token;
            RefreshMcpControls();
        };
        mcpEnabled.IsCheckedChanged += (_, _) => RefreshMcpControls();
        RefreshMcpControls();

        // ── Buttons ─────────────────────────────────────────────────────────────────────────────
        var ok = new Button
        {
            Content = "OK",
            Padding = new Thickness(18, 5),
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
            Title = "Preferências",
            Icon = AppIcon.Window,
            Width = 474,
            Height = 640,
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
                    new ScrollViewer
                    {
                        // Keep content clear of the overlay scrollbar (§ STYLE_GUIDE surface rules).
                        Padding = new Thickness(0, 0, 14, 0),
                        Content = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                Section("Caches"),
                                CacheRow("Cache de proxy", proxySizeText, clearProxy),
                                CacheRow("Cache de processamento (este projeto)", renderSizeText, clearRender),
                                CacheRow("Cache de análise de estabilização", analysisSizeText, clearAnalysis),

                                Section("Predefinições de metadados de exportação"),
                                Labeled("Título", title),
                                Labeled("Autor", author),
                                Labeled("Direitos de autor", copyright),
                                Labeled("Comentário", comment),
                                Hint("Os tokens {project}, {username}, {year} e {date} são substituídos ao exportar. "
                                    + "Se deixar o Comentário em branco, todas as exportações continuam a incluir "
                                    + "“Criado com o Sprocket” mais a versão do Sprocket."),

                                Section("Gravação automática"),
                                Labeled($"Intervalo (segundos, {UserSettingsStore.MinAutosaveSeconds}–{UserSettingsStore.MaxAutosaveSeconds})", autosave),

                                Section("Importar"),
                                Labeled("Duração predefinida de imagens fixas (segundos)", stillDuration),

                                Section("Áudio"),
                                Labeled("Dispositivo de saída", audioDevice),
                                Hint("Escolha onde a reprodução é monitorizada. Uma alteração aplica-se de imediato "
                                    + "sem interromper a reprodução; se o dispositivo escolhido ficar indisponível "
                                    + "mais tarde, o Sprocket usa a predefinição do sistema."),

                                Section("Atualizações"),
                                updatesEnabled,
                                updatesNote,

                                Section("Controlo de IA (MCP)"),
                                mcpEnabled,
                                Labeled("Porta", port),
                                requireToken,
                                tokenText,
                                copySetup,
                                mcpWarning,
                            },
                        },
                    },
                },
            },
        };

        clearProxy.Click += async (_, _) =>
        {
            long size = proxyCacheSize();
            bool confirmed = await ConfirmDialog.Show(dialog, "Limpar Cache de Proxy",
                $"Eliminar todos os ficheiros proxy em cache?\n\nAtualmente a usar {PreferencesFormat.Bytes(size)}. " +
                "Os proxies são regenerados a pedido; os projetos abertos podem voltar a pôr construções de proxy na fila.", "Limpar", "Cancelar");
            if (!confirmed)
                return;
            clearProxyCache();
            proxySizeText.Text = PreferencesFormat.Bytes(proxyCacheSize());
        };

        clearAnalysis.Click += async (_, _) =>
        {
            long size = analysisCacheSize();
            bool confirmed = await ConfirmDialog.Show(dialog, "Limpar Cache de Análise",
                $"Eliminar todos os traçados de movimento de estabilização em cache?\n\nAtualmente a usar {PreferencesFormat.Bytes(size)}. " +
                "Os clipes estabilizados voltam a ser analisados a pedido; os projetos abertos podem voltar a pôr análises na fila.", "Limpar", "Cancelar");
            if (!confirmed)
                return;
            clearAnalysisCache();
            analysisSizeText.Text = PreferencesFormat.Bytes(analysisCacheSize());
        };

        clearRender.Click += async (_, _) =>
        {
            long size = renderCacheSize();
            bool confirmed = await ConfirmDialog.Show(dialog, "Eliminar Ficheiros de Processamento",
                $"Eliminar os ficheiros de processamento de pré-visualização deste projeto?\n\nAtualmente a usar {PreferencesFormat.Bytes(size)}. " +
                "As pré-visualizações processadas são reconstruídas quando voltar a processar.", "Eliminar", "Cancelar");
            if (!confirmed)
                return;
            clearRenderCache();
            renderSizeText.Text = PreferencesFormat.Bytes(renderCacheSize());
        };

        copySetup.Click += async (_, _) =>
        {
            token = PreferencesFormat.ResolveToken(requireToken.IsChecked == true, token, UserSettingsStore.NewToken);
            tokenText.Text = token;
            string command = PreferencesFormat.McpSetupCommand(
                PreferencesFormat.ParsePort(port.Text) is { } p ? Math.Clamp(p, UserSettingsStore.MinPort, UserSettingsStore.MaxPort) : current.McpPort,
                requireToken.IsChecked == true, token);
            if (TopLevel.GetTopLevel(dialog)?.Clipboard is { } clipboard)
                await clipboard.SetValueAsync(DataFormat.Text, command);
        };

        ok.Click += (_, _) => dialog.Close(UserSettingsStore.Clamp(current with
        {
            ExportTitle = title.Text ?? "",
            ExportAuthor = author.Text ?? "",
            ExportCopyright = copyright.Text ?? "",
            ExportComment = comment.Text ?? "",
            AutosaveIntervalSeconds = PreferencesFormat.ParseSeconds(autosave.Text) ?? current.AutosaveIntervalSeconds,
            StillImageDefaultSeconds =
                double.TryParse(stillDuration.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double s)
                    ? s
                    : current.StillImageDefaultSeconds,
            McpEnabled = mcpEnabled.IsChecked == true,
            McpPort = PreferencesFormat.ParsePort(port.Text) ?? current.McpPort,
            McpRequireToken = requireToken.IsChecked == true,
            McpToken = token,
            UpdateCheckEnabled = updatesEnabled.IsChecked == true,
            AudioOutputDevice = deviceSpecs[audioDevice.SelectedIndex >= 0 ? audioDevice.SelectedIndex : 0],
        }));
        cancel.Click += (_, _) => dialog.Close((UserSettings?)null);

        return dialog.ShowDialog<UserSettings?>(owner);
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        Foreground = Palette.TextBrush,
        FontWeight = FontWeight.SemiBold,
        FontSize = Typography.Emphasis,
        Margin = new Thickness(0, 10, 0, 2),
    };

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        Foreground = Palette.MutedTextBrush,
        FontSize = Typography.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A wrapping muted caption under a settings section (e.g. the metadata-token legend).</summary>
    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        Foreground = Palette.MutedTextBrush,
        FontSize = Typography.Caption,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 0),
    };

    private static Button SmallButton(string label) => new()
    {
        Content = label,
        Padding = new Thickness(12, 3),
        FontSize = Typography.Body,
        Foreground = Palette.TextBrush,
        Background = Palette.PanelBgBrush,
        CornerRadius = new CornerRadius(5),
    };

    private static TextBox MakeText(string value) => new()
    {
        Text = value,
        Foreground = Palette.TextBrush,
        Background = Palette.PanelBgBrush,
        FontSize = Typography.Body,
    };

    private static StackPanel Labeled(string label, Control control) => new()
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, Foreground = Palette.MutedTextBrush, FontSize = Typography.Body },
            control,
        },
    };

    /// <summary>"Proxy cache   1.2 GB   [Clear]" — label left, size + action pinned right.</summary>
    private static Grid CacheRow(string label, TextBlock size, Button clear)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        var name = new TextBlock
        {
            Text = label,
            Foreground = Palette.TextBrush,
            FontSize = Typography.Body,
            VerticalAlignment = VerticalAlignment.Center,
        };
        size.Margin = new Thickness(0, 0, 10, 0);
        name.SetValue(Grid.ColumnProperty, 0);
        size.SetValue(Grid.ColumnProperty, 1);
        clear.SetValue(Grid.ColumnProperty, 2);
        grid.Children.Add(name);
        grid.Children.Add(size);
        grid.Children.Add(clear);
        return grid;
    }
}
