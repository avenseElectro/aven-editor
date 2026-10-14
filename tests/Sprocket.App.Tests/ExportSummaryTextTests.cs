using System;
using Sprocket.App;
using Sprocket.Export;
using Xunit;

namespace Sprocket.App.Tests;

/// <summary>
/// Headless tests for <see cref="ExportSummaryText"/> (export-speed phase 1): the status-bar / queue-row line and the
/// Export Complete diagnostics block. The key property is that the text names the encoder that actually ran and
/// states a hardware → software fallback explicitly.
/// </summary>
public class ExportSummaryTextTests
{
    private static readonly ExportStageTimings Timings = new(
        VideoDecode: TimeSpan.FromMilliseconds(1200),
        VideoRender: TimeSpan.FromMilliseconds(15000),
        VideoEncode: TimeSpan.FromMilliseconds(20000.4),
        AudioMix: TimeSpan.FromMilliseconds(300),
        AudioEncode: TimeSpan.FromMilliseconds(200),
        Total: TimeSpan.FromSeconds(42));

    private static ExportRunSummary Summary(
        ExportAcceleration requested, string actual, bool hardware, long frames = 1260, long samples = 2_016_000) =>
        new(requested, actual.Length == 0 ? "" : "libx264", actual, hardware, frames, samples,
            actual.Length == 0 ? Timings with { VideoDecode = default, VideoRender = default, VideoEncode = default } : Timings);

    [Fact]
    public void HardwareEngaged_NamesTheGpuEncoder()
    {
        ExportRunSummary s = Summary(ExportAcceleration.Hardware, "h264_nvenc", hardware: true);

        Assert.Equal("Exportado com h264_nvenc em 00:42", ExportSummaryText.Compact(s));
        Assert.Equal(
            "Codificador: h264_nvenc (hardware)\n" +
            "Decorrido: 00:42 · 30.0 fps em média\n" +
            "Tempo: total 42000 ms · descodificação 1200 ms · composição 15000 ms · codificação 20000 ms · áudio 500 ms",
            ExportSummaryText.CompletionDetails(s));
    }

    [Fact]
    public void SoftwareFallback_IsStatedExplicitly()
    {
        ExportRunSummary s = Summary(ExportAcceleration.Hardware, "libx264", hardware: false);

        Assert.Equal("Exportado com libx264 (hardware indisponível) em 00:42", ExportSummaryText.Compact(s));
        Assert.StartsWith(
            "Codificador: libx264 (software — o hardware foi pedido mas nenhum codificador GPU abriu)\n",
            ExportSummaryText.CompletionDetails(s));
    }

    [Fact]
    public void SoftwareRequested_SaysSoftware_WithoutFallbackWording()
    {
        ExportRunSummary s = Summary(ExportAcceleration.Software, "libx264", hardware: false);

        Assert.Equal("Exportado com libx264 em 00:42", ExportSummaryText.Compact(s));
        Assert.StartsWith("Codificador: libx264 (software)\n", ExportSummaryText.CompletionDetails(s));
    }

    [Fact]
    public void AudioOnly_OmitsTheEncoderAndVideoStages()
    {
        ExportRunSummary s = Summary(ExportAcceleration.Software, "", hardware: false, frames: 0);

        Assert.Equal("Exportado em 00:42", ExportSummaryText.Compact(s));
        Assert.Equal(
            "Apenas áudio · decorrido 00:42\n" +
            "Tempo: total 42000 ms · áudio 500 ms (mistura 300 ms, codificação 200 ms)",
            ExportSummaryText.CompletionDetails(s));
    }

    [Fact]
    public void ZeroElapsed_OmitsFps()
    {
        ExportRunSummary s = Summary(ExportAcceleration.Software, "libx264", hardware: false) with
        {
            Timings = default,
        };
        Assert.DoesNotContain("fps", ExportSummaryText.CompletionDetails(s));
    }

    private static ExportRunSummary Fast(ExportDecodeSummary decode, string actual = "h264_nvenc", bool hardware = true) =>
        Summary(ExportAcceleration.Hardware, actual, hardware) with { Mode = ExportMode.Fast, Decode = decode };

    [Fact]
    public void FastExport_AllSourcesOnGpu_SaysFastAndGpuDecode()
    {
        ExportRunSummary s = Fast(new ExportDecodeSummary(true, 2, 0, 0, "d3d11va"));

        Assert.Equal("Exportação rápida com h264_nvenc em 00:42 · descodificação GPU", ExportSummaryText.Compact(s));
        Assert.StartsWith(
            "Modo: Exportação Rápida\n" +
            "Descodificação: hardware (d3d11va) para 2 fontes\n" +
            "Codificador: h264_nvenc (hardware)\n",
            ExportSummaryText.CompletionDetails(s));
    }

    [Fact]
    public void FastExport_SomeSourcesInSoftware_NamesTheSplit()
    {
        ExportRunSummary s = Fast(new ExportDecodeSummary(true, 2, 1, 0, "cuda"));

        Assert.Equal("Exportação rápida com h264_nvenc em 00:42 · 1 de 3 fontes descodificadas em software", ExportSummaryText.Compact(s));
        Assert.Contains("Descodificação: hardware (cuda) para 2 de 3 fontes, software para 1\n", ExportSummaryText.CompletionDetails(s));
    }

