using System.Collections.Generic;
using System.Linq;
using Sprocket.App;
using Sprocket.Core.Model;
using Sprocket.Core.Timing;
using Xunit;

namespace Sprocket.App.Tests;

/// <summary>
/// Headless tests for <see cref="RandomTransitions"/> — the pure planning behind «Aplicar transições
/// aleatórias…»: seed determinism, the skip rule for cuts that already carry a transition, gap/single-clip
/// cases, duration clamping + frame snapping, and the emoji glyph pool.
/// </summary>
public class RandomTransitionsTests
{
    private static readonly long Second = Timecode.TicksPerSecond;
    private static readonly long Frame30 = Second / 30;

    private static readonly string[] AllTypes =
    [
        TransitionTypeIds.CrossDissolve, TransitionTypeIds.DipToBlack,
        TransitionTypeIds.DipToWhite, TransitionTypeIds.Wipe,
    ];

    private static VideoTrack TrackWithClips(params long[] durations)
    {
        var track = new VideoTrack();
        long start = 0;
        foreach (long d in durations)
        {
            track.Clips.Add(new Clip(MediaRefId.New(), Timecode.Zero, new Timecode(d), new Timecode(start)));
            start += d;
        }
        return track;
    }

    private static Timeline TimelineOf(params VideoTrack[] tracks)
    {
        var timeline = new Timeline(new Rational(30, 1), new Resolution(1920, 1080), 48000);
        foreach (VideoTrack t in tracks)
            timeline.Tracks.Add(t);
        return timeline;
    }

    [Fact]
    public void Same_Seed_Rebuilds_The_Same_Assignments()
    {
        var timeline = TimelineOf(TrackWithClips(Second, Second, Second, Second)); // 3 cuts
        IReadOnlyList<RandomTransitionAssignment> first = RandomTransitions.Build(
            timeline, new Timecode(Second / 2), AllTypes, seed: 7, Frame30);
        IReadOnlyList<RandomTransitionAssignment> second = RandomTransitions.Build(
            timeline, new Timecode(Second / 2), AllTypes, seed: 7, Frame30);
        Assert.Equal(
            first.Select(a => (a.CutPoint.Ticks, a.TransitionTypeId, a.Emoji)),
            second.Select(a => (a.CutPoint.Ticks, a.TransitionTypeId, a.Emoji)));
        Assert.Equal(3, first.Count);
    }

    [Fact]
    public void Different_Seeds_Re_Roll_The_Types()
    {
        // Six cuts over four types: a different seed re-rolls the draw («Gerar de novo»).
        var timeline = TimelineOf(TrackWithClips(Second, Second, Second, Second, Second, Second, Second));
        IReadOnlyList<RandomTransitionAssignment> a = RandomTransitions.Build(timeline, new Timecode(Second / 2), AllTypes, 7, Frame30);
        IReadOnlyList<RandomTransitionAssignment> b = RandomTransitions.Build(timeline, new Timecode(Second / 2), AllTypes, 8, Frame30);
        Assert.Contains(a.Zip(b), pair => pair.First.TransitionTypeId != pair.Second.TransitionTypeId);
    }

    [Fact]
    public void Cuts_With_An_Existing_Transition_Are_Skipped()
    {
        VideoTrack track = TrackWithClips(Second, Second, Second); // cuts at 1 s and 2 s
        track.Transitions.Add(new Transition(TransitionTypeIds.Wipe, new Timecode(Second), new Timecode(Second / 2)));
        var assignments = RandomTransitions.Build(TimelineOf(track), new Timecode(Second / 2), AllTypes, 7, Frame30);
        Assert.Single(assignments);
        Assert.Equal(2 * Second, assignments[0].CutPoint.Ticks);
    }

    [Fact]
    public void Gaps_And_Single_Clips_Yield_Nothing()
    {
        VideoTrack single = TrackWithClips(3 * Second);
        Assert.Empty(RandomTransitions.Build(TimelineOf(single), new Timecode(Second / 2), AllTypes, 7, Frame30));

        var gapped = new VideoTrack();
        gapped.Clips.Add(new Clip(MediaRefId.New(), Timecode.Zero, new Timecode(Second), Timecode.Zero));
        gapped.Clips.Add(new Clip(MediaRefId.New(), Timecode.Zero, new Timecode(Second),
            new Timecode(2 * Second))); // 1 s gap before it
        Assert.Empty(RandomTransitions.Build(TimelineOf(gapped), new Timecode(Second / 2), AllTypes, 7, Frame30));
    }

    [Fact]
    public void Locked_Tracks_Are_Skipped()
    {
        VideoTrack track = TrackWithClips(Second, Second);
        track.Locked = true;
        Assert.Empty(RandomTransitions.Build(TimelineOf(track), new Timecode(Second / 2), AllTypes, 7, Frame30));
    }

    [Fact]
    public void Duration_Clamps_To_The_Shorter_Clip_And_Snaps_Down_To_Whole_Frames()
    {
        // 0.51 s requested over 30 fps (15 frames = 0.5 s) — snapped down; a second case clamps to the 1 s clip.
        VideoTrack track = TrackWithClips(4 * Second, 4 * Second);
        RandomTransitionAssignment snapped = Assert.Single(RandomTransitions.Build(
            TimelineOf(track), new Timecode(Second * 51 / 100), AllTypes, 7, Frame30));
        Assert.Equal(15 * Frame30, snapped.Duration.Ticks);

        VideoTrack shortPair = TrackWithClips(4 * Second, Second);
        RandomTransitionAssignment clamped = Assert.Single(RandomTransitions.Build(
            TimelineOf(shortPair), new Timecode(2 * Second), AllTypes, 7, Frame30));
        Assert.Equal(Second, clamped.Duration.Ticks);
    }

    [Fact]
    public void Emoji_Assignments_Draw_From_The_Pool_And_Seed_The_Instance()
    {
        VideoTrack track = TrackWithClips(Second, Second, Second); // 2 cuts
        string[] emojiTypes = [TransitionTypeIds.EmojiZoom, TransitionTypeIds.EmojiMask];
        string[] pool = ["💀", "🔥"];
        IReadOnlyList<RandomTransitionAssignment> assignments = RandomTransitions.Build(
            TimelineOf(track), new Timecode(Second / 2), emojiTypes, 7, Frame30, pool);
        Assert.Equal(2, assignments.Count);
        foreach (RandomTransitionAssignment a in assignments)
        {
            Assert.Contains(a.Emoji, pool);
            Transition instance = a.ToTransition();
            Assert.Equal(a.Emoji, instance.GetString("emoji"));
            Assert.Contains(instance.TransitionTypeId, emojiTypes);
        }
    }

    [Fact]
    public void NonEmoji_Assignments_Carry_NoGlyph()
    {
        VideoTrack track = TrackWithClips(Second, Second);
        RandomTransitionAssignment a = Assert.Single(RandomTransitions.Build(
            TimelineOf(track), new Timecode(Second / 2), [TransitionTypeIds.CrossDissolve], 7, Frame30, ["💀"]));
        Assert.Null(a.Emoji);
        Assert.Empty(a.ToTransition().Strings);
    }

    [Fact]
    public void Empty_Allowed_List_Yields_Nothing()
    {
        VideoTrack track = TrackWithClips(Second, Second);
        Assert.Empty(RandomTransitions.Build(TimelineOf(track), new Timecode(Second / 2), [], 7, Frame30));
    }
}
