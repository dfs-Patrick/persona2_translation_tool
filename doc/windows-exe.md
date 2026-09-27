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

Baixe os ZIPs em [Releases](https://github.com/dfs-Patrick/persona2_translation_tool/releases):

- `Persona2Tool-Windows-x64.zip`
- `Persona2Tool-Linux-x64.zip`

Os builds são feitos localmente e anexados à Release, sem workflow automático.
Consulte `doc/releases.md` no repositório para publicar uma nova versão.

## Erros

A CLI mostra a causa e o caminho afetado. No PowerShell, use `$env:P2_DEBUG = "1"`
antes de executar o comando para incluir os detalhes técnicos. Se a extração
falhar, recomece em uma pasta nova e preserve qualquer tradução já existente.
