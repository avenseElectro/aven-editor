using System.IO.Pipelines;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Sprocket.Mcp;

/// <summary>One tool the in-app assistant may call: its name, description, and the JSON schema of its arguments.</summary>
public sealed record AssistantTool(string Name, string Description, string InputSchemaJson);

/// <summary>The outcome of one tool call: the concatenated text content and whether the tool reported an error.</summary>
public sealed record AssistantToolResult(bool IsError, string Text);

/// <summary>
/// The in-app assistant's tool surface (Etapa D): the same <see cref="SprocketTools"/> collection the loopback
/// MCP server exposes, driven through the SDK's in-memory stream transport — no HTTP, port, or token, because
/// the only caller already lives inside this process. Tools whose scope sits outside an approved plan
/// (<see cref="ExcludedTools"/>: export, project lifecycle, save, undo/redo, transport, edit-group
/// transactions, long-running analysis) never join the collection, so a plan can only contain synchronous
/// model reads/edits — everything the executor runs lands in one undoable <see cref="Sprocket.Core.Commands.EditHistory"/>
/// transaction. Created via <see cref="CreateAsync"/>; the session resolver keeps the host valid across
/// File ▸ New/Open session swaps (the <see cref="McpServerHost"/> pattern).
/// </summary>
public sealed class InProcessToolHost : IAsyncDisposable
{
    /// <summary>Tool names the assistant may never call. Every name must exist in the full surface (tests
    /// assert it) so the filter cannot silently rot; anything not listed here joins automatically.</summary>
    public static readonly IReadOnlySet<string> ExcludedTools = new HashSet<string>
    {
        // Exporting stays a deliberate user action, never an assistant step.
        "export_video", "export_audio", "get_export_status", "cancel_export",
        // Project lifecycle and saving belong to the user's File menu.
        "open_project", "close_project", "new_project", "save_project", "save_project_as",
        // Undo/redo is the user's lever over an executed plan; the assistant must not pull it.
        "undo", "redo",
        // Transport is interactive; a plan is a set of edits, not a playback script.
        "stop", "play", "pause", "seek",
        // The executor wraps the plan in its own transaction — group tools would nest.
        "begin_edit_group", "end_edit_group", "cancel_edit_group",
        // Background analysis is asynchronous and out of scope for v1.
        "stabilization_analyze",
    };

    private static readonly string ServerVersion =
        typeof(InProcessToolHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly CancellationTokenSource _shutdown;
    private readonly StreamServerTransport _serverTransport;
    private readonly McpServer _server;
    private readonly McpClient _client;
    private readonly Task _run;

    private InProcessToolHost(
        StreamServerTransport serverTransport, McpServer server, McpClient client, Task run,
        CancellationTokenSource shutdown)
    {
        _serverTransport = serverTransport;
        _server = server;
        _client = client;
        _run = run;
        _shutdown = shutdown;
    }

    /// <summary>Builds the host over the live session resolved per call. A null session (no window yet,
    /// mid swap) surfaces as a tool error on use, mirroring <see cref="McpServerHost"/>'s 503.</summary>
    public static async Task<InProcessToolHost> CreateAsync(Func<IEditorSession?> session)
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "Sprocket", Title = "Sprocket video editor", Version = ServerVersion },
            ToolCollection = [],
        };
        foreach (McpServerTool tool in new SprocketTools(new ResolvingSession(session)).BuildTools()
                     .Where(t => !ExcludedTools.Contains(t.ProtocolTool.Name)))
            options.ToolCollection!.Add(tool);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var serverTransport = new StreamServerTransport(
            clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream(), serverName: "Sprocket");
        McpServer server = McpServer.Create(serverTransport, options);
        var shutdown = new CancellationTokenSource();
        Task run = server.RunAsync(shutdown.Token);
        McpClient client = await McpClient.CreateAsync(new StreamClientTransport(
            serverInput: clientToServer.Writer.AsStream(), serverOutput: serverToClient.Reader.AsStream()));
        return new InProcessToolHost(serverTransport, server, client, run, shutdown);
    }

    /// <summary>The assistant-callable catalog, for the planner's prompt.</summary>
    public async Task<IReadOnlyList<AssistantTool>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        IList<McpClientTool> tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);
        return [.. tools.Select(t => new AssistantTool(t.Name, t.Description ?? "", t.JsonSchema.GetRawText()))];
    }

    /// <summary>Calls one tool by name with JSON-compatible arguments. Errors come back as
    /// <see cref="AssistantToolResult.IsError"/> with the tool's actionable text, never as a throw.</summary>
    public async Task<AssistantToolResult> CallToolAsync(
        string name, IReadOnlyDictionary<string, object?>? arguments = null, CancellationToken cancellationToken = default)
    {
        CallToolResult result;
        try
        {
            result = await _client.CallToolAsync(name, arguments, cancellationToken: cancellationToken);
        }
        catch (McpException ex) // an unknown tool name is a plan-level failure, not a transport fault
        {
            return new AssistantToolResult(true, ex.Message);
        }
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        return new AssistantToolResult(result.IsError == true, text);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        try { await _run; }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch { /* the pipes are closing underneath the pump — nothing to report */ }
        await _client.DisposeAsync();
        await _server.DisposeAsync();
        await _serverTransport.DisposeAsync();
        _shutdown.Dispose();
    }
}

/// <summary>Delegates to whichever editor session is currently attached, so session swaps never invalidate a
/// host or its tool collection (shared by <see cref="McpServerHost"/> and <see cref="InProcessToolHost"/>).</summary>
internal sealed class ResolvingSession(Func<IEditorSession?> resolve) : IEditorSession
{
    public Task<T> OnModelThreadAsync<T>(Func<IEditorApi, T> fn) =>
        resolve() is { } session
            ? session.OnModelThreadAsync(fn)
            : throw new McpException("no project session is available — retry shortly.");
}