    [Fact]
    public void FastExport_NoGpuDecoderOrEncoder_StatesBothFallbacks()
    {
        ExportRunSummary s = Fast(new ExportDecodeSummary(true, 0, 1, 0, null), actual: "libx264", hardware: false);

        Assert.Equal(
            "Exportação rápida com libx264 (hardware indisponível) em 00:42 · 1 de 1 fonte descodificada em software",
            ExportSummaryText.Compact(s));
        string details = ExportSummaryText.CompletionDetails(s);
        Assert.Contains("Descodificação: software para 1 fonte — nenhum descodificador GPU estava disponível\n", details);
        Assert.Contains("Codificador: libx264 (software — o hardware foi pedido mas nenhum codificador GPU abriu)\n", details);
    }

    [Fact]
    public void FastExport_MidExportFallback_IsStatedExplicitly()
    {
        ExportRunSummary s = Fast(new ExportDecodeSummary(true, 1, 1, 1, "cuda"));

        Assert.Equal("Exportação rápida com h264_nvenc em 00:42 · a descodificação GPU falhou para 1 fonte, terminada em software",
            ExportSummaryText.Compact(s));
        Assert.Contains(
            "Descodificação: hardware (cuda) para 1 de 2 fontes, software para 1\n" +
            "A descodificação GPU falhou durante a exportação para 1 fonte — reaberta em software no mesmo fotograma\n",
            ExportSummaryText.CompletionDetails(s));
    }

    [Fact]
    public void FastExport_EveryGpuSourceFellBack_DoesNotClaimNoDecoderWasAvailable()
    {
        ExportRunSummary s = Fast(new ExportDecodeSummary(true, 0, 1, 1, "cuda"));

        string details = ExportSummaryText.CompletionDetails(s);
        Assert.DoesNotContain("nenhum descodificador GPU estava disponível", details);
        Assert.Contains(
            "Descodificação: software para 1 fonte\n" +
            "A descodificação GPU (cuda) falhou durante a exportação para 1 fonte — reaberta em software no mesmo fotograma\n",
            details);
    }

    [Fact]
    public void FastExport_DisabledByEnvironment_SaysSo()
    {
        ExportRunSummary s = Fast(new ExportDecodeSummary(true, 0, 2, 0, null, DisabledByUser: true));

        Assert.EndsWith(" · descodificação GPU desativada (SPROCKET_HWACCEL)", ExportSummaryText.Compact(s));
        Assert.Contains("Descodificação: software para 2 fontes — descodificação GPU desativada por SPROCKET_HWACCEL\n",
            ExportSummaryText.CompletionDetails(s));
    }

    [Fact]
    public void FastExport_NoVideoSources_OmitsTheCompactDecodeNote()
    {
        ExportRunSummary s = Fast(new ExportDecodeSummary(true, 0, 0, 0, null));

        Assert.Equal("Exportação rápida com h264_nvenc em 00:42", ExportSummaryText.Compact(s));
        Assert.Contains("Descodificação: sem fontes de vídeo\n", ExportSummaryText.CompletionDetails(s));
    }

    [Fact]
    public void FastExport_DefaultSoftwareDecode_AddsNoCompactNote()
    {
        ExportRunSummary s = Fast(new ExportDecodeSummary(false, 0, 2, 0, null));

        Assert.Equal("Exportação rápida com h264_nvenc em 00:42", ExportSummaryText.Compact(s));
        Assert.StartsWith(
            "Modo: Exportação Rápida\n" +
            "Descodificação: software para 2 fontes\n" +
            "Codificador: h264_nvenc (hardware)\n",
            ExportSummaryText.CompletionDetails(s));
    }

    [Fact]
    public void FastExport_SoftwareEncoderFallback_NamesTheSpeedPreset()
    {
        ExportRunSummary s = Fast(new ExportDecodeSummary(false, 0, 1, 0, null), actual: "libx264", hardware: false) with
        {
            SoftwarePreset = "veryfast",
        };

        Assert.Equal("Exportação rápida com libx264 (veryfast — hardware indisponível) em 00:42", ExportSummaryText.Compact(s));
        Assert.Contains(
            "Codificador: libx264 (software, predefinição veryfast — o hardware foi pedido mas nenhum codificador GPU abriu)\n",
            ExportSummaryText.CompletionDetails(s));
    }

    [Fact]
    public void FinalExport_DoesNotMentionTheDefaultPreset()
    {
        ExportRunSummary s = Summary(ExportAcceleration.Software, "libx264", hardware: false) with { SoftwarePreset = "medium" };

        Assert.Equal("Exportado com libx264 em 00:42", ExportSummaryText.Compact(s));
        Assert.StartsWith("Codificador: libx264 (software)\n", ExportSummaryText.CompletionDetails(s));
    }

    [Fact]
    public void FinalExport_DetailsCarryNoModeOrDecodeLines()
    {
        ExportRunSummary s = Summary(ExportAcceleration.Software, "libx264", hardware: false) with
        {
            Decode = new ExportDecodeSummary(false, 0, 1, 0, null),
        };
        string details = ExportSummaryText.CompletionDetails(s);
        Assert.DoesNotContain("Modo:", details);
        Assert.DoesNotContain("Descodificação:", details);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(59.9, "00:59")]
    [InlineData(61, "01:01")]
    [InlineData(3725, "1:02:05")]
    public void FormatElapsed_UsesMinutesSeconds_ThenHours(double seconds, string expected) =>
        Assert.Equal(expected, ExportSummaryText.FormatElapsed(TimeSpan.FromSeconds(seconds)));
}
