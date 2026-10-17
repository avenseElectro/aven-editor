using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Sprocket.App.Ai;

/// <summary>
/// The in-app assistant panel (Etapa D): the fixed column right of the Inspector (Ver ▸ Painel Assistente IA)
/// that owns the pedido → plano → aprovação → execução flow. Pure UI over injected seams — the controller
/// provider (the app-scoped tool host may still be starting), the live settings, and an open-Preferences
/// action — so the whole plan/approve/execute contract it renders is headlessly tested in
/// <c>AssistantControllerTests</c>. Every state change lands as PT-PT text in the log; the request box is
/// never cleared behind the user's back, so a rejected or clarified plan can be re-asked by editing it.
/// </summary>
internal sealed class AssistantPanel : UserControl
{
    private const int MaxStepChars = 80;

    private Func<AssistantController?>? _controllerFactory;
    private Func<UserSettings>? _settings;
    private Action? _openPreferences;

    private readonly TextBox _request;
    private readonly Button _ask;
    private readonly Button _approve;
    private readonly Button _replan;
    private readonly Button _cancelPlan;
    private readonly Button _preferences;
    private readonly StackPanel _thinking;
    private readonly TextBlock _thinkingText;
    private readonly StackPanel _unconfigured;
    private readonly TextBlock _unconfiguredHint;
    private readonly StackPanel _planArea;
    private readonly TextBlock _summary;
    private readonly ItemsControl _steps;
    private readonly TextBlock _log;

    private AiPlanOutcome.Plan? _plan;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _exportBlocked;

