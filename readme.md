# Persona 2 Translation Tool

Ferramenta para extrair, traduzir e reconstruir a versão americana de **Persona
2: Innocent Sin** para PSP.

Este projeto não inclui o jogo. Você precisa fornecer sua própria ISO original.

## Comece aqui

O caminho recomendado para quem está começando no Windows é usar o VS Code com
Docker. A instalação prepara a ferramenta e instala automaticamente as duas
extensões do projeto.

1. Instale o [Docker Desktop para Windows](https://docs.docker.com/desktop/setup/install/windows-install/),
   com backend WSL 2 e containers Linux.
2. Instale o [VS Code](https://code.visualstudio.com/) e a extensão
   [Dev Containers](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers).
3. Instale o [PPSSPP para Windows](https://www.ppsspp.org/docs/getting-started/introduction/)
   para testar a ISO traduzida.
4. Clone este repositório e abra a pasta no VS Code:

   ```powershell
   git clone https://github.com/dfs-Patrick/persona2_translation_tool.git
   cd persona2_translation_tool
   code .
   ```

5. Pressione `Ctrl+Shift+P` e execute **Dev Containers: Reopen in Container**.
   Aguarde a primeira construção terminar. O container compila a ferramenta,
   prepara as extensões e instala automaticamente **Persona 2 — Editor TBF** e
   **Persona 2 — PPSSPP Host**.
6. No Explorer do Windows, abra a pasta `lab/iso` dentro do projeto e copie sua
   ISO original para lá com o nome `p2is.iso`.
7. Na barra de atividades do VS Code, abra **Persona 2** e clique em
   **Extrair**.
8. Na árvore **Arquivos de tradução**, abra `en/new/messages` e depois um
   arquivo `.msg.tbf`.
9. Traduza na coluna **Depois · tradução** e pressione `Ctrl+S` ou clique em
   **Salvar**. Use **Compilar** para gerar `lab/p2is-translated.iso`.
10. Execute **Persona 2: Configurar PPSSPP no Windows** uma vez, informe o
    executável do PPSSPP e o caminho Windows da ISO gerada. Depois use
    **Compilar e executar** para testar.

O editor mostra o original à esquerda e a tradução à direita. Pressionar Enter
cria uma quebra de linha real; a extensão converte isso para `\n` no arquivo
`.tbf`. O campo `context` do cabeçalho pode ser alterado, mas os demais dados do
cabeçalho devem permanecer intactos.

## Arquivos importantes

- `lab/iso/p2is.iso`: ISO original fornecida por você.
- `lab/translation/en/new/messages/`: TBFs que você traduz.
- `lab/p2is-translated.iso`: ISO reconstruída para testar no PPSSPP.
- `lab/tools/`: cópias dos VSIX gerados pelo container.

Tudo dentro de `lab/` é local e ignorado pelo Git. Não envie ISOs, arquivos
extraídos ou traduções para o repositório.

## Quando precisar de mais detalhes

- [Guia visual do editor no VS Code](doc/vscode-editor.md): instalação,
  extração, edição, compilação, PPSSPP e execução ao salvar.
- [Windows, Docker e PowerShell](doc/windows-docker.md): uso do launcher pelo
  terminal e diagnóstico do ambiente Docker.
- [README do editor TBF](vscode-extension/README.md): comportamento do editor e
  desenvolvimento da extensão.
- [README do PPSSPP Host](vscode-ppsspp-host/README.md): integração com o
  emulador no Windows.

## Linux ou WSL

Também é possível usar a CLI diretamente com Node.js e npm instalados:

```bash
npm ci
npx tsc
node dist/cli/mod.js --help
```

Para esse fluxo, siga as seções equivalentes no [guia Windows/Docker](doc/windows-docker.md)
ou consulte a ajuda da CLI. O perfil de fonte `pt-br` é aplicado por padrão ao
reconstruir IS US.

## Origem do projeto

Este trabalho é um fork do [p2_tool](https://github.com/eiowlta/p2_tool), de
eiowlta. Os créditos e o histórico do projeto original são preservados.
