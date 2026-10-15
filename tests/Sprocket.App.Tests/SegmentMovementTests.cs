using System.Linq;
using Sprocket.App;
using Sprocket.Core.Model;
using Sprocket.Core.Timing;
using Xunit;

namespace Sprocket.App.Tests;

/// <summary>
/// Headless tests for <see cref="SegmentMovement"/> — the pure math behind the «Editar recorte e movimento»
/// window: duration clamping, the entry/exit preset mapping onto the Transform parameters, and the keyframe
/// timeline (the exit always departs from the entry's resulting framing, so the two compose without a jump).
/// The window's own staging/preview plumbing is manual verification (the App is a UI-bound WinExe).
/// </summary>
public class SegmentMovementTests
{
    private static readonly long Second = Timecode.TicksPerSecond;

    // ── Durations ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Durations_Clamp_Each_Into_The_Segment()
    {
        long dur = 4 * Second;
        Assert.Equal((2 * Second, Second),
            SegmentMovement.Durations(2, 1, dur, entryActive: true, exitActive: true));
        Assert.Equal((dur, 0), SegmentMovement.Durations(9, 9, dur, true, false)); // só a entrada: ocupa tudo
        Assert.Equal((0, dur), SegmentMovement.Durations(9, 9, dur, false, true));
        Assert.Equal((0, 0), SegmentMovement.Durations(2, 2, dur, false, false));
    }

    [Fact]
    public void Durations_Meet_At_The_Middle_When_Both_Exceed_The_Segment()
    {
        long dur = 4 * Second;
        (long de, long dx) = SegmentMovement.Durations(3, 3, dur, true, true);
        Assert.Equal(dur / 2, de);
        Assert.Equal(dur - de, dx);
        Assert.Equal(dur, de + dx);
    }

    // ── Preset mapping ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EntryStart_Mirrors_ExitEnd_For_Every_Direction()
    {
        double zoom = SegmentMovement.ZoomAmplitude(0.5);   // 1.25
        double pan = SegmentMovement.PanAmplitude(0.5);     // 0.24

        // Zoom in: a entrada começa larga e cresce até ao enquadramento; a saída cresce para fora dele.
        Assert.Equal(1 / zoom, SegmentMovement.EntryStart(SegmentMovement.MovePreset.ZoomIn, EffectParamNames.Scale, 1, 0, 0, 0.5, 0, 0, 0));
        Assert.Equal(zoom, SegmentMovement.ExitEnd(SegmentMovement.MovePreset.ZoomIn, EffectParamNames.Scale, 1, 0, 0, 0.5, 0, 0, 0));

        // Zoom out é o inverso.
        Assert.Equal(zoom, SegmentMovement.EntryStart(SegmentMovement.MovePreset.ZoomOut, EffectParamNames.Scale, 1, 0, 0, 0.5, 0, 0, 0));
        Assert.Equal(1 / zoom, SegmentMovement.ExitEnd(SegmentMovement.MovePreset.ZoomOut, EffectParamNames.Scale, 1, 0, 0, 0.5, 0, 0, 0));

        // Deslizamentos: ± pan na direção escolhida, só no parâmetro dessa direção.
        Assert.Equal(0 + pan, SegmentMovement.EntryStart(SegmentMovement.MovePreset.Left, EffectParamNames.PositionX, 1, 0, 0, 0.5, 0, 0, 0));
        Assert.Equal(0 - pan, SegmentMovement.ExitEnd(SegmentMovement.MovePreset.Left, EffectParamNames.PositionX, 1, 0, 0, 0.5, 0, 0, 0));
        Assert.Null(SegmentMovement.EntryStart(SegmentMovement.MovePreset.Left, EffectParamNames.PositionY, 1, 0, 0, 0.5, 0, 0, 0));
        Assert.Null(SegmentMovement.ExitEnd(SegmentMovement.MovePreset.Up, EffectParamNames.PositionX, 1, 0, 0, 0.5, 0, 0, 0));
        Assert.Equal(-pan, SegmentMovement.EntryStart(SegmentMovement.MovePreset.Up, EffectParamNames.PositionY, 1, 0, 0, 0.5, 0, 0, 0));
        Assert.Equal(0 - pan, SegmentMovement.EntryStart(SegmentMovement.MovePreset.Right, EffectParamNames.PositionX, 1, 0, 0, 0.5, 0, 0, 0));

        // Sem movimento não toca em nada; Personalizado usa os valores explícitos.
        Assert.Null(SegmentMovement.EntryStart(SegmentMovement.MovePreset.None, EffectParamNames.Scale, 1, 0, 0, 0.5, 2, 0, 0));
        Assert.Equal(2, SegmentMovement.EntryStart(SegmentMovement.MovePreset.Custom, EffectParamNames.Scale, 1, 0, 0, 0.5, 2, 0.3, -0.2));
        Assert.Equal(0.3, SegmentMovement.EntryStart(SegmentMovement.MovePreset.Custom, EffectParamNames.PositionX, 1, 0, 0, 0.5, 2, 0.3, -0.2));
        Assert.Equal(-0.2, SegmentMovement.ExitEnd(SegmentMovement.MovePreset.Custom, EffectParamNames.PositionY, 1, 0, 0, 0.5, 2, 0.3, -0.2));
    }

