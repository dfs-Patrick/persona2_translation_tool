# Linux no WSL

Este guia é para quem quer manter o checkout e o `lab` dentro de uma
distribuição Linux do WSL, mas usar o Docker Desktop como engine. É o fluxo
recomendado para desenvolvimento no Windows quando o desempenho do filesystem é
importante.

## Pré-requisitos

- Windows 10/11 com WSL 2 habilitado.
- Uma distribuição Linux instalada no WSL.
- Docker Desktop com **Use the WSL 2 based engine** habilitado.
- A distribuição usada pelo projeto habilitada em **Settings → Resources → WSL Integration**.
- VS Code com **Dev Containers** se for usar o editor visual.

Não mantenha o checkout em `/mnt/c` ou `/mnt/d` neste fluxo. Use o filesystem
Linux da distribuição, por exemplo `~/projetos/p2-tool`.

## Preparar o checkout

Dentro do terminal da distribuição WSL:

```bash
mkdir -p ~/projetos
cd ~/projetos
git clone https://github.com/dfs-Patrick/persona2_translation_tool.git
cd p2-tool
docker version
docker compose version
```

O servidor do Docker deve responder em `docker version`. Se não responder,
abra o Docker Desktop e confirme a integração WSL da distribuição.

## Usar o Dev Container

Abra a pasta pelo VS Code do Windows usando `\\wsl.localhost\<distribuicao>\root\...`
ou pelo comando `code .` dentro do WSL. Execute **Dev Containers: Reopen in
Container**. A imagem de desenvolvimento compila a ferramenta e pré-instala o
Editor TBF no servidor remoto do VS Code.

Depois da conexão, a pasta `/lab` dentro do container aponta para o diretório
`lab` do checkout. Coloque a ISO em `lab/iso/p2is.iso` e use as ações da aba
Persona 2.

## Usar a CLI pelo WSL

Para usar o serviço `tool` do Compose:

```bash
mkdir -p lab/iso
P2_UID="$(id -u)" P2_GID="$(id -g)" docker compose build tool
P2_UID="$(id -u)" P2_GID="$(id -g)" docker compose run --rm tool --help
P2_UID="$(id -u)" P2_GID="$(id -g)" docker compose run --rm tool rebuildTbf \
  lab/iso/p2is.iso lab/translation/en \
  --game is --variant us --locale en
```

O Compose monta somente `lab` no container normal. Os arquivos criados devem
pertencer ao usuário WSL porque `P2_UID` e `P2_GID` são passados ao serviço.

## Diagnóstico

```bash
wsl.exe --status
docker context show
docker info --format '{{.OSType}}'
findmnt -T .
```

`docker info` deve indicar `linux`; `findmnt -T .` deve mostrar o filesystem da
distribuição, e não `/mnt/c` ou `/mnt/d`.

Para o funcionamento detalhado da ferramenta, consulte [CLI nativa](cli.md).
