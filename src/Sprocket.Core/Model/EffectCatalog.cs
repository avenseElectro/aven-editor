using Sprocket.Core.Stabilization;
using Sprocket.Core.Timing;

namespace Sprocket.Core.Model;

/// <summary>The broad grouping an effect falls under, used to organise the Effects/Audio browsers (PLAN.md step 15).</summary>
public enum EffectCategory
{
    /// <summary>Geometry / compositing on the video frame (alpha, transform).</summary>
    Video,

    /// <summary>Colour / tone adjustments on the video frame (brightness, exposure, contrast).</summary>
    Color,

    /// <summary>Effects that act on the audio signal (gain, fade gain).</summary>
    Audio,
}

/// <summary>
/// The control a parameter descriptor asks the Inspector to build. The kind is always declared
/// explicitly on the descriptor — it is never inferred from <c>Min</c>/<c>Max</c>/<c>Step</c>, because
/// genuinely continuous parameters (Rotation, Temperature, delay times…) also use a step of 1.
/// </summary>
public enum ParameterKind
{
    /// <summary>A continuous scalar — slider + numeric entry (the default).</summary>
    Continuous,

    /// <summary>An on/off flag stored as 0/1 and read with a ≥ 0.5 threshold — a checkbox; keyframes
    /// use <see cref="Interpolation.Hold"/> so the value never interpolates through the threshold.</summary>
    Toggle,

    /// <summary>A whole-number scalar — slider snapped to integers; new keyframes default to
    /// <see cref="Interpolation.Hold"/> but may be re-eased.</summary>
    Integer,

    /// <summary>A choice from <see cref="EffectParameterDescriptor.Choices"/> stored as its index —
    /// a dropdown; constant-only (not keyframeable).</summary>
    Dropdown,

    /// <summary>A file/asset reference (PLAN.md step 49) — e.g. the Convolution Reverb's impulse-response
    /// WAV. Lives in <see cref="EffectInstance.Assets"/> as a path string rather than in
    /// <see cref="EffectInstance.Parameters"/>, so it is constant-only and the numeric
    /// <c>Default</c>/<c>Min</c>/<c>Max</c> on its descriptor are unused; the Inspector builds a file-picker
    /// row (name + Browse… + clear) and <see cref="EffectDescriptor.CreateInstance"/> leaves it unset.</summary>
    Asset,
}

/// <summary>
/// A type-driven description of one editable effect parameter (PLAN.md step 16): its stable name (matches
/// the key in <see cref="EffectInstance.Parameters"/>), a display label, the default value a fresh instance
/// gets, the slider range, an editing step for numeric nudge, an optional unit suffix, an optional
/// one-line description shown as the label's tooltip, and the control kind the Inspector should build
/// (with the choice labels when the kind is <see cref="ParameterKind.Dropdown"/>). The Inspector builds
/// the control per descriptor, so a new effect's UI falls out of its registration with no bespoke
/// control code (and a plugin gets the same treatment, ARCHITECTURE.md §4).
/// </summary>
/// <param name="Name">The parameter key (matches <see cref="EffectParamNames"/>).</param>
/// <param name="DisplayName">Human-readable label shown in the Inspector.</param>
/// <param name="Default">The value a freshly created instance is given.</param>
/// <param name="Min">Minimum of the slider range.</param>
/// <param name="Max">Maximum of the slider range.</param>
/// <param name="Step">Suggested increment for numeric nudge / arrow keys.</param>
/// <param name="Unit">Optional unit suffix for display (e.g. <c>"°"</c>, <c>"EV"</c>).</param>
/// <param name="Description">One-line plain-language explanation of what the parameter does, shown as the
/// Inspector label's tooltip (and surfaced to MCP clients). <see langword="null"/> = no tooltip.</param>
/// <param name="Kind">The control the Inspector builds for this parameter (default
/// <see cref="ParameterKind.Continuous"/>).</param>
/// <param name="Choices">Display labels for a <see cref="ParameterKind.Dropdown"/> parameter, indexed by
/// the parameter's value; <see langword="null"/> for every other kind.</param>
public sealed record EffectParameterDescriptor(
    string Name,
    string DisplayName,
    double Default,
    double Min,
    double Max,
    double Step = 0.01,
    string? Unit = null,
    string? Description = null,
    ParameterKind Kind = ParameterKind.Continuous,
    IReadOnlyList<string>? Choices = null)
{
    /// <summary>
    /// Multiplier applied to the model value for display only (1.0 = shown exactly as stored). A 0–1 ratio
    /// presented as a percentage sets 100 alongside <c>Unit = "%"</c>, so Opacity 0.5 reads <c>"50%"</c> and
    /// typing <c>"50"</c> commits 0.5 — matching how Premiere / Final Cut / After Effects surface opacity,
    /// scale and audio mix amounts.
    /// <para>
    /// <see cref="Default"/>, <see cref="Min"/>, <see cref="Max"/> and <see cref="Step"/> stay in
    /// <em>model</em> units: the slider, the clamp, the keyframe lane, every command and every MCP tool
    /// argument are unscaled. Only the Inspector's numeric field converts.
    /// </para>
    /// Declared explicitly like <see cref="Kind"/> — never inferred from <see cref="Unit"/>, since a
    /// parameter may legitimately store 0–100 and display <c>"%"</c> with no scaling at all.
    /// </summary>
    public double DisplayScale { get; init; } = 1.0;

    /// <summary>
    /// For a <see cref="ParameterKind.Asset"/> parameter, the file kind the Inspector's picker filters to: a
    /// display name (e.g. <c>"WAV audio"</c>) and its extensions without the dot (e.g. <c>["wav"]</c>).
    /// <see langword="null"/> for every other kind, and for an asset that accepts any file.
    /// </summary>
    public AssetFileType? AssetType { get; init; }
}

/// <summary>The file kind an <see cref="ParameterKind.Asset"/> parameter accepts (see
/// <see cref="EffectParameterDescriptor.AssetType"/>): a display name and its extensions without the dot.</summary>
public sealed record AssetFileType(string Name, IReadOnlyList<string> Extensions);

/// <summary>
/// A named factory preset for an effect (PLAN.md step 41): the parameter values that give a recognisable
/// starting point (Room / Plate / Hall / Cathedral …). Values are constants applied over the descriptor's
/// defaults — parameters a preset omits keep their current value, so a preset can share the tweaks that
/// define it without flattening unrelated edits. The Inspector offers a preset picker for any descriptor
/// that carries presets; applying one is an ordinary undoable parameter edit.
/// </summary>
/// <param name="Name">Human-readable preset name shown in the picker.</param>
/// <param name="Values">Parameter values by name (keys match <see cref="EffectParamNames"/>).</param>
/// <param name="Description">Optional one-line note shown as the picker item's tooltip (e.g. the B&amp;W
/// film presets' "Inspired by &lt;stock&gt;. &lt;character&gt;."). <see langword="null"/> for presets without one.</param>
public sealed record EffectPreset(string Name, IReadOnlyDictionary<string, double> Values, string? Description = null);

