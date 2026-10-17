# Ajuda do Aven Editor

Um editor de vídeo simples, em português, para vídeos de YouTube. Esta página cobre o dia a dia: importar, cortar, transições, IA e exportação.

## Começar

- A **biblioteca** está à esquerda: importe vídeos, músicas e imagens com o botão de importar ou arrastando ficheiros para lá.
- Clique num item da biblioteca (ou arraste-o) para o pôr na timeline, em baixo.
- A **pré-visualização** fica ao centro; as **propriedades** do elemento selecionado aparecem à direita.
- O **menu contextual** (clique direito num clipe, na timeline ou na pré-visualização) tem quase tudo — dividir, transições, recorte e movimento, marcadores, looks.
- Se largar um clipe sobre um espaço ocupado, a edição é recusada com uma dica. Mantenha **Ctrl** premido ao largar para **inserir** o clipe (os seguintes são empurrados).

## Atalhos

| Atalho | O que faz |
|---|---|
| Espaço | Reproduzir ou pausar |
| Ctrl+Shift+Espaço | Reproduzir do ponto de entrada ao ponto de saída |
| Ctrl+Z | Anular |
| Ctrl+Shift+Z | Refazer |
| Delete ou Backspace | Apagar o selecionado |
| Shift+Delete | Apagar o selecionado e fechar o espaço |
| Z | Ajustar a timeline ao ecrã |
| Ctrl+S | Gravar o projeto |
| Ctrl+Shift+S | Gravar com outro nome |

## Edição essencial

- **Dividir um clipe**: posicione o indicador de reprodução e use a ferramenta de cortar ou o menu contextual do clipe.
- **Transições aleatórias**: clique direito no clipe e escolha «Aplicar transições aleatórias…». Define a duração, os tipos permitidos e a semente; a lista mostra qual transição calha em cada corte. Aplicar é uma única edição — um Ctrl+Z remove todas.
- **Transições emoji**: nos tipos de transição, «emoji zoom» faz o glifo crescer até cobrir o ecrã; «emoji máscara» revela o vídeo pela silhueta do emoji.
- **Autocolantes**: no separador «Autocolantes» da biblioteca, um clique coloca a imagem no indicador de reprodução. Ajuste o enquadramento em «Editar recorte e movimento».
- **Editar recorte e movimento**: clique direito no clipe de vídeo. A janela mostra o fotograma real; arraste ou use a roda para enquadrar, escolha predefinições de entrada e saída (movimento de câmara) e pré-visualize antes de aplicar.
- **Looks**: no grupo «Limpo» existem 24 aspetos, incluindo «Preto e branco» e «Sépia».
- **Marcadores**: adicione pelo menu contextual da timeline para marcar posições importantes.

## Assistente IA (dentro da app)

- Ative em Ver ▸ Painel Assistente IA. A configuração fica em Editar ▸ Preferências ▸ «Assistente de IA»: fornecedor (Google Gemini ou GLM), modelo e chave.
- A chave é guardada **cifrada** no cofre do Windows (`%AppData%\Sprocket\ai-keys.bin`) — nunca em texto simples.
- Como funciona: escreve o pedido → o assistente propõe um plano passo a passo → **aprova** → executa → um Ctrl+Z anula tudo. Pedidos ambíguos recebem uma pergunta de esclarecimento. O assistente nunca exporta sozinho.

## Editar com IA externa (MCP)

Deixe um agente de IA (como o Claude Code) editar a timeline por si:

- Em Editar ▸ Preferências ▸ «Controlo de IA (MCP)», ative o servidor, ative a exigência de token e use «Copiar comando de configuração».
- Cole o comando copiado num terminal onde corre o seu agente e reinicie o agente. O endereço é `http://127.0.0.1:41008/mcp` (a porta aparece nas Preferências).
- O servidor só aceita ligações do próprio computador (localhost). Cada ação do agente é aprovada por si no agente, e um Ctrl+Z no editor anula tudo. A exportação continua sempre manual.

## Exportar

A exportação é sempre iniciada e confirmada por si — nunca automática. Escolha o formato e a pasta na janela de exportação e verifique a duração e a resolução apresentadas antes de avançar.

## Ficheiros e definições

- Definições: `%AppData%\Sprocket\settings.json`
- Chaves da IA (cifradas): `%AppData%\Sprocket\ai-keys.bin`
- Registos de erros: Ajuda ▸ Acerca do Aven Editor ▸ «Abrir pasta de registos»

## Mais ajuda

- Projeto no GitHub: <https://github.com/avenseElectro>
