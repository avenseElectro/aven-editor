using Sprocket.Core.Timing;

namespace Sprocket.Core.Model;

/// <summary>
/// A browsable description of one generator type (PLAN.md step 19): its stable id (<see cref="GeneratorTypeIds"/>),
/// a display name, a one-line description, and a factory for a default <see cref="GeneratorSpec"/>. The Project
/// panel lists these as synthetic bin items the user can drop on the timeline, mirroring <see cref="EffectCatalog"/>
/// for effects; a plugin generator (later) would register here too.
/// </summary>
/// <param name="Id">The generator type id (matches <see cref="GeneratorSpec.GeneratorTypeId"/>).</param>
/// <param name="DisplayName">Human-readable name for the browser.</param>
/// <param name="Description">A one-line summary shown under the name.</param>
public sealed record GeneratorDescriptor(string Id, string DisplayName, string Description)
{
    private readonly Func<GeneratorSpec>? _factory;

    /// <summary>Internal ctor capturing the default-spec factory.</summary>
    internal GeneratorDescriptor(string id, string displayName, string description, Func<GeneratorSpec> factory)
        : this(id, displayName, description) => _factory = factory;

    /// <summary>
    /// The generator's editable numeric parameters, in display order, described exactly the way an effect's are
    /// (<see cref="EffectParameterDescriptor"/>) so the Inspector builds the rows from the registration instead
    /// of hard-coding a control per generator. Empty for the title family, whose bespoke TEXT sections predate
    /// this and carry text/font controls a generic list cannot express.
    /// </summary>
    public IReadOnlyList<EffectParameterDescriptor> Parameters { get; init; } = [];

    /// <summary>The generator's editable colour attributes, in display order (each a <c>#AARRGGBB</c> string in
    /// <see cref="GeneratorSpec.Strings"/>). Same purpose as <see cref="Parameters"/>, for the one non-numeric
    /// attribute the procedural generators need.</summary>
    public IReadOnlyList<GeneratorColorDescriptor> Colors { get; init; } = [];

    /// <summary>
    /// Builds a fresh <see cref="GeneratorSpec"/> with this type's default parameters — from the explicit
    /// factory when one was registered, otherwise seeded from <see cref="Parameters"/>/<see cref="Colors"/>
    /// so a descriptor-driven generator states each default exactly once.
    /// </summary>
    public GeneratorSpec CreateSpec()
    {
        if (_factory is not null)
            return _factory();

        var spec = new GeneratorSpec(Id);
        foreach (EffectParameterDescriptor p in Parameters)
            spec.Set(p.Name, p.Default);
        foreach (GeneratorColorDescriptor c in Colors)
            spec.SetString(c.Name, c.Default);
        return spec;
    }

    /// <summary>
    /// Builds a fresh generator <see cref="Clip"/> of this type, <paramref name="duration"/> long, placed at
    /// <paramref name="timelineStart"/> — what dropping the bin item on a track produces.
    /// </summary>
    public Clip CreateClip(Timecode duration, Timecode timelineStart) =>
        Clip.CreateGenerator(CreateSpec(), duration, timelineStart);
}

/// <summary>
/// One editable colour attribute of a generator: its key in <see cref="GeneratorSpec.Strings"/>, the Inspector
/// label, and the <c>#AARRGGBB</c> value a fresh spec gets.
/// </summary>
/// <param name="Name">The string-parameter key (matches <see cref="GeneratorParamNames"/>).</param>
/// <param name="DisplayName">Human-readable label shown in the Inspector.</param>
/// <param name="Default">The <c>#AARRGGBB</c> colour a freshly created spec is given.</param>
public sealed record GeneratorColorDescriptor(string Name, string DisplayName, string Default);

/// <summary>
/// The registry of built-in generators (PLAN.md step 19). The Project panel and "insert generator" actions list
/// over this, so a new generator's bin entry falls out of registering it here rather than hard-coding the UI.
/// </summary>
public static class GeneratorCatalog
{
    /// <summary>The default length a freshly inserted generator/adjustment clip spans (NLE convention ~5 s).</summary>
    public static Timecode DefaultDuration { get; } = Timecode.FromSeconds(5);

