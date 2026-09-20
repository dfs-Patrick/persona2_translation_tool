# Windows, Docker e PPSSPP

A ferramenta executa em um container Arch Linux; o PPSSPP executa no Windows e abre a ISO pela pasta compartilhada `lab`. O editor TBF do VS Code reconstrói a ISO e pode iniciar o jogo ao salvar, sem executar uma interface gráfica dentro do Docker. Consulte o [guia do editor](vscode-editor.md) para a instalação automática no Dev Container.

## Preparação

1. Instale o [Docker Desktop para Windows](https://docs.docker.com/desktop/setup/install/windows-install/), usando o backend [WSL 2](https://docs.docker.com/desktop/features/wsl/) e containers Linux. Mantenha o Docker Desktop iniciado.
2. Instale o [Git para Windows](https://git-scm.com/downloads/win) e clone este fork em uma pasta do Windows. Abra essa pasta no VS Code local e use um terminal PowerShell do Windows para os comandos abaixo.
3. Instale ou extraia o [PPSSPP para Windows](https://www.ppsspp.org/docs/getting-started/introduction/). **O PPSSPP é necessário para testar a ISO e conferir a tradução e os acentos no jogo.** Ele é instalado separadamente e não acompanha a imagem Docker.

Não é necessário instalar Arch Linux como uma distribuição WSL: o Arch já está dentro da imagem Docker. Atenda aos requisitos de WSL 2 e virtualização indicados pelo instalador do Docker Desktop e reinicie o Windows se solicitado. Em Settings → General, selecione o backend WSL 2. Use containers Linux.

No PowerShell, escolha uma pasta para o projeto e execute:

```powershell
git clone https://github.com/dfs-Patrick/persona2_translation_tool.git
cd persona2_translation_tool
docker version
docker compose version
```

`docker version` deve mostrar tanto Client quanto Server. Se o servidor não responder, inicie o Docker Desktop e aguarde o engine ficar pronto antes de continuar.

Na raiz do checkout:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\p2-tool.ps1 -Action setup
```

O setup cria `lab/iso` no seu computador e constrói a imagem a partir da [imagem oficial do Arch Linux](https://hub.docker.com/_/archlinux/). O build atualiza os pacotes com `pacman -Syu`, instala Node.js, npm e certificados, instala as dependências do lockfile, compila o TypeScript e executa os testes de fontes. Não é necessário instalar Node.js no Windows. Execute o setup novamente quando atualizar o código ou os perfis distribuídos.

Abra a pasta `lab/iso` do checkout pelo Explorador de Arquivos, copie sua ISO original de Innocent Sin US e dê a ela o nome `p2is.iso`. O setup imprime o caminho completo dessa pasta. Para abri-la pelo terminal:

```powershell
explorer.exe .\lab\iso
```

A pasta do computador está montada diretamente em `/lab` no container: `lab/iso/p2is.iso` aparece como `/lab/iso/p2is.iso`. Não é necessário copiar arquivos para a imagem ou usar `docker cp`; a pasta continua acessível mesmo com o container parado. A ISO gerada em `/lab/p2is-translated.iso` também aparece no computador em `lab/p2is-translated.iso`.

Por exemplo, se você clonou em `C:\Projetos\persona2_translation_tool`, abra `C:\Projetos\persona2_translation_tool\lab` na barra de endereços do Explorer. `/lab` é o caminho interno do container; não significa `C:\lab`. Para abrir a pasta inteira a partir da raiz do projeto, use `explorer.exe .\lab`.

**Você deve fornecer sua própria ISO original.** O jogo, os textos extraídos e as ISOs reconstruídas não são distribuídos neste repositório. Não adicione o conteúdo de `lab` ao Git.

Depois de adicionar a ISO:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\p2-tool.ps1 -Action extract
```

O launcher recusa a extração se já existir `lab/translation/en/new/messages`, para preservar traduções existentes. Edite os `.msg.tbf` nessa pasta conforme o [README](../readme.md).

## Reconstruir e executar

Informe o caminho real do executável instalado. Nesta validação, a instalação pelo Chocolatey ficou em `C:\Program Files\PPSSPP\PPSSPPWindows64.exe`; confirme no seu computador pelo Explorer ou pelas propriedades do atalho. O exemplo abaixo usa esse caminho:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\p2-tool.ps1 -Action rebuild-run -PpssppPath 'C:\Program Files\PPSSPP\PPSSPPWindows64.exe'
```

Esse comando fecha a instância do emulador anteriormente iniciada pelo launcher, reconstrói `lab/p2is-translated.iso` e abre a nova ISO somente se o rebuild terminar com sucesso. Não restaura save states automaticamente. Se o emulador não responder ao fechamento, a operação para e pede que você o feche.

O perfil padrão é o PT-BR distribuído com a ferramenta. Use `-Font minha-fonte` para passar `--font minha-fonte` à CLI, ou `-Font original` para desativar o perfil. Perfis personalizados ficam em `lab/translation/en/fonts/new/<nome>`.

Também é possível configurar o executável para a sessão atual:

```powershell
$env:P2_PPSSPP = 'C:\Program Files\PPSSPP\PPSSPPWindows64.exe'
powershell -ExecutionPolicy Bypass -File .\scripts\p2-tool.ps1 -Action rebuild-run
```

| Ação | Resultado |
| --- | --- |
| `setup` | Constrói ou atualiza a imagem da ferramenta. |
| `extract` | Extrai a ISO original e prepara os arquivos editáveis. |
| `rebuild` | Reconstrói a ISO, sem abrir o emulador. |
| `run` | Abre a ISO já gerada, sem depender do Docker. |
| `rebuild-run` | Reconstrói e abre a ISO após sucesso. |

O launcher registra sua instância do PPSSPP em `lab/.ppsspp-session.json` e confere PID, horário de início e executável antes de fechá-la. Instâncias abertas manualmente não são encerradas; feche-as se estiverem mantendo a ISO em uso. Um lock impede operações simultâneas pelo launcher no mesmo projeto.

## Extensão do VS Code

O editor TBF, os comandos no container e a integração com o PPSSPP estão descritos no [guia do editor](vscode-editor.md). Use esse fluxo para editar em duas colunas e executar ao salvar sem chamar o launcher PowerShell.

### Referência do launcher

A extensão chama a CLI diretamente no container. Para outras automações que usem este launcher no Windows, observe:

1. Observar salvamentos dos TBFs editáveis e dos perfis locais, ignorando dumps, `after`, ISOs e arquivos de estado do launcher.
2. Agrupar salvamentos próximos e executar uma única operação `rebuild-run` por vez.
3. Se houver novos salvamentos durante o rebuild, marcar uma execução pendente e reconstruir novamente ao terminar; o lock do launcher apenas rejeita concorrência, não mantém uma fila.
4. Mostrar stdout/stderr no painel de saída e tratar código de saída diferente de zero como falha. Não iniciar o jogo após falha.
5. Executar o launcher no Windows, passando caminhos como argumentos separados de processo, sem montar comandos com texto dos arquivos traduzidos.

O [host de extensões do VS Code](https://code.visualstudio.com/api/advanced-topics/extension-host) pode ser local ou remoto. Neste fluxo, o processo que inicia o PPSSPP deve estar no Windows. Uma extensão executada no WSL ou em Dev Containers precisará de uma ponte explícita para o host; veja as [orientações para extensões remotas](https://code.visualstudio.com/api/advanced-topics/remote-extensions).

## Projeto mantido no WSL (opcional)

Quem mantém o checkout em uma distribuição WSL precisa habilitá-la em **Docker Desktop → Settings → Resources → WSL Integration** e aplicar a alteração. Selecionar WSL 2 na tela General não habilita automaticamente todas as distribuições. Confira `docker version` e `docker compose version` no terminal dessa distribuição.

Para encontrar `lab` no Explorer, execute `explorer.exe .` dentro da pasta `lab` no WSL, ou use o endereço `\\wsl.localhost\<distribuição>\<caminho-do-projeto>\lab`. Neste ambiente, o endereço é `\\wsl.localhost\archlinux\root\projetos\p2-tool\lab`; adapte-o à sua distribuição e pasta.

Execute o rebuild pelo terminal WSL com os comandos abaixo. Para testar, abra a ISO no PPSSPP do Windows pelo Explorer. O launcher PowerShell completo descrito acima tem como fluxo principal um checkout em uma pasta do Windows.

## Uso direto do container

No terminal da raiz do projeto:

```bash
mkdir -p lab/iso
docker compose build tool
docker compose run --rm tool --help
docker compose run --rm tool rebuildTbf lab/iso/p2is.iso lab/translation/en --game is --variant us --locale en
```

No Linux, configure `P2_UID` e `P2_GID` com seu usuário antes desses comandos para que os arquivos gerados pertençam a ele:

```bash
export P2_UID="$(id -u)"
export P2_GID="$(id -g)"
```

O container usa os pacotes Node.js e npm dos repositórios do Arch e as dependências JavaScript do lockfile. As versões dos pacotes do sistema acompanham o Arch no momento da atualização da imagem. Apenas `lab` é montado em execução, com acesso de leitura e escrita. O contexto de build usa uma lista explícita de código, mapas, testes e fontes distribuídas: não inclui `lab`, `.git`, credenciais ou chaves SSH. A imagem não contém a ISO nem a tradução do usuário. Ela não replica a instalação pessoal do Arch.
