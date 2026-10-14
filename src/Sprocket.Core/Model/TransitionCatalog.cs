using Sprocket.Core.Timing;

namespace Sprocket.Core.Model;

/// <summary>
/// A browsable description of one transition type (PLAN.md step 25): its stable id (<see cref="TransitionTypeIds"/>),
/// a display name, a one-line description, and any editable parameters (reusing <see cref="EffectParameterDescriptor"/>
/// — the same type-driven shape effects use). This is the registry the Project panel's <b>Transitions</b> tab lists
/// over, mirroring the <see cref="EffectCatalog"/>.
/// </summary>
/// <param name="Id">The transition type id (matches <see cref="Transition.TransitionTypeId"/>).</param>
/// <param name="DisplayName">Human-readable name for the browser.</param>
/// <param name="Description">A one-line summary shown under the name.</param>
/// <param name="Parameters">The transition's editable parameters, in display order (empty for the v1 built-ins).</param>
public sealed record TransitionDescriptor(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<EffectParameterDescriptor> Parameters)
{
    /// <summary>
    /// Builds a fresh <see cref="Transition"/> of this type at <paramref name="cutPoint"/> spanning
    /// <paramref name="duration"/>, with every parameter set to its <see cref="EffectParameterDescriptor.Default"/>.
    /// </summary>
    public Transition CreateInstance(
        Timecode cutPoint, Timecode duration, TransitionAlignment alignment = TransitionAlignment.CenterOnCut)
    {
        var transition = new Transition(Id, cutPoint, duration, alignment);
        foreach (EffectParameterDescriptor p in Parameters)
            transition.Set(p.Name, p.Default);
        return transition;
    }
}

/// <summary>
/// The registry of built-in transitions (PLAN.md step 25). The v1 library covers the most-used NLE transitions —
/// a cross dissolve (the default), dip to black / white, and a left-to-right wipe — each realised as a two-input
/// SkSL shader in the Render layer (ARCHITECTURE.md §7). Plugin-contributed transitions would register here, so the
/// Transitions browser draws from one list rather than hard-coding the built-ins.
/// </summary>
public static class TransitionCatalog
{
    /// <summary>The default transition duration a freshly-applied transition gets — one second, the common NLE
    /// default (leading editors ship ~1 s). Snapped to whole frames by the editor when applied.</summary>
    public static Timecode DefaultDuration { get; } = Timecode.FromSeconds(1.0);

    /// <summary>The transition applied by the default "Apply Transition" gesture (cross dissolve, the NLE default).</summary>
    public static string DefaultTransitionId => TransitionTypeIds.CrossDissolve;

    /// <summary>All registered transition descriptors, in display order.</summary>
    public static IReadOnlyList<TransitionDescriptor> BuiltIns { get; } =
    [
        new TransitionDescriptor(
            TransitionTypeIds.CrossDissolve,
            "Dissolução cruzada",
            "Desvanece o clipe de saída diretamente para o de entrada — a transição padrão.",
            []),

        new TransitionDescriptor(
            TransitionTypeIds.DipToBlack,
            "Passagem a preto",
            "Desvanece o clipe de saída para preto e depois do preto para o clipe de entrada.",
            []),

        new TransitionDescriptor(
            TransitionTypeIds.DipToWhite,
            "Passagem a branco",
            "Desvanece o clipe de saída para branco e depois do branco para o clipe de entrada.",
            []),

        new TransitionDescriptor(
            TransitionTypeIds.Wipe,
            "Varrimento",
            "Varre o clipe de entrada sobre o de saída, da esquerda para a direita.",
            []),
    ];

    /// <summary>Looks up a descriptor by transition type id, or returns <see langword="null"/> if it is not registered.</summary>
    public static TransitionDescriptor? Find(string transitionTypeId) =>
        BuiltIns.FirstOrDefault(d => d.Id == transitionTypeId);

    /// <summary>A friendly display name for a transition type id, falling back to the id for unknown (plugin) ids.</summary>
    public static string DisplayName(string transitionTypeId) => Find(transitionTypeId)?.DisplayName ?? transitionTypeId;
}