    /// <summary>All registered generator descriptors, in display order.</summary>
    public static IReadOnlyList<GeneratorDescriptor> BuiltIns { get; } =
    [
        new GeneratorDescriptor(
            GeneratorTypeIds.Title, "Título", "Texto centrado sobre um fundo transparente.",
            () => new GeneratorSpec(GeneratorTypeIds.Title)
                .SetString(GeneratorParamNames.Text, "Título")
                .SetString(GeneratorParamNames.Color, "#FFFFFFFF")
                .SetString(GeneratorParamNames.BackgroundColor, "#00000000")
                .Set(GeneratorParamNames.FontSize, 0.12)),

        // The step-40 title templates share the Title render path (rich text + scroll); each id is distinct so
        // the browser/timeline label them and their defaults differ (PLAN.md step 40).
        new GeneratorDescriptor(
            GeneratorTypeIds.LowerThird, "Terço inferior", "Nome + função sobre uma barra de fundo, em baixo à esquerda.",
            () => new GeneratorSpec(GeneratorTypeIds.LowerThird)
                .SetString(GeneratorParamNames.Text, "Nome")
                .SetString(GeneratorParamNames.Text2, "Função")
                .SetString(GeneratorParamNames.Color, "#FFFFFFFF")
                .SetString(GeneratorParamNames.BackgroundColor, "#00000000")
                .SetString(GeneratorParamNames.BoxColor, "#B4101418")
                .SetString(GeneratorParamNames.Alignment, "left")
                .Set(GeneratorParamNames.FontSize, 0.07)
                .Set(GeneratorParamNames.FontSize2, 0.045)
                .Set(GeneratorParamNames.BoxPadding, 0.02)
                .Set(GeneratorParamNames.PositionX, 0.24)
                .Set(GeneratorParamNames.PositionY, 0.82)),

        new GeneratorDescriptor(
            GeneratorTypeIds.Roll, "Rolagem de créditos", "Créditos multilinha em rolagem de baixo para cima.",
            () => new GeneratorSpec(GeneratorTypeIds.Roll)
                .SetString(GeneratorParamNames.Text, "Créditos\nRealização\nNome\nProdução\nNome")
                .SetString(GeneratorParamNames.Color, "#FFFFFFFF")
                .SetString(GeneratorParamNames.BackgroundColor, "#00000000")
                .SetString(GeneratorParamNames.ScrollMode, TitleScrollModes.Roll)
                .Set(GeneratorParamNames.FontSize, 0.06)),

        new GeneratorDescriptor(
            GeneratorTypeIds.Crawl, "Letreiro", "Uma única linha a deslizar da direita para a esquerda.",
            () => new GeneratorSpec(GeneratorTypeIds.Crawl)
                .SetString(GeneratorParamNames.Text, "Texto do letreiro")
                .SetString(GeneratorParamNames.Color, "#FFFFFFFF")
                .SetString(GeneratorParamNames.BackgroundColor, "#00000000")
                .SetString(GeneratorParamNames.ScrollMode, TitleScrollModes.Crawl)
                .Set(GeneratorParamNames.FontSize, 0.06)
                .Set(GeneratorParamNames.PositionY, 0.85)),

        new GeneratorDescriptor(
            GeneratorTypeIds.SolidColor, "Cor sólida", "Um preenchimento de cor sólida.",
            () => new GeneratorSpec(GeneratorTypeIds.SolidColor)
                .SetString(GeneratorParamNames.Color, "#FF1E6FFF")),

        // -- Atmospherics (plan/features/special-effects.md, phase 2) ------------------------------------
        // Procedural mood layers: drop one on an upper video track (Screen/Add for the luminous ones, Normal
        // for smoke and fog) and it composites like any other clip, carrying effects and keyframes. Two shared
        // render paths - fractal noise clouds and a hashed particle field - with the defaults, not the code,
        // making each entry read as smoke / fog / dust / embers / sparks. Every default below is stated once
        // here and flows into the spec, the browser, the Inspector and MCP.

        new GeneratorDescriptor(GeneratorTypeIds.Smoke, "Fumo",
            "Fumo fractal à deriva sobre um fotograma transparente.")
        {
            Parameters = CloudParameters(amount: 0.4, scale: 2.6, speed: 1.0, direction: 75, detail: 0.5,
                softness: 0.7, falloff: 0.0),
            Colors = [new GeneratorColorDescriptor(GeneratorParamNames.Color, "Matiz", "#FFB9BEC4")],
        },

        new GeneratorDescriptor(GeneratorTypeIds.Fog, "Nevoeiro",
            "Neblina densa e lenta que assenta no fundo do fotograma.")
        {
            Parameters = CloudParameters(amount: 0.55, scale: 1.6, speed: 0.3, direction: 0, detail: 0.3,
                softness: 0.85, falloff: 0.6),
            Colors = [new GeneratorColorDescriptor(GeneratorParamNames.Color, "Matiz", "#FFC8D2DC")],
        },

        new GeneratorDescriptor(GeneratorTypeIds.Dust, "Poeira",
            "Partículas de poeira suspensas no ar.")
        {
            Parameters = ParticleParameters(amount: 0.4, count: 10, size: 0.0075, speed: 0.15, direction: 200,
                spread: 0.5, flicker: 0.25, streak: 0.0, falloff: 0.0),
            Colors = [new GeneratorColorDescriptor(GeneratorParamNames.Color, "Matiz", "#FFF0E4C8")],
        },

        new GeneratorDescriptor(GeneratorTypeIds.Embers, "Brasas",
            "Brasas de fogo a subir, quentes e cintilantes.")
        {
            Parameters = ParticleParameters(amount: 0.8, count: 16, size: 0.0075, speed: 0.55, direction: 90,
                spread: 0.7, flicker: 0.8, streak: 0.35, falloff: 0.8),
            Colors = [new GeneratorColorDescriptor(GeneratorParamNames.Color, "Matiz", "#FFFF8A2B")],
        },

        new GeneratorDescriptor(GeneratorTypeIds.Sparks, "Faíscas",
            "Faíscas rápidas de percussão com rastos de movimento.")
        {
            Parameters = ParticleParameters(amount: 0.9, count: 12, size: 0.008, speed: 1.6, direction: 75,
                spread: 0.6, flicker: 0.5, streak: 0.9, falloff: 0.85),
            Colors = [new GeneratorColorDescriptor(GeneratorParamNames.Color, "Matiz", "#FFFFD37A")],
        },

        new GeneratorDescriptor(GeneratorTypeIds.LightLeak, "Fuga de luz",
            "Uma fuga de luz de filme em ângulo. Melhor numa pista superior definida como Escrita (Screen).")
        {
            Parameters =
            [
                Amount(0.45),
                new EffectParameterDescriptor(GeneratorParamNames.Direction, "Direção", 25, 0, 360, 1, "°",
                    "Ângulo da banda de fuga de luz, no sentido anti-horário a partir da direita do ecrã."),
                new EffectParameterDescriptor(GeneratorParamNames.PositionX, "Posição X", 0.85, 0, 1, 0.005,
                    Description: "Onde a fuga está centrada na largura do fotograma."),
                new EffectParameterDescriptor(GeneratorParamNames.PositionY, "Posição Y", 0.3, 0, 1, 0.005,
                    Description: "Onde a fuga está centrada na altura do fotograma."),
                new EffectParameterDescriptor(GeneratorParamNames.Size, "Largura", 0.11, 0.01, 0.8, 0.005,
                    Description: "Meia-largura da banda como fração da altura do fotograma."),
                Softness(0.6),
                Speed(0.5),
                Seed(),
            ],
            Colors = [new GeneratorColorDescriptor(GeneratorParamNames.Color, "Matiz", "#FFFF6A28")],
        },
    ];

