using Sprocket.Core.Commands;
using Sprocket.Mcp;

namespace Sprocket.App.Ai;

/// <summary>What one executed step did, for the panel's step-by-step log (PT-PT text, safe to show as-is).</summary>
internal sealed record AssistantStepReport(string Description, string Tool, bool Ok, string? Error);

/// <summary>
/// The outcome of executing an approved plan: every step's report, plus a PT-PT error explaining what
/// happened when the whole plan was rolled back (<see cref="Error"/> is <see langword="null"/> on success).
/// </summary>
internal sealed record AssistantExecution(
    bool Ok, string Summary, IReadOnlyList<AssistantStepReport> Steps, string? Error);

/// <summary>
/// The assistant's engine (Etapa D), UI-free: planning turns a natural-language request into a validated
/// <see cref="AiPlanOutcome"/> by sending the project state and the tool catalog to the model; execution runs
/// an approved plan's tool calls through the <see cref="InProcessToolHost"/> inside one
/// <see cref="EditHistory"/> transaction, so a single Ctrl+Z undoes the whole plan and a mid-plan failure
/// reverts everything. The export gate keeps both halves inert while an export runs. Pure orchestration over
/// injected seams — the model client, the session resolver, and the blocked gate — so it is headlessly tested.
/// </summary>
internal sealed class AssistantController(
    InProcessToolHost host, Func<IEditorSession?> session, int maxSteps, Func<bool> blocked)
{
    /// <summary>
    /// Plans for <paramref name="request"/>: reads the live project state through the tool layer, sends it with
    /// the catalog and the rules to the model, and parses the answer. Every failure mode (no session read,
    /// provider error, malformed answer) comes back as a PT-PT <see cref="AiPlanOutcome.Error"/>.
    /// </summary>
    public async Task<AiPlanOutcome> PlanAsync(IAiChatClient client, string request, CancellationToken cancellationToken = default)
    {
        if (blocked())
            return new AiPlanOutcome.Error("Uma exportação está a decorrer — aguarde que termine antes de pedir edições ao assistente.");
        if (string.IsNullOrWhiteSpace(request.Trim()))
            return new AiPlanOutcome.Error("Escreva primeiro o que quer fazer.");

        IReadOnlyList<AssistantTool> tools = await host.ListToolsAsync(cancellationToken);
        AssistantToolResult state = await host.CallToolAsync("get_project_state", cancellationToken: cancellationToken);
        if (state.IsError)
            return new AiPlanOutcome.Error($"Não foi possível ler o estado do projeto: {state.Text}");

        string output;
        try
        {
            output = await client.CompleteAsync(
                AiPlanner.BuildSystemPrompt(tools, maxSteps),
                AiPlanner.BuildUserPrompt(state.Text, request),
                cancellationToken);
        }
        catch (AiClientException ex) // already PT-PT, mapped per failure mode by the client
        {
            return new AiPlanOutcome.Error(ex.Message);
        }

        return AiPlanner.ParsePlan(output, new HashSet<string>(tools.Select(t => t.Name)), maxSteps);
    }

    /// <summary>
    /// Executes an approved plan inside one undoable transaction labelled «Assistente IA: …». A failing or
    /// throwing step cancels the transaction — every earlier step is reverted and the history is left exactly
    /// as it was — and the returned <see cref="AssistantExecution.Error"/> says so in PT-PT. Read-only plans
    /// (no model changes) commit to no undo entry at all.
    /// </summary>
    public async Task<AssistantExecution> ExecuteAsync(
        AiPlanOutcome.Plan plan, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (blocked())
            return RolledBack(plan, [], "Uma exportação está a decorrer — aguarde que termine antes de executar planos.");
        if (plan.Steps.Count == 0)
            return RolledBack(plan, [], "O plano está vazio — não há nada para executar.");
        IEditorSession? current = session();
        if (current is null)
            return RolledBack(plan, [], "Não há nenhum projeto aberto.");

        EditHistory.EditTransaction transaction = await current.OnModelThreadAsync(
            api => api.History.BeginTransaction(TransactionLabel(plan.Summary)));

        var reports = new List<AssistantStepReport>(plan.Steps.Count);
        try
        {
            for (int i = 0; i < plan.Steps.Count; i++)
            {
                AiPlanStep step = plan.Steps[i];
                progress?.Report($"A executar o passo {i + 1} de {plan.Steps.Count}: {step.Tool}");
                AssistantToolResult result = await host.CallToolAsync(step.Tool, step.Arguments, cancellationToken);
                if (result.IsError)
                {
                    reports.Add(new AssistantStepReport(step.Description, step.Tool, false, result.Text));
                    await CancelAsync(current, transaction);
                    return RolledBack(plan, reports,
                        $"O passo {i + 1} ({step.Tool}) falhou: {result.Text} Todo o plano foi anulado — o projeto ficou como estava.");
                }
                reports.Add(new AssistantStepReport(step.Description, step.Tool, true, null));
            }
        }
        catch (OperationCanceledException)
        {
            await CancelAsync(current, transaction);
            throw;
        }
        catch (Exception ex)
        {
            await CancelAsync(current, transaction);
            return RolledBack(plan, reports,
                $"O plano foi interrompido por um erro inesperado ({ex.Message}) e anulado por completo — o projeto ficou como estava.");
        }

        await current.OnModelThreadAsync<object?>(_ => { transaction.Commit(); return null; });
        progress?.Report($"Plano concluído — {plan.Steps.Count} {(plan.Steps.Count == 1 ? "passo executado" : "passos executados")}.");
        return new AssistantExecution(true, plan.Summary, reports, null);
    }

    private static async Task CancelAsync(IEditorSession session, EditHistory.EditTransaction transaction) =>
        await session.OnModelThreadAsync<object?>(_ => { transaction.Cancel(); return null; });

    private static AssistantExecution RolledBack(
        AiPlanOutcome.Plan plan, IReadOnlyList<AssistantStepReport> reports, string error) =>
        new(false, plan.Summary, reports, error);

    /// <summary>The undo-entry label: the summary on one line, capped so history menus stay readable.</summary>
    private static string TransactionLabel(string summary)
    {
        string oneLine = string.Join(" ", summary.Split('\n')).Trim();
        return oneLine.Length <= 60 ? $"Assistente IA: {oneLine}" : $"Assistente IA: {oneLine[..60]}…";
    }
}
