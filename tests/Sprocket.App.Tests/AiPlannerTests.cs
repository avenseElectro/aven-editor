using System.Text.Json;
using Sprocket.App.Ai;
using Sprocket.Mcp;
using Xunit;

namespace Sprocket.App.Tests;

/// <summary>
/// The assistant's prompt/plan contract (Etapa D): the prompts carry the catalog, rules and request; the
/// parser accepts only well-formed plans over allow-listed tools, and turns everything else into a PT-PT
/// error before anything reaches the approval UI or the executor.
/// </summary>
public sealed class AiPlannerTests
{
    private static readonly IReadOnlyList<AssistantTool> Catalog =
    [
        new("add_marker", "Acrescenta um marcador.", """{"type":"object","properties":{"positionTicks":{"type":"integer"}}}"""),
        new("split_clip", "Divide um clipe.", """{"type":"object","properties":{"clipId":{"type":"integer"}}}"""),
    ];

    private static readonly IReadOnlySet<string> Allowed = new HashSet<string> { "add_marker", "split_clip" };

    private const string DoisPassos =
        """{"tipo":"plano","resposta":"Marca e divide.","passos":[{"ferramenta":"add_marker","argumentos":{"positionTicks":480000,"nome":"início"},"descricao":"Marcador a 2 s"},{"ferramenta":"split_clip","argumentos":{"clipId":1},"descricao":"Divide o primeiro clipe"}]}""";

    [Fact]
    public void System_Prompt_Carries_Rules_And_The_Tool_Catalog()
    {
        string prompt = AiPlanner.BuildSystemPrompt(Catalog, maxSteps: 7);

        Assert.Contains("240000 ticks = 1 segundo", prompt);
        Assert.Contains("Máximo de 7 passos", prompt);
        Assert.Contains("português europeu", prompt);
        Assert.Contains("\"tipo\":\"plano\"", prompt);
        Assert.Contains("add_marker — Acrescenta um marcador.", prompt);
        Assert.Contains("positionTicks", prompt); // the schema rides along
    }

    [Fact]
    public void User_Prompt_Carries_State_And_Request_And_Truncates_Huge_State()
    {
        string prompt = AiPlanner.BuildUserPrompt("""{"ticks":240000}""", "  corta ao meio  ");
        Assert.Contains("""{"ticks":240000}""", prompt);
        Assert.Contains("corta ao meio", prompt);

        string huge = new string('x', AiPlanner.MaxStateChars + 10_000);
        string truncated = AiPlanner.BuildUserPrompt(huge, "pedido");
        Assert.True(truncated.Length < huge.Length + 500);
        Assert.Contains("estado truncado", truncated);
    }

    [Fact]
    public void Parse_Accepts_A_Well_Formed_Plan()
    {
        AiPlanOutcome.Plan plan = Assert.IsType<AiPlanOutcome.Plan>(AiPlanner.ParsePlan(DoisPassos, Allowed, 20));

        Assert.Equal("Marca e divide.", plan.Summary);
        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal("add_marker", plan.Steps[0].Tool);
        Assert.Equal(480000L, ((JsonElement)plan.Steps[0].Arguments["positionTicks"]!).GetInt64());
        Assert.Equal("início", ((JsonElement)plan.Steps[0].Arguments["nome"]!).GetString());
        Assert.Equal("Marcador a 2 s", plan.Steps[0].Description);
        Assert.Equal(1, ((JsonElement)plan.Steps[1].Arguments["clipId"]!).GetInt32());
    }

    [Fact]
    public void Parse_Strips_Code_Fences_And_Chatter()
    {
        string fenced = "Here you go:\n```json\n" + DoisPassos + "\n```\nHope it helps!";
        Assert.IsType<AiPlanOutcome.Plan>(AiPlanner.ParsePlan(fenced, Allowed, 20));
    }

    [Fact]
    public void Parse_Returns_The_Clarifying_Question()
    {
        AiPlanOutcome.Clarify clarify = Assert.IsType<AiPlanOutcome.Clarify>(
            AiPlanner.ParsePlan("""{"tipo":"esclarecer","resposta":"Que clipe quer dividir?"}""", Allowed, 20));
        Assert.Equal("Que clipe quer dividir?", clarify.Question);
    }

    [Fact]
    public void Parse_Rejects_Unknown_Tools_Empty_And_Oversized_Plans()
    {
        Assert.IsType<AiPlanOutcome.Error>(AiPlanner.ParsePlan(
            """{"tipo":"plano","resposta":"x","passos":[{"ferramenta":"export_video","argumentos":{},"descricao":"x"}]}""",
            Allowed, 20));

        AiPlanOutcome.Error empty = Assert.IsType<AiPlanOutcome.Error>(
            AiPlanner.ParsePlan("""{"tipo":"plano","resposta":"x","passos":[]}""", Allowed, 20));
        Assert.Contains("vazio", empty.Message);

        string many = """{"tipo":"plano","resposta":"x","passos":[""" +
            string.Join(",", Enumerable.Range(0, 4).Select(i =>
                $$"""{"ferramenta":"add_marker","argumentos":{"positionTicks":{{i}}},"descricao":"x"}""")) + "]}";
        AiPlanOutcome.Error oversized = Assert.IsType<AiPlanOutcome.Error>(AiPlanner.ParsePlan(many, Allowed, 3));
        Assert.Contains("máximo de 3", oversized.Message);
    }

    [Fact]
    public void Parse_Rejects_Malformed_And_Incomplete_Answers()
    {
        Assert.IsType<AiPlanOutcome.Error>(AiPlanner.ParsePlan("não há json aqui", Allowed, 20));
        Assert.IsType<AiPlanOutcome.Error>(AiPlanner.ParsePlan("""{"tipo":"plano" """.Trim(), Allowed, 20)); // JSON cortado
        Assert.IsType<AiPlanOutcome.Error>(AiPlanner.ParsePlan("""{"resposta":"x"}""", Allowed, 20)); // sem tipo
        Assert.IsType<AiPlanOutcome.Error>(AiPlanner.ParsePlan(
            """{"tipo":"coisa","resposta":"x"}""", Allowed, 20));
        Assert.IsType<AiPlanOutcome.Error>(AiPlanner.ParsePlan(
            """{"tipo":"plano","resposta":"x","passos":[{"ferramenta":"add_marker","argumentos":42,"descricao":"x"}]}""",
            Allowed, 20));
        Assert.IsType<AiPlanOutcome.Error>(AiPlanner.ParsePlan(
            """{"tipo":"esclarecer"}""", Allowed, 20)); // sem pergunta
    }
}
