using System;
using System.Collections.Generic;
using System.Linq;
using Sprocket.Analysis.Features;
using Sprocket.Core.Model;
using Sprocket.Core.Timing;

namespace Sprocket.App;

/// <summary>One planned transition: the <paramref name="Track"/> and <paramref name="CutPoint"/> it lands on,
/// the chosen <paramref name="TransitionTypeId"/>, the (clamped, frame-snapped) <paramref name="Duration"/>,
/// and — for the emoji types — the <paramref name="Emoji"/> glyph seeded onto the instance (null otherwise).</summary>
/// <param name="Track">The video track holding the cut.</param>
/// <param name="CutPoint">The cut between the two adjacent clips.</param>
/// <param name="TransitionTypeId">One of the allowed type ids, drawn from the seeded RNG.</param>
/// <param name="Duration">Clamped to fit inside both clips and snapped down to whole frames.</param>
/// <param name="Emoji">The glyph for <see cref="TransitionTypeIds.EmojiZoom"/>/<see cref="TransitionTypeIds.EmojiMask"/>
/// assignments (from the caller's pool); null for non-emoji types.</param>
internal sealed record RandomTransitionAssignment(
    Track Track, Timecode CutPoint, string TransitionTypeId, Timecode Duration, string? Emoji = null)
{
    /// <summary>The model instance this assignment applies — the transition with its glyph seeded, ready for
    /// <c>AddTransitionCommand</c>.</summary>
    public Transition ToTransition() =>
        Emoji is null
            ? new Transition(TransitionTypeId, CutPoint, Duration)
            : new Transition(TransitionTypeId, CutPoint, Duration).SetString("emoji", Emoji);
}

/// <summary>
/// The pure planning behind «Aplicar transições aleatórias…» (Etapa C): which cuts on a timeline get which
/// transition type, drawn from a seeded <see cref="XorShiftRng"/> so the same seed always rebuilds the same
/// assignment (and «Gerar de novo» just moves to a fresh seed). Kept free of UI types so the semantics are
/// unit-testable headlessly, the <see cref="SegmentMovement"/> pattern. Only video tracks are considered
/// (transitions are a visual blend); a cut already carrying a transition is skipped, never replaced.
/// </summary>
internal static class RandomTransitions
{
    /// <summary>
    /// Plans one transition per eligible cut: every cut between adjacent clips (a clip ending exactly where the
    /// next starts) on every video track that does not already carry a transition. The duration is clamped to fit
    /// inside both clips and snapped down to whole frames (min one frame); cuts whose clips are shorter than a
    /// frame are skipped. An empty <paramref name="allowedTypeIds"/> yields no assignments.
    /// </summary>
    /// <param name="timeline">The timeline to scan (video tracks only, in z-order).</param>
    /// <param name="duration">The requested transition duration.</param>
    /// <param name="allowedTypeIds">The transition types to draw from (catalog display order recommended).</param>
    /// <param name="seed">The RNG seed — same seed, same assignments.</param>
    /// <param name="frameTicks">One frame in ticks, for whole-frame snapping.</param>
    /// <param name="emojiPool">The glyphs an emoji-type assignment draws from; null/empty makes emoji types fall
    /// back to their catalog default glyph.</param>
    internal static IReadOnlyList<RandomTransitionAssignment> Build(
        Timeline timeline,
        Timecode duration,
        IReadOnlyList<string> allowedTypeIds,
        ulong seed,
        long frameTicks,
        IReadOnlyList<string>? emojiPool = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(allowedTypeIds);
        if (allowedTypeIds.Count == 0 || duration.Ticks <= 0 || frameTicks <= 0)
            return [];

        var rng = new XorShiftRng(seed);
        var assignments = new List<RandomTransitionAssignment>();
        foreach (VideoTrack track in timeline.VideoTracks)
        {
            if (track.Locked)
                continue;

            // Cuts are where one clip ends exactly as the next begins; placement order is not timeline order.
            List<Clip> ordered = track.Clips.OrderBy(c => c.TimelineStart.Ticks).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                Clip from = ordered[i - 1];
                Clip to = ordered[i];
                if (from.TimelineEnd.Ticks != to.TimelineStart.Ticks)
                    continue; // a gap: no cut to bridge
                if (track.Transitions.Any(t => t.CutPoint.Ticks == from.TimelineEnd.Ticks))
                    continue; // already has one: skip, never replace

                long maxByClips = Math.Min(from.Duration.Ticks, to.Duration.Ticks);
                long dur = Math.Min(duration.Ticks, maxByClips);
                dur = Math.Max(frameTicks, dur / frameTicks * frameTicks); // snap down, at least one frame
                if (dur > maxByClips)
                    continue; // clips shorter than a frame: no room

                string typeId = allowedTypeIds[rng.NextInt(allowedTypeIds.Count)];
                string? emoji = null;
                if (typeId is TransitionTypeIds.EmojiZoom or TransitionTypeIds.EmojiMask
                    && emojiPool is { Count: > 0 })
                    emoji = emojiPool[rng.NextInt(emojiPool.Count)];
                assignments.Add(new RandomTransitionAssignment(track, from.TimelineEnd, typeId, new Timecode(dur), emoji));
            }
        }
        return assignments;
    }
}
