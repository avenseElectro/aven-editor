using Sprocket.App.Ai;
using Sprocket.Core.Model;
using Sprocket.Mcp;
using Xunit;

namespace Sprocket.App.Tests;

/// <summary>
/// The assistant's engine (Etapa D): planning sends the project state + catalog through the chat client and
/// returns the parsed outcome; execution drives the approved steps through the real tool layer inside one
/// EditHistory transaction — a two-step plan is one Ctrl+Z, and a mid-plan failure reverts everything.
/// </summary>
public sealed class AssistantControllerTests
{
    private sealed class FakeChatClient(Func<string, string, string> respond) : IAiChatClient
    {
        public string? SystemPrompt { get; private set; }
        public string? UserPrompt { get; private set; }

        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            SystemPrompt = systemPrompt;
            UserPrompt = userPrompt;
            return Task.FromResult(respond(systemPrompt, userPrompt));
        }
    }

    private static AiPlanStep Marker(string name, long positionTicks) => new(
        "add_marker",
        new Dictionary<string, object?> { ["positionTicks"] = positionTicks, ["name"] = name },
        $"Marcador {name}");

    private static AssistantController MakeController(
        TestEditorSession session, InProcessToolHost host, Func<bool>? blocked = null) =>
        new(host, () => session, maxSteps: 20, blocked: blocked ?? (() => false));

    [Fact]
    public async Task Two_Step_Plan_Executes_As_One_Undo_Entry()
    {
        var session = new TestEditorSession();
        await using var host = await InProcessToolHost.CreateAsync(() => session);
        AssistantController controller = MakeController(session, host);

        AssistantExecution result = await controller.ExecuteAsync(
            new AiPlanOutcome.Plan("Dois marcadores.", [Marker("um", 480000), Marker("dois", 960000)]));

        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Equal(2, result.Steps.Count);
        Assert.All(result.Steps, s => Assert.True(s.Ok));
        Assert.Equal(2, session.Project.Timeline.Markers.Count);
        Assert.Equal(1, session.History.UndoCount); // the whole plan collapsed into one entry
        Assert.StartsWith("Assistente IA: ", session.History.UndoLabel);

        Assert.True(session.History.Undo()); // one Ctrl+Z…
        Assert.Empty(session.Project.Timeline.Markers); // …undoes everything the plan did
    }

    [Fact]
    public async Task Failure_On_The_Second_Step_Reverts_The_First()
    {
        var session = new TestEditorSession();
        await using var host = await InProcessToolHost.CreateAsync(() => session);
        AssistantController controller = MakeController(session, host);
        var failing = new AiPlanStep(
            "delete_clip", new Dictionary<string, object?> { ["clipId"] = 99999 }, "Apaga um clipe inexistente");

        AssistantExecution result = await controller.ExecuteAsync(
            new AiPlanOutcome.Plan("Marca e apaga.", [Marker("um", 0), failing]));

        Assert.False(result.Ok);
        Assert.Contains("anulado", result.Error);
        Assert.True(result.Steps[0].Ok); // the first step itself succeeded…
        Assert.False(result.Steps[1].Ok);
        Assert.NotNull(result.Steps[1].Error);
        Assert.Empty(session.Project.Timeline.Markers); // …but was rolled back
        Assert.Equal(0, session.History.UndoCount); // and the history is exactly as it was
        Assert.False(session.History.HasOpenTransaction);
    }

    [Fact]
    public async Task Empty_Plan_Is_Rejected_Before_Touching_The_History()
    {
        var session = new TestEditorSession();
        await using var host = await InProcessToolHost.CreateAsync(() => session);
        AssistantController controller = MakeController(session, host);

        AssistantExecution result = await controller.ExecuteAsync(new AiPlanOutcome.Plan("vazio", []));

        Assert.False(result.Ok);
        Assert.Contains("vazio", result.Error);
        Assert.Equal(0, session.History.UndoCount);
        Assert.False(session.History.HasOpenTransaction);
    }

    [Fact]
    public async Task The_Export_Gate_Blocks_Planning_And_Execution()
    {
        var session = new TestEditorSession();
        await using var host = await InProcessToolHost.CreateAsync(() => session);
        AssistantController controller = MakeController(session, host, blocked: () => true);
        var client = new FakeChatClient((_, _) => """{"tipo":"plano","resposta":"x","passos":[]}""");

        AiPlanOutcome.Error plan = Assert.IsType<AiPlanOutcome.Error>(
            await controller.PlanAsync(client, "adiciona um marcador"));
        Assert.Contains("exportação", plan.Message);

        AssistantExecution result = await controller.ExecuteAsync(
            new AiPlanOutcome.Plan("Um marcador.", [Marker("um", 0)]));
        Assert.False(result.Ok);
        Assert.Contains("exportação", result.Error);
        Assert.Empty(session.Project.Timeline.Markers);
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public async Task PlanAsync_Sends_State_And_Catalog_And_Returns_The_Parsed_Plan()
    {
        var session = new TestEditorSession();
        await using var host = await InProcessToolHost.CreateAsync(() => session);
        AssistantController controller = MakeController(session, host);
        var client = new FakeChatClient((_, _) =>
            """{"tipo":"plano","resposta":"Um marcador.","passos":[{"ferramenta":"add_marker","argumentos":{"positionTicks":240000,"name":"teste"},"descricao":"Marcador de teste"}]}""");

        AiPlanOutcome.Plan plan = Assert.IsType<AiPlanOutcome.Plan>(
            await controller.PlanAsync(client, "  adiciona um marcador chamado teste a 1 segundo  "));

        Assert.Equal("Um marcador.", plan.Summary);
        Assert.Single(plan.Steps);
        Assert.Equal("add_marker", plan.Steps[0].Tool);

        // The prompts carried the catalog (a real tool's schema) and the live state alongside the request.
        Assert.Contains("add_marker", client.SystemPrompt);
        Assert.Contains("Pedido do utilizador", client.UserPrompt);
        Assert.Contains("adiciona um marcador chamado teste a 1 segundo", client.UserPrompt);
        Assert.Contains("ticks", client.UserPrompt); // the get_project_state payload rode along
    }

    [Fact]
    public async Task PlanAsync_Maps_Client_Failures_And_Garbage_Answers_To_PT_Errors()
    {
        var session = new TestEditorSession();
        await using var host = await InProcessToolHost.CreateAsync(() => session);
        AssistantController controller = MakeController(session, host);

        var throwing = new FakeChatClient((_, _) => throw new AiClientException("A chave API foi rejeitada — verifique-a nas Preferências do Aven Editor."));
        AiPlanOutcome.Error key = Assert.IsType<AiPlanOutcome.Error>(await controller.PlanAsync(throwing, "faz qualquer coisa"));
        Assert.Contains("chave API", key.Message);

        var babbling = new FakeChatClient((_, _) => "claro, posso ajudar com isso!");
        AiPlanOutcome.Error noise = Assert.IsType<AiPlanOutcome.Error>(await controller.PlanAsync(babbling, "faz qualquer coisa"));
        Assert.Contains("sem JSON", noise.Message);
    }
}
