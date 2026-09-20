# Persona 2 — Editor TBF

Editor de tradução para os arquivos JSON `.tbf` da ferramenta Persona 2.
Execute a extensão no **Dev Container** do repositório.

- Aba Persona 2 com arquivos de `lab/translation`.
- Cabeçalho com diretório e arquivo originais somente para leitura; contexto
  editável como objeto JSON (exemplo: `{"scene":"Entrada da escola"}`).
- Original e tradução editáveis lado a lado, com a mesma altura em cada par.
- Enter aparece como quebra de linha na interface e como `\n` no JSON salvo.
  Uma sequência literal `\n` digitada é preservada como texto literal; use Enter
  para criar uma quebra real.
- Ctrl+S, botão Salvar, desfazer/refazer e indicação de alterações não salvas
  usam o documento do VS Code. “Reabrir editor com → Editor de texto” mostra o JSON.
- Extrair, compilar, compilar e executar, saída de comandos e executar ao salvar.

Ao abrir o projeto no Dev Container, o VSIX do Editor TBF é instalado
automaticamente no ambiente remoto pelo comando pós-conexão. Depois da
instalação, abra a aba **Persona 2**, coloque `p2is.iso` em
`lab/iso`, clique em **Extrair**, abra `en/new/messages` e edite um `.msg.tbf`.
Configure o caminho do emulador e da ISO para usar o PPSSPP Host. O editor chama
Node diretamente no container; o componente de interface abre somente o
emulador.
Nenhum comando PowerShell é usado pela extensão.

A fila agrupa salvamentos consecutivos e nunca executa dois rebuilds da mesma
janela em paralelo. Se houver outra edição durante um rebuild, a fila compila
novamente antes de abrir o jogo. Erros são mostrados no canal de saída Persona 2.
Use uma única janela por projeto e não execute rebuilds externos simultaneamente.

Os `.ef.tbf` e `.cf.tbf` contêm comentários/referências; os diálogos compilados
vêm dos `.msg.tbf`. Um campo `after` vazio mantém `before`. A ISO não acompanha
a extensão e todo o diretório `lab` permanece fora do Git.

Instalação detalhada: [guia no repositório](https://github.com/dfs-Patrick/persona2_translation_tool/blob/main/doc/vscode-editor.md).