    public AssistantPanel()
    {
        _request = new TextBox
        {
            MinHeight = 68,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
            Foreground = Palette.TextBrush,
            Background = Palette.PanelBgBrush,
            FontSize = Typography.Body,
        };
        _request.PlaceholderText = "Peça uma edição em palavras, por exemplo: acrescenta um marcador a 2 segundos";
        _ask = Button("Pedir");
        _ask.Classes.Add("accent");
        _approve = Button("Aprovar e executar");
        _replan = Button("Replanear");
        _cancelPlan = Button("Cancelar");
        _preferences = Button("Abrir Preferências");
        _thinkingText = new TextBlock
        {
            Foreground = Palette.MutedTextBrush,
            FontSize = Typography.Caption,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "A pensar…",
        };
        _thinking = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                new ProgressBar { IsIndeterminate = true, Width = 130, Height = 4, Foreground = Palette.AccentBrush },
                _thinkingText,
            },
        };
        _unconfiguredHint = new TextBlock
        {
            Foreground = Palette.MutedTextBrush,
            FontSize = Typography.Body,
            TextWrapping = TextWrapping.Wrap,
        };
        _unconfigured = new StackPanel { Spacing = 8, IsVisible = false, Children = { _unconfiguredHint, _preferences } };
        _summary = new TextBlock
        {
            Foreground = Palette.TextBrush,
            FontSize = Typography.Body,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        _steps = new ItemsControl();
        _planArea = new StackPanel
        {
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                new TextBlock
                {
                    Text = "Plano proposto — nada é executado sem a sua aprovação",
                    Foreground = Palette.MutedTextBrush,
                    FontSize = Typography.Emphasis,
                    TextWrapping = TextWrapping.Wrap,
                },
                _summary,
                _steps,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { _approve, _replan, _cancelPlan },
                },
            },
        };
        _log = new TextBlock
        {
            Foreground = Palette.MutedTextBrush,
            FontSize = Typography.Caption,
            TextWrapping = TextWrapping.Wrap,
        };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(10),
                Spacing = 8,
                Children =
                {
                    _unconfigured,
                    new TextBlock { Text = "Pedido", Foreground = Palette.MutedTextBrush, FontSize = Typography.Body },
                    _request,
                    _ask,
                    _thinking,
                    _planArea,
                    _log,
                },
            },
        };

        _ask.Click += (_, _) => Ask();
        _request.KeyDown += (_, e) =>
        {
            if (e.Key is Avalonia.Input.Key.Enter && e.KeyModifiers is Avalonia.Input.KeyModifiers.None)
            {
                Ask(); // a plain Enter asks; a line break stays on Shift+Enter
                e.Handled = true;
            }
        };
        _approve.Click += (_, _) => Execute();
        _replan.Click += (_, _) => Ask();
        _cancelPlan.Click += (_, _) =>
        {
            HidePlan();
            AppendLog("Plano cancelado — nada foi alterado.");
        };
        _preferences.Click += (_, _) => _openPreferences?.Invoke();

        SetInteractivity();
    }

    /// <summary>Wires the panel to the shell. Must run before the panel is shown; until then every action
    /// is disabled (the shell builds panels before the app-scoped services exist).</summary>
    public void Initialize(Func<AssistantController?> controller, Func<UserSettings> settings, Action openPreferences)
    {
        _controllerFactory = controller;
        _settings = settings;
        _openPreferences = openPreferences;
        Refresh();
    }

    /// <summary>Re-reads the configuration (provider/key) and re-renders the unconfigured state — called on
    /// Initialize and after the Preferences dialog closes, since the key never flows through settings.</summary>
    public void Refresh()
    {
        if (_settings is null)
            return;
        UserSettings s = _settings();
        bool ready = true;
        string hint = "";
        if (s.AiProvider is not (Ai.SecretStore.Gemini or Ai.SecretStore.Glm))
        {
            ready = false;
            hint = "O assistente está desativado. Escolha um fornecedor (Gemini ou GLM) nas Preferências para o ativar.";
        }
        else if (!Ai.SecretStore.IsSupported)
        {
            ready = false;
            hint = "Guardar chaves API só é suportado no Windows — o assistente não pode memorizar a chave neste sistema.";
        }
        else if (!Ai.SecretStore.HasKey(s.AiProvider))
        {
            ready = false;
            hint = $"Falta a chave API ({ProviderName(s.AiProvider)}). Abra as Preferências e use «Guardar chave».";
        }

        _unconfiguredHint.Text = hint;
        _unconfigured.IsVisible = !ready;
        _ready = ready;
        SetInteractivity();
    }

    private bool _ready;

    /// <summary>Visual gate while an export (or a preview render) is in flight — the controller blocks the
    /// real work anyway; this keeps the buttons from promising what would be refused.</summary>
    public void SetExportBlocked(bool blocked)
    {
        _exportBlocked = blocked;
        SetInteractivity();
        if (blocked)
            AppendLog("Assistente pausado: está uma exportação em curso.");
    }

    private static string ProviderName(string provider) =>
        provider == Ai.SecretStore.Gemini ? "Google Gemini" : "GLM (Z.ai)";

    private static Button Button(string label) => new()
    {
        Content = label,
        Padding = new Thickness(12, 3),
        FontSize = Typography.Body,
        Foreground = Palette.TextBrush,
        Background = Palette.PanelBgBrush,
        CornerRadius = new CornerRadius(5),
    };

    private void SetInteractivity()
    {
        bool idle = !_busy && !_exportBlocked && _controllerFactory is not null && _ready;
        _ask.IsEnabled = idle;
        _request.IsEnabled = !_exportBlocked;
        _preferences.IsEnabled = !_busy;
        _approve.IsEnabled = idle && _plan is not null;
        _replan.IsEnabled = idle && _plan is not null;
        _cancelPlan.IsEnabled = !_busy && !_exportBlocked && _plan is not null;
    }

    private void SetBusyUi(bool busy, string message)
    {
        _thinking.IsVisible = busy;
        if (message is not null)
            _thinkingText.Text = message;
        SetInteractivity();
    }

    private void AppendLog(string message)
    {
        _log.Text = _log.Text?.Length > 0 ? $"{_log.Text}\n{message}" : message;
    }

    private void ShowPlan(AiPlanOutcome.Plan plan)
    {
        _summary.Text = plan.Summary.Length > 0 ? plan.Summary : "(sem resumo)";
        var items = new List<Control>();
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            AiPlanStep step = plan.Steps[i];
            string arguments = Truncate(JsonSerializer.Serialize(step.Arguments), MaxStepChars);
            items.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{i + 1}. {(step.Description.Length > 0 ? step.Description : step.Tool)}",
                        Foreground = Palette.TextBrush,
                        FontSize = Typography.Body,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = $"{step.Tool}({arguments})",
                        FontFamily = Typography.Mono,
                        FontSize = Typography.Caption,
                        Foreground = Palette.MutedTextBrush,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            });
        }
        _steps.ItemsSource = items;
        _planArea.IsVisible = true;
        SetInteractivity();
    }

    private void HidePlan()
    {
        _plan = null;
        _planArea.IsVisible = false;
        _steps.ItemsSource = null;
        SetInteractivity();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>Resolves the settings + key + live controller into a ready pair, or renders the PT-PT reason
    /// nothing can run yet. The key is read fresh from the DPAPI store every ask, so saving one in
    /// Preferences takes effect without a restart.</summary>
    private bool TryBegin(out IAiChatClient? client, out AssistantController? controller)
    {
        client = null;
        controller = null;
        Refresh();
        if (!_ready || _settings is null)
            return false;
        UserSettings s = _settings();
        controller = _controllerFactory?.Invoke();
        if (controller is null)
        {
            AppendLog("O assistente ainda está a arrancar — tente novamente dentro de momentos.");
            return false;
        }
        string key = Ai.SecretStore.Get(s.AiProvider) ?? "";
        string model = s.AiProvider == Ai.SecretStore.Gemini ? s.AiGeminiModel : s.AiGlmModel;
        client = AiClients.Create(s.AiProvider, key, model);
        return true;
    }

    private async void Ask()
    {
        if (_busy || !TryBegin(out IAiChatClient? client, out AssistantController? controller) || client is null)
            return;
        HidePlan();
        _busy = true;
        _cts = new CancellationTokenSource();
        SetBusyUi(true, "A pensar…");
        try
        {
            AiPlanOutcome outcome = await controller!.PlanAsync(client, _request.Text ?? "", _cts.Token);
            switch (outcome)
            {
                case AiPlanOutcome.Plan plan:
                    _plan = plan;
                    ShowPlan(plan);
                    AppendLog("Reveja cada passo antes de aprovar — a execução mostra a ferramenta e os argumentos exatos.");
                    break;
                case AiPlanOutcome.Clarify clarify:
                    AppendLog($"O assistente perguntou: {clarify.Question}");
                    AppendLog("Responda no campo de pedido e volte a pedir.");
                    break;
                case AiPlanOutcome.Error error:
                    AppendLog(error.Message);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Pedido cancelado.");
        }
        catch (Exception ex)
        {
            AppendLog($"Ocorreu um erro inesperado: {ex.Message}");
        }
        finally
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            SetBusyUi(false, "A pensar…");
        }
    }

    private async void Execute()
    {
        if (_busy || _plan is not { } plan || _controllerFactory?.Invoke() is not { } controller)
            return;
        _busy = true;
        _cts = new CancellationTokenSource();
        SetBusyUi(true, "A executar o plano…");
        try
        {
            var progress = new Progress<string>(AppendLog);
            AssistantExecution result = await controller.ExecuteAsync(plan, progress, _cts.Token);
            if (result.Ok)
            {
                HidePlan();
                AppendLog($"Plano concluído — {plan.Steps.Count} {(plan.Steps.Count == 1 ? "passo executado" : "passos executados")}. Um Ctrl+Z anula tudo.");
            }
            else
            {
                HidePlan();
                AppendLog(result.Error ?? "O plano falhou.");
            }
        }
        catch (OperationCanceledException)
        {
            HidePlan();
            AppendLog("Execução cancelada — o plano foi anulado por completo; o projeto ficou como estava.");
        }
        catch (Exception ex)
        {
            AppendLog($"Ocorreu um erro inesperado: {ex.Message}");
        }
        finally
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            SetBusyUi(false, "A executar o plano…");
        }
    }
}
