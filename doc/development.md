# Desenvolvimento e manutenção

Este guia é para quem mantém o projeto, atualiza a imagem, altera a CLI ou
empacota as extensões. O usuário final deve começar pelo [README](../readme.md).

## Layout relevante

- `cli/`: comandos yargs e entrada da ferramenta.
- `lib/`: formatos, ISO, CPK, mensagens, fontes e infraestrutura.
- `game/`: tabelas e metadados específicos de cada jogo.
- `fonts/`: perfis distribuídos com a ferramenta.
- `vscode-extension/`: Editor TBF executado no host remoto do Dev Container.
- `vscode-ppsspp-host/`: extensão de interface executada no Windows.
- `.devcontainer/`: definição do ambiente de desenvolvimento.
- `Dockerfile`: estágios `runtime`, `extension-build` e `development`.
- `lab/`: estado local do usuário; não deve entrar no Git.

## Dependências e compilação

Na raiz do projeto:

```bash
npm ci
npx tsc
node --test dist/tests/font_profile.test.js
```

O `tsconfig.json` compila TypeScript para `dist/`. A imagem runtime recompila a
CLI durante o build e copia apenas `dist`, `game` e `fonts` para o estágio final.

## Testes da extensão

```bash
cd vscode-extension
npm ci
npm test
npm run package
cd ../vscode-ppsspp-host
../vscode-extension/node_modules/.bin/vsce package \
  --no-dependencies --skip-license -o p2-ppsspp-host.vsix
```

O primeiro comando gera `p2-tbf-editor.vsix`; o segundo gera o VSIX do host. Os
artefatos são versionados porque a imagem do Dev Container os usa como backup
em `lab/tools`. `node_modules` permanece ignorado.

## Build do Dev Container

```bash
docker build --target development -t p2-tool-dev:test .
```

O estágio `extension-build` executa os testes e empacota ambos os VSIX. O estágio
`development` instala o Editor TBF em `/root/.vscode-server/extensions/` para
que ele esteja disponível assim que o servidor remoto do VS Code conectar.

Para reconstruir pelo VS Code, use **Dev Containers: Rebuild Container**. Uma
alteração em `Dockerfile`, `.devcontainer/devcontainer.json`, extensões ou
código copiado para a imagem exige rebuild; uma alteração apenas em arquivos
montados em `/workspaces/p2-tool` não exige rebuild da imagem.

## Regras para `lab`

Não adicione arquivos de `lab` ao commit. A extração, a tradução, os dumps, as
ISOs e os arquivos `after` são dados do usuário. Para confirmar:

```bash
git status --short
git check-ignore lab/iso/p2is.iso lab/p2is-translated.iso
git ls-files lab
```

Os caminhos locais devem estar ignorados e `git ls-files lab` deve não produzir
saída.

## Checklist antes de publicar

```bash
npx tsc --noEmit
node --test dist/tests/font_profile.test.js
cd vscode-extension && npm test && npm run package
cd ..
git diff --check
git status --short
```

Revise o VSIX, o `Dockerfile`, o manifesto do Dev Container e a documentação
quando alterar o fluxo de instalação. Não inclua ISOs, dumps ou traduções de
usuários em commits, releases ou imagens públicas.
