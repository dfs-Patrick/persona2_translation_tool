# Tradução de Persona 2

Este projeto permite traduzir **Persona 2: Innocent Sin** para PSP. Você não
precisa entender programação para participar como voluntário.

O jogo não acompanha o projeto. Para começar, você precisa ter uma ISO original
de **Persona 2: Innocent Sin US** obtida da sua própria cópia.

## Instalação para iniciantes no Windows

Você não precisa criar uma conta, instalar Git ou usar o terminal.

### 1. Instale os programas

Abra cada link e aceite as opções recomendadas do instalador:

- [Docker Desktop](https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe)
- [Visual Studio Code](https://code.visualstudio.com/sha/download?build=stable&os=win32-x64-user)
- [PPSSPP para Windows](https://www.ppsspp.org/download/)

O instalador do Docker prepara automaticamente os componentes necessários do
Windows. Se ele pedir para reiniciar o computador, reinicie antes de continuar.
Depois de reiniciar, abra o Docker Desktop e espere até ele indicar que está
pronto.

### 2. Baixe o projeto

1. Abra a página do projeto no GitHub:
   <https://github.com/dfs-Patrick/persona2_translation_tool>
2. Clique no botão verde **Code**.
3. Clique em **Download ZIP**.
4. Abra o arquivo baixado e extraia a pasta para um local fácil, como
   `Documentos`.
5. Abra o **Visual Studio Code** e escolha **File → Open Folder**.
6. Selecione a pasta extraída do projeto.

Não é necessário fazer login no GitHub.

### 3. Instale o suporte do projeto

Ao abrir a pasta, o VS Code mostrará uma recomendação para instalar **Dev
Containers**. Clique em **Install**. Essa é a única extensão externa que você
precisa instalar manualmente, porque ela é o recurso do VS Code que permite
abrir o projeto dentro do ambiente preparado.

Depois que a instalação terminar:

1. Pressione `Ctrl+Shift+P`.
2. Escolha **Dev Containers: Reopen in Container**.
3. Aguarde. Na primeira vez pode levar alguns minutos.
4. Quando o VS Code perguntar se deve recarregar, escolha **Reload Window**.
  Se o projeto já estava aberto antes desta configuração, execute **Dev
  Containers: Rebuild Container** uma vez.

O projeto já inclui **Persona 2 — Editor TBF** na imagem do container, então a
aba deve aparecer assim que o VS Code conectar, sem instalar ou recarregar nada.
O **PPSSPP Host** precisa ficar no VS Code do Windows, porque é ele
que consegue iniciar um programa do Windows; ele fica disponível em
`lab/tools/p2-ppsspp-host.vsix` para a instalação local quando você for testar
o jogo. A aba **Persona 2** deve aparecer depois que o container terminar de
recarregar.

### 4. Coloque sua ISO

1. Abra a pasta do projeto no Explorador de Arquivos.
2. Entre em `lab` e depois em `iso`.
3. Copie sua ISO original para essa pasta.
4. Renomeie o arquivo para `p2is.iso`.

O caminho deve terminar assim:

```text
pasta-do-projeto\lab\iso\p2is.iso
```

### 5. Extraia os arquivos para tradução

1. No VS Code conectado ao container, clique no ícone **Persona 2** na barra
   lateral.
2. No painel **Arquivos de tradução**, clique em **Extrair**.
3. Aguarde a mensagem de conclusão na saída do VS Code.
4. Abra as pastas `en`, `new`, `messages`.
5. Abra um arquivo com final `.msg.tbf`.

Se o painel estiver vazio, confira se a ISO está no local indicado no passo 4.

### 6. Faça sua primeira tradução

O editor mostra:

- **Antes · original**: texto original do jogo, à esquerda.
- **Depois · tradução**: texto que será usado no jogo, à direita.

Digite a tradução na coluna da direita e clique em **Salvar** ou pressione
`Ctrl+S`. Pressione Enter normalmente para criar uma nova linha. O editor cuida
da conversão para o formato do arquivo.

### 7. Gere a ISO traduzida

Clique em **Compilar** no alto do editor. Ao terminar, a ISO estará em:

```text
pasta-do-projeto\lab\p2is-translated.iso
```

Para testar no jogo:

1. Abra a Paleta de Comandos com `Ctrl+Shift+P`.
2. Execute **Persona 2: Configurar PPSSPP no Windows**.
3. Escolha o programa PPSSPP instalado e informe o caminho da ISO traduzida.
4. Volte ao editor e clique em **Compilar e executar**.

Depois da primeira configuração, você pode marcar **Executar ao salvar** para
recompilar e abrir o jogo automaticamente.

## Ajuda para situações específicas

- [Guia visual completo do VS Code](doc/vscode-editor.md): detalhes da tela,
  edição, salvamento, compilação e execução.
- [Uso com Docker e PowerShell](doc/windows-docker.md): instalação alternativa
  e comandos para quem prefere controlar o processo pelo terminal.
- [Editor TBF](vscode-extension/README.md): funcionamento e desenvolvimento da
  extensão de tradução.
- [PPSSPP Host](vscode-ppsspp-host/README.md): integração avançada com o
  emulador do Windows.

A pasta `lab` contém sua ISO, arquivos extraídos e traduções locais. Ela não é
enviada ao Git nem compartilhada pelo projeto.

## Origem

Este projeto é baseado no [p2_tool](https://github.com/eiowlta/p2_tool), de
eiowlta. Os créditos e o histórico do projeto original são preservados.
