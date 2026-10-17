# Aven Editor

**Um editor de vídeo simples, 100% em português europeu, pensado para vídeos do YouTube — com assistente de IA opcional.**

Aven Editor é um editor de vídeo não destrutivo com uma interface mínima: biblioteca de media à
esquerda, pré-visualização ao centro, propriedades à direita e a timeline em baixo. A barra de
ferramentas tem só o essencial — Selecionar e Cortar/Dividir — e tudo o resto está no menu
contextual do elemento com que estás a trabalhar. Sem terminologia profissional: as coisas
chamam-se pelo nome.

O Aven Editor é derivado do projeto open source
[Sprocket](https://github.com/SprocketVideo/Sprocket), de D'Arcy Rittich, e mantém o seu motor —
reprodução, timeline por comandos com anular/refazer completo, efeitos, exportação — com a
interface redesenhada para quem edita para o YouTube e quer simplicidade acima de quantidade de
funcionalidades. O inventário completo de funcionalidades está em [FEATURES.md](FEATURES.md).

---

## O essencial

- **Edição não destrutiva** — os clips mudam pontos de entrada/saída, posição e efeitos; os
  ficheiros originais nunca são alterados.
- **Anular/refazer em tudo** — cada ação é um comando; uma sequência inteira de ações da IA
  anula-se com um único Ctrl+Z.
- **Menu contextual por tipo de elemento** — clipe, pista, corte, autocolante: cada um abre o
  menu certo para o que é.
- **«Editar recorte e movimento»** — enquadramento arrastável com pré-visualização do fotograma
  real e predefinições independentes de entrada/saída para zoom e movimento de câmara.
- **Captura de fotograma** — tira um fotograma do vídeo e transforma-o numa imagem sobreposta
  persistente.
- **Transições aleatórias em todos os cortes** — com semente reproduzível, pré-visualização
  corte a corte e anulação num só gesto.
- **Transições e autocolantes emoji** — um glifo que cresce e cobre o ecrã, revelação por
  silhueta, e autocolantes PNG com um clique.
- **Catálogo de looks** — 24 predefinições num clique (de preto e branco a sépia).
- **Exportação validada, nunca automática** — nada é escrito sem confirmação explícita; a
  pré-visualização e a exportação usam o mesmo grafo de renderização, pelo que o que vês é o
  que sai.
- **100% PT-PT** — interface e ajuda local (Ajuda ▸ Documentação) inteiramente em português
  europeu.

---

## Assistente de IA (opcional)

- Escolhe **Gemini** ou **GLM** e usa a tua própria chave de subscrição — guardada encriptada
  com o DPAPI do Windows, nunca em texto simples.
- Descreves a edição em palavras; o assistente propõe um **plano passo a passo**; aprovas; ele
  executa; um Ctrl+Z anula tudo.
- O assistente nunca exporta nem grava sozinho — a exportação continua a exigir a tua
  confirmação explícita.
- O painel está escondido por defeito (Ver ▸ Painel Assistente IA).

---

## Transferir

O Aven Editor é desenvolvido e testado em **Windows 10 e 11**. A primeira release — instalador
com atualização automática e FFmpeg incluído — está em preparação e será publicada na página de
[releases](https://github.com/avenseElectro/aven-editor/releases). Até lá, podes compilar a
partir do código-fonte.

---

## Compilar a partir do código-fonte

Precisas de:

- **.NET 10 SDK**
- **Bibliotecas partilhadas do FFmpeg 8** (build BtbN `*-gpl-shared`) para executar a partir da
  fonte — indica a pasta `bin` na variável de ambiente `SPROCKET_FFMPEG8_DIR`. Nas builds
  empacotadas o FFmpeg vem incluído, sem configuração.
- **`ffmpeg` no PATH** apenas para correr os testes (geram um clipe de teste determinista).

```bash
# Compilar a solução completa
dotnet build Sprocket.slnx

# Correr todos os testes (xUnit)
dotnet test Sprocket.slnx

# Executar o editor (argumento opcional = ficheiro de média)
dotnet run --project src/Sprocket.App [caminho/para/video.mp4]
```

> **Nota:** os projetos e namespaces internos mantêm o nome `Sprocket` da base original; a
> identidade do produto é **Aven Editor**.

---

## Arquitetura em resumo

Base de código gerida em C# / .NET 10 (Avalonia UI, SkiaSharp, FFmpeg 8, OpenAL), com
dependências estritamente acíclicas entre projetos:

```
Sprocket.App ──► Sprocket.Playback ──► Sprocket.Render ──► Sprocket.Core
     │              │      │              │
     │              │      └──► Sprocket.Audio ──► Sprocket.Core
     │              └──► Sprocket.Media ──────────► Sprocket.Core
     └──► (Persistence, Export, Mcp) ────► Sprocket.Core
```

- **`Sprocket.Core`** — modelo de timeline em dados puros, grafo de renderização (função pura de
  projeto + tempo, partilhado entre pré-visualização e exportação) e pilha de comandos.
- **`Sprocket.Media`** — interação com FFmpeg 8 via binding próprio (descodificação,
  codificação, aceleração por hardware com recurso a software).
- **`Sprocket.Render`** — composição GPU e efeitos.
- **`Sprocket.Playback`** / **`Sprocket.Audio`** — o áudio é o relógio mestre; o vídeo segue.
- **`Sprocket.App`** — interface Avalonia e composição raiz.

O desenho completo está em [ARCHITECTURE.md](ARCHITECTURE.md).

---

## Licença e créditos

[MIT](LICENSE). O Aven Editor é derivado do
[Sprocket](https://github.com/SprocketVideo/Sprocket) de D'Arcy Rittich — os créditos e avisos
do projeto original mantêm-se em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) (também na
app, em Ajuda ▸ Avisos de terceiros).

As builds empacotadas incluem o **FFmpeg 8** (configuração GPL, que fornece os codificadores
x264/x265); o código-fonte correspondente está ligado nos avisos de terceiros. Os glifos emoji
vêm do conjunto **Twemoji** (CC-BY 4.0).
