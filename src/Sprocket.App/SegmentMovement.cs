using System;
using System.Linq;
using Sprocket.Core.Model;
using Sprocket.Core.Timing;

namespace Sprocket.App;

/// <summary>
/// The pure math behind the «Editar recorte e movimento» window (<see cref="SegmentEditWindow"/>): the
/// entry/exit movement presets mapped onto the Transform effect's parameters, and the keyframe timeline each
/// staged parameter takes. Kept free of UI types so the movement semantics are unit-testable headlessly.
/// Keyframe times are absolute timeline time (ARCHITECTURE.md §9): the window builds them anchored to the
/// preview clip's zero for staging and to the real clip's <see cref="Clip.TimelineStart"/> on «Aplicar».
/// The exit movement always departs from the entry movement's resulting framing, so the two compose without
/// a jump (the approved spec's guarantee).
/// </summary>
internal static class SegmentMovement
{
    internal enum MovePreset { None = 0, ZoomIn, ZoomOut, Left, Right, Up, Down, Custom }

    /// <summary>The zoom amplitude an intensity in [0,1] buys: ×1.0 (none) to ×1.5 (strong).</summary>
    internal static double ZoomAmplitude(double intensity) => 1 + 0.5 * intensity;

    /// <summary>The pan amplitude (fraction of the frame) an intensity in [0,1] buys: 0.08 to 0.40.</summary>
    internal static double PanAmplitude(double intensity) => 0.08 + 0.32 * intensity;

    /// <summary>
    /// The entry/exit movement durations in ticks, each clamped into the segment; when the two together exceed
    /// the segment they meet at its middle. An inactive preset (its flag false) contributes zero, freeing the
    /// whole segment to the other movement.
    /// </summary>
    internal static (long Entry, long Exit) Durations(
        double entrySeconds, double exitSeconds, long durationTicks, bool entryActive, bool exitActive)
    {
        long de = entryActive ? ClampTicks(entrySeconds, durationTicks) : 0;
        long dx = exitActive ? ClampTicks(exitSeconds, durationTicks) : 0;
        if (de + dx > durationTicks)
        {
            de = Math.Max(0, durationTicks / 2);
            dx = durationTicks - de;
        }
        return (de, dx);
    }

    /// <summary>
    /// The value the <b>entry</b> movement starts from for one Transform parameter (null = the preset doesn't
    /// touch it, so the parameter stays at its framing base). <paramref name="param"/> is an
    /// <see cref="EffectParamNames"/> name; the custom values apply only under
    /// <see cref="MovePreset.Custom"/>.
    /// </summary>
    internal static double? EntryStart(
        MovePreset preset, string param, double scale, double x, double y, double intensity,
        double customScale, double customX, double customY)
    {
        double zoom = ZoomAmplitude(intensity);
        double pan = PanAmplitude(intensity);
        return preset switch
        {
            MovePreset.ZoomIn => param == EffectParamNames.Scale ? scale / zoom : null,
            MovePreset.ZoomOut => param == EffectParamNames.Scale ? scale * zoom : null,
            MovePreset.Left => param == EffectParamNames.PositionX ? x + pan : null,
            MovePreset.Right => param == EffectParamNames.PositionX ? x - pan : null,
            MovePreset.Up => param == EffectParamNames.PositionY ? y - pan : null,
            MovePreset.Down => param == EffectParamNames.PositionY ? y + pan : null,
            MovePreset.Custom => param == EffectParamNames.Scale ? customScale
                : param == EffectParamNames.PositionX ? customX
                : customY,
            _ => null,
        };
    }

    /// <summary>The value the <b>exit</b> movement ends at — the mirror of <see cref="EntryStart"/> (each
    /// preset runs the opposite direction, so «Zoom in» entry grows into the framing and «Zoom in» exit grows
    /// out of it).</summary>
    internal static double? ExitEnd(
        MovePreset preset, string param, double scale, double x, double y, double intensity,
        double customScale, double customX, double customY)
    {
        double zoom = ZoomAmplitude(intensity);
        double pan = PanAmplitude(intensity);
        return preset switch
        {
            MovePreset.ZoomIn => param == EffectParamNames.Scale ? scale * zoom : null,
            MovePreset.ZoomOut => param == EffectParamNames.Scale ? scale / zoom : null,
            MovePreset.Left => param == EffectParamNames.PositionX ? x - pan : null,
            MovePreset.Right => param == EffectParamNames.PositionX ? x + pan : null,
            MovePreset.Up => param == EffectParamNames.PositionY ? y + pan : null,
            MovePreset.Down => param == EffectParamNames.PositionY ? y - pan : null,
            MovePreset.Custom => param == EffectParamNames.Scale ? customScale
                : param == EffectParamNames.PositionX ? customX
                : customY,
            _ => null,
        };
    }

    /// <summary>
    /// Builds one staged parameter's value: the framing base held at the entry movement's start key, the base
    /// again from where the entry settles (<paramref name="entryTicks"/> in) to where the exit departs
    /// (its duration short of the end), and the exit's end value at the segment's end. Easing shapes both
    /// moving segments; the still hold between them is flat. Parameters neither movement touches (or whose
    /// animated values all equal the base) collapse to a constant, so an untouched framing stays a plain
    /// static Transform.
    /// </summary>
    internal static AnimatableValue Parameter(
        double baseValue, double? entryFrom, double? exitTo,
        long startTicks, long durationTicks, long entryTicks, long exitTicks, Interpolation ease)
    {
        double? entry = entryTicks > 0 ? entryFrom : null;
        double? exit = exitTicks > 0 ? exitTo : null;
        if (entry is null && exit is null)
            return AnimatableValue.Constant(baseValue);

        long settle = startTicks + entryTicks;
        long depart = startTicks + durationTicks - exitTicks;
        long end = startTicks + durationTicks;
        var keys = new System.Collections.Generic.List<Keyframe>();
        if (entry is { } from)
            keys.Add(new Keyframe(new Timecode(startTicks), from, ease));
        if (entry is not null && depart > settle)
            keys.Add(new Keyframe(new Timecode(settle), baseValue, Interpolation.Hold));
        if (exit is not null)
            keys.Add(new Keyframe(new Timecode(depart), baseValue, ease));
        if (exit is { } to)
            keys.Add(new Keyframe(new Timecode(end), to, Interpolation.Linear));
        if (keys.Count == 0 || keys.All(k => k.Value == baseValue))
            return AnimatableValue.Constant(baseValue);
        return AnimatableValue.Animated(keys);
    }

    /// <summary>Whether a staged Transform is the do-nothing identity (no animation, framing untouched) —
    /// «Aplicar» then only removes any previously committed Transform instead of adding a no-op.</summary>
    internal static bool IsIdentity(EffectInstance fx)
    {
        if (fx.Parameters.Values.Any(v => v.IsAnimated))
            return false;
        return Constant(fx, EffectParamNames.Scale, 1) == 1
            && Constant(fx, EffectParamNames.PositionX, 0) == 0
            && Constant(fx, EffectParamNames.PositionY, 0) == 0;
    }

    internal static double Constant(EffectInstance fx, string name, double fallback) =>
        fx.Parameters.TryGetValue(name, out AnimatableValue? v) && !v.IsAnimated
            ? v.Evaluate(Timecode.Zero)
            : fallback;

    private static long ClampTicks(double seconds, long durationTicks) =>
        Math.Clamp((long)(seconds * Timecode.TicksPerSecond), 0, durationTicks);
}
