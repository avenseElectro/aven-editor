using System;
using System.IO;
using System.Linq;
using Sprocket.App;
using Sprocket.Core.Model;
using Sprocket.Core.Timing;
using Sprocket.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace Sprocket.App.Tests;

/// <summary>
/// Reproducible playback-audio path check: mixes the sample projects through the very mixer the
/// playback clock feeds (MediaBootstrap.CreateAnalysisMixer uses the same PCM resolvers as playback)
/// and asserts the mix is audible. Guards against the "video plays, waveform visible, no sound"
/// regression class — a dead PCM reader, a muted-by-default fader or a clip that never landed on an
/// audio track all collapse the peak to (near) zero here. The audible media is runner.mp3 (real
/// music); the bundled sample.mp4 deliberately carries a silent audio track, so it only asserts the
/// stream exists. The device side (OpenAL endpoint) stays manual verification.
/// Needs the FFmpeg 8 natives at runtime (SPROCKET_FFMPEG8_DIR or bundled beside the output).
/// </summary>
public class AudioPlaybackPathTests
{
    private readonly ITestOutputHelper _out;

    public AudioPlaybackPathTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void SampleSprocketProject_MixIsAudible()
    {
        string projectPath = FindRepoFile(Path.Combine("src", "Sprocket.App", "Samples", "sample.sprocket.json"));

        Project project = ProjectSerializer.Load(projectPath);
        Assert.True(
            project.Timeline.AudioTracks.SelectMany(t => t.Clips).Any(),
            "o projeto de exemplo não tem clipes de áudio");

        float peak = MixPeak(project, seconds: 5);
        Assert.True(peak > 0.05f, $"a mistura do projeto de exemplo é silenciosa (pico {peak:0.###})");
    }

    [Fact]
    public void ImportedAudibleMedia_MixesAudible()
    {
        // O caminho exato de «importar e colocar na timeline»: BuildProjectFromMedia sonda o ficheiro,
        // cria as pistas e pousa o clipe companheiro de áudio — a mistura tem de ser audível.
        string media = FindRepoFile(Path.Combine("src", "Sprocket.App", "Samples", "runner.mp3"));

        Project project = MediaBootstrap.BuildProjectFromMedia(media);
        Assert.True(
            project.Timeline.AudioTracks.SelectMany(t => t.Clips).Any(),
            "o projeto construído a partir de runner.mp3 não tem clipes de áudio");

        float peak = MixPeak(project, seconds: 5);
        Assert.True(peak > 0.05f, $"a mistura de runner.mp3 é silenciosa (pico {peak:0.###})");
    }

    [Fact]
    public void SampleMedia_StillCarriesAnAudioStream()
    {
        // O sample.mp4 de demonstração tem a faixa de áudio silenciada por conceção (a música do
        // projeto de exemplo vem do runner.mp3). Este teste impede que uma troca de asset perca a
        // faixa por completo — a armadilha original de «sem som» ficaria impossível de diagnosticar.
        string media = FindRepoFile(Path.Combine("src", "Sprocket.App", "Samples", "sample.mp4"));

        Project project = MediaBootstrap.BuildProjectFromMedia(media);
        Assert.True(project.MediaPool.Items.Any(m => m.Info.HasAudio), "o sample.mp4 perdeu a faixa de áudio");
    }

    /// <summary>Mixes the first <paramref name="seconds"/> of the project exactly the way the playback
    /// clock's feeder does and returns the loudest sample.</summary>
    private float MixPeak(Project project, int seconds)
    {
        int sampleRate = project.Timeline.SampleRate > 0 ? project.Timeline.SampleRate : 48000;
        using var mixer = MediaBootstrap.CreateAnalysisMixer(project);
        var buffer = new float[sampleRate * 2 * seconds]; // stereo, intercalado
        mixer.MixInto(buffer, Timecode.Zero, project);
        float peak = buffer.Max(MathF.Abs);
        _out.WriteLine($"pico da mistura ({seconds}s) = {peak:0.#####}");
        return peak;
    }

    private static string FindRepoFile(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(relative + " não encontrado a partir de " + AppContext.BaseDirectory);
    }
}