/// <summary>
/// A browsable description of one effect type: its stable id (<see cref="EffectTypeIds"/>), a display name,
/// a category, a one-line description, and the ordered list of its editable <see cref="EffectParameterDescriptor"/>s.
/// This is the "effect registry" the Effects browser lists over (PLAN.md step 15); the Inspector (step 16)
/// builds its per-effect controls from <see cref="Parameters"/>, and a future plugin host (step 23) registers
/// here too, so every browser and the Inspector draw from one list rather than hard-coding the built-ins.
/// </summary>
/// <param name="Id">The effect type id (matches <see cref="EffectInstance.EffectTypeId"/>).</param>
/// <param name="DisplayName">Human-readable name for the browser.</param>
/// <param name="Category">Which browser section this effect belongs to.</param>
/// <param name="Description">A one-line summary shown under the name.</param>
/// <param name="Parameters">The effect's editable parameters, in display order.</param>
public sealed record EffectDescriptor(
    string Id,
    string DisplayName,
    EffectCategory Category,
    string Description,
    IReadOnlyList<EffectParameterDescriptor> Parameters)
{
    /// <summary>Named factory presets (PLAN.md step 41), empty for effects without any.</summary>
    public IReadOnlyList<EffectPreset> Presets { get; init; } = [];

    /// <summary>
    /// The 2-letter code instance reference tags are built from (<see cref="EffectTags"/>, e.g. <c>"RV"</c>
    /// → tag <c>"RV-1"</c>). Unique across the built-ins; a plugin descriptor that omits it gets one derived
    /// from its display name (<see cref="EffectTags.DeriveShortCode"/>).
    /// </summary>
    public string? ShortCode { get; init; }

    /// <summary>
    /// Whether this effect modifies the clip's <em>time</em> rather than its pixels (plan/features/toy-cassette-camera.md
    /// phase 2 — <see cref="EffectTypeIds.PosterizeTime"/>): it is read by the clip's video time map
    /// (<see cref="Clip.MapToSourceVideo"/> / <see cref="Clip.EffectEvalTime"/>), dropped from the resolved effect
    /// list the shader pipeline sees (and skipped there if one reaches it), and its parameters are static — the
    /// Inspector hides their keyframe control and MCP refuses keyframes for them.
    /// </summary>
    public bool IsTimeModifier { get; init; }

    /// <summary>
    /// For a <em>temporal</em> effect (plan/features/toy-cassette-camera.md phase 6 — <see cref="EffectTypeIds.Echo"/>):
    /// computes, from the effect's resolved parameter values, how many earlier frames of its clip it reads and how
    /// far apart (<see cref="Model.TemporalFootprint"/>). The render planner maps those prior times through the
    /// clip's video time map and attaches them to the resolved effect as data, so the renderer can bind the prior
    /// frames as extra shader inputs while every frame stays a pure function of (project, time).
    /// <see langword="null"/> (the default) for every ordinary, single-frame effect. Must be pure and cheap.
    /// </summary>
    public Func<IReadOnlyDictionary<string, double>, TemporalFootprint>? TemporalFootprint { get; init; }

    /// <summary>
    /// Builds a fresh <see cref="EffectInstance"/> of this type with every numeric parameter set to its
    /// <see cref="EffectParameterDescriptor.Default"/>. <see cref="ParameterKind.Asset"/> descriptors are
    /// left unset (there is no default file). Each call yields an independent instance.
    /// </summary>
    public EffectInstance CreateInstance()
    {
        var instance = new EffectInstance(Id);
        foreach (EffectParameterDescriptor p in Parameters)
        {
            if (p.Kind != ParameterKind.Asset)
                instance.Set(p.Name, p.Default);
        }
        return instance;
    }

    /// <summary>
    /// Builds a fresh instance (as <see cref="CreateInstance()"/>) with <paramref name="preset"/>'s values
    /// applied over the defaults — what a one-click look entry point (the Effects browser's DAY FOR NIGHT
    /// group, MCP <c>add_effect</c> with a preset) adds, so the result is identical to adding the effect and
    /// then picking the preset in the Inspector, in one undo step instead of two.
    /// </summary>
    /// <exception cref="ArgumentException">The preset sets a parameter this descriptor does not declare as numeric.</exception>
    public EffectInstance CreateInstance(EffectPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        EffectInstance instance = CreateInstance();
        foreach ((string name, double value) in preset.Values)
        {
            if (!Parameters.Any(p => p.Name == name && p.Kind != ParameterKind.Asset))
                throw new ArgumentException($"Preset '{preset.Name}' sets '{name}', which {Id} does not declare.", nameof(preset));
            instance.Set(name, value);
        }
        return instance;
    }

    /// <summary>The factory preset named <paramref name="name"/> (case-insensitive), or <see langword="null"/>.</summary>
    public EffectPreset? FindPreset(string name) =>
        Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The registry of effect descriptors (ARCHITECTURE.md §4/§7/§13). <see cref="BuiltIns"/> holds the built-in
/// effects; plugin-contributed effects (PLAN.md step 33) are added at load time via <see cref="Register"/> and
/// removed on unload via <see cref="Unregister"/>, so every browser and the Inspector draw from one combined
/// list (<see cref="All"/>) rather than hard-coding the built-ins.
/// </summary>
public static class EffectCatalog
{
    /// <summary>All built-in effect descriptors, in display order.</summary>
    public static IReadOnlyList<EffectDescriptor> BuiltIns { get; } =
    [
        new EffectDescriptor(
            EffectTypeIds.Transform,
            "Transformação",
            EffectCategory.Video,
            "Escala, posiciona e roda a camada em torno de uma âncora, com opacidade de camada.",
            [
                new EffectParameterDescriptor(EffectParamNames.Scale, "Escala", 1.0, 0.0, 4.0, 0.05, "%",
                    "Tamanho uniforme da camada (100% = tamanho original).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.PositionX, "Posição X", 0.0, -1.0, 1.0, 0.01,
                    Description: "Desvio horizontal como fração da largura do fotograma (0 = centrado)."),
                new EffectParameterDescriptor(EffectParamNames.PositionY, "Posição Y", 0.0, -1.0, 1.0, 0.01,
                    Description: "Desvio vertical como fração da altura do fotograma (0 = centrado)."),
                new EffectParameterDescriptor(EffectParamNames.Rotation, "Rotação", 0.0, -180.0, 180.0, 1.0, "°",
                    "Ângulo de rotação em torno do ponto de âncora, em graus."),
                new EffectParameterDescriptor(EffectParamNames.AnchorX, "Âncora X", 0.5, 0.0, 1.0, 0.01,
                    Description: "Pivô horizontal para escala e rotação (0 = aresta esquerda, 1 = aresta direita)."),
                new EffectParameterDescriptor(EffectParamNames.AnchorY, "Âncora Y", 0.5, 0.0, 1.0, 0.01,
                    Description: "Pivô vertical para escala e rotação (0 = aresta superior, 1 = aresta inferior)."),
                new EffectParameterDescriptor(EffectParamNames.Opacity, "Opacidade", 1.0, 0.0, 1.0, 0.05, "%",
                    "Transparência da camada (100% = totalmente opaca, 0% = invisível).") { DisplayScale = 100 },
            ]) { ShortCode = "TR" },

        // ── Stabilization (plan/features/stabilization.md) — a hard-coded pipeline stage (like Transform),
        // driven by the headless motion analysis. Dropdowns store their choice index; toggles store 0/1.
        // Inspector order follows the leading editors (FCP/Resolve): mode + master smoothness/strength first,
        // then the per-channel controls, the framing controls, and the analysis/workflow toggles last. ──
        new EffectDescriptor(
            EffectTypeIds.Stabilization,
            "Estabilização",
            EffectCategory.Video,
            "Remove a tremulação indesejada da câmara — instabilidade de deslocamento/inclinação, rotação e oscilação de escala — a partir do movimento analisado.",
            [
                new EffectParameterDescriptor(EffectParamNames.StabMode, "Modo", 0.0, 0.0,
                    StabilizationSettings.ModeChoices.Count - 1, 1.0,
                    Description: "Como o trajeto da câmara é suavizado: Smooth Camera mantém os movimentos intencionais, Smooth Motion filtra todo o trajeto, Camera Lock imobiliza-o (tripé).",
                    Kind: ParameterKind.Dropdown, Choices: StabilizationSettings.ModeChoices),
                new EffectParameterDescriptor(EffectParamNames.Smoothness, "Suavidade", 0.5, 0.0, 1.0, 0.05, "%",
                    "Quanto do trajeto da câmara é suavizado — valores maiores fazem a média numa janela mais longa.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Strength, "Intensidade", 1.0, 0.0, 1.0, 0.05, "%",
                    "Mistura entre o trajeto original (0%) e o totalmente suavizado (100%), para manter algum movimento original.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.StabMethod, "Método", 1.0, 0.0,
                    StabilizationSettings.MethodChoices.Count - 1, 1.0,
                    Description: "O modelo de movimento usado na resolução: Translation (apenas deslocamento/inclinação), Similarity (acrescenta rotação + escala) ou Perspective.",
                    Kind: ParameterKind.Dropdown, Choices: StabilizationSettings.MethodChoices),
                new EffectParameterDescriptor(EffectParamNames.PositionSmooth, "Suavização de posição", 1.0, 0.0, 2.0, 0.05,
                    Description: "Multiplica a janela de suavização do deslocamento/inclinação (1.0 = suavidade global, 0 = não toca na posição)."),
                new EffectParameterDescriptor(EffectParamNames.RotationSmooth, "Suavização de rotação", 1.0, 0.0, 2.0, 0.05,
                    Description: "Multiplica a janela de suavização da rotação (1.0 = suavidade global, 0 = não toca na rotação)."),
                new EffectParameterDescriptor(EffectParamNames.ScaleMode, "Escala", 0.0, 0.0,
                    StabilizationSettings.ScaleModeChoices.Count - 1, 1.0,
                    Description: "Como a escala é tratada: suavizá-la como os outros canais, preservar o zoom original ou fixá-la numa referência (a correção da respiração de foco).",
                    Kind: ParameterKind.Dropdown, Choices: StabilizationSettings.ScaleModeChoices),
                new EffectParameterDescriptor(EffectParamNames.ScaleSmooth, "Suavização de escala", 1.0, 0.0, 2.0, 0.05,
                    Description: "Multiplica a janela de suavização da escala quando Escala = suavizar (1.0 = suavidade global)."),
                new EffectParameterDescriptor(EffectParamNames.ScaleLockRef, "Referência de fixação", 0.0, 0.0,
                    StabilizationSettings.ScaleLockRefChoices.Count - 1, 1.0,
                    Description: "A que escala Escala = fixar prende todos os fotogramas (a mais fechada nunca reduz, logo não introduz margens).",
                    Kind: ParameterKind.Dropdown, Choices: StabilizationSettings.ScaleLockRefChoices),
                new EffectParameterDescriptor(EffectParamNames.LockRotation, "Bloquear horizonte", 0.0, 0.0, 1.0, 1.0,
                    Description: "Remove toda a rotação relativamente ao primeiro fotograma, mantendo o horizonte nivelado.",
                    Kind: ParameterKind.Toggle),
                new EffectParameterDescriptor(EffectParamNames.Zoom, "Zoom automático", 1.0, 0.0, 1.0, 1.0,
                    Description: "Amplia para recortar as margens estabilizadas; desligado mostra margens transparentes (\"apenas estabilizar\").",
                    Kind: ParameterKind.Toggle),
                new EffectParameterDescriptor(EffectParamNames.CroppingRatio, "Rácio de recorte", 0.8, 0.5, 1.0, 0.05, "%",
                    "A fração do fotograma que tem de sobreviver à estabilização — limita o zoom automático a 1 / este valor.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.DetailedAnalysis, "Análise detalhada", 0.0, 0.0, 1.0, 1.0,
                    Description: "Maior resolução de análise e mais elementos acompanhados (mais lento); altera a chave da cache de análise.",
                    Kind: ParameterKind.Toggle),
                new EffectParameterDescriptor(EffectParamNames.ShowTrackPoints, "Mostrar pontos de acompanhamento", 0.0, 0.0, 1.0, 1.0,
                    Description: "Uma sobreposição de pré-visualização dos elementos acompanhados (não afeta a renderização).",
                    Kind: ParameterKind.Toggle),
                new EffectParameterDescriptor(EffectParamNames.HideBanner, "Ocultar aviso", 0.0, 0.0, 1.0, 1.0,
                    Description: "Suprime a faixa de aviso do monitor \"requer análise / a analisar / baixa confiança\".",
                    Kind: ParameterKind.Toggle),
            ])
        {
            ShortCode = "ST",
            Presets = StabilizationPresets.All,
        },

        // ── Action-VFX primitives (plan/features/special-effects.md, phase 1) — registry SkSL effects, the
        // reusable optical/geometric building blocks the later action, atmospheric and day-for-night presets
        // combine. Naming and default values follow the equivalent After Effects / Resolve primitives so the
        // controls read the way editors expect. Every spatial value is a fraction of the layer rect, so a
        // preview at one resolution and an export at another match (§5). ──
        new EffectDescriptor(
            EffectTypeIds.Glow,
            "Resplandecência",
            EffectCategory.Video,
            "Faz resplandecer as zonas mais claras da imagem para fora — halação para fogo, explosões e luzes práticas.",
            [
                new EffectParameterDescriptor(EffectParamNames.Threshold, "Limiar", 0.7, 0.0, 1.0, 0.01, "%",
                    "Quão claro um píxel tem de ficar antes de resplandecer (mais baixo = mais imagem resplandece).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Radius, "Raio", 0.03, 0.0, 0.25, 0.005, "%",
                    "Até onde a resplandecência se espalha, como fração da largura do fotograma.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Intensity, "Intensidade", 1.0, 0.0, 4.0, 0.05, "%",
                    "Com que força a resplandecência é somada de novo à imagem (100% = unidade).") { DisplayScale = 100 },
            ]) { ShortCode = "GL" },

        new EffectDescriptor(
            EffectTypeIds.DirectionalBlur,
            "Desfocagem direcional",
            EffectCategory.Video,
            "Esbate a imagem ao longo de um ângulo — velocidade, impactos e desfocagem de movimento simulada.",
            [
                new EffectParameterDescriptor(EffectParamNames.Angle, "Ângulo", 0.0, -180.0, 180.0, 1.0, "°",
                    "Direção do esbatimento, em graus no sentido horário a partir da horizontal."),
                new EffectParameterDescriptor(EffectParamNames.BlurLength, "Comprimento", 0.02, 0.0, 0.25, 0.005, "%",
                    "Comprimento do esbatimento, como fração da largura do fotograma.") { DisplayScale = 100 },
            ]) { ShortCode = "DB" },

        new EffectDescriptor(
            EffectTypeIds.ZoomBlur,
            "Desfocagem de zoom",
            EffectCategory.Video,
            "Esbate a imagem ao longo dos raios de um ponto central — aproximações, acertos e momentos de explosão.",
            [
                new EffectParameterDescriptor(EffectParamNames.Amount, "Quantidade", 0.0, 0.0, 1.0, 0.01, "%",
                    "Até onde a imagem é esbatida em direção ao centro e a partir dele.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.CenterX, "Centro X", 0.5, 0.0, 1.0, 0.01,
                    Description: "Centro horizontal do zoom (0 = aresta esquerda, 1 = aresta direita)."),
                new EffectParameterDescriptor(EffectParamNames.CenterY, "Centro Y", 0.5, 0.0, 1.0, 0.01,
                    Description: "Centro vertical do zoom (0 = aresta superior, 1 = aresta inferior)."),
            ]) { ShortCode = "ZB" },

        new EffectDescriptor(
            EffectTypeIds.HeatDistortion,
            "Distorção de calor",
            EffectCategory.Video,
            "Cintilação de refração animada — o ar sobre fogo, escapamentos ou solo quente.",
            [
                new EffectParameterDescriptor(EffectParamNames.Amount, "Quantidade", 0.01, 0.0, 0.1, 0.001, "%",
                    "Até onde a imagem é refratada, como fração da largura do fotograma.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.NoiseScale, "Detalhe", 12.0, 1.0, 60.0, 1.0,
                    Description: "Quantas células de cintilação cabem no fotograma — mais alto é cintilação mais fina e fechada."),
                new EffectParameterDescriptor(EffectParamNames.Speed, "Velocidade", 1.0, 0.0, 5.0, 0.05,
                    Description: "Velocidade a que a cintilação se move (0 = distorção congelada e imóvel)."),
            ]) { ShortCode = "HD" },

        new EffectDescriptor(
            EffectTypeIds.Shockwave,
            "Onda de choque",
            EffectCategory.Video,
            "Um anel de deslocamento em expansão a partir de um centro — a onda de choque de uma explosão. Crie fotogramas-chave no Raio para a disparar.",
            [
                new EffectParameterDescriptor(EffectParamNames.Radius, "Raio", 0.0, 0.0, 1.5, 0.01, "%",
                    "Onde está o anel neste momento, como fração da meia-diagonal do fotograma — crie fotogramas-chave aqui para a onda avançar.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.RingWidth, "Largura", 0.1, 0.01, 1.0, 0.01, "%",
                    "Espessura do anel — mais largo é uma onda mais suave e aparentemente mais lenta.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Amplitude, "Amplitude", 0.02, 0.0, 0.2, 0.005, "%",
                    "Até onde o anel empurra a imagem, como fração da largura do fotograma.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.CenterX, "Centro X", 0.5, 0.0, 1.0, 0.01,
                    Description: "Origem horizontal da onda (0 = aresta esquerda, 1 = aresta direita)."),
                new EffectParameterDescriptor(EffectParamNames.CenterY, "Centro Y", 0.5, 0.0, 1.0, 0.01,
                    Description: "Origem vertical da onda (0 = aresta superior, 1 = aresta inferior)."),
            ]) { ShortCode = "SW" },

        new EffectDescriptor(
            EffectTypeIds.ChromaticAberration,
            "Aberração cromática",
            EffectCategory.Video,
            "Separa o vermelho e o azul radialmente a partir de um centro — franja de lente e um acento nos impactos.",
            [
                new EffectParameterDescriptor(EffectParamNames.Amount, "Quantidade", 0.0, 0.0, 1.0, 0.01, "%",
                    "Até onde os canais vermelho e azul são afastados em direção às margens do fotograma.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.CenterX, "Centro X", 0.5, 0.0, 1.0, 0.01,
                    Description: "Centro horizontal de onde a franja irradia (0 = aresta esquerda, 1 = aresta direita)."),
                new EffectParameterDescriptor(EffectParamNames.CenterY, "Centro Y", 0.5, 0.0, 1.0, 0.01,
                    Description: "Centro vertical de onde a franja irradia (0 = aresta superior, 1 = aresta inferior)."),
            ]) { ShortCode = "CA" },

        new EffectDescriptor(
            EffectTypeIds.ImpactShake,
            "Tremor de impacto",
            EffectCategory.Video,
            "Acrescenta tremor de câmara — o toque final numa explosão ou acerto. Crie fotogramas-chave na Quantidade para o fazer decrescer.",
            [
                new EffectParameterDescriptor(EffectParamNames.Amount, "Quantidade", 0.5, 0.0, 1.0, 0.05, "%",
                    "Intensidade global do tremor — dimensiona o arremesso e a rotação; reduza com fotogramas-chave para o acerto decrescer.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Frequency, "Frequência", 8.0, 0.1, 30.0, 0.5, "Hz",
                    "Rapidez da oscilação do tremor, em tremores por segundo."),
                new EffectParameterDescriptor(EffectParamNames.Rotation, "Rotação", 1.0, 0.0, 15.0, 0.5, "°",
                    "Quanta rotação o tremor acrescenta, em graus."),
                new EffectParameterDescriptor(EffectParamNames.Overscan, "Overscan", 1.05, 1.0, 1.5, 0.01, "%",
                    "Amplia o fotograma para o tremor nunca revelar a margem (100% = sem ampliação).") { DisplayScale = 100 },
            ]) { ShortCode = "IS" },

        new EffectDescriptor(
            EffectTypeIds.Flicker,
            "Cintilação",
            EffectCategory.Video,
            "Pulsa a exposição ao longo do tempo — luz de fogo, luzes práticas a falhar, pulsação de clarão de disparo.",
            [
                new EffectParameterDescriptor(EffectParamNames.Amount, "Quantidade", 0.2, 0.0, 1.0, 0.01, "%",
                    "Até onde a exposição oscila acima e abaixo do normal.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Frequency, "Frequência", 6.0, 0.1, 30.0, 0.5, "Hz",
                    "Rapidez da pulsação da exposição, em ciclos por segundo."),
                new EffectParameterDescriptor(EffectParamNames.Randomness, "Aleatoriedade", 0.5, 0.0, 1.0, 0.05, "%",
                    "Mistura de um pulso estável (0%) para uma cintilação irregular, tipo luz de fogo (100%).") { DisplayScale = 100 },
            ]) { ShortCode = "FL" },

        // ── Mosaic (plan/features/toy-cassette-camera.md, phase 1) — the After Effects / Premiere primitive,
        // with its naming and 10×10 defaults; Edge Softness is our addition (soft-edged pixels, the look of a
        // low-res sensor shown on a TV). The grid spans the layer rect, so it follows the layer's transform
        // and crop rather than the canvas. ──
        new EffectDescriptor(
            EffectTypeIds.Mosaic,
            "Mosaico",
            EffectCategory.Video,
            "Quebra a imagem numa grelha de blocos de cor sólida — pixelização, censura, aspetos retrógrados de baixa resolução.",
            [
                new EffectParameterDescriptor(EffectParamNames.HorizontalBlocks, "Blocos horizontais", 10.0, 1.0, 1920.0, 1.0,
                    Description: "Quantos blocos cabem na largura da camada (mais = blocos mais pequenos).",
                    Kind: ParameterKind.Integer),
                new EffectParameterDescriptor(EffectParamNames.VerticalBlocks, "Blocos verticais", 10.0, 1.0, 1080.0, 1.0,
                    Description: "Quantos blocos cabem na altura da camada (mais = blocos mais pequenos).",
                    Kind: ParameterKind.Integer),
                new EffectParameterDescriptor(EffectParamNames.SharpColors, "Cores nítidas", 0.0, 0.0, 1.0, 1.0,
                    Description: "Preenche cada bloco com a cor do seu centro em vez da média do bloco — blocos mais nítidos e contrastados.",
                    Kind: ParameterKind.Toggle),
                new EffectParameterDescriptor(EffectParamNames.EdgeSoftness, "Suavidade das arestas", 0.0, 0.0, 1.0, 0.05, "%",
                    "Esbatimento suave através das fronteiras dos blocos em vez de arestas duras (0% = blocos nítidos).") { DisplayScale = 100 },
            ]) { ShortCode = "MO" },

        // ── Posterize Time (plan/features/toy-cassette-camera.md, phase 2) — the After Effects / Premiere primitive
        // with its naming and 12 fps default. A time modifier: the clip's video time map reads it (the picture
        // steps at the rate, on a grid anchored to sequence time), the shader pipeline skips it, and its rate is
        // static. Audio is never chopped. ──
        new EffectDescriptor(
            EffectTypeIds.PosterizeTime,
            "Tempo posterizado",
            EffectCategory.Video,
            "Atualiza a imagem a uma taxa de fotogramas inferior, mantendo cada fotograma — movimento entrecortado de baixa taxa. O áudio não é afetado.",
            [
                new EffectParameterDescriptor(EffectParamNames.PosterizeFrameRate, "Taxa de fotogramas", 12.0, 1.0, 60.0, 0.001, "fps",
                    "Quantas vezes por segundo a imagem é atualizada (mais baixo = mais entrecortado). Não admite fotogramas-chave."),
            ]) { ShortCode = "PT", IsTimeModifier = true },

        // ── Toy Cassette Camera (plan/features/toy-cassette-camera.md, phase 3) — the picture side of the
        // late-1980s toy cassette camcorder look in one ordered stage (pixel grid → low-range mono → bloom →
        // smear → tape noise → grain → 4:3 border), cheaper than chaining Mosaic + Black & White + noise since
        // each chained tap re-evaluates upstream. The name is generic; the brand lives only in the description
        // (the B&W film-preset trademark rule). The defaults are the camera's typical look; the presets are the
        // three looks the phase-5 one-tap stacks build on. Pair with Posterize Time at 15 fps for the stutter. ──
        new EffectDescriptor(
            EffectTypeIds.ToyCam,
            "Câmara de cassete de brincar",
            EffectCategory.Video,
            "Inspirada na Fisher-Price PXL 2000 (1987): pixéis pretos e brancos grosseiros, altas luzes resplandecentes e esbatidas, ruído de fita e uma margem preta espessa. Acrescente Tempo posterizado a 15 fps para o seu entrecortar.",
            [
                // Picture (pixel grid)
                new EffectParameterDescriptor(EffectParamNames.HorizontalPixels, "Pixéis horizontais", 120.0, 8.0, 640.0, 1.0,
                    Description: "Quantos pixéis de imagem cabem na largura da janela 4:3 (a câmara gravava cerca de 120).",
                    Kind: ParameterKind.Integer),
                new EffectParameterDescriptor(EffectParamNames.VerticalPixels, "Pixéis verticais", 90.0, 6.0, 480.0, 1.0,
                    Description: "Quantos pixéis de imagem cabem na altura da janela 4:3 (a câmara gravava cerca de 90).",
                    Kind: ParameterKind.Integer),
                new EffectParameterDescriptor(EffectParamNames.PixelSoftness, "Suavidade dos pixéis", 0.15, 0.0, 1.0, 0.05, "%",
                    "Desfoca as fronteiras entre os pixéis — mais ao longo de cada linha, como um sensor de baixa resolução visto numa TV (0% = blocos duros).") { DisplayScale = 100 },
                // Tone
                new EffectParameterDescriptor(EffectParamNames.Contrast, "Contraste", 1.15, 0.0, 2.0, 0.05,
                    Description: "Agrava ou acha os tons em torno do cinzento médio (1.0 = inalterado)."),
                new EffectParameterDescriptor(EffectParamNames.BlackCrush, "Esmagamento dos pretos", 0.08, 0.0, 0.6, 0.01, "%",
                    "Quanto da gama das sombras colapsa para preto sólido — a gama dinâmica estreita da câmara.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.HighlightBloom, "Resplandecência das altas luzes", 0.5, 0.0, 1.0, 0.05, "%",
                    "As zonas claras estouram e resplandecem para os pixéis ao lado.") { DisplayScale = 100 },
                // Smear
                new EffectParameterDescriptor(EffectParamNames.SmearLength, "Comprimento do rasto", 0.35, 0.0, 1.0, 0.05, "%",
                    "Comprimento do rasto tipo cometa que as zonas claras deixam à direita (0% = sem rasto; 100% = oito pixéis).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.SmearThreshold, "Limiar do rasto", 0.78, 0.0, 1.0, 0.01, "%",
                    "Quão claro um píxel tem de ficar antes de deixar rasto (mais baixo = mais imagem deixa rasto).") { DisplayScale = 100 },
                // Tape
                new EffectParameterDescriptor(EffectParamNames.NoiseLines, "Linhas de ruído", 0.2, 0.0, 1.0, 0.05, "%",
                    "Riscos horizontais de ruído de fita que cintilam em linhas aleatórias a cada fotograma.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Dropouts, "Quedas de sinal", 0.15, 0.0, 1.0, 0.05, "%",
                    "Clarões brancos ocasionais onde o sinal da fita cai durante parte de uma linha.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.GrainAmount, "Grão", 0.3, 0.0, 1.0, 0.05, "%",
                    "Cintilação riscada que muda a cada fotograma, como ruído de sensor e fita analógicos.") { DisplayScale = 100 },
                // Border
                new EffectParameterDescriptor(EffectParamNames.BorderSize, "Tamanho da margem", 0.25, 0.0, 0.5, 0.01, "%",
                    "Espessura da margem preta — toda a imagem 4:3 encolhe dentro dela (0% = preenche a maior área 4:3).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.BorderSoftness, "Suavidade da margem", 0.2, 0.0, 1.0, 0.05, "%",
                    "Esbate a aresta da imagem para a margem e arredonda os cantos, como um ecrã de TV (0% = aresta dura e reta).") { DisplayScale = 100 },
                // Variation
                new EffectParameterDescriptor(EffectParamNames.Seed, "Semente", 0.0, 0.0, 999.0, 1.0,
                    Description: "Escolhe um padrão diferente de linhas de ruído, quedas de sinal e grão.",
                    Kind: ParameterKind.Integer),
            ])
        {
            ShortCode = "TC",
            Presets = ToyCamPresets.All,
        },

        // ── Echo (plan/features/toy-cassette-camera.md, phase 6) — the After Effects primitive with its naming,
        // operator list and defaults (one echo, −0.033 s, full intensity, no decay, Add). A temporal effect: the
        // footprint below tells the planner which earlier frames to resolve. Deliberate departures: Echo Time is
        // limited to the past (−1 s…0) — future echoes would need look-ahead decode in the live preview — and
        // Highlight Key is our addition (only bright pixels echo — the toy cassette camera's highlight lag;
        // 0 = AE behaviour). ──
        new EffectDescriptor(
            EffectTypeIds.Echo,
            "Eco",
            EffectCategory.Video,
            "Combina cada fotograma com fotogramas anteriores do clipe — rastos de movimento, imagens fantasma e pilhas estroboscópicas.",
            [
                new EffectParameterDescriptor(EffectParamNames.EchoTime, "Tempo de eco", -0.033, -1.0, 0.0, 0.001, "s",
                    "Tempo entre ecos (negativo = fotogramas anteriores; −0,033 s é um fotograma a 30 fps)."),
                new EffectParameterDescriptor(EffectParamNames.EchoCount, "Número de ecos", 1.0, 0.0, TemporalFootprint.MaxPriorFrames, 1.0,
                    Description: "Quantos fotogramas anteriores são combinados com o atual (0 = sem ecos).",
                    Kind: ParameterKind.Integer),
                new EffectParameterDescriptor(EffectParamNames.StartingIntensity, "Intensidade inicial", 1.0, 0.0, 1.0, 0.01,
                    Description: "Opacidade da primeira imagem da sequência — o fotograma atual."),
                new EffectParameterDescriptor(EffectParamNames.Decay, "Decaimento", 1.0, 0.0, 1.0, 0.01,
                    Description: "Quanto mais ténue é cada eco relativamente ao anterior (1.0 = sem desvanecimento, 0.5 = cada eco com metade da força)."),
                new EffectParameterDescriptor(EffectParamNames.EchoOperator, "Operador de eco", EchoOperators.Add, 0.0, EchoOperators.Names.Count - 1, 1.0,
                    Description: "Como os ecos se combinam: Add, Maximum (rastos claros), Minimum, Screen, Composite in Back/Front ou Blend (média).",
                    Kind: ParameterKind.Dropdown, Choices: EchoOperators.Names),
                new EffectParameterDescriptor(EffectParamNames.HighlightKey, "Chave de altas luzes", 0.0, 0.0, 1.0, 0.01, "%",
                    "Só os pixéis mais claros do que isto deixam rasto (0% = tudo ecoa; mais alto = só as altas luzes).") { DisplayScale = 100 },
            ])
        {
            ShortCode = "EC",
            TemporalFootprint = EchoFootprint,
        },

        new EffectDescriptor(
            EffectTypeIds.Color,
            "Cor",
            EffectCategory.Color,
            "Ajuste de exposição, contraste, saturação e vividez.",
            [
                new EffectParameterDescriptor(EffectParamNames.Exposure, "Exposição", 0.0, -3.0, 3.0, 0.1, "EV",
                    "Clareia ou escurece em passos fotográficos (+1 EV duplica a luz)."),
                new EffectParameterDescriptor(EffectParamNames.Contrast, "Contraste", 1.0, 0.0, 2.0, 0.05,
                    Description: "Agrava ou acha os tons em torno do cinzento médio (1.0 = inalterado)."),
                new EffectParameterDescriptor(EffectParamNames.Saturation, "Saturação", 1.0, 0.0, 2.0, 0.05,
                    Description: "Intensidade geral da cor (0 = tons de cinzento, 1.0 = inalterado)."),
                new EffectParameterDescriptor(EffectParamNames.Vibrance, "Vividez", 0.0, -1.0, 1.0, 0.05,
                    Description: "Realça mais as cores discretas do que as já vivas, protegindo os tons de pele."),
            ]) { ShortCode = "CO" },

        // ── Colour grading toolset (PLAN.md step 34) — SkSL registry effects, like ACES Filmic. ──
        new EffectDescriptor(
            EffectTypeIds.WhiteBalance,
            "Balanço de brancos",
            EffectCategory.Color,
            "Correção de temperatura e matiz, aplicada em luz linear.",
            [
                new EffectParameterDescriptor(EffectParamNames.Temperature, "Temperatura", 0.0, -100.0, 100.0, 1.0,
                    Description: "Desloca as cores para mais quentes (laranja, +) ou mais frias (azul, −)."),
                new EffectParameterDescriptor(EffectParamNames.Tint, "Matiz", 0.0, -100.0, 100.0, 1.0,
                    Description: "Desloca as cores para o magenta (+) ou o verde (−)."),
            ]) { ShortCode = "WB" },

        new EffectDescriptor(
            EffectTypeIds.ColorWheels,
            "Rodas de cor",
            EffectCategory.Color,
            "Gradação em três vias: elevação (sombras), gama (tons médios), ganho (altas luzes), global + RGB.",
            [
                new EffectParameterDescriptor(EffectParamNames.LiftMaster, "Elevação", 0.0, -1.0, 1.0, 0.005,
                    Description: "Sobe ou desce as sombras (tons mais escuros)."),
                new EffectParameterDescriptor(EffectParamNames.LiftR, "Elevação R", 0.0, -1.0, 1.0, 0.005,
                    Description: "Equilíbrio de vermelho das sombras."),
                new EffectParameterDescriptor(EffectParamNames.LiftG, "Elevação G", 0.0, -1.0, 1.0, 0.005,
                    Description: "Equilíbrio de verde das sombras."),
                new EffectParameterDescriptor(EffectParamNames.LiftB, "Elevação B", 0.0, -1.0, 1.0, 0.005,
                    Description: "Equilíbrio de azul das sombras."),
                new EffectParameterDescriptor(EffectParamNames.GammaMaster, "Gama", 0.0, -1.0, 1.0, 0.005,
                    Description: "Sobe ou desce os tons médios."),
                new EffectParameterDescriptor(EffectParamNames.GammaR, "Gama R", 0.0, -1.0, 1.0, 0.005,
                    Description: "Equilíbrio de vermelho dos tons médios."),
                new EffectParameterDescriptor(EffectParamNames.GammaG, "Gama G", 0.0, -1.0, 1.0, 0.005,
                    Description: "Equilíbrio de verde dos tons médios."),
                new EffectParameterDescriptor(EffectParamNames.GammaB, "Gama B", 0.0, -1.0, 1.0, 0.005,
                    Description: "Equilíbrio de azul dos tons médios."),
                new EffectParameterDescriptor(EffectParamNames.GainMaster, "Ganho", 0.0, -1.0, 1.0, 0.005,
                    Description: "Sobe ou desce as altas luzes (tons mais claros)."),
                new EffectParameterDescriptor(EffectParamNames.GainR, "Ganho R", 0.0, -1.0, 1.0, 0.005,
                    Description: "Equilíbrio de vermelho das altas luzes."),
                new EffectParameterDescriptor(EffectParamNames.GainG, "Ganho G", 0.0, -1.0, 1.0, 0.005,
                    Description: "Equilíbrio de verde das altas luzes."),
                new EffectParameterDescriptor(EffectParamNames.GainB, "Ganho B", 0.0, -1.0, 1.0, 0.005,
                    Description: "Equilíbrio de azul das altas luzes."),
            ]) { ShortCode = "CW" },

        new EffectDescriptor(
            EffectTypeIds.Curves,
            "Curvas",
            EffectCategory.Color,
            "Curvas paramétricas RGB + por canal: cinco pontos desviam a identidade de cada canal.",
            [
                new EffectParameterDescriptor(EffectParamNames.CurveMasterBlacks, "Pretos RGB", 0.0, -1.0, 1.0, 0.01,
                    Description: "Desvia o ponto de preto em todos os canais."),
                new EffectParameterDescriptor(EffectParamNames.CurveMasterShadows, "Sombras RGB", 0.0, -1.0, 1.0, 0.01,
                    Description: "Sobe ou desce as sombras em todos os canais."),
                new EffectParameterDescriptor(EffectParamNames.CurveMasterMids, "Tons médios RGB", 0.0, -1.0, 1.0, 0.01,
                    Description: "Sobe ou desce os tons médios em todos os canais."),
                new EffectParameterDescriptor(EffectParamNames.CurveMasterHighlights, "Altas luzes RGB", 0.0, -1.0, 1.0, 0.01,
                    Description: "Sobe ou desce as altas luzes em todos os canais."),
                new EffectParameterDescriptor(EffectParamNames.CurveMasterWhites, "Brancos RGB", 0.0, -1.0, 1.0, 0.01,
                    Description: "Desvia o ponto de branco em todos os canais."),
                new EffectParameterDescriptor(EffectParamNames.CurveRedBlacks, "Pretos vermelhos", 0.0, -1.0, 1.0, 0.01,
                    Description: "Desvia o ponto de preto no canal vermelho."),
                new EffectParameterDescriptor(EffectParamNames.CurveRedShadows, "Sombras vermelhas", 0.0, -1.0, 1.0, 0.01,
                    Description: "Sobe ou desce as sombras no canal vermelho."),
                new EffectParameterDescriptor(EffectParamNames.CurveRedMids, "Tons médios vermelhos", 0.0, -1.0, 1.0, 0.01,
                    Description: "Sobe ou desce os tons médios no canal vermelho."),
                new EffectParameterDescriptor(EffectParamNames.CurveRedHighlights, "Altas luzes vermelhas", 0.0, -1.0, 1.0, 0.01,
                    Description: "Sobe ou desce as altas luzes no canal vermelho."),
                new EffectParameterDescriptor(EffectParamNames.CurveRedWhites, "Brancos vermelhos", 0.0, -1.0, 1.0, 0.01,
                    Description: "Desvia o ponto de branco no canal vermelho."),
                new EffectParameterDescriptor(EffectParamNames.CurveGreenBlacks, "Pretos verdes", 0.0, -1.0, 1.0, 0.01,
                    Description: "Desvia o ponto de preto no canal verde."),
                new EffectParameterDescriptor(EffectParamNames.CurveGreenShadows, "Sombras verdes", 0.0, -1.0, 1.0, 0.01,
                    Description: "Sobe ou desce as sombras no canal verde."),
                new EffectParameterDescriptor(EffectParamNames.CurveGreenMids, "Tons médios verdes", 0.0, -1.0, 1.0, 0.01,
                    Description: "Sobe ou desce os tons médios no canal verde."),
                new EffectParameterDescriptor(EffectParamNames.CurveGreenHighlights, "Altas luzes verdes", 0.0, -1.0, 1.0, 0.01,
                    Description: "Sobe ou desce as altas luzes no canal verde."),
                new EffectParameterDescriptor(EffectParamNames.CurveGreenWhites, "Brancos verdes", 0.0, -1.0, 1.0, 0.01,
                    Description: "Desvia o ponto de branco no canal verde."),
                new EffectParameterDescriptor(EffectParamNames.CurveBlueBlacks, "Pretos azuis", 0.0, -1.0, 1.0, 0.01,
                    Description: "Desvia o ponto de preto no canal azul."),
                new EffectParameterDescriptor(EffectParamNames.CurveBlueShadows, "Sombras azuis", 0.0, -1.0, 1.0, 0.01,
                    Description: "Sobe ou desce as sombras no canal azul."),
                new EffectParameterDescriptor(EffectParamNames.CurveBlueMids, "Tons médios azuis", 0.0, -1.0, 1.0, 0.01,
                    Description: "Sobe ou desce os tons médios no canal azul."),
                new EffectParameterDescriptor(EffectParamNames.CurveBlueHighlights, "Altas luzes azuis", 0.0, -1.0, 1.0, 0.01,
                    Description: "Sobe ou desce as altas luzes no canal azul."),
                new EffectParameterDescriptor(EffectParamNames.CurveBlueWhites, "Brancos azuis", 0.0, -1.0, 1.0, 0.01,
                    Description: "Desvia o ponto de branco no canal azul."),
            ]) { ShortCode = "CV" },

        new EffectDescriptor(
            EffectTypeIds.HslQualifier,
            "Qualificador HSL",
            EffectCategory.Color,
            "Isola uma gama de matiz/saturação/luminância e grada apenas os pixéis isolados.",
            [
                new EffectParameterDescriptor(EffectParamNames.HueCenter, "Centro do matiz", 0.0, 0.0, 360.0, 1.0, "°",
                    "O matiz que a chave seleciona, em graus no círculo cromático (0° = vermelho)."),
                new EffectParameterDescriptor(EffectParamNames.HueWidth, "Largura do matiz", 60.0, 0.0, 180.0, 1.0, "°",
                    "Até onde, de cada lado do matiz central, a chave alcança."),
                new EffectParameterDescriptor(EffectParamNames.HueSoftness, "Suavidade do matiz", 20.0, 0.0, 90.0, 1.0, "°",
                    "Queda esbatida para além da largura do matiz."),
                new EffectParameterDescriptor(EffectParamNames.SatLow, "Saturação mín.", 0.0, 0.0, 1.0, 0.01,
                    Description: "Limite inferior da gama de saturação que a chave seleciona."),
                new EffectParameterDescriptor(EffectParamNames.SatHigh, "Saturação máx.", 1.0, 0.0, 1.0, 0.01,
                    Description: "Limite superior da gama de saturação que a chave seleciona."),
                new EffectParameterDescriptor(EffectParamNames.LumaLow, "Luminância mín.", 0.0, 0.0, 1.0, 0.01,
                    Description: "Limite inferior da gama de brilho que a chave seleciona."),
                new EffectParameterDescriptor(EffectParamNames.LumaHigh, "Luminância máx.", 1.0, 0.0, 1.0, 0.01,
                    Description: "Limite superior da gama de brilho que a chave seleciona."),
                new EffectParameterDescriptor(EffectParamNames.RangeSoftness, "Suavidade", 0.1, 0.0, 0.5, 0.01,
                    Description: "Queda esbatida nas margens das gamas de saturação e luminância."),
                new EffectParameterDescriptor(EffectParamNames.HueShift, "Desvio de matiz", 0.0, -180.0, 180.0, 1.0, "°",
                    "Roda o matiz dos pixéis isolados."),
                new EffectParameterDescriptor(EffectParamNames.Saturation, "Saturação", 1.0, 0.0, 2.0, 0.05,
                    Description: "Intensidade da cor dos pixéis isolados (1.0 = inalterado)."),
                new EffectParameterDescriptor(EffectParamNames.Exposure, "Exposição", 0.0, -3.0, 3.0, 0.1, "EV",
                    "Clareia ou escurece os pixéis isolados, em passos fotográficos."),
                new EffectParameterDescriptor(EffectParamNames.ShowMask, "Mostrar máscara", 0.0, 0.0, 1.0, 1.0,
                    Description: "Mostra a chave como uma máscara a preto e branco em vez da imagem gradada.",
                    Kind: ParameterKind.Toggle),
            ]) { ShortCode = "HQ" },

        // ── Black & White (plan/features/black-and-white.md) — a registry SkSL effect like the grading toolset.
        // Phase 1: conversion core + film tone response. Grain/vignette/toning and the preset library follow. ──
        new EffectDescriptor(
            EffectTypeIds.BlackWhite,
            "Preto e branco",
            EffectCategory.Color,
            "Monocromo estilo filme: misturador de canais por matiz, filtro ótico de cor e uma resposta tonal.",
            [
                // Conversion
                new EffectParameterDescriptor(EffectParamNames.Mix, "Quantidade", 1.0, 0.0, 1.0, 0.05, "%",
                    "Mistura com o original a cores (100% = monocromo total, 0% = inalterado).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.FilterHue, "Matiz do filtro", 0.0, 0.0, 360.0, 1.0, "°",
                    "Cor de um filtro ótico virtual sobre a objetiva (ex.: um filtro vermelho escurece os céus)."),
                new EffectParameterDescriptor(EffectParamNames.FilterStrength, "Intensidade do filtro", 0.0, 0.0, 1.0, 0.05, "%",
                    "Com que força o filtro ótico é aplicado (0% = sem filtro).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.MixReds, "Vermelhos", 0.0, -100.0, 100.0, 1.0,
                    Description: "Quão claros ou escuros ficam os tons vermelhos (positivo = mais claros)."),
                new EffectParameterDescriptor(EffectParamNames.MixOranges, "Laranjas", 0.0, -100.0, 100.0, 1.0,
                    Description: "Quão claros ou escuros ficam os tons laranja (positivo = mais claros)."),
                new EffectParameterDescriptor(EffectParamNames.MixYellows, "Amarelos", 0.0, -100.0, 100.0, 1.0,
                    Description: "Quão claros ou escuros ficam os tons amarelos (positivo = mais claros)."),
                new EffectParameterDescriptor(EffectParamNames.MixGreens, "Verdes", 0.0, -100.0, 100.0, 1.0,
                    Description: "Quão claros ou escuros ficam os tons verdes (positivo = mais claros)."),
                new EffectParameterDescriptor(EffectParamNames.MixAquas, "Azuis-verdes", 0.0, -100.0, 100.0, 1.0,
                    Description: "Quão claros ou escuros ficam os tons azul-esverdeados (positivo = mais claros)."),
                new EffectParameterDescriptor(EffectParamNames.MixBlues, "Azuis", 0.0, -100.0, 100.0, 1.0,
                    Description: "Quão claros ou escuros ficam os tons azuis (positivo = mais claros)."),
                new EffectParameterDescriptor(EffectParamNames.MixPurples, "Roxos", 0.0, -100.0, 100.0, 1.0,
                    Description: "Quão claros ou escuros ficam os tons roxos (positivo = mais claros)."),
                new EffectParameterDescriptor(EffectParamNames.MixMagentas, "Magentas", 0.0, -100.0, 100.0, 1.0,
                    Description: "Quão claros ou escuros ficam os tons magenta (positivo = mais claros)."),
                // Film (tone response)
                new EffectParameterDescriptor(EffectParamNames.Exposure, "Brilho", 0.0, -3.0, 3.0, 0.1, "EV",
                    "Clareia ou escurece a imagem monocromática, em passos fotográficos."),
                new EffectParameterDescriptor(EffectParamNames.Contrast, "Contraste", 1.0, 0.0, 2.0, 0.05,
                    Description: "Agrava ou acha os tons em torno do cinzento médio (1.0 = inalterado)."),
                new EffectParameterDescriptor(EffectParamNames.Shadows, "Sombras", 0.0, -1.0, 1.0, 0.05,
                    Description: "Sobe (+) ou esmaga (−) os tons mais escuros — o pé do filme."),
                new EffectParameterDescriptor(EffectParamNames.Highlights, "Altas luzes", 0.0, -1.0, 1.0, 0.05,
                    Description: "Sobe (+) ou enrola (−) os tons mais claros — o ombro do filme."),
                new EffectParameterDescriptor(EffectParamNames.GrainAmount, "Grão", 0.0, 0.0, 1.0, 0.05, "%",
                    "Grão de filme procedural (ponderado pela luminância — mais forte nos tons médios, como uma emulsão real).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.GrainSize, "Tamanho do grão", 1.0, 0.5, 4.0, 0.1,
                    Description: "Tamanho da célula de grão em pixéis de origem — sensível à resolução, para 1080p e 4K coincidirem."),
                new EffectParameterDescriptor(EffectParamNames.GrainSeedLock, "Grão estático", 0.0, 0.0, 1.0, 1.0,
                    Description: "Um campo de grão fixo para todo o clipe (para fixos/stop motion) em vez de renovar a semente a cada fotograma.",
                    Kind: ParameterKind.Toggle),
                // Finishing
                new EffectParameterDescriptor(EffectParamNames.ToneHue, "Matiz da tonalização", 35.0, 0.0, 360.0, 1.0, "°",
                    "Cor da tonalização geral (35° ≈ sépia)."),
                new EffectParameterDescriptor(EffectParamNames.ToneStrength, "Intensidade da tonalização", 0.0, 0.0, 1.0, 0.05, "%",
                    "Com que força a tonalização de tom único é aplicada (0% = neutro).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.SplitShadowHue, "Matiz das sombras", 35.0, 0.0, 360.0, 1.0, "°",
                    "Cor da tonalização dividida para as sombras."),
                new EffectParameterDescriptor(EffectParamNames.SplitHighlightHue, "Matiz das altas luzes", 210.0, 0.0, 360.0, 1.0, "°",
                    "Cor da tonalização dividida para as altas luzes."),
                new EffectParameterDescriptor(EffectParamNames.SplitStrength, "Intensidade da divisão", 0.0, 0.0, 1.0, 0.05, "%",
                    "Com que força a tonalização dividida é aplicada (0% = neutro).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.SplitBalance, "Balanço da divisão", 0.0, -1.0, 1.0, 0.05,
                    Description: "Desloca a transição sombra/alta luz para as sombras (−) ou as altas luzes (+)."),
                new EffectParameterDescriptor(EffectParamNames.VignetteAmount, "Vinheta", 0.0, -1.0, 1.0, 0.05,
                    Description: "Escurece (−) ou clareia (+) as margens do fotograma."),
                new EffectParameterDescriptor(EffectParamNames.VignetteSize, "Tamanho da vinheta", 0.7, 0.0, 1.5, 0.05,
                    Description: "Raio da vinheta relativamente à meia-diagonal do fotograma."),
                new EffectParameterDescriptor(EffectParamNames.VignetteSoftness, "Suavidade da vinheta", 0.5, 0.0, 1.0, 0.05,
                    Description: "Com que suavidade a vinheta se desvanece do centro para a margem."),
            ])
        {
            ShortCode = "BW",
            // The non-film preset library (Neutral / Filters / Toning / Cinematic, ~30 presets) lives in
            // BlackWhitePresets to keep this file readable; the 19 film-stock presets land in phase 5. Every
            // preset leaves Mix untouched (the Studio Reverb rule) and touches only its family's parameters so
            // filters/tonings/tonal looks layer — see the BlackWhitePresets doc comment.
            Presets = BlackWhitePresets.All,
        },

        // ── Day for Night (plan/features/special-effects.md, phase 4) — one purpose-built grade, the Final Cut
        // Pro "Day into Night" / Magic Bullet shape, rather than a hand-assembled stack: the stage applies its
        // corrections in the fixed order a colourist would (exposure → sky → shoulder → saturation → tint →
        // floor → vignette), so no control can be stacked in the wrong place. The defaults are a usable
        // general-purpose night at full strength; the presets are the three looks in the roadmap. ──
        new EffectDescriptor(
            EffectTypeIds.DayForNight,
            "Dia para noite",
            EffectCategory.Color,
            "Transforma um exterior diurno numa noite ao luar: céu mais escuro, altas luzes enroladas, matiz fria, candeeiros acesos.",
            [
                new EffectParameterDescriptor(EffectParamNames.NightStrength, "Intensidade noturna", 1.0, 0.0, 1.0, 0.05, "%",
                    "Mistura do plano diurno original (0%) para a gradação noturna completa (100%).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Exposure, "Exposição", -2.0, -4.0, 0.0, 0.1, "EV",
                    "Quanto toda a tomada fica subexposta, em passos fotográficos."),
                new EffectParameterDescriptor(EffectParamNames.SkyDarken, "Céu", 0.6, 0.0, 1.0, 0.05, "%",
                    "Baixa um céu claro ou azulado na parte superior do fotograma para ler-se como noite, não como nublado.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.HighlightRolloff, "Altas luzes", 0.6, 0.0, 1.0, 0.05, "%",
                    "Enrola as altas luzes diurnas sob um ombro suave — o luar não tem brancos quentes.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.ShadowFloor, "Piso das sombras", 0.02, 0.0, 0.25, 0.005, "%",
                    "Sobe os pretos até um ténue piso ao luar para as sombras manterem algum detalhe em vez de colapsarem.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Saturation, "Saturação", 0.4, 0.0, 1.0, 0.05, "%",
                    "Quanta cor sobrevive (a visão noturna é quase incolor; 100% = inalterado).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.MoonlightTint, "Luar (intensidade)", 0.6, 0.0, 1.0, 0.05, "%",
                    "Força do laivo frio de luar, que mantém o brilho enquanto desloca a cor.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.MoonlightHue, "Matiz do luar", 215.0, 0.0, 360.0, 1.0, "°",
                    "Cor do laivo de luar (215° ≈ noite azul clássica; abaixo = azul-petróleo, acima = violeta)."),
                new EffectParameterDescriptor(EffectParamNames.PracticalLights, "Luzes práticas", 0.5, 0.0, 1.0, 0.05, "%",
                    "Mantém as fontes claras e quentes — candeeiros, janelas iluminadas, fogo — a resplandecer através da gradação.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.ProtectSkin, "Proteger tons de pele", 0.3, 0.0, 1.0, 0.05, "%",
                    "Mantém alguma cor natural nos tons de pele para os rostos não ficarem cinento-azulados (o brilho continua a escurecer).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.VignetteAmount, "Vinheta", 0.3, 0.0, 1.0, 0.05, "%",
                    "Escurece as margens do fotograma, conduzindo o olhar para um centro ao luar.") { DisplayScale = 100 },
            ])
        {
            ShortCode = "DN",
            Presets = DayForNightPresets.All,
        },

        // ── Creative LUT (plan/features/looks-browser.md) — a tier-2 creative look from a user .cube file, through
        // the input color transform's packed-LUT GPU stage. Kept a separate type from the tier-1 transform below
        // (ARCHITECTURE §18) so a camera conversion can never be mistaken for, or stacked as, a look. ──
        new EffectDescriptor(
            EffectTypeIds.CreativeLut,
            "LUT criativa",
            EffectCategory.Color,
            "Aplica um look LUT criativo .cube a imagens Rec.709, misturado pela Intensidade.",
            [
                new EffectParameterDescriptor(EffectParamNames.LutFile, "Ficheiro LUT", 0.0, 0.0, 0.0, 1.0,
                    Description: "Uma LUT 3D criativa em formato .cube, feita para imagens Rec.709 (não é uma conversão de log de câmara).",
                    Kind: ParameterKind.Asset) { AssetType = new AssetFileType("Cube LUT", ["cube"]) },
                new EffectParameterDescriptor(EffectParamNames.Mix, "Intensidade", 1.0, 0.0, 1.0, 0.05, "%",
                    "Mistura entre o original (0%) e o look completo (100%).") { DisplayScale = 100 },
            ]) { ShortCode = "LU" },

        new EffectDescriptor(
            EffectTypeIds.ColorTransform,
            "Transformação de cor de entrada",
            EffectCategory.Color,
            "Converte uma fonte log (DJI, ARRI, Sony, Panasonic, Canon, Blackmagic, Fujifilm ou Nikon) para Rec.709.",
            [
                new EffectParameterDescriptor(EffectParamNames.SourceProfile, "Perfil de origem",
                    0.0, 0.0, ColorProfiles.All.Count - 1, 1.0,
                    Description: "O perfil log da câmara em que as imagens foram gravadas.",
                    Kind: ParameterKind.Dropdown, Choices: ColorProfiles.DisplayNames),
            ]) { ShortCode = "CT" },

        new EffectDescriptor(
            EffectTypeIds.AcesFilmic,
            "ACES Filmic",
            EffectCategory.Color,
            "Mapeamento tonal ACES filmic em luz de cena (aproximação RRT + ODT) com exposição.",
            [
                new EffectParameterDescriptor(EffectParamNames.Exposure, "Exposição", 0.0, -8.0, 8.0, 0.1, "EV",
                    "Exposição de cena aplicada antes do mapeamento tonal, em passos fotográficos."),
            ]) { ShortCode = "AF" },

        new EffectDescriptor(
            EffectTypeIds.Brightness,
            "Brilho",
            EffectCategory.Color,
            "Multiplica o brilho da imagem (1.0 = inalterado).",
            [
                new EffectParameterDescriptor(EffectParamNames.Amount, "Quantidade", 1.0, 0.0, 4.0, 0.05,
                    Description: "Multiplica o brilho da imagem (1.0 = inalterado, 2.0 = o dobro do brilho)."),
            ]) { ShortCode = "BR" },

        new EffectDescriptor(
            EffectTypeIds.Fade,
            "Desvanecimento",
            EffectCategory.Video,
            "Rampa de opacidade — conduz em simultâneo a alpha do vídeo e o ganho do áudio.",
            [
                new EffectParameterDescriptor(EffectParamNames.Opacity, "Opacidade", 1.0, 0.0, 1.0, 0.05, "%",
                    "Opacidade do clipe — também dimensiona em passo o ganho de áudio do clipe.") { DisplayScale = 100 },
            ]) { ShortCode = "FD" },

        // ── Audio chain stages (PLAN.md step 31) — executed by the mixer, not the shader pipeline. ──
        new EffectDescriptor(
            EffectTypeIds.AudioGain,
            "Ganho / Balanço",
            EffectCategory.Audio,
            "Ajusta o nível e o balanço estéreo.",
            [
                new EffectParameterDescriptor(EffectParamNames.GainDb, "Ganho", 0.0, -24.0, 24.0, 0.5, "dB",
                    "Ajuste de volume em decibéis (0 = inalterado)."),
                new EffectParameterDescriptor(EffectParamNames.Pan, "Balanço", 0.0, -1.0, 1.0, 0.05,
                    Description: "Balanço estéreo (−1 = totalmente à esquerda, +1 = totalmente à direita)."),
            ]) { ShortCode = "GP" },

        new EffectDescriptor(
            EffectTypeIds.AudioEq,
            "Equalização paramétrica",
            EffectCategory.Audio,
            "Equalização de três bandas: prateleira grave, pico médio, prateleira aguda.",
            [
                new EffectParameterDescriptor(EffectParamNames.LowGainDb, "Ganho grave", 0.0, -15.0, 15.0, 0.5, "dB",
                    "Aumenta ou corta abaixo da frequência da prateleira grave."),
                new EffectParameterDescriptor(EffectParamNames.LowFreq, "Frequência grave", 100.0, 20.0, 500.0, 5.0, "Hz",
                    "Frequência de canto da prateleira grave."),
                new EffectParameterDescriptor(EffectParamNames.MidGainDb, "Ganho médio", 0.0, -15.0, 15.0, 0.5, "dB",
                    "Aumenta ou corta em torno da frequência central da banda média."),
                new EffectParameterDescriptor(EffectParamNames.MidFreq, "Frequência média", 1000.0, 200.0, 8000.0, 50.0, "Hz",
                    "Frequência central da banda de pico média."),
                new EffectParameterDescriptor(EffectParamNames.MidQ, "Q médio", 1.0, 0.3, 8.0, 0.1,
                    Description: "Largura da banda média (mais alto = mais estreita)."),
                new EffectParameterDescriptor(EffectParamNames.HighGainDb, "Ganho agudo", 0.0, -15.0, 15.0, 0.5, "dB",
                    "Aumenta ou corta acima da frequência da prateleira aguda."),
                new EffectParameterDescriptor(EffectParamNames.HighFreq, "Frequência aguda", 8000.0, 2000.0, 16000.0, 100.0, "Hz",
                    "Frequência de canto da prateleira aguda."),
            ]) { ShortCode = "EQ" },

        new EffectDescriptor(
            EffectTypeIds.AudioCompressor,
            "Compressor",
            EffectCategory.Audio,
            "Uniformiza a dinâmica: atenua os picos acima do limiar.",
            [
                new EffectParameterDescriptor(EffectParamNames.ThresholdDb, "Limiar", -18.0, -60.0, 0.0, 0.5, "dB",
                    "Nível acima do qual a compressão começa."),
                new EffectParameterDescriptor(EffectParamNames.Ratio, "Rácio", 4.0, 1.0, 20.0, 0.5,
                    Description: "Com que força os picos acima do limiar são reduzidos (4 = 4 dB de entrada → 1 dB de saída)."),
                new EffectParameterDescriptor(EffectParamNames.AttackMs, "Ataque", 10.0, 0.1, 200.0, 1.0, "ms",
                    "Rapidez com que a compressão age quando o sinal ultrapassa o limiar."),
                new EffectParameterDescriptor(EffectParamNames.ReleaseMs, "Libertação", 100.0, 10.0, 1000.0, 10.0, "ms",
                    "Rapidez com que a compressão larga depois de o sinal cair abaixo do limiar."),
                new EffectParameterDescriptor(EffectParamNames.MakeupDb, "Compensação", 0.0, 0.0, 24.0, 0.5, "dB",
                    "Ganho de saída para repor a sonoridade perdida com a compressão."),
            ]) { ShortCode = "CP" },

        new EffectDescriptor(
            EffectTypeIds.AudioReverb,
            "Reverberação (leve)",
            EffectCategory.Audio,
            "Acrescenta ambiente de sala (estilo Freeverb) — a reverberação editorial de baixo CPU.",
            [
                new EffectParameterDescriptor(EffectParamNames.RoomSize, "Tamanho da sala", 0.5, 0.0, 1.0, 0.05, "%",
                    "Tamanho aparente da sala simulada — maior = cauda mais longa.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Damping, "Amortecimento", 0.5, 0.0, 1.0, 0.05, "%",
                    "Rapidez com que as frequências altas se extinguem na cauda.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Mix, "Mistura", 0.3, 0.0, 1.0, 0.05, "%",
                    "Balanço seco/efeito (0% = só seco, 100% = só efeito).") { DisplayScale = 100 },
            ]) { ShortCode = "RV" },

        new EffectDescriptor(
            EffectTypeIds.AudioStudioReverb,
            "Reverberação de estúdio",
            EffectCategory.Audio,
            "Reverberação algorítmica de alta qualidade: salas, placas, salões (tanque estilo Dattorro).",
            [
                new EffectParameterDescriptor(EffectParamNames.PreDelayMs, "Pré-atraso", 10.0, 0.0, 200.0, 1.0, "ms",
                    "Intervalo entre o som seco e o início da reverberação."),
                new EffectParameterDescriptor(EffectParamNames.Decay, "Decaimento", 2.0, 0.1, 20.0, 0.1, "s",
                    "Quanto tempo a cauda da reverberação demora a extinguir-se."),
                new EffectParameterDescriptor(EffectParamNames.Size, "Tamanho", 0.5, 0.0, 1.0, 0.05, "%",
                    "Tamanho aparente do espaço simulado.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Diffusion, "Difusão", 0.7, 0.0, 1.0, 0.05, "%",
                    "Densidade de ecos — baixa = repetições discretas, alta = cortina suave.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.ModDepth, "Profundidade de modulação", 0.3, 0.0, 1.0, 0.05, "%",
                    "Quantidade de modulação de afinação na cauda (acrescenta movimento tipo coro).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.ModRateHz, "Taxa de modulação", 0.5, 0.05, 5.0, 0.05, "Hz",
                    "Velocidade da modulação de afinação da cauda."),
                new EffectParameterDescriptor(EffectParamNames.EarlyLate, "Início / fim", 0.7, 0.0, 1.0, 0.05, "%",
                    "Balanço entre reflexões iniciais (0%) e a cauda tardia (100%).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Width, "Largura", 1.0, 0.0, 1.0, 0.05, "%",
                    "Abertura estéreo da reverberação (0% = mono, 100% = estéreo total).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.LowDamp, "Amortecimento grave", 0.1, 0.0, 1.0, 0.05, "%",
                    "Rapidez com que as frequências graves se extinguem na cauda.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.HighDamp, "Amortecimento agudo", 0.4, 0.0, 1.0, 0.05, "%",
                    "Rapidez com que as frequências altas se extinguem na cauda.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Mix, "Mistura", 0.3, 0.0, 1.0, 0.05, "%",
                    "Balanço seco/efeito (0% = só seco, 100% = só efeito).") { DisplayScale = 100 },
            ])
        {
            ShortCode = "SR",
            // The step-41 preset families (room / chamber / plate / hall / cathedral / ambient bloom); the
            // shimmer/cloud/nonlinear creative tiers ship as their own effects (steps 49–50). Every preset
            // leaves Mix untouched so switching character keeps the user's wet/dry blend.
            Presets =
            [
                new EffectPreset("Room", new Dictionary<string, double>
                {
                    [EffectParamNames.PreDelayMs] = 5, [EffectParamNames.Decay] = 0.6,
                    [EffectParamNames.Size] = 0.35, [EffectParamNames.Diffusion] = 0.65,
                    [EffectParamNames.ModDepth] = 0.15, [EffectParamNames.ModRateHz] = 0.8,
                    [EffectParamNames.EarlyLate] = 0.45, [EffectParamNames.Width] = 0.8,
                    [EffectParamNames.LowDamp] = 0.25, [EffectParamNames.HighDamp] = 0.55,
                }),
                new EffectPreset("Chamber", new Dictionary<string, double>
                {
                    [EffectParamNames.PreDelayMs] = 10, [EffectParamNames.Decay] = 1.1,
                    [EffectParamNames.Size] = 0.45, [EffectParamNames.Diffusion] = 0.75,
                    [EffectParamNames.ModDepth] = 0.2, [EffectParamNames.ModRateHz] = 0.6,
                    [EffectParamNames.EarlyLate] = 0.55, [EffectParamNames.Width] = 0.9,
                    [EffectParamNames.LowDamp] = 0.2, [EffectParamNames.HighDamp] = 0.45,
                }),
                new EffectPreset("Plate", new Dictionary<string, double>
                {
                    [EffectParamNames.PreDelayMs] = 0, [EffectParamNames.Decay] = 1.8,
                    [EffectParamNames.Size] = 0.5, [EffectParamNames.Diffusion] = 0.9,
                    [EffectParamNames.ModDepth] = 0.35, [EffectParamNames.ModRateHz] = 1.0,
                    [EffectParamNames.EarlyLate] = 0.85, [EffectParamNames.Width] = 1.0,
                    [EffectParamNames.LowDamp] = 0.1, [EffectParamNames.HighDamp] = 0.35,
                }),
                new EffectPreset("Hall", new Dictionary<string, double>
                {
                    [EffectParamNames.PreDelayMs] = 20, [EffectParamNames.Decay] = 2.8,
                    [EffectParamNames.Size] = 0.75, [EffectParamNames.Diffusion] = 0.7,
                    [EffectParamNames.ModDepth] = 0.25, [EffectParamNames.ModRateHz] = 0.4,
                    [EffectParamNames.EarlyLate] = 0.7, [EffectParamNames.Width] = 1.0,
                    [EffectParamNames.LowDamp] = 0.15, [EffectParamNames.HighDamp] = 0.4,
                }),
                new EffectPreset("Cathedral", new Dictionary<string, double>
                {
                    [EffectParamNames.PreDelayMs] = 40, [EffectParamNames.Decay] = 6.0,
                    [EffectParamNames.Size] = 1.0, [EffectParamNames.Diffusion] = 0.8,
                    [EffectParamNames.ModDepth] = 0.2, [EffectParamNames.ModRateHz] = 0.3,
                    [EffectParamNames.EarlyLate] = 0.85, [EffectParamNames.Width] = 1.0,
                    [EffectParamNames.LowDamp] = 0.05, [EffectParamNames.HighDamp] = 0.5,
                }),
                new EffectPreset("Ambient Bloom", new Dictionary<string, double>
                {
                    [EffectParamNames.PreDelayMs] = 60, [EffectParamNames.Decay] = 10.0,
                    [EffectParamNames.Size] = 0.9, [EffectParamNames.Diffusion] = 1.0,
                    [EffectParamNames.ModDepth] = 0.5, [EffectParamNames.ModRateHz] = 0.7,
                    [EffectParamNames.EarlyLate] = 1.0, [EffectParamNames.Width] = 1.0,
                    [EffectParamNames.LowDamp] = 0.3, [EffectParamNames.HighDamp] = 0.25,
                }),
            ],
        },

        // ── Delay family (PLAN.md step 46) — separate purpose-built effects, the DAW convention. ──
        new EffectDescriptor(
            EffectTypeIds.AudioDelayDigital,
            "Atraso digital",
            EffectCategory.Audio,
            "Atraso com realimentação limpa e corte agudo no caminho de realimentação.",
            [
                new EffectParameterDescriptor(EffectParamNames.DelayMs, "Tempo", 500.0, 1.0, 2000.0, 1.0, "ms",
                    "Intervalo entre o som seco e cada repetição."),
                new EffectParameterDescriptor(EffectParamNames.Feedback, "Realimentação", 0.35, 0.0, 1.0, 0.05, "%",
                    "Quanto de cada repetição realimenta — mais alto, mais repetições.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.HighCutHz, "Corte agudo", 8000.0, 200.0, 20000.0, 100.0, "Hz",
                    "Filtra os agudos das repetições — mais baixo = ecos mais escuros."),
                new EffectParameterDescriptor(EffectParamNames.Mix, "Mistura", 0.3, 0.0, 1.0, 0.05, "%",
                    "Balanço seco/efeito (0% = só seco, 100% = só efeito).") { DisplayScale = 100 },
            ]) { ShortCode = "DD" },

        new EffectDescriptor(
            EffectTypeIds.AudioDelayTape,
            "Atraso de fita",
            EffectCategory.Audio,
            "Atraso com realimentação e coloração de fita: saturação, repetições a escurecer, wow & flutter.",
            [
                new EffectParameterDescriptor(EffectParamNames.DelayMs, "Tempo", 500.0, 1.0, 2000.0, 1.0, "ms",
                    "Intervalo entre o som seco e cada repetição."),
                new EffectParameterDescriptor(EffectParamNames.Feedback, "Realimentação", 0.4, 0.0, 1.0, 0.05, "%",
                    "Quanto de cada repetição realimenta — mais alto, mais repetições.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.WowFlutterDepth, "Wow / Flutter", 0.25, 0.0, 1.0, 0.05, "%",
                    "Quantidade de oscilação de afinação estilo fita nas repetições.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.WowFlutterRateHz, "Taxa de wow", 1.0, 0.1, 10.0, 0.1, "Hz",
                    "Velocidade da oscilação de afinação."),
                new EffectParameterDescriptor(EffectParamNames.Drive, "Saturação", 0.3, 0.0, 1.0, 0.05, "%",
                    "Carga de fita — acrescenta calor e aspereza às repetições.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Mix, "Mistura", 0.3, 0.0, 1.0, 0.05, "%",
                    "Balanço seco/efeito (0% = só seco, 100% = só efeito).") { DisplayScale = 100 },
            ]) { ShortCode = "TD" },

        new EffectDescriptor(
            EffectTypeIds.AudioDelayMultiTap,
            "Atraso multitap",
            EffectCategory.Audio,
            "Até oito tomadas independentes, cada uma com o seu tempo, nível e balanço.",
            MultiTapParameters()) { ShortCode = "MT" },

        new EffectDescriptor(
            EffectTypeIds.AudioDelayStereo,
            "Atraso estéreo",
            EffectCategory.Audio,
            "Tempos de atraso esquerdo/direito independentes com um modo Ping-pong de cruzamento.",
            [
                new EffectParameterDescriptor(EffectParamNames.LeftTimeMs, "Tempo esquerdo", 375.0, 1.0, 2000.0, 1.0, "ms",
                    "Tempo de atraso das repetições do canal esquerdo."),
                new EffectParameterDescriptor(EffectParamNames.RightTimeMs, "Tempo direito", 500.0, 1.0, 2000.0, 1.0, "ms",
                    "Tempo de atraso das repetições do canal direito."),
                new EffectParameterDescriptor(EffectParamNames.Feedback, "Realimentação", 0.35, 0.0, 1.0, 0.05, "%",
                    "Quanto de cada repetição realimenta — mais alto, mais repetições.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.PingPong, "Ping-pong", 0.0, 0.0, 1.0, 1.0,
                    Description: "Faz as repetições saltarem alternadamente entre a esquerda e a direita.",
                    Kind: ParameterKind.Toggle),
                new EffectParameterDescriptor(EffectParamNames.CrossFeed, "Cruzamento", 1.0, 0.0, 1.0, 0.05, "%",
                    "Quanto as repetições de cada canal sangram para o outro lado.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Mix, "Mistura", 0.3, 0.0, 1.0, 0.05, "%",
                    "Balanço seco/efeito (0% = só seco, 100% = só efeito).") { DisplayScale = 100 },
            ]) { ShortCode = "SD" },

        // ── Noise Gate (PLAN.md step 47) — the standard DAW gate/expander design. ──
        new EffectDescriptor(
            EffectTypeIds.AudioNoiseGate,
            "Porta de ruído",
            EffectCategory.Audio,
            "Atenua o sinal abaixo de um limiar: ataque / manutenção / libertação, piso de gama, histerese.",
            [
                new EffectParameterDescriptor(EffectParamNames.ThresholdDb, "Limiar", -40.0, -80.0, 0.0, 0.5, "dB",
                    "Nível abaixo do qual a porta fecha e atenua o sinal."),
                new EffectParameterDescriptor(EffectParamNames.AttackMs, "Ataque", 1.0, 0.01, 100.0, 0.1, "ms",
                    "Rapidez com que a porta abre quando o sinal sobe acima do limiar."),
                new EffectParameterDescriptor(EffectParamNames.HoldMs, "Manter", 50.0, 0.0, 1000.0, 5.0, "ms",
                    "Tempo mínimo que a porta fica aberta depois de o sinal cair."),
                new EffectParameterDescriptor(EffectParamNames.ReleaseMs, "Libertação", 100.0, 1.0, 2000.0, 10.0, "ms",
                    "Rapidez com que a porta fecha depois de o tempo de manutenção expirar."),
                new EffectParameterDescriptor(EffectParamNames.RangeDb, "Gama", -80.0, -80.0, 0.0, 1.0, "dB",
                    "Quanto a porta fechada baixa o sinal (−80 dB ≈ silêncio)."),
                new EffectParameterDescriptor(EffectParamNames.HysteresisDb, "Histerese", 3.0, 0.0, 24.0, 0.5, "dB",
                    "Intervalo entre os limiares de abrir e de fechar, evitando trepidação rápida."),
            ]) { ShortCode = "NG" },

        // ── Shelving EQ (PLAN.md step 48) — standalone low/high shelves for quick tone shaping. ──
        new EffectDescriptor(
            EffectTypeIds.AudioShelvingEq,
            "Equalização de prateleiras",
            EffectCategory.Audio,
            "Prateleiras grave + aguda isoladas (frequência, ganho, inclinação, ativação por prateleira) para inclinação / calor / ar rápidos.",
            [
                new EffectParameterDescriptor(EffectParamNames.LowFreq, "Frequência grave", 100.0, 20.0, 500.0, 5.0, "Hz",
                    "Frequência de canto da prateleira grave."),
                new EffectParameterDescriptor(EffectParamNames.LowGainDb, "Ganho grave", 0.0, -15.0, 15.0, 0.5, "dB",
                    "Aumenta ou corta abaixo da frequência da prateleira grave."),
                new EffectParameterDescriptor(EffectParamNames.LowSlope, "Inclinação grave", 1.0, 0.1, 2.0, 0.05,
                    Description: "Inclinação da transição da prateleira grave."),
                new EffectParameterDescriptor(EffectParamNames.LowEnable, "Prateleira grave", 1.0, 0.0, 1.0, 1.0,
                    Description: "Ativa a prateleira grave.", Kind: ParameterKind.Toggle),
                new EffectParameterDescriptor(EffectParamNames.HighFreq, "Frequência aguda", 8000.0, 2000.0, 16000.0, 100.0, "Hz",
                    "Frequência de canto da prateleira aguda."),
                new EffectParameterDescriptor(EffectParamNames.HighGainDb, "Ganho agudo", 0.0, -15.0, 15.0, 0.5, "dB",
                    "Aumenta ou corta acima da frequência da prateleira aguda."),
                new EffectParameterDescriptor(EffectParamNames.HighSlope, "Inclinação aguda", 1.0, 0.1, 2.0, 0.05,
                    Description: "Inclinação da transição da prateleira aguda."),
                new EffectParameterDescriptor(EffectParamNames.HighEnable, "Prateleira aguda", 1.0, 0.0, 1.0, 1.0,
                    Description: "Ativa a prateleira aguda.", Kind: ParameterKind.Toggle),
            ]) { ShortCode = "SE" },

        // ── Shimmer Reverb (PLAN.md step 50) — the "Creative Reverb ▸ shimmer" tier as its own effect. ──
        new EffectDescriptor(
            EffectTypeIds.AudioShimmerReverb,
            "Reverberação cintilante",
            EffectCategory.Audio,
            "Cortina de reverberação etérea com subida de afinação: um caminho de realimentação deslocado uma oitava sob uma cauda convencional.",
            [
                new EffectParameterDescriptor(EffectParamNames.ShimmerAmount, "Cintilância", 0.5, 0.0, 1.0, 0.05, "%",
                    "Quanto sinal com afinação subida alimenta a cauda — mais alto = mais etéreo.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.ShimmerInterval, "Intervalo", 12.0, 1.0, 12.0, 1.0, "st",
                    "Desvio de afinação do caminho de cintilância, em semitons (12 = uma oitava acima).",
                    Kind: ParameterKind.Integer),
                new EffectParameterDescriptor(EffectParamNames.Size, "Tamanho", 0.5, 0.0, 1.0, 0.05, "%",
                    "Tamanho aparente do espaço simulado.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Decay, "Decaimento", 4.0, 0.1, 20.0, 0.1, "s",
                    "Quanto tempo a cauda da reverberação demora a extinguir-se."),
                new EffectParameterDescriptor(EffectParamNames.Damping, "Amortecimento", 0.3, 0.0, 1.0, 0.05, "%",
                    "Rapidez com que as frequências altas se extinguem na cauda.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Mix, "Mistura", 0.3, 0.0, 1.0, 0.05, "%",
                    "Balanço seco/efeito (0% = só seco, 100% = só efeito).") { DisplayScale = 100 },
            ])
        {
            ShortCode = "SH",
            // The step-50 preset family. Like the Studio Reverb presets, every preset leaves Mix untouched
            // so switching character keeps the user's wet/dry blend.
            Presets =
            [
                new EffectPreset("Classic Shimmer", new Dictionary<string, double>
                {
                    [EffectParamNames.ShimmerAmount] = 0.6, [EffectParamNames.ShimmerInterval] = 12,
                    [EffectParamNames.Size] = 0.7, [EffectParamNames.Decay] = 5.0,
                    [EffectParamNames.Damping] = 0.25,
                }),
                new EffectPreset("Dark Shimmer", new Dictionary<string, double>
                {
                    [EffectParamNames.ShimmerAmount] = 0.45, [EffectParamNames.ShimmerInterval] = 12,
                    [EffectParamNames.Size] = 0.6, [EffectParamNames.Decay] = 4.0,
                    [EffectParamNames.Damping] = 0.75,
                }),
                new EffectPreset("Fifth Shimmer", new Dictionary<string, double>
                {
                    [EffectParamNames.ShimmerAmount] = 0.55, [EffectParamNames.ShimmerInterval] = 7,
                    [EffectParamNames.Size] = 0.65, [EffectParamNames.Decay] = 4.5,
                    [EffectParamNames.Damping] = 0.3,
                }),
                new EffectPreset("Drone / Infinite", new Dictionary<string, double>
                {
                    [EffectParamNames.ShimmerAmount] = 1.0, [EffectParamNames.ShimmerInterval] = 12,
                    [EffectParamNames.Size] = 0.9, [EffectParamNames.Decay] = 20.0,
                    [EffectParamNames.Damping] = 0.1,
                }),
            ],
        },

        // ── Convolution Reverb (PLAN.md step 49) — the acoustic-emulation tier as its own effect. The IR is
        // an Asset descriptor (a file reference, not a number) so the Inspector builds a file-picker row; no
        // IRs are bundled at day one (licensing), so a fresh instance passes the dry signal through until the
        // user imports one. No factory presets: with user IRs the "preset" IS the impulse response.
        new EffectDescriptor(
            EffectTypeIds.AudioConvolutionReverb,
            "Reverberação por convolução",
            EffectCategory.Audio,
            "Emulação de espaço acústico: convolui o sinal com uma resposta de impulso capturada (WAV).",
            [
                new EffectParameterDescriptor(EffectParamNames.ImpulseResponse, "Resposta de impulso", 0.0, 0.0, 0.0, 1.0,
                    Description: "O espaço capturado — uma resposta de impulso WAV mono ou estéreo (até 10 s).",
                    Kind: ParameterKind.Asset) { AssetType = new AssetFileType("WAV audio", ["wav"]) },
                new EffectParameterDescriptor(EffectParamNames.PreDelayMs, "Pré-atraso", 0.0, 0.0, 200.0, 1.0, "ms",
                    "Intervalo entre o som seco e o início da reverberação."),
                new EffectParameterDescriptor(EffectParamNames.IrLength, "Comprimento", 1.0, 0.05, 1.0, 0.05, "%",
                    "Quanto da resposta de impulso soa — mais curto aparra a cauda sem uma nova RI.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.LowDamp, "Amortecimento grave", 0.0, 0.0, 1.0, 0.05, "%",
                    "Enfraquece as frequências graves da cauda da reverberação.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.HighDamp, "Amortecimento agudo", 0.0, 0.0, 1.0, 0.05, "%",
                    "Escurece a cauda da reverberação cortando as frequências altas.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Width, "Largura", 1.0, 0.0, 1.0, 0.05, "%",
                    "Abertura estéreo da reverberação (0% = mono, 100% = conforme capturada).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.Mix, "Mistura", 0.3, 0.0, 1.0, 0.05, "%",
                    "Balanço seco/efeito (0% = só seco, 100% = só efeito).") { DisplayScale = 100 },
            ]) { ShortCode = "IR" },

        // ── Cassette (plan/features/toy-cassette-camera.md, phase 4) — the "recorded on cassette" sound as one
        // stage, and the audio half of the Toy Cassette Camera look. The defaults are a cheap camcorder's mono
        // cassette track; the presets share the video look's names so the phase-5 stacks pair them. ──
        new EffectDescriptor(
            EffectTypeIds.AudioCassette,
            "Cassete",
            EffectCategory.Audio,
            "Som de gravador de cassete barato: mono, banda estreita, sopro de fita, wow & flutter, saturação e ganho automático pulsante.",
            [
                // Recording
                new EffectParameterDescriptor(EffectParamNames.Mono, "Mono", 1.0, 0.0, 1.0, 1.0,
                    Description: "Dobra o som para um só canal, como um gravador de microfone único.",
                    Kind: ParameterKind.Toggle),
                new EffectParameterDescriptor(EffectParamNames.AgcAmount, "Quantidade de AGC", 0.5, 0.0, 1.0, 0.05, "%",
                    "Controlo automático de ganho: os sons baixos sobem e os altos descem, pelo que o nível pulsa (0% = desligado).") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.ReleaseMs, "Libertação do AGC", 600.0, 50.0, 3000.0, 10.0, "ms",
                    "Com que lentidão o ganho automático recupera depois de um som forte — mais curto pulsa mais depressa."),
                new EffectParameterDescriptor(EffectParamNames.Drive, "Saturação", 0.3, 0.0, 1.0, 0.05, "%",
                    "Carga de fita — os sons altos suavizam-se e ganham aspereza.") { DisplayScale = 100 },
                // Tape
                new EffectParameterDescriptor(EffectParamNames.LowCutHz, "Corte grave", 100.0, 20.0, 1000.0, 5.0, "Hz",
                    "Remove os graves abaixo desta frequência (24 dB/oit) — um altifalante e um microfone minúsculos."),
                new EffectParameterDescriptor(EffectParamNames.HighCutHz, "Corte agudo", 5000.0, 1000.0, 20000.0, 100.0, "Hz",
                    "Remove os agudos acima desta frequência (24 dB/oit) — mais baixo = fita mais surda."),
                new EffectParameterDescriptor(EffectParamNames.HissDb, "Sopro", -42.0, -90.0, -20.0, 1.0, "dB",
                    "Nível do sopro de fita sob o som (−90 dB = desligado)."),
                new EffectParameterDescriptor(EffectParamNames.WowFlutterDepth, "Wow / Flutter", 0.25, 0.0, 1.0, 0.05, "%",
                    "Quantidade de oscilação de afinação da velocidade da fita, com uma ligeira oscilação de nível.") { DisplayScale = 100 },
                new EffectParameterDescriptor(EffectParamNames.WowFlutterRateHz, "Taxa de wow", 0.9, 0.1, 10.0, 0.1, "Hz",
                    "Velocidade da oscilação de afinação."),
                new EffectParameterDescriptor(EffectParamNames.Mix, "Mistura", 1.0, 0.0, 1.0, 0.05, "%",
                    "Balanço seco/efeito (0% = só seco, 100% = só efeito).") { DisplayScale = 100 },
            ])
        {
            ShortCode = "CS",
            Presets = CassettePresets.All,
        },
    ];

    /// <summary>
    /// The Multi-Tap Delay's per-tap parameter grid (PLAN.md step 46): enable / time / level / pan ×
    /// <see cref="EffectParamNames.MultiTapCount"/> taps, then Mix. Defaults give two audible taps (an
    /// eighth-note-ish 150/300 ms pattern) with the rest staged at later times, disabled. The Inspector's
    /// generic one-row-per-parameter layout renders all 33 rows — a compact custom tap-grid section is a
    /// flagged UI follow-up, not a blocker (per the step-46 note).
    /// </summary>
    private static EffectParameterDescriptor[] MultiTapParameters()
    {
        var parameters = new EffectParameterDescriptor[EffectParamNames.MultiTapCount * 4 + 1];
        for (int i = 0; i < EffectParamNames.MultiTapCount; i++)
        {
            int tap = i + 1;
            parameters[i * 4 + 0] = new EffectParameterDescriptor(
                EffectParamNames.TapEnable[i], $"Tomada {tap}", i < 2 ? 1.0 : 0.0, 0.0, 1.0, 1.0,
                Description: $"Ativa a tomada {tap}.", Kind: ParameterKind.Toggle);
            parameters[i * 4 + 1] = new EffectParameterDescriptor(
                EffectParamNames.TapTimeMs[i], $"Tomada {tap} · tempo", 150.0 * tap, 1.0, 2000.0, 1.0, "ms",
                $"Tempo de atraso da tomada {tap}.");
            parameters[i * 4 + 2] = new EffectParameterDescriptor(
                EffectParamNames.TapLevel[i], $"Tomada {tap} · nível", Math.Round(1.0 - i * 0.1, 2), 0.0, 1.0, 0.05, "%",
                $"Volume da tomada {tap}.") { DisplayScale = 100 };
            parameters[i * 4 + 3] = new EffectParameterDescriptor(
                EffectParamNames.TapPan[i], $"Tomada {tap} · balanço", 0.0, -1.0, 1.0, 0.05,
                Description: $"Posição estéreo da tomada {tap} (−1 = esquerda, +1 = direita).");
        }
        parameters[^1] = new EffectParameterDescriptor(EffectParamNames.Mix, "Mistura", 0.3, 0.0, 1.0, 0.05, "%",
            "Balanço seco/efeito (0% = só seco, 100% = só efeito).") { DisplayScale = 100 };
        return parameters;
    }

    // Plugin-registered descriptors (PLAN.md step 33). Swapped atomically as a whole array so readers
    // (including the render graph's per-frame IsAudio routing) never see a partially-mutated list.
    private static readonly object RegistrationGate = new();
    private static volatile EffectDescriptor[] _registered = [];

    /// <summary>Built-in plus plugin-registered descriptors, built-ins first, in registration order.</summary>
    public static IReadOnlyList<EffectDescriptor> All
    {
        get
        {
            EffectDescriptor[] registered = _registered;
            return registered.Length == 0 ? BuiltIns : [.. BuiltIns, .. registered];
        }
    }

    /// <summary>
    /// Registers a plugin effect descriptor (PLAN.md step 33). Returns <see langword="false"/> — leaving the
    /// catalog unchanged — if the id is already taken or uses the reserved <c>builtin.</c> prefix.
    /// </summary>
    public static bool Register(EffectDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (RegistrationGate)
        {
            if (descriptor.Id.StartsWith("builtin.", StringComparison.Ordinal) || Find(descriptor.Id) is not null)
                return false;
            _registered = [.. _registered, descriptor];
            return true;
        }
    }

    /// <summary>Removes a plugin-registered descriptor (built-ins cannot be removed). Returns whether it was present.</summary>
    public static bool Unregister(string effectTypeId)
    {
        lock (RegistrationGate)
        {
            EffectDescriptor[] next = _registered.Where(d => d.Id != effectTypeId).ToArray();
            if (next.Length == _registered.Length)
                return false;
            _registered = next;
            return true;
        }
    }

    /// <summary>The descriptors in a given category, in display order.</summary>
    public static IEnumerable<EffectDescriptor> InCategory(EffectCategory category) =>
        All.Where(d => d.Category == category);

    /// <summary>Looks up a descriptor by effect type id, or returns <see langword="null"/> if it is not registered.</summary>
    public static EffectDescriptor? Find(string effectTypeId)
    {
        foreach (EffectDescriptor d in BuiltIns)
            if (d.Id == effectTypeId)
                return d;
        foreach (EffectDescriptor d in _registered)
            if (d.Id == effectTypeId)
                return d;
        return null;
    }

    /// <summary>Whether <paramref name="effectTypeId"/> names a registered time-modifier effect
    /// (<see cref="EffectDescriptor.IsTimeModifier"/>).</summary>
    public static bool IsTimeModifier(string effectTypeId) => Find(effectTypeId)?.IsTimeModifier == true;

    /// <summary>Whether <paramref name="effectTypeId"/> names a registered temporal effect — one whose descriptor
    /// supplies a <see cref="EffectDescriptor.TemporalFootprint"/> (plan/features/toy-cassette-camera.md phase 6).</summary>
    public static bool IsTemporal(string effectTypeId) => Find(effectTypeId)?.TemporalFootprint is not null;

    /// <summary>
    /// Echo's temporal footprint (<see cref="EffectDescriptor.TemporalFootprint"/>): Number of Echoes prior frames
    /// (whole, 0–8), Echo Time apart. A hand-edited non-finite / out-of-range value reads as the descriptor's clamp,
    /// so the footprint is always bounded; a zero Echo Time reads no prior frame (every echo would be the current one).
    /// </summary>
    private static TemporalFootprint EchoFootprint(IReadOnlyDictionary<string, double> values)
    {
        double count = values.TryGetValue(EffectParamNames.EchoCount, out double c) && double.IsFinite(c) ? c : 1.0;
        double seconds = values.TryGetValue(EffectParamNames.EchoTime, out double s) && double.IsFinite(s) ? s : -0.033;
        int k = (int)Math.Clamp(Math.Round(count), 0, TemporalFootprint.MaxPriorFrames);
        long spacing = (long)Math.Round(Math.Clamp(seconds, -1.0, 0.0) * Timecode.TicksPerSecond);
        return spacing == 0 || k == 0 ? TemporalFootprint.None : new TemporalFootprint(k, new Timecode(spacing));
    }

    /// <summary>A friendly display name for an effect type id, falling back to the id itself for unknown (plugin) ids.</summary>
    public static string DisplayName(string effectTypeId) => Find(effectTypeId)?.DisplayName ?? effectTypeId;
}