    // ── Keyframe timeline ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parameter_Entry_Only_Animates_From_The_Start_Key_And_Settles()
    {
        long start = 5 * Second;
        AnimatableValue v = SegmentMovement.Parameter(
            1.0, entryFrom: 0.8, exitTo: null, start, 4 * Second, 2 * Second, 0, Interpolation.EaseInOut);

        Assert.True(v.IsAnimated);
        Assert.Equal(2, v.Keyframes.Count);
        Assert.Equal(new Timecode(start), v.Keyframes[0].Time);
        Assert.Equal(0.8, v.Keyframes[0].Value);
        Assert.Equal(Interpolation.EaseInOut, v.Keyframes[0].Interpolation);
        Assert.Equal(new Timecode(start + 2 * Second), v.Keyframes[1].Time);
        Assert.Equal(0.8, v.Evaluate(new Timecode(start)));                 // parte do valor de entrada
        Assert.Equal(0.9, v.Evaluate(new Timecode(start + Second)));        // a meio do movimento (EaseInOut: metade exata)
        Assert.Equal(1.0, v.Evaluate(new Timecode(start + 2 * Second)));    // assenta no enquadramento
        Assert.Equal(1.0, v.Evaluate(new Timecode(start + 4 * Second)));    // mantém até ao fim
    }

    [Fact]
    public void Parameter_Exit_Only_Holds_The_Base_Then_Departs_At_The_End()
    {
        long start = Second;
        AnimatableValue v = SegmentMovement.Parameter(
            1.0, entryFrom: null, exitTo: 1.25, start, 4 * Second, 0, Second, Interpolation.EaseInOut);

        Assert.True(v.IsAnimated);
        Assert.Equal(1.0, v.Evaluate(new Timecode(start)));                 // antes do início da saída
        Assert.Equal(1.0, v.Evaluate(new Timecode(start + 3 * Second)));    // até ao ponto de partida
        Assert.Equal(1.25, v.Evaluate(new Timecode(start + 4 * Second)));   // termina no valor de saída
        // a saída parte do valor resultante da entrada (aqui, o enquadramento) — nunca de outro valor
        Assert.Equal(1.0, v.Keyframes[0].Value);
        Assert.Equal(new Timecode(start + 3 * Second), v.Keyframes[0].Time);
    }

    [Fact]
    public void Parameter_Both_Movements_Compose_Through_The_Settled_Framing()
    {
        long start = 0;
        AnimatableValue v = SegmentMovement.Parameter(
            1.0, entryFrom: 0.8, exitTo: 1.25, start, 4 * Second, Second, Second, Interpolation.Linear);

        // entrada [0,1s), paragem [1s,3s), saída [3s,4s] → chaves em 0, 1s, 3s, 4s
        Assert.Equal(4, v.Keyframes.Count);
        Assert.Equal(0.8, v.Evaluate(Timecode.Zero));
        Assert.Equal(1.0, v.Evaluate(new Timecode(Second)));
        Assert.Equal(1.0, v.Evaluate(new Timecode(2 * Second)));
        Assert.Equal(1.25, v.Evaluate(new Timecode(4 * Second)));
        Assert.All(v.Keyframes, k => Assert.Equal(1, v.Keyframes.Count(x => x.Time == k.Time))); // tempos únicos
    }

    [Fact]
    public void Parameter_Movements_Meeting_In_The_Middle_Never_Duplicate_A_Keyframe()
    {
        AnimatableValue v = SegmentMovement.Parameter(
            1.0, entryFrom: 0.8, exitTo: 1.25, 0, 2 * Second, Second, Second, Interpolation.Linear);

        Assert.Equal(3, v.Keyframes.Count); // início, encontro (1s), fim — sem chave duplicada no encontro
        Assert.Equal(new Timecode(Second), v.Keyframes[1].Time);
        Assert.Equal(1.0, v.Keyframes[1].Value);
    }

    [Fact]
    public void Parameter_Untouched_Or_No_Op_Values_Collapse_To_A_Constant()
    {
        Assert.False(SegmentMovement.Parameter(1.0, null, null, 0, Second, Second, Second, Interpolation.Linear).IsAnimated);
        // o valor animado coincide com o enquadramento → não há nada a animar
        Assert.False(SegmentMovement.Parameter(1.0, 1.0, 1.0, 0, Second, Second, Second, Interpolation.Linear).IsAnimated);
    }

    // ── Identity ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsIdentity_Detects_A_Do_Nothing_Transform()
    {
        var identity = new EffectInstance(EffectTypeIds.Transform)
            .Set(EffectParamNames.Scale, 1.0)
            .Set(EffectParamNames.PositionX, 0.0)
            .Set(EffectParamNames.PositionY, 0.0);
        Assert.True(SegmentMovement.IsIdentity(identity));

        var reframed = new EffectInstance(EffectTypeIds.Transform)
            .Set(EffectParamNames.Scale, 1.5);
        Assert.False(SegmentMovement.IsIdentity(reframed));

        var moved = new EffectInstance(EffectTypeIds.Transform)
            .Set(EffectParamNames.Scale, 1.0)
            .Set(EffectParamNames.PositionX, 0.2);
        Assert.False(SegmentMovement.IsIdentity(moved));

        var animated = new EffectInstance(EffectTypeIds.Transform)
            .Set(EffectParamNames.Scale, AnimatableValue.Animated(
            [
                new Keyframe(Timecode.Zero, 0.8),
                new Keyframe(new Timecode(Second), 1.0),
            ]));
        Assert.False(SegmentMovement.IsIdentity(animated));
    }
}
