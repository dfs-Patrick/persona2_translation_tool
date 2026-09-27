# Publicação manual em Releases

Não há workflow de Actions para compilar ou publicar. Os binários são anexados
às Releases, sem entrar no histórico Git.

## Preparar os executáveis

Com Node 22, npm e Python 3 instalados:

```sh
npm ci
npm test
npm --prefix vscode-extension ci
npm --prefix vscode-extension test
npm --prefix vscode-extension run package
cd vscode-ppsspp-host
node ../vscode-extension/node_modules/@vscode/vsce/vsce package --no-dependencies --skip-license -o p2-ppsspp-host.vsix
cd ..
npm run package:exe
python scripts/archive-native.py
```

O destino padrão é Windows x64. Em Linux, para a outra variante:

```sh
P2_EXE_TARGET=node22-linux-x64 npm run package:exe
P2_EXE_TARGET=node22-linux-x64 node scripts/test-exe.js
P2_EXE_TARGET=node22-linux-x64 python3 scripts/archive-native.py
```

No Windows, execute `node scripts/test-exe.js` para validar o executável local.
Os builds recusam sobrescrever pastas de pacotes existentes: mova o pacote
anterior antes de recompilar. Nunca apague traduções para refazer um build.

## Publicar

Após commit e push, crie uma Release no GitHub apontando para esse commit e
anexe somente:

- `release/Persona2Tool-Windows-x64.zip`
- `release/Persona2Tool-Linux-x64.zip`

Informe os testes realizados e as limitações. Marque como pré-release enquanto
não houver validação completa no Windows e com uma ISO do jogo. O teste sintético
não substitui verificar extração, rebuild e execução no PPSSPP.
