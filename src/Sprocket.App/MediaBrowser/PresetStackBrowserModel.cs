using System;
using System.Collections.Generic;
using System.Linq;
using Sprocket.Core.Commands;
using Sprocket.Core.Model;

namespace Sprocket.App.MediaBrowser;

/// <summary>
/// The Effects browser's one-tap preset stacks (the TOY CASSETTE CAMERA group, plan/features/toy-cassette-camera.md
/// phase 5), split out of <see cref="MediaBrowserPanel"/> so they are headlessly testable: the row tooltip, the status
/// line an apply reports, and <see cref="ApplyToClip"/>, the one apply path the browser's double-click and the
/// timeline's drop share.
/// </summary>
internal static class PresetStackBrowserModel
{
    /// <summary>The Effects-browser sections the stacks are listed in: each group's header (upper-cased, like DAY FOR
    /// NIGHT) with its stacks in catalog order.</summary>
    public static IReadOnlyList<(string Header, IReadOnlyList<PresetStack> Stacks)> Groups(IReadOnlyList<PresetStack> all)
    {
        ArgumentNullException.ThrowIfNull(all);
        return [.. all.GroupBy(s => s.Group).Select(g => (g.Key.ToUpperInvariant(), (IReadOnlyList<PresetStack>)[.. g]))];
    }

    /// <summary>The drag payload of a stack's row — its unambiguous title, resolved back by
    /// <see cref="PresetStackCatalog.Find"/> on the drop.</summary>
    public static string DragPayload(PresetStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return stack.Title;
    }

    /// <summary>The tooltip on a stack's row: the "Inspired by…" description, then how to use the row.</summary>
    public static string Tooltip(PresetStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return $"{stack.Description}\n\nFaça duplo clique para aplicar {stack.Name} ao clipe selecionado e ao respetivo áudio ligado, ou arraste-o para um clipe. Aplicar de novo troca o estilo em vez de o empilhar.";
    }

    /// <summary>The badge on a stack's row: what it adds to picture and sound.</summary>
    public static string Badge(PresetStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return stack.AudioEntries.Count == 0 ? "Vídeo" : "Vídeo + Áudio";
    }

    /// <summary>The status-strip line after applying <paramref name="stack"/> (or failing to), naming anything skipped.</summary>
    public static string ApplyStatus(PresetStack stack, PresetStackApplyResult result, string clipName)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(result);
        string skipped = result.Skipped.Count == 0 ? string.Empty
            : " Ignorados: " + string.Join(", ", result.Skipped
                .GroupBy(s => s.Reason)
                .Select(g => $"{string.Join(" + ", g.Select(s => EffectCatalog.DisplayName(s.EffectTypeId)).Distinct())} ({SkipReason(g.Key)})")) + ".";
        if (result.Command is null)
            return $"Não foi possível aplicar {stack.Title}.{skipped}";
        string verb = result.Replaced > 0 ? "Atualizado" : "Aplicado";
        return $"{verb} {stack.Title} em {clipName}.{skipped}";
    }

    /// <summary>The user-facing form of a skip reason. <see cref="PresetStackApplication"/>'s constants stay English
    /// (the MCP server surfaces them verbatim in its JSON), so they are localized here, at the UI boundary.</summary>
    private static string SkipReason(string reason) => reason switch
    {
        PresetStackApplication.NoVideoClip => "sem clipe de vídeo",
        PresetStackApplication.NoAudioClip => "sem clipe de áudio ligado",
        PresetStackApplication.UnknownEffect => "efeito não disponível",
        PresetStackApplication.UnknownPreset => "predefinição não disponível",
        _ => reason,
    };

    /// <summary>
    /// Applies <paramref name="stack"/> to <paramref name="clip"/> and its linked companions as one undoable edit through
    /// <paramref name="history"/> (nothing is executed when no entry applies) and returns the status-strip line.
    /// </summary>
    public static string ApplyToClip(PresetStack stack, Clip clip, Project project, EditHistory history)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(history);
        PresetStackApplyResult result = PresetStackApplication.Build(project.Timeline, clip, stack);
        if (result.Command is not null)
            history.Execute(result.Command);
        return ApplyStatus(stack, result, LooksBrowserModel.ClipName(project, clip));
    }
}
