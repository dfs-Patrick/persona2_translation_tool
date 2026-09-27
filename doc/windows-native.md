> Para reduzir o número de arquivos ao copiar, use o novo [executável único](windows-exe.md).
> O workflow agora gera essa versão; o guia abaixo descreve o pacote antigo com Node separado.

# Pacote nativo — sem Docker

O pacote contém a CLI compilada, o runtime Node, dependências, configurações do
jogo e fontes. Não é necessário instalar Node, npm, WSL ou Docker. Mantenha a
pasta inteira: o executável em `runtime` depende dos demais arquivos do pacote.

## Usar no Windows

1. Baixe o artefato Windows do workflow **Native packages** no GitHub Actions e
   extraia o ZIP inteiro para uma pasta gravável. Não execute dentro do ZIP.
2. Copie sua ISO original de Persona 2 Innocent Sin **US** para `lab/iso/p2is.iso`.
3. Abra `Extrair.cmd`. A janela informa os erros e aguarda uma tecla ao terminar.
4. Edite a tradução em `lab/translation/en/new/messages`.
5. Abra `Compilar.cmd`. O resultado será `lab/p2is-translated.iso`.

Os atalhos mantêm o perfil US/en do fluxo anterior. Para EU, use os parâmetros
explícitos da CLI (`--variant eu`); isso não representa validação do rebuild EU.
Não execute duas operações na mesma pasta ao mesmo tempo, inclusive pelo editor.

## Editor no VS Code

Instale o VS Code localmente. Na aba Extensões, menu `...` → **Instalar do VSIX**,
instale `extensions/p2-tbf-editor.vsix` e `extensions/p2-ppsspp-host.vsix`.
Abra a pasta do pacote no VS Code, confie no workspace e use o painel Persona 2.
Não use “Reopen in Container”. Configure o PPSSPP pelo comando
**Persona 2: Configurar PPSSPP no Windows** antes de compilar e executar.
As extensões precisam ser instaladas novamente quando atualizar o pacote.

## Terminal

```powershell
.\p2-tool.cmd --help
.\p2-tool.cmd extractAll lab/iso/p2is.iso -o lab/dump --translation-output lab/translation/en --game is --variant us --locale en
.\p2-tool.cmd rebuildTbf lab/iso/p2is.iso lab/translation/en --game is --variant us --locale en
```

Os caminhos relativos da CLI são relativos ao terminal. Os atalhos Extrair e
Compilar usam a pasta do pacote. O launcher PowerShell também aceita `-Action
rebuild-run` e `-PpssppPath`, preservando a integração com o emulador.

## Gerar o pacote a partir do código-fonte

Na plataforma de destino, com Node 22 e npm:

```text
npm ci
npm test
npm --prefix vscode-extension ci
npm --prefix vscode-extension test
npm --prefix vscode-extension run package
cd vscode-ppsspp-host
node ../vscode-extension/node_modules/@vscode/vsce/vsce package --no-dependencies --skip-license -o p2-ppsspp-host.vsix
cd ..
npm run package:native
```

A saída fica em `release/p2-tool-<plataforma>-<arquitetura>`. O script inclui o
Node que executou o build. Para Windows, compile no Windows: copiar o Node do
Linux não produz um executável Windows. O workflow gera e testa pacotes nos dois
sistemas. Nenhuma ISO ou tradução local é incluída. Para gerar novamente, mova
ou remova apenas a pasta do pacote anterior (preserve seus trabalhos locais).

No Linux, execute `sh ./p2-tool --help` após extrair o artefato; se necessário,
restaure a permissão do runtime com `chmod +x runtime/node`.

### Montar o ZIP Windows a partir do Linux

Para as dependências JavaScript atuais, também é possível montar o pacote com
`P2_WINDOWS_RUNTIME=/caminho/node-v22.x.x-win-x64 npm run package:native`.
Essa pasta deve conter `node.exe` e `LICENSE` da distribuição oficial Windows x64.
A verificação da CLI usa o Node do Linux; isso não substitui os testes em Windows.
O workflow no Windows é o caminho preferido para validar os artefatos distribuídos.

## Erros e extrações interrompidas

A CLI mostra o código do erro, o arquivo afetado e uma orientação, sem listar a
ajuda inteira. Para incluir o stack trace no PowerShell:

```powershell
$env:P2_DEBUG = "1"
.\Extrair.cmd
```

Após uma extração interrompida, extraia o pacote atualizado em uma pasta nova e
copie apenas a ISO original para `lab/iso/p2is.iso`. Preserve a pasta antiga se
contiver traduções. Não reutilize dumps parciais: alguns arquivos podem ter sido
criados, mas não gravados por completo.
