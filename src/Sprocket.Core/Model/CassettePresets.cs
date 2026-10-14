using static Sprocket.Core.Model.EffectParamNames;

namespace Sprocket.Core.Model;

/// <summary>
/// The Cassette audio stage's factory characters (plan/features/toy-cassette-camera.md, phase 4), kept beside
/// <see cref="EffectCatalog"/> like <see cref="ToyCamPresets"/> and named to match its video looks so the phase-5
/// one-tap stacks pair them by name. Each is a <em>complete</em> character — it sets every tone parameter, so
/// switching never leaves a stray value from the previous one — except <see cref="EffectParamNames.Mix"/>, which
/// every preset leaves untouched so the user's wet/dry blend survives (the Studio Reverb rule).
/// </summary>
public static class CassettePresets
{
    /// <summary>A recorder in good order on fresh tape: mono and band-limited, a gentle hiss, barely any wobble.</summary>
    public static EffectPreset Clean { get; } = new("Clean", new Dictionary<string, double>
    {
        [Mono] = 1.0, [LowCutHz] = 90.0, [HighCutHz] = 6500.0, [HissDb] = -54.0,
        [WowFlutterDepth] = 0.1, [WowFlutterRateHz] = 0.8, [Drive] = 0.2,
        [AgcAmount] = 0.35, [ReleaseMs] = 800.0,
    }, "Um gravador bem conservado com fita nova: som mono estreitado, com um sopro ténue e apenas uma sugestão de oscilação.");

    /// <summary>A tape played to death: dull and thin, heavy hiss, seasick wow &amp; flutter, and the level
    /// wobble of a tape losing contact with the head.</summary>
    public static EffectPreset WornTape { get; } = new("Worn Tape", new Dictionary<string, double>
    {
        [Mono] = 1.0, [LowCutHz] = 160.0, [HighCutHz] = 3600.0, [HissDb] = -34.0,
        [WowFlutterDepth] = 0.65, [WowFlutterRateHz] = 1.3, [Drive] = 0.55,
        [AgcAmount] = 0.55, [ReleaseMs] = 600.0,
    }, "Uma cassete copiada e reproduzida demasiadas vezes: som surdo e fino, sopro intenso, oscilação enjoativa e nível vacilante.");

    /// <summary>Shooting somewhere quiet: the camera's automatic gain cranks up and pumps hard, dragging the hiss and
    /// the room's own noise up with it between sounds.</summary>
    public static EffectPreset LowLight { get; } = new("Low Light", new Dictionary<string, double>
    {
        [Mono] = 1.0, [LowCutHz] = 120.0, [HighCutHz] = 4500.0, [HissDb] = -36.0,
        [WowFlutterDepth] = 0.3, [WowFlutterRateHz] = 1.0, [Drive] = 0.35,
        [AgcAmount] = 0.95, [ReleaseMs] = 300.0,
    }, "Um quarto silencioso à noite: o ganho automático pulsa com força, pelo que o sopro e o ruído ambiente sobem entre os sons.");

    /// <summary>All characters, in picker order.</summary>
    public static IReadOnlyList<EffectPreset> All { get; } = [Clean, WornTape, LowLight];
}
