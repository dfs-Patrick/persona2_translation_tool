# Tradução de Persona 2

Ferramenta para traduzir **Persona 2: Innocent Sin US para PSP**, com editor TBF,
fontes personalizadas e reconstrução da ISO. O jogo não acompanha a ferramenta:
use uma ISO original obtida da sua própria cópia.

## Baixar a ferramenta

Abra [Releases](https://github.com/dfs-Patrick/persona2_translation_tool/releases)
e, em **Assets**, baixe o pacote do seu sistema:

- **Persona2Tool-Windows-x64.zip** — Windows 64 bits.
- **Persona2Tool-Linux-x64.zip** — Linux x64 com glibc.

Os pacotes incluem o runtime, o código e os recursos no executável. Não é
necessário instalar Node, npm, Docker ou WSL. Os arquivos **Source code** são
para desenvolvimento; para usar a ferramenta, escolha um dos ZIPs acima.
Confira nas notas da versão as limitações e se ela está marcada como pré-release.

## Começar no Windows

1. Extraia o ZIP inteiro para uma pasta gravável, como `Documentos/Persona2Tool`.
2. Copie a ISO original US para `lab/iso/p2is.iso` dentro dessa pasta.
3. Execute **Extrair.cmd** e aguarde a mensagem de conclusão. Se aparecer `ERRO`,
   a extração não terminou; preserve suas traduções e consulte o guia.
4. Instale o [Visual Studio Code](https://code.visualstudio.com/).
5. Na aba Extensões, abra o menu `...` → **Instalar do VSIX** e selecione
   `extensions/p2-tbf-editor.vsix` do pacote.
6. Abra a pasta do pacote no VS Code e confirme a confiança no workspace.
7. No painel **Persona 2**, abra `en/new/messages` e um arquivo `.msg.tbf`.
8. Edite a coluna **Depois · tradução** e salve com `Ctrl+S`.
9. Execute **Compilar.cmd** ou clique em **Compilar** no editor.

A ISO traduzida será criada em `lab/p2is-translated.iso`. A pasta `lab` guarda
seus arquivos de trabalho: preserve-a ao atualizar a ferramenta.
Não é necessário usar Dev Containers.

## Testar no PPSSPP

No Windows, instale também `extensions/p2-ppsspp-host.vsix`. Instale o
[PPSSPP](https://www.ppsspp.org/download/) e execute no VS Code o comando
**Persona 2: Configurar PPSSPP no Windows**, informando o executável do emulador
e o caminho absoluto da ISO traduzida. Depois use **Compilar e executar**.
No Linux, abra a ISO no emulador manualmente.

## Linux e documentação

- [Executável Linux](doc/linux-exe.md): comandos para extrair e reconstruir.
- [Executável Windows](doc/windows-exe.md): detalhes e diagnóstico.
- [CLI](doc/cli.md): opções avançadas.
- [Publicar uma versão](doc/releases.md): build local e ZIPs por sistema.
- [Linux pelo código-fonte](doc/linux-native.md): desenvolvimento com Node.
- [Documentação do editor](doc/vscode-editor.md): recursos do editor e fluxo antigo de container.

O fluxo de Docker permanece disponível como alternativa técnica em
[Windows/Docker](doc/windows-docker.md) e [Linux/WSL](doc/linux-wsl.md).
A distribuição recomendada é a dos executáveis em Releases.

## Atualizar ou recuperar uma extração interrompida

Extraia a nova versão em uma pasta separada. Preserve a pasta antiga se houver
traduções. Dumps de uma extração interrompida podem estar incompletos: não os
reutilize para reconstruir a ISO. Instale o VSIX atualizado ao trocar de versão.

## Origem

Baseado no [p2_tool](https://github.com/eiowlta/p2_tool), de eiowlta.
Os créditos e o histórico do projeto original são preservados.
