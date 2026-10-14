using System;
using System.Globalization;
using Sprocket.Export;

namespace Sprocket.App;

/// <summary>
/// Pure, culture-invariant text for an <see cref="ExportRunSummary"/> (export-speed phase 1): the one-line form the
/// status bar and export-queue rows show, and the multi-line diagnostics block appended to the Export Complete
/// dialog. Names the encoder that <em>actually</em> ran and states a hardware → software fallback explicitly, so
/// "Hardware (if available)" is never implied to have engaged when it did not. Avalonia-free and unit-tested.
/// </summary>
internal static class ExportSummaryText
{
    /// <summary>One line, e.g. <c>Exported with h264_nvenc in 00:42</c>; a fallback adds
    /// <c>(hardware unavailable)</c>; an audio-only export omits the encoder. A Fast Export (export-speed phase 3)
    /// reads <c>Fast export with …</c>, names the speed preset a software fallback ran with, and — when GPU decode was
    /// opted into — appends how the sources decoded, naming any software decode.</summary>
    public static string Compact(ExportRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        string elapsed = FormatElapsed(summary.Timings.Total);
        if (summary.IsAudioOnly)
            return $"Exportado em {elapsed}";
        bool fast = summary.Mode == ExportMode.Fast;
        string fallback = !summary.FellBackToSoftware ? ""
            : fast && summary.SoftwarePreset is { } preset ? $" ({preset} — hardware indisponível)"
            : " (hardware indisponível)";
        return fast
            ? $"Exportação rápida com {summary.ActualVideoEncoder}{fallback} em {elapsed}{CompactDecodeNote(summary.Decode)}"
            : $"Exportado com {summary.ActualVideoEncoder}{fallback} em {elapsed}";
    }

    /// <summary>The Fast Export GPU-decode suffix (only when GPU decode was opted into): <c> · GPU decode</c> when
    /// every source decoded on the GPU, otherwise what ran in software and why (disabled, failed mid-export, or no GPU
    /// decoder). Default software decode needs no note — it is the expected path.</summary>
    private static string CompactDecodeNote(ExportDecodeSummary d)
    {
        if (d.Sources == 0 || !d.HardwareRequested)
            return "";
        if (d.DisabledByUser)
            return " · descodificação GPU desativada (SPROCKET_HWACCEL)";
        if (d.FallbackSources > 0)
            return $" · a descodificação GPU falhou para {Sources(d.FallbackSources)}, terminada em software";
        if (d.SoftwareSources > 0)
            return $" · {d.SoftwareSources} de {Sources(d.Sources)} {(d.Sources == 1 ? "descodificada" : "descodificadas")} em software";
        return " · descodificação GPU";
    }

    /// <summary>The Fast Export <c>Decode:</c> line — software by default; with the GPU-decode opt-in, the device and
    /// per-source split, stating software decode and mid-export fallbacks explicitly.</summary>
    private static string DecodeLine(ExportDecodeSummary d)
    {
        if (d.Sources == 0)
            return "Descodificação: sem fontes de vídeo";
        if (!d.HardwareRequested)
            return $"Descodificação: software para {Sources(d.Sources)}";
        if (d.DisabledByUser)
            return $"Descodificação: software para {Sources(d.Sources)} — descodificação GPU desativada por SPROCKET_HWACCEL";
        string line = d.HardwareSources == 0 && d.FallbackSources == 0
            ? $"Descodificação: software para {Sources(d.Sources)} — nenhum descodificador GPU estava disponível"
            : d.HardwareSources == 0
                ? $"Descodificação: software para {Sources(d.Sources)}"
            : d.SoftwareSources == 0
                ? $"Descodificação: hardware ({d.HardwareDevice}) para {Sources(d.Sources)}"
                : $"Descodificação: hardware ({d.HardwareDevice}) para {d.HardwareSources} de {Sources(d.Sources)}, software para {d.SoftwareSources}";
        if (d.FallbackSources > 0)
        {
            // Name the device here when the first line could not (every GPU source fell back).
            string device = d.HardwareSources == 0 && d.HardwareDevice is { } dev ? $" ({dev})" : "";
            line += $"\nA descodificação GPU{device} falhou durante a exportação para {Sources(d.FallbackSources)} — reaberta em software no mesmo fotograma";
        }
        return line;
    }

    private static string Sources(int count) => count == 1 ? "1 fonte" : $"{count} fontes";

    /// <summary>The Export Complete dialog's diagnostics block: actual encoder + hardware/software, elapsed time
    /// with average export fps, and the per-stage breakdown in milliseconds.</summary>
    public static string CompletionDetails(ExportRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ExportStageTimings t = summary.Timings;
        TimeSpan audio = t.AudioMix + t.AudioEncode;

        if (summary.IsAudioOnly)
        {
            return $"Apenas áudio · decorrido {FormatElapsed(t.Total)}\n" +
                   $"Tempo: total {Ms(t.Total)} · áudio {Ms(audio)} (mistura {Ms(t.AudioMix)}, codificação {Ms(t.AudioEncode)})";
        }

        // A Fast Export software fallback names its speed-first preset (a Final Export's preset is the codec default).
        string presetNote = summary.Mode == ExportMode.Fast && summary.SoftwarePreset is { } preset ? $", predefinição {preset}" : "";
        string engine = summary.HardwareVideoEngaged ? "hardware"
            : summary.FellBackToSoftware ? $"software{presetNote} — o hardware foi pedido mas nenhum codificador GPU abriu"
            : $"software{presetNote}";
        string fps = summary.VideoFrames > 0 && t.Total > TimeSpan.Zero
            ? string.Create(CultureInfo.InvariantCulture, $" · {summary.VideoFrames / t.Total.TotalSeconds:0.0} fps em média")
            : "";

        // Fast Export (export-speed phase 3) leads with the mode and adds how the sources decoded; a Final Export's
        // block is unchanged (it always decodes in software, with the codec's default preset).
        string fastLines = summary.Mode == ExportMode.Fast
            ? $"Modo: Exportação Rápida\n{DecodeLine(summary.Decode)}\n"
            : "";
        return fastLines +
               $"Codificador: {summary.ActualVideoEncoder} ({engine})\n" +
               $"Decorrido: {FormatElapsed(t.Total)}{fps}\n" +
               $"Tempo: total {Ms(t.Total)} · descodificação {Ms(t.VideoDecode)} · composição {Ms(t.VideoRender)} · " +
               $"codificação {Ms(t.VideoEncode)} · áudio {Ms(audio)}";
    }

    /// <summary><c>mm:ss</c>, or <c>h:mm:ss</c> from an hour up; truncates to whole seconds.</summary>
    internal static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        return elapsed.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{elapsed.Minutes:00}:{elapsed.Seconds:00}");
    }

    private static string Ms(TimeSpan span) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, span.TotalMilliseconds):0} ms");
}
