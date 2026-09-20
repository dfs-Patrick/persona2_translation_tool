# Editor TBF no VS Code

## Instalação no Windows

1. Instale **Docker Desktop**, inicie-o com containers Linux e backend WSL 2.
2. Instale o **VS Code** e a extensão Microsoft **Dev Containers** (`ms-vscode-remote.remote-containers`). Instale também o **PPSSPP para Windows** para testar o jogo. Veja o [guia Windows](windows-docker.md) para os links e requisitos.
3. Clone o projeto em uma pasta do Windows e abra essa pasta no VS Code.
4. Execute **Dev Containers: Reopen in Container** pela paleta (Ctrl+Shift+P).
   A primeira construção instala a ferramenta e prepara os dois VSIX versionados.
5. Aguarde o comando pós-conexão instalar **Persona 2 — Editor TBF** e
   **Persona 2 — PPSSPP Host**. Se necessário, execute **Developer: Reload Window**.
   A barra inferior deve indicar o container.

A extensão principal roda no container, conforme o modelo de [extensões de workspace](https://code.visualstudio.com/api/advanced-topics/extension-host).
O [Dev Container](https://code.visualstudio.com/docs/devcontainers/containers) monta o projeto em `/workspaces/p2-tool`; `/lab` aponta para a pasta `lab` desse projeto.

No Explorer do Windows, abra `<pasta-do-projeto>\lab\iso` e coloque sua ISO
original como `p2is.iso`. Nada do jogo é incluído na imagem ou no repositório.

## Primeira extração e edição

Na paleta, execute **Persona 2: Extrair ISO**. Se você já tem os TBFs, pule a
extração: ela recusa sobrescrever `new/messages` existente.

Clique no ícone Persona 2 na barra lateral. Navegue por `en/new/messages` e abra
um `.msg.tbf`. O cabeçalho mostra o diretório e o nome originais; somente o
contexto pode ser alterado. Ele deve continuar sendo um objeto JSON.

Cada mensagem tem original à esquerda e tradução à direita, em caixas da mesma
altura. Ambos são editáveis. Enter cria uma quebra visual; ao salvar, o JSON a
representa como `\n`. Aspas, barras e acentos são escapados pela serialização,
sem precisar fazê-lo manualmente na interface. Preserve os comandos do jogo.

Use **Salvar** ou Ctrl+S. Desfazer/refazer é integrado ao documento do VS Code.
Para inspecionar o arquivo, use **Reabrir editor com → Editor de texto**. Um TBF
com JSON inválido mostra um erro e pode ser corrigido no editor de texto.

**Compilar** gera `lab/p2is-translated.iso`, usando o PT-BR por padrão. Os logs
aparecem em **Saída → Persona 2**. O código da ferramenta vem da imagem: após
atualizá-lo, use **Dev Containers: Rebuild Container**.

## PPSSPP do Windows e executar ao salvar

Uma extensão dentro do container não pode iniciar diretamente um executável do
Windows. Por isso, há um segundo VSIX pequeno, **PPSSPP Host**, que roda no host
do VS Code e somente gerencia o emulador. Extração e compilação ficam no container.

1. A preparação também copia os VSIX para `lab/tools` como backup. O
   **PPSSPP Host** é instalado automaticamente pelo comando pós-conexão; ele
   permanece como extensão de interface do VS Code para acessar o Windows.
2. Execute **Persona 2: Configurar PPSSPP no Windows**. Informe o executável
   (por exemplo `C:\Program Files\PPSSPP\PPSSPPWindows64.exe`) e o caminho
   absoluto de **`lab\p2is-translated.iso` desse projeto no Windows**.
3. Use **Compilar e executar**. Depois, ative **Executar ao salvar** no cabeçalho.

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