    // -- Shared atmospheric parameter sets ---------------------------------------------------------------
    // Smoke/Fog and Dust/Embers/Sparks each share one render path, so they share one parameter list too; only
    // the defaults passed in differ. Sizes and positions are fractions of the frame height and rates are per
    // second, which is what keeps a generator identical at preview and export resolution (ARCHITECTURE.md 5).

    private static EffectParameterDescriptor Amount(double def) =>
        new(GeneratorParamNames.Amount, "Quantidade", def, 0, 1, 0.01, "%",
            "Densidade global. 0 não renderiza nada.") { DisplayScale = 100 };

    private static EffectParameterDescriptor Speed(double def) =>
        new(GeneratorParamNames.Speed, "Velocidade", def, 0, 4, 0.05,
            Description: "Ritmo de animação. 0 congela o aspeto num fotograma imóvel.");

    private static EffectParameterDescriptor Direction(double def) =>
        new(GeneratorParamNames.Direction, "Direção", def, 0, 360, 1, "°",
            "Direção do fluxo, no sentido anti-horário a partir da direita do ecrã — 90° deriva para cima no fotograma.");

    private static EffectParameterDescriptor Softness(double def) =>
        new(GeneratorParamNames.Softness, "Suavidade", def, 0, 1, 0.01, "%",
            "Com que suavidade as arestas caem.") { DisplayScale = 100 };

