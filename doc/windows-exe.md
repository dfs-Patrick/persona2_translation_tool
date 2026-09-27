# Executável Windows

Esta distribuição contém código, dependências, configurações do jogo e fontes
padrão dentro de `Persona2Tool.exe`. Não requer instalação de Node ou Docker.

1. Extraia o ZIP em uma pasta nova.
2. Coloque a ISO original **US** em `lab/iso/p2is.iso`.
3. Execute `Extrair.cmd`.
4. Edite os TBFs em `lab/translation/en/new/messages`.
5. Execute `Compilar.cmd` para gerar `lab/p2is-translated.iso`.

Para o editor, instale os VSIX de `extensions` no VS Code local e abra a pasta
do pacote. A versão incluída detecta o executável automaticamente.

Também é possível usar `./Persona2Tool.exe --help` no PowerShell. Os caminhos
relativos da CLI usam a pasta atual do terminal. Os atalhos usam a pasta do pacote.
As traduções e fontes personalizadas continuam externas e editáveis em `lab`.
Não reutilize dumps de uma extração interrompida. Preserve suas traduções antigas.

## Desenvolvimento

Após `npm ci` e o empacotamento dos VSIX, execute `npm run package:exe`.
O destino padrão é Windows x64. `P2_EXE_TARGET=node22-linux-x64` gera a variante
Linux usada para testes locais. O build baixa o runtime do empacotador na primeira
execução. O pacote preserva o JavaScript como fonte dentro do executável; não é
uma conversão para código de máquina nem uma proteção contra leitura do código.

## Downloads separados por sistema

Cada push na branch `main`, tag `v*` ou execução manual de **Native packages**
gera os artefatos no GitHub Actions:

- `Persona2Tool-Windows-x64.zip`
- `Persona2Tool-Linux-x64.zip`

Os binários ficam nos artefatos do workflow, não no histórico Git. Após o build,
`python scripts/archive-native.py` gera o ZIP Windows; para Linux, defina
`P2_EXE_TARGET=node22-linux-x64` antes de executar o mesmo script.
