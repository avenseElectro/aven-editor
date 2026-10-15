using System.Security.Cryptography;
using Sprocket.Core.Model;
using Sprocket.Core.Timing;
using Xunit;

namespace Sprocket.Export.Tests;

/// <summary>
/// O resultado de «Aplicar» na janela «Editar recorte e movimento» tem de chegar ao ficheiro exportado: um
/// Transform animado (zoom in de entrada, a forma de keyframes que SegmentMovement.Parameter constrói) muda
/// os píxeis renderizados — a exportação com o movimento difere da mesma exportação sem ele — sem tocar na
/// contagem de fotogramas nem de amostras de áudio (duração e áudio intactos). É a evidência do lado da
/// exportação para o teste §9.14; a confirmação visual fica para o teste pessoal do utilizador.
/// </summary>
public sealed class MovementExportTests
{
    [Fact]
    public void Animated_Transform_Movement_Reaches_The_Exported_File()
    {
        Project plain = ExportFixture.BuildProject(withAudio: true);
        Project moved = ExportFixture.BuildProject(withAudio: true);
        Clip clip = moved.Timeline.VideoTracks.First().Clips[0];
        long half = clip.TimelineEnd.Ticks / 2;
        clip.Effects.Add(new EffectInstance(EffectTypeIds.Transform).Set(EffectParamNames.Scale,
            AnimatableValue.Animated(
            [
                new Keyframe(Timecode.Zero, 0.8, Interpolation.EaseInOut),
                new Keyframe(new Timecode(half), 1.0, Interpolation.Hold),
            ])));

        using var semMovimento = new TempFile();
        ExportRunSummary baseRun = VideoExporter.ExportCore(
            plain, semMovimento.Path, default, null, null, null, default, null, pipelined: false);
        using var comMovimento = new TempFile();
        ExportRunSummary movedRun = VideoExporter.ExportCore(
            moved, comMovimento.Path, default, null, null, null, default, null, pipelined: false);

        Assert.NotEqual(Hash(semMovimento.Path), Hash(comMovimento.Path)); // o movimento muda a imagem
        Assert.Equal(baseRun.VideoFrames, movedRun.VideoFrames);           // a duração não muda
        Assert.Equal(baseRun.AudioSampleFrames, movedRun.AudioSampleFrames); // o áudio não muda
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