    private static EffectParameterDescriptor Seed() =>
        new(GeneratorParamNames.Seed, "Semente", 0, 0, 999, 1,
            Description: "Relança a disposição aleatória sem alterar mais nada.",
            Kind: ParameterKind.Integer);

    private static EffectParameterDescriptor[] CloudParameters(
        double amount, double scale, double speed, double direction, double detail, double softness, double falloff) =>
    [
        Amount(amount),
        new EffectParameterDescriptor(GeneratorParamNames.Scale, "Escala", scale, 0.5, 12, 0.1,
            Description: "Tamanho dos elementos, em células na altura do fotograma — mais alto é mais fino."),
        Speed(speed),
        Direction(direction),
        new EffectParameterDescriptor(GeneratorParamNames.Detail, "Detalhe", detail, 0, 1, 0.01, "%",
            "Detalhe fractal fino: baixo é fumo macio em rolos, alto é esfumado.") { DisplayScale = 100 },
        Softness(softness),
        new EffectParameterDescriptor(GeneratorParamNames.Falloff, "Tendência para o solo", falloff, 0, 1, 0.01, "%",
            "Faz a nuvem assentar no fundo do fotograma (nevoeiro de solo).") { DisplayScale = 100 },
        Seed(),
    ];

    private static EffectParameterDescriptor[] ParticleParameters(
        double amount, double count, double size, double speed, double direction, double spread,
        double flicker, double streak, double falloff) =>
    [
        Amount(amount),
        new EffectParameterDescriptor(GeneratorParamNames.Count, "Contagem", count, 4, 80, 1,
            Description: "Partículas na altura do fotograma — mais alto é mais partículas e mais pequenas.",
            Kind: ParameterKind.Integer),
        new EffectParameterDescriptor(GeneratorParamNames.Size, "Tamanho", size, 0.001, 0.05, 0.001,
            Description: "Raio da partícula como fração da altura do fotograma."),
        Speed(speed),
        Direction(direction),
        new EffectParameterDescriptor(GeneratorParamNames.Spread, "Dispersão", spread, 0, 1, 0.01, "%",
            "Até onde as partículas se desviam lateralmente do fluxo.") { DisplayScale = 100 },
        new EffectParameterDescriptor(GeneratorParamNames.Flicker, "Cintilação", flicker, 0, 1, 0.01, "%",
            "Pulsação de brilho por partícula.") { DisplayScale = 100 },
        new EffectParameterDescriptor(GeneratorParamNames.Streak, "Rasto", streak, 0, 1, 0.01, "%",
            "Estica uma cauda atrás de cada partícula.") { DisplayScale = 100 },
        new EffectParameterDescriptor(GeneratorParamNames.Falloff, "Tendência para a origem", falloff, 0, 1, 0.01, "%",
            "Enfraquece o campo quanto mais viajou, como as brasas rarearem ao subir.") { DisplayScale = 100 },
        Seed(),
    ];

    /// <summary>Looks up a descriptor by generator type id, or <see langword="null"/> if it is not registered.</summary>
    public static GeneratorDescriptor? Find(string generatorTypeId) =>
        BuiltIns.FirstOrDefault(d => d.Id == generatorTypeId);

    /// <summary>A friendly display name for a generator type id, falling back to the id for unknown ids.</summary>
    public static string DisplayName(string generatorTypeId) => Find(generatorTypeId)?.DisplayName ?? generatorTypeId;
}
