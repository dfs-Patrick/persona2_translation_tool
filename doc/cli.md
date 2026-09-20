# CLI nativa

Este documento descreve os comandos da ferramenta para automação e suporte.
Execute-os na raiz do projeto depois de compilar com `npx tsc`, ou substitua
`node dist/cli/mod.js` por `/opt/p2-tool/dist/cli/mod.js` quando estiver dentro
da imagem de desenvolvimento.

## Convenções de caminhos

- `lab/iso/p2is.iso`: ISO original.
- `lab/dump`: arquivos temporários da extração.
- `lab/translation/en`: raiz da tradução.
- `lab/translation/en/new/messages`: TBFs editáveis.
- `lab/translation/en/after/msg`: mensagens já traduzidas em formato `.msg`.
- `lab/translation/en/after/scripts`: scripts traduzidos em formato `.script`.
- `lab/p2is-translated.iso`: saída padrão do rebuild.

A pasta `lab` é local e está no `.gitignore`. Não use `git add -f` para incluir
ISO, dump, TBF ou conteúdo extraído.

## `extractAll`

Extrai a ISO, desmonta os arquivos internos e prepara uma árvore de tradução:

```bash
node dist/cli/mod.js extractAll <iso> \
  -o <dump> \
  --translation-output <translation> \
  --game is --variant us --locale en
```

Exemplo:

```bash
node dist/cli/mod.js extractAll lab/iso/p2is.iso \
  -o lab/dump \
  --translation-output lab/translation/en \
  --game is --variant us --locale en
```

`--game` aceita `is` ou `ep`; `--variant` aceita `us` ou `eu`; `--locale`
define a codificação de origem. A extração exporta cópias em `before/`, TBFs
em `new/messages`, scripts de referência em `new/scripts` e fontes em
`fonts/original` e `fonts/new`.

## `exportTbf`

Converte um dump de mensagens já existente em TBFs:

```bash
node dist/cli/mod.js exportTbf <source> <output>
```

O comando cria `messages/` e `scripts/` no destino. Ele não cria a árvore
completa de `extractAll` e não copia fontes.

## `importTbf`

Importa os campos `after` de TBFs para arquivos de texto/scripts:

```bash
node dist/cli/mod.js importTbf \
  lab/translation/en/new/messages \
  lab/import-preview
```

Quando `text.after` está vazio ou contém apenas espaços, o importador usa
`text.before`. Em mensagens, `info.after_msg` é preservado. Scripts sem
traduções válidas não substituem o código original.

## Arquivos `after`

Durante `rebuildTbf`, a ferramenta procura arquivos como:

```text
lab/translation/en/after/msg/e0000.msg
lab/translation/en/after/scripts/e0000.script
```

O identificador `e0000` é associado ao diretório
`event.bin$/e0000.bin$` do cabeçalho do TBF. Para mensagens, cada `msg_N:` é
associado à mesma chave do TBF e o texto é colocado em `text.after`.

O formato `after/msg` pode conter opcodes numéricos produzidos pelo
descompilador, como `[31(0, 0, 34)]`; o rebuild normaliza os opcodes conhecidos
para comandos TBF como `[color(name_green)]`. Separadores `[end_diag][wait]`
são convertidos para `[wait][clear]`. Opções ou opcodes sem tradução textual
segura são descartados apenas da camada importada, sem alterar o arquivo
`after` original.

Scripts em `after/scripts` só preenchem TBFs quando contêm blocos de diálogo
compatíveis. Um script que contém apenas código de controle é preservado sem
alteração.

## `rebuildTbf`

Reconstrói a ISO a partir da tradução:

```bash
node dist/cli/mod.js rebuildTbf <iso> <translation> \
  --game is --variant us --locale en
```

Opções relevantes:

| Opção | Padrão | Função |
| --- | --- | --- |
| `--output`, `-o` | `lab/dump/rebuild` | Diretório temporário do rebuild. |
| `--iso-output` | `lab/p2is-translated.iso` | Caminho da ISO final. |
| `--game` | obrigatório | Jogo: `is` ou `ep`. |
| `--variant` | `us` | Variante: `us` ou `eu`. |
| `--locale` | `en` | Codificação da ISO original. |
| `--font` | perfil padrão | Perfil de fonte; `original` desativa o perfil. |
| `--game-id` | `ULUS-10584` | ID usado na criação da ISO. |

O rebuild valida caracteres antes de modificar a ISO. Se um caractere não
existir no perfil selecionado, a operação falha informando o TBF e a chave da
mensagem. Em caso de erro, `lab/dump/rebuild` é preservado para diagnóstico;
após sucesso, o diretório temporário é removido.

## Fontes

Para copiar o perfil distribuído e personalizá-lo:

```bash
mkdir -p lab/translation/en/fonts/new/minha-fonte
cp -R fonts/pt-br/. lab/translation/en/fonts/new/minha-fonte/
```

Use-o com:

```bash
node dist/cli/mod.js rebuildTbf lab/iso/p2is.iso lab/translation/en \
  --font minha-fonte --game is --variant us --locale en
```

Um perfil precisa de `event.json`, `font.json` e as páginas PNG correspondentes.
Consulte a documentação de fontes do projeto e os testes em
`tests/font_profile.test.ts` antes de alterar tabelas.

## Ajuda e diagnóstico

```bash
node dist/cli/mod.js --help
node dist/cli/mod.js extractAll --help
node dist/cli/mod.js rebuildTbf --help
npx tsc --noEmit
node --test dist/tests/font_profile.test.js
```

Para observar o fluxo da extensão, abra **Saída → Persona 2** no VS Code. Para
Windows com Docker, consulte [windows-docker.md](windows-docker.md); para WSL,
consulte [linux-wsl.md](linux-wsl.md).
