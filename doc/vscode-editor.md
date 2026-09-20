# Editor TBF no VS Code

## Instalação e primeiro uso

Siga esta ordem uma única vez. O Editor TBF já vem dentro da imagem do Dev
Container e aparece assim que o VS Code conecta. O PPSSPP Host é instalado no VS Code do Windows quando
você for usar **Compilar e executar**, porque somente o VS Code local consegue
iniciar o PPSSPP do Windows.

1. Instale o **Docker Desktop** com containers Linux e backend WSL 2.
2. Instale o **VS Code** e a extensão Microsoft **Dev Containers**
   (`ms-vscode-remote.remote-containers`). Instale também o **PPSSPP para
   Windows** se quiser testar o jogo.
3. Clone o projeto em uma pasta do Windows e abra essa pasta no VS Code.
4. Pressione `Ctrl+Shift+P`, execute **Dev Containers: Reopen in Container** e
   aguarde a construção terminar. Na primeira vez, isso compila a ferramenta e
   gera os dois VSIX dentro da imagem.
5. Aguarde o aviso de conexão concluída. O comando pós-conexão copia os VSIX
   para `lab/tools`; o **Persona 2 — Editor TBF** já veio na imagem e não
   precisa de instalação ou recarga. A barra inferior deve mostrar o container.
   Se o projeto já estava aberto antes desta configuração, execute **Dev
   Containers: Rebuild Container** uma vez para atualizar a imagem.
6. Abra a aba **Persona 2** na barra de atividades. Ela mostra a árvore
   **Arquivos de tradução**. Mesmo vazia, o cabeçalho da árvore oferece os
   botões **Extrair**, **Compilar**, **Compilar e executar**, **Saída** e
   **Atualizar**.
7. No Explorer do Windows, abra `<pasta-do-projeto>\lab\iso` e coloque sua ISO
   original com o nome `p2is.iso`.
8. No cabeçalho da árvore, clique em **Extrair**. Aguarde a mensagem de
   conclusão na saída e abra `en/new/messages` na árvore lateral.
9. Abra um arquivo `.msg.tbf`. O editor visual mostra o cabeçalho no alto,
   **Antes · original** à esquerda e **Depois · tradução** à direita.

A extensão principal roda no container, conforme o modelo de [extensões de workspace](https://code.visualstudio.com/api/advanced-topics/extension-host).
O [Dev Container](https://code.visualstudio.com/docs/devcontainers/containers) monta o projeto em `/workspaces/p2-tool`; `/lab` aponta para a pasta `lab` desse projeto.

## Editar e salvar uma tradução

Se você já tem os TBFs, pule o passo de extração e abra diretamente
`en/new/messages` na árvore **Arquivos de tradução**. A extração recusa
sobrescrever `new/messages` existente.

O passo 9 já abre o editor visual. O cabeçalho mostra o diretório e o nome
originais; somente o contexto pode ser alterado. Ele deve continuar sendo um
objeto JSON.

Cada mensagem tem original à esquerda e tradução à direita, em caixas da mesma
altura. Ambos são editáveis. Enter cria uma quebra visual; ao salvar, o JSON a
representa como `\n`. Aspas, barras e acentos são escapados pela serialização,
sem precisar fazê-lo manualmente na interface. Preserve os comandos do jogo.

Arquivos `.tbf` podem ser localizados pelo `Ctrl+Shift+F`, inclusive quando
estão dentro de `lab/`. O projeto associa `.tbf` à linguagem TBF, mantendo o
JSON válido e destacando comandos entre colchetes e variáveis usadas nos textos.

10. Edite a coluna **Depois · tradução** e use **Salvar** ou `Ctrl+S`.
   Desfazer/refazer é integrado ao documento do VS Code.
Para inspecionar o arquivo, use **Reabrir editor com → Editor de texto**. Um TBF
com JSON inválido mostra um erro e pode ser corrigido no editor de texto.

11. Clique em **Compilar** para gerar `lab/p2is-translated.iso`, usando o PT-BR
   por padrão. Os logs
aparecem em **Saída → Persona 2**. O código da ferramenta vem da imagem: após
atualizá-lo, use **Dev Containers: Rebuild Container**.

## Executar o jogo

Uma extensão dentro do container não pode iniciar diretamente um executável do
Windows. Por isso, há um segundo VSIX pequeno, **PPSSPP Host**, que roda no host
do VS Code e somente gerencia o emulador. Extração e compilação ficam no container.

1. Abra a pasta `lab/tools` no Explorer do Windows e dê duplo clique em
   `p2-ppsspp-host.vsix`. O VS Code local instalará o **Persona 2 — PPSSPP Host**.
2. Execute **Persona 2: Configurar PPSSPP no Windows**. Informe o executável
   (por exemplo `C:\Program Files\PPSSPP\PPSSPPWindows64.exe`) e o caminho
   absoluto de **`lab\p2is-translated.iso` desse projeto no Windows**.
3. Use **Compilar e executar**. Depois, ative **Executar ao salvar** no cabeçalho
   se quiser recompilar e abrir o jogo automaticamente depois de cada salvamento.

O PPSSPP iniciado pela extensão é encerrado antes de recompilar e reiniciado
com a ISO nova somente após sucesso. Salve o progresso do jogo antes de editar.
Instâncias abertas manualmente não são encerradas. Save states não são restaurados.
Ao trocar de projeto, ajuste o caminho Windows da ISO nas configurações do host.

Salvamentos próximos são agrupados. Se houver salvamentos durante a compilação,
uma nova compilação fica pendente; a extensão abre o jogo após a última. Somente
TBFs de `new/messages` e arquivos de `fonts/new` disparam esse fluxo. Saídas em
`after`, dumps e ISOs não provocam ciclos. Use uma janela por projeto e não rode
a CLI/launcher em paralelo com a extensão.

## Desenvolvimento

O editor usa `CustomTextEditorProvider`, preservando o documento, salvamento e
histórico do VS Code, conforme a [API de editores personalizados](https://code.visualstudio.com/api/extension-guides/custom-editors).
Os dois componentes são JavaScript e não exigem compilação de TypeScript.

```bash
cd vscode-extension
npm ci
npm test
npm run package
```

O estágio `development` do Dockerfile empacota ambos os VSIX. Nenhum arquivo de
`lab`, credencial ou chave SSH entra no contexto da imagem. O Dev Container usa
root para trabalhar também com os arquivos existentes do checkout WSL; o
container CLI normal continua usando o usuário configurado no Compose.
