# Linux nativo

Este guia executa a CLI diretamente no Linux, sem Docker. É adequado para
manutenção, testes rápidos e desenvolvimento da ferramenta. O fluxo assume que
Node.js, npm, Git e uma ISO original já estão disponíveis.

## Pré-requisitos

- Linux x86_64 ou uma distribuição WSL.
- Node.js compatível com ES2021 e npm.
- Git.
- ISO original de Persona 2: Innocent Sin US.
- Espaço livre para a ISO, dumps, fontes e diretórios temporários.

Verifique as versões:

```bash
node --version
npm --version
git --version
```

## Instalação

```bash
git clone https://github.com/dfs-Patrick/persona2_translation_tool.git
cd p2-tool
npm ci
npx tsc
node dist/cli/mod.js --help
```

A compilação escreve JavaScript em `dist/`. O pacote publicado do projeto
original não substitui a compilação deste checkout, pois pode não conter as
alterações do fluxo TBF.

## Preparar e extrair

```bash
mkdir -p lab/iso
cp /caminho/da/sua/iso/p2is.iso lab/iso/p2is.iso
node dist/cli/mod.js extractAll lab/iso/p2is.iso \
  -o lab/dump \
  --translation-output lab/translation/en \
  --game is --variant us --locale en
```

A extração cria `before/`, `new/`, `after/` e `fonts/` em
`lab/translation/en`. Não execute a extração novamente sobre uma tradução que já
contenha TBFs editados.

## Reconstruir

```bash
node dist/cli/mod.js rebuildTbf lab/iso/p2is.iso lab/translation/en \
  --game is --variant us --locale en
```

O perfil `pt-br` é selecionado automaticamente para IS US. A saída padrão é
`lab/p2is-translated.iso`. Para desativar o perfil ou escolher um perfil local:

```bash
node dist/cli/mod.js rebuildTbf lab/iso/p2is.iso lab/translation/en \
  --font original --game is --variant us --locale en

node dist/cli/mod.js rebuildTbf lab/iso/p2is.iso lab/translation/en \
  --font minha-fonte --game is --variant us --locale en
```

## Testes de desenvolvimento

```bash
npx tsc --noEmit
node --test dist/tests/font_profile.test.js
cd vscode-extension
npm ci
npm test
npm run package
```

Os testes da extensão não exigem VS Code aberto. O empacotamento gera o VSIX do
Editor TBF no diretório da extensão.
