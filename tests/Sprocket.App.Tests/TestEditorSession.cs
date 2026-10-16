using Sprocket.Core.Commands;
using Sprocket.Core.Model;
using Sprocket.Core.Timing;
using Sprocket.Mcp;

namespace Sprocket.App.Tests;

/// <summary>
/// A synchronous in-memory <see cref="IEditorSession"/>/<see cref="IEditorApi"/> over a real
/// <see cref="Project"/> + <see cref="EditHistory"/> for the assistant's engine tests — the same shape as
/// the MCP test fake, trimmed to what the driven tools touch (marker edits, state reads, clip lookups).
/// Lifecycle/export/placement seams stay unsupported: those tools are outside the assistant's allow-list.
/// </summary>
internal sealed class TestEditorSession : IEditorSession, IEditorApi
{
    public TestEditorSession()
    {
        var timeline = new Timeline(new Rational(30, 1), new Resolution(1920, 1080), 48000);
        timeline.Tracks.Add(new VideoTrack { Name = "V1" });
        timeline.Tracks.Add(new AudioTrack { Name = "A1" });
        Project = new Project(timeline);
    }

    public Project Project { get; }
    public EditHistory History { get; } = new();
    public string? ProjectPath { get; set; }
    public long PlayheadTicks { get; private set; }
    public long DurationTicks => Project.Timeline.Duration.Ticks;
    public bool IsPlaying => false;

    public Task<T> OnModelThreadAsync<T>(Func<IEditorApi, T> fn) => Task.FromResult(fn(this));

    public void Seek(long ticks) => PlayheadTicks = Math.Max(0, ticks);
    public void Play() { }
    public void Pause() { }
    public void RefreshPreview() { }
    public void InvalidateFailedAsset(string path) { }
    public bool IsDirty => History.CanUndo;
    public bool SaveProject() => false;
    public bool SaveProjectAs(string absolutePath) => false;
    public McpResult<bool> OpenProject(string absolutePath) => McpResult<bool>.Fail("not supported");
    public McpResult<bool> NewProject() => McpResult<bool>.Fail("not supported");
    public McpResult<MediaRef> ImportMedia(string absolutePath) => McpResult<MediaRef>.Fail("not supported");
    public McpResult<Clip> PlaceClip(
        Guid mediaId, long startTicks, int? videoTrackIndex, int? audioTrackIndex,
        bool linked, bool includeVideo, bool includeAudio) => McpResult<Clip>.Fail("not supported");
    public McpResult<bool> StartExport(
        string outputPath, bool videoOnly, long? rangeInTicks, long? rangeOutTicks,
        string? rateControl = null, int? crf = null, double? bitrateMbps = null, double? maxBitrateMbps = null,
        bool hardware = false, bool fast = false) => McpResult<bool>.Fail("not supported");
    public McpResult<bool> StartAudioExport(
        string outputPath, string audioFormat, long? rangeInTicks, long? rangeOutTicks) =>
        McpResult<bool>.Fail("not supported");
    public McpExportStatus ExportStatus => new(false, 0, null, false, false, null);
    public void CancelExport() { }
    public McpStabilizationInfo StabilizationInfoForClip(Clip clip) => new(false, false, "not_analyzed", 0, 0);
    public McpResult<bool> AnalyzeStabilizationForClip(Clip clip) => McpResult<bool>.Fail("not supported");
}
