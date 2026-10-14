#!/usr/bin/env python3
"""Etapa 2: traduz literais PT-PT no MainWindow.axaml (mapeamento exato, ancorado em aspas)."""
import sys
from pathlib import Path

FICHEIRO = Path(__file__).resolve().parents[1] / "src" / "Sprocket.App" / "MainWindow.axaml"

# (atributo-agnóstico: substitui "VALOR" exato; teclas de acesso _ únicas por menu)
MAPA = {
    # ── Menu Ficheiro ─────────────────────────────────────────────
    "_File": "_Ficheiro",
    "_New Project": "_Novo projeto",
    "_Open Project…": "_Abrir projeto…",
    "Open _Recent": "Abrir re_centes",
    "Open Sa_mple Project": "Abrir projeto de e_xemplo",
    "_Save Project": "_Guardar projeto",
    "Save Project _As…": "Guardar projeto _como…",
    "_Import Media…": "_Importar multimédia…",
    "_Relink Media…": "_Religar multimédia…",
    "_Export…": "_Exportar…",
    "Export _Queue…": "Fila de e_xportação…",
    "Export I_nterchange": "Exportar intercâmbio",
    "_EDL (CMX3600)…": "_EDL (CMX3600)…",
    "_Final Cut XML…": "_Final Cut XML…",
    "E_xit": "Sai_r",
    # ── Menu Editar ───────────────────────────────────────────────
    "_Edit": "_Editar",
    "_Undo": "_Anular",
    "_Redo": "_Refazer",
    "Cu_t": "Cor_tar",
    "_Copy": "_Copiar",
    "_Paste": "Co_lar",
    "_Delete": "_Eliminar",
    "_Ripple Delete": "Eliminar em _cadeia",
    "Select _All": "Selecionar _tudo",
    "Pre_ferences…": "_Preferências…",
    "_Plugins…": "_Plugins…",
    # ── Menu Clipe ────────────────────────────────────────────────
    "_Clip": "_Clipe",
    "_Insert": "_Inserir",
    "Insert Ed_it  (,)": "Edição por inserção  (,)",
    "_Overwrite Edit  (.)": "Edição por sobreposição  (.)",
    "Fit to Fill": "Preencher para caber",
    "Spli_t at Playhead": "Dividir no indicador de reprodução",
    "Du_plicate": "_Duplicar",
    "_Enable": "_Ativar",
    "_Link": "_Ligar",
    "_Unlink": "_Desligar",
    "_Speed / Duration…": "_Velocidade / duração…",
    "Interpret _Footage…": "Interpretar metragem…",
    "Frame _Hold Options…": "Opções de congelação de fotograma…",
    "_Add Frame Hold": "Adicionar congelação de fotograma",
    "Insert Frame Hold Se_gment": "Inserir segmento de congelação",
    "_Duplicate Frame": "Duplicar fotograma",
    "Remo_ve Frame": "Remover fotograma",
    "_Normalize Audio": "_Normalizar áudio",
    "Create _Multicam Source": "Criar fonte _multicâmara",
    "Nudge _Left": "Mover para a esquerda",
    "Nudge _Right": "Mover para a direita",
    # ── Menu Sequência ────────────────────────────────────────────
    "_Sequence": "_Sequência",
    "_New Sequence": "_Nova sequência",
    "_Open Sequence": "_Abrir sequência",
    "_Nest": "_Aninhar",
    "_Play In to Out": "_Reproduzir da entrada à saída",
    "_In / Out": "_Entrada/Saída",
    "Mark _In": "Marcar _entrada",
    "Mark _Out": "Marcar _saída",
    "Mark _Clip": "Marcar _clipe",
    "Mark _Selection  (/)": "Marcar seleção  (/)",
    "_Go to In": "Ir para _entrada",
    "Go to O_ut": "Ir para _saída",
    "Clear I_n": "Limpar _entrada",
    "Clear Ou_t": "Limpar _saída",
    "Clear In _and Out": "Limpar entrada e saída",
    "Lif_t  (;)": "Elevar  (;)",
    "_Extract  (')": "_Extrair  (')",
    "_Render In to Out": "P_rocessar de entrada a saída",
    "Render Se_lection": "Processar _seleção",
    "Render _Audio": "Processar _áudio",
    "Free_ze Clip Audio": "Congelar áudio do clipe",
    "Un_freeze Clip Audio": "Descongelar áudio do clipe",
    "_Delete Render Files…": "Eliminar _ficheiros de processamento…",
    "Sequence _Settings…": "_Definições da sequência…",
    # ── Efeitos / Ver ─────────────────────────────────────────────
    "Effe_cts": "Efe_itos",
    "_View": "_Ver",
    "Zoom _In": "_Ampliar",
    "Zoom _Out": "_Reduzir",
    "Zoom to _Fit": "Zoom para ajustar",
    "_Snapping": "_Ajuste magnético",
    "_Guides": "_Guias",
    "Playback _Auto-Scroll": "Deslocamento automático",
    "_No Scroll": "_Sem deslocamento",
    "_Page Scroll": "Deslocamento por _página",
    "_Smooth Scroll": "Deslocamento suave",
    "_Project Panel": "Painel _Projeto",
    "I_nspector Panel": "Painel I_nspetor",
    "Playback _Statistics": "_Estatísticas de reprodução",
    "_Background Tasks": "Tarefas em _segundo plano",
    "_Full Screen": "Ecrã _inteiro",
    "Full Screen Previe_w": "Pré-visualização em ecrã inteiro",
    # ── Janela / Ajuda ────────────────────────────────────────────
    "_Window": "_Janela",
    "_Reset Layout": "_Repor disposição",
    "_Help": "_Ajuda",
    "Check for _Updates…": "Verificar _atualizações…",
    "Add to _Applications Menu": "Adicionar ao menu de a_plicações",
    "Remove from Applications _Menu": "Remover do menu de aplicações",
    "_Documentation": "_Documentação",
    "Sprocket _Website": "_Site do Sprocket",
    "_Report an Issue…": "_Comunicar um problema…",
    "_Support Sprocket ♥": "_Apoiar o Sprocket ♥",
    "_Third-Party Notices": "Avisos de _terceiros",
    "_About Sprocket": "_Acerca do Sprocket",
    # ── Barra de título / estado ──────────────────────────────────
    "Untitled": "Sem título",
    "• all changes saved": "• todas as alterações guardadas",
    "Ready": "Pronto",
    "0 items": "0 itens",
    # ── Painéis / monitores ───────────────────────────────────────
    "Project": "Projeto",
    "Inspector": "Inspetor",
    "Timeline": "Linha do tempo",
    "Program": "Programa",
    "Source": "Origem",
    "Markers": "Marcadores",
    "+ Track": "+ Pista",
    "No Scopes": "Sem âmbitos",
    "Waveform": "Forma de onda",
    "RGB Parade": "Desfile RGB",
    "Vectorscope": "Vetorescópio",
    "Histogram": "Histograma",
    "Guides": "Guias",
    "Fit": "Ajustar",
    "Audio only": "Apenas áudio",
    "In --  Out --  Dur --": "Ent --  Saí --  Dur --",
    "Export": "Exportar",
    "View": "Ver",
    "Minimize": "Minimizar",
    "Maximize": "Maximizar",
    "Close": "Fechar",
    "Dismiss": "Dispensar",
    # ── Ferramentas ───────────────────────────────────────────────
    "Select": "Selecionar",
    "Blade": "Lâmina",
    "Ripple": "Onda",
    "Roll": "Rolar",
    "Slip": "Deslizar",
    "Slide": "Arrastar",
    "Hand": "Mão",
    "Zoom": "Zoom",
    "Snapping": "Ajuste magnético",
    "Linked": "Ligados",
    # ── Tooltips ──────────────────────────────────────────────────
    "Select, move, and edge-trim clips": "Selecionar, mover e aparar arestas de clipes",
    "Razor: click a clip to split it at the cursor": "Lâmina: clique num clipe para o dividir no cursor",
    "Ripple edit: trim a clip's edge and shift everything downstream to close the gap":
        "Edição em cadeia: apare a aresta do clipe e desloca o que se segue para fechar o espaço",
    "Rolling edit: drag the cut between two adjacent clips, keeping their combined length":
        "Edição por rolagem: arraste o corte entre dois clipes adjacentes mantendo o comprimento total",
    "Slip a clip's source in/out without moving it on the timeline":
        "Deslizar a origem (entrada/saída) do clipe sem o mover na linha do tempo",
    "Slide a clip along the timeline while its neighbours absorb the change":
        "Arrastar um clipe ao longo da linha do tempo enquanto os vizinhos absorvem a alteração",
    "Pan the timeline view": "Deslocar a vista da linha do tempo",
    "Zoom the timeline view (click to zoom in, Alt-click to zoom out)":
        "Ampliar a vista da linha do tempo (clique para ampliar, Alt+clique para reduzir)",
    "Snap clip edges and the playhead to nearby edits, markers, and the playhead while dragging":
        "Ajustar arestas de clipes e o indicador de reprodução a cortes e marcadores próximos durante o arrastamento",
    "Move linked audio and video clips together as a group when editing":
        "Mover clipes de áudio e vídeo ligados em grupo ao editar",
    "A newer version of Sprocket is available — click for details.":
        "Está disponível uma versão mais recente do Sprocket — clique para ver detalhes.",
    "Jump to start — press Home": "Ir para o início — prima Home",
    "Step back one frame — press ← (Shift+← for five)": "Recuar um fotograma — prima ← (Shift+← para cinco)",
    "Play / Pause (Space)": "Reproduzir / Pausa (Espaço)",
    "Step forward one frame — press → (Shift+→ for five)": "Avançar um fotograma — prima → (Shift+→ para cinco)",
    "Jump to end — press End": "Ir para o fim — prima End",
    "Jump to previous keyframe of the selected clip — press [":
        "Ir para o fotograma-chave anterior do clipe selecionado — prima [",
    "Jump to next keyframe of the selected clip — press ]":
        "Ir para o fotograma-chave seguinte do clipe selecionado — prima ]",
    "Mark In — press I (Alt+I clears, Shift+I goes to it)":
        "Marcar entrada — prima I (Alt+I limpa, Shift+I vai para a marca)",
    "Mark Out — press O (Alt+O clears, Shift+O goes to it)":
        "Marcar saída — prima O (Alt+O limpa, Shift+O vai para a marca)",
    "Insert the marked source range into the sequence — press ,":
        "Inserir o intervalo de origem marcado na sequência — prima ,",
    "Overwrite the sequence with the marked source range — press .":
        "Sobrescrever a sequência com o intervalo de origem marcado — prima .",
    "Drag Video Only — drop on a video track to overwrite (Ctrl+drop inserts)":
        "Arrastar só o vídeo — largue numa pista de vídeo para sobrescrever (Ctrl+largar insere)",
    "Drag Audio Only — drop on an audio track to overwrite (Ctrl+drop inserts)":
        "Arrastar só o áudio — largue numa pista de áudio para sobrescrever (Ctrl+largar insere)",
    "Expand all sections": "Expandir todas as secções",
    "Collapse all sections": "Recolher todas as secções",
    "Zoom Out (- or Ctrl+-)": "Reduzir (- ou Ctrl+-)",
    "Zoom In (= or Ctrl+=)": "Ampliar (= ou Ctrl+=)",
    "Markers panel (M adds at the playhead; Shift+M / Ctrl+Shift+M navigate)":
        "Painel de marcadores (M adiciona no indicador; Shift+M / Ctrl+Shift+M navegam)",
    # ── AutomationProperties.Name ─────────────────────────────────
    "Jump to start": "Ir para o início",
    "Step back one frame": "Recuar um fotograma",
    "Play": "Reproduzir",
    "Step forward one frame": "Avançar um fotograma",
    "Jump to end": "Ir para o fim",
    "Previous keyframe": "Fotograma-chave anterior",
    "Next keyframe": "Fotograma-chave seguinte",
    "Mark In": "Marcar entrada",
    "Mark Out": "Marcar saída",
    "Insert": "Inserir",
    "Overwrite": "Sobrescrever",
    "Drag Video Only": "Arrastar só o vídeo",
    "Drag Audio Only": "Arrastar só o áudio",
    "Project panel": "Painel Projeto",
    "Zoom out": "Reduzir",
    "Zoom in": "Ampliar",
    "Monitor": "Monitor",
}

def main() -> int:
    texto = FICHEIRO.read_text(encoding="utf-8")
    por_aplicar = [k for k in MAPA if f'"{k}"' not in texto]
    n = 0
    for velho, novo in MAPA.items():
        alvo, sub = f'"{velho}"', f'"{novo}"'
        conta = texto.count(alvo)
        if conta:
            texto = texto.replace(alvo, sub)
            n += conta
    FICHEIRO.write_text(texto, encoding="utf-8", newline="")
    print(f"Substituições: {n}")
    if por_aplicar:
        print("Sem correspondência (verificar):")
        for k in por_aplicar:
            print(f"  - {k}")
        return 1
    return 0

if __name__ == "__main__":
    sys.exit(main())
