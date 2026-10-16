using Sprocket.Core.Model;
using Xunit;

namespace Sprocket.Mcp.Tests;

/// <summary>
/// The in-app assistant's tool surface (Etapa D): the same SprocketTools collection the HTTP server exposes,
/// over the SDK's in-memory transport — filtered to synchronous reads/edits only, swap-safe across sessions,
/// and driving real commands through the fake session's EditHistory.
/// </summary>
public sealed class InProcessToolHostTests
{
    [Fact]
    public async Task Catalog_Lists_The_Unfiltered_Surface_And_The_Filter_Cannot_Rot()
    {
        await using var host = await InProcessToolHost.CreateAsync(() => new FakeEditorSession());
        IReadOnlyList<AssistantTool> tools = await host.ListToolsAsync();

        Assert.All(tools, t => Assert.False(InProcessToolHost.ExcludedTools.Contains(t.Name)));
        Assert.DoesNotContain(tools, t => t.Name is "export_video" or "undo" or "save_project" or "seek");
        Assert.Contains(tools, t => t.Name == "add_marker");

        // Every excluded name must still exist in the full surface — a typo here would silently widen the filter.
        HashSet<string> all = [.. new SprocketTools(new FakeEditorSession()).BuildTools()
            .Select(t => t.ProtocolTool.Name)];
        Assert.All(InProcessToolHost.ExcludedTools, name => Assert.Contains(name, all));
        Assert.Equal(all.Count - InProcessToolHost.ExcludedTools.Count, tools.Count);
    }

    [Fact]
    public async Task Catalog_Carries_Descriptions_And_Argument_Schemas()
    {
        await using var host = await InProcessToolHost.CreateAsync(() => new FakeEditorSession());
        AssistantTool marker = (await host.ListToolsAsync()).Single(t => t.Name == "add_marker");
        Assert.NotEqual("", marker.Description);
        Assert.Contains("positionTicks", marker.InputSchemaJson);
    }

    [Fact]
    public async Task Calls_Run_Edits_Through_The_Real_Tool_Layer()
    {
        var session = new FakeEditorSession();
        await using var host = await InProcessToolHost.CreateAsync(() => session);

        AssistantToolResult result = await host.CallToolAsync("add_marker",
            new Dictionary<string, object?> { ["positionTicks"] = 240000L, ["name"] = "teste" });

        Assert.False(result.IsError);
        Marker marker = Assert.Single(session.Project.Timeline.Markers);
        Assert.Equal("teste", marker.Name);
        Assert.Equal(1, session.History.UndoCount); // one undoable entry, like every other edit path
    }

    [Fact]
    public async Task Tool_Failure_Comes_Back_As_An_Error_Not_A_Throw()
    {
        var session = new FakeEditorSession();
        await using var host = await InProcessToolHost.CreateAsync(() => session);

        AssistantToolResult result = await host.CallToolAsync("delete_clip",
            new Dictionary<string, object?> { ["clipId"] = 99999 });
        AssistantToolResult unknown = await host.CallToolAsync("no_such_tool");

        Assert.True(result.IsError);
        Assert.Contains("list_clips", result.Text); // actionable text survives
        Assert.True(unknown.IsError);
    }

    [Fact]
    public async Task Session_Swap_Between_Calls_Routes_To_The_Live_Session()
    {
        var first = new FakeEditorSession();
        var second = new FakeEditorSession();
        IEditorSession? current = first;
        await using var host = await InProcessToolHost.CreateAsync(() => current);

        await host.CallToolAsync("add_marker", new Dictionary<string, object?> { ["positionTicks"] = 0L });
        current = second;
        AssistantToolResult result = await host.CallToolAsync("add_marker",
            new Dictionary<string, object?> { ["positionTicks"] = 0L });

        Assert.False(result.IsError);
        Assert.Single(first.Project.Timeline.Markers);
        Assert.Single(second.Project.Timeline.Markers);
    }
}
