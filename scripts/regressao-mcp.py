#!/usr/bin/env python3
"""Regressão fumador ponta-a-ponta via MCP (Etapa 0 da Fase 2).

Lança a aplicação com o servidor MCP, executa o fluxo completo de edição
programática e valida o resultado:
  importar amostra -> 2 clips -> dividir -> transições -> exportar ->
  validar com ffprobe/ffmpeg -> undo/redo -> gravar projeto.

Uso: python scripts/regressao-mcp.py [--ffprobe caminho/ffprobe.exe]
Requer: build de Debug já feito e SPROCKET_FFMPEG8_DIR no ambiente (ou FFmpeg no PATH).
"""
import json
import os
import subprocess
import sys
import tempfile
import time
import urllib.request
from pathlib import Path

RAIZ = Path(__file__).resolve().parents[1]
APP = RAIZ / "src" / "Sprocket.App" / "bin" / "Debug" / "net10.0" / "Sprocket.exe"
AMOSTRA = RAIZ / "src" / "Sprocket.App" / "bin" / "Debug" / "net10.0" / "Samples" / "sample.mp4"
PORTA = 8941
TOKEN = "regressao-fase2"
URL = f"http://127.0.0.1:{PORTA}/mcp"
HDR = {
    "Content-Type": "application/json",
    "Accept": "application/json, text/event-stream",
    "Authorization": f"Bearer {TOKEN}",
}

FALHAS: list[str] = []


def passo(nome: str, condicao: bool, detalhe: str = "") -> None:
    estado = "OK " if condicao else "FALHOU"
    print(f"[{estado}] {nome}" + (f" — {detalhe}" if detalhe and not condicao else ""))
    if not condicao:
        FALHAS.append(nome)


def rpc(corpo: dict, timeout: int = 300) -> dict:
    pedido = urllib.request.Request(URL, data=json.dumps(corpo).encode(), headers=HDR)
    with urllib.request.urlopen(pedido, timeout=timeout) as resposta:
        bruto = resposta.read().decode()
    for linha in bruto.splitlines():
        if linha.startswith("data: "):
            return json.loads(linha[6:])
    return json.loads(bruto) if bruto else {}


_contador = [0]


def ferramenta(nome: str, argumentos: dict | None = None) -> dict:
    _contador[0] += 1
    return rpc({
        "jsonrpc": "2.0",
        "id": 1000 + _contador[0],
        "method": "tools/call",
        "params": {"name": nome, "arguments": argumentos or {}},
    })


def texto(resposta: dict) -> str:
    try:
        return " ".join(c.get("text", "") for c in resposta["result"]["content"])
    except Exception:
        return json.dumps(resposta)[:400]


def main() -> int:
    if not APP.exists():
        print(f"Executável não encontrado: {APP} — compila primeiro (dotnet build)")
        return 2
    if not AMOSTRA.exists():
        print(f"Amostra não encontrada: {AMOSTRA}")
        return 2

    ambiente = os.environ.copy()
    ambiente["SPROCKET_MCP_TOKEN"] = TOKEN
    processo = subprocess.Popen(
        [str(APP), "--mcp-port", str(PORTA)], cwd=str(APP.parent), env=ambiente,
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
    )
    try:
        # espera o servidor aceitar ligações
        for _ in range(40):
            time.sleep(0.5)
            try:
                rpc({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
                    "protocolVersion": "2025-06-18", "capabilities": {},
                    "clientInfo": {"name": "regressao-fase2", "version": "1.0"}}}, timeout=5)
                break
            except Exception:
                if processo.poll() is not None:
                    print("A aplicação terminou durante o arranque")
                    return 2
        else:
            print("O servidor MCP não respondeu")
            return 2
        rpc({"jsonrpc": "2.0", "method": "notifications/initialized"})

        r = ferramenta("import_media", {"path": str(AMOSTRA)})
        dados = json.loads(texto(r))
        passo("importar media", "media_id" in dados, texto(r))
        meio = dados.get("media_id", "")
        dur = dados.get("duration_ticks", 0)
        passo("duracao detetada", dur > 0, f"dur={dur}")

        r = ferramenta("add_clip_to_timeline", {"mediaId": meio, "startTicks": 0})
        passo("colocar clipe 1", '"clip_id"' in texto(r))
        r = ferramenta("add_clip_to_timeline", {"mediaId": meio, "startTicks": dur})
        passo("colocar clipe 2", '"clip_id"' in texto(r))

        r = ferramenta("split_clip", {"clipId": 1, "positionTicks": dur // 2})
        passo("dividir clipe", '"right_clip_id"' in texto(r))

        r = ferramenta("add_transition", {"clipId": 1, "cutEdge": "end", "durationTicks": 480000})
        passo("transicao crossdissolve", '"transition_id"' in texto(r))
        r = ferramenta("add_transition", {"clipId": 3, "cutEdge": "start", "transitionTypeId": "builtin.wipe", "durationTicks": 480000})
        passo("transicao wipe", '"transition_id"' in texto(r))

        with tempfile.TemporaryDirectory() as pasta:
            saida = Path(pasta) / "saida_regressao.mp4"
            ferramenta("export_video", {"outputPath": str(saida)})
            for _ in range(120):
                time.sleep(2)
                estado = json.loads(texto(ferramenta("get_export_status")))
                if not estado.get("running", False):
                    break
            passo("exportacao concluida", estado.get("completed") is True, json.dumps(estado))

            ffprobe = subprocess.run(
                [os.environ.get("FFPROBE", "ffprobe"), "-v", "error", "-print_format", "json",
                 "-show_entries", "format=duration:stream=codec_type,width,height", str(saida)],
                capture_output=True, text=True)
            if ffprobe.returncode == 0:
                info = json.loads(ffprobe.stdout)
                duracao = float(info["format"]["duration"])
                codecs = {s["codec_type"] for s in info["streams"]}
                passo("ffprobe: duracao esperada", abs(duracao - (2 * dur / 240000)) < 0.05, f"{duracao}s")
                passo("ffprobe: 1920x1080", any(s.get("width") == 1920 for s in info["streams"]))
                passo("ffprobe: video+audio", codecs == {"video", "audio"}, str(codecs))
            else:
                passo("ffprobe executado", False, ffprobe.stderr[:200])

        r = ferramenta("undo")
        passo("undo", "undid" in texto(r))
        r = ferramenta("redo")
        passo("redo", "redid" in texto(r))
        r = ferramenta("save_project_as", {"path": str(Path(tempfile.gettempdir()) / "regressao_fase2.sprocket")})
        passo("gravar projeto", "saved" in texto(r))
    finally:
        processo.terminate()
        try:
            processo.wait(timeout=10)
        except subprocess.TimeoutExpired:
            processo.kill()

    print()
    if FALHAS:
        print(f"RESULTADO: {len(FALHAS)} PASSOS FALHARAM: {', '.join(FALHAS)}")
        return 1
    print("RESULTADO: TODOS OS PASSOS PASSARAM")
    return 0


if __name__ == "__main__":
    sys.exit(main())
