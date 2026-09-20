# Persona 2 modding tools — fluxo de tradução

Ferramentas em TypeScript/Node.js para extrair, editar e reconstruir arquivos das versões de PSP de Persona 2.

Este trabalho é uma extensão do projeto **[p2_tool, de eiowlta](https://github.com/eiowlta/p2_tool)**. A autoria das ferramentas originais e seu histórico de commits são preservados. As alterações locais acrescentam um fluxo de tradução em TBF e aplicação de perfis de fontes, incluindo conversão de caracteres e métricas de desenho no executável.

O fluxo descrito abaixo é voltado a **Persona 2: Innocent Sin, versão americana (IS US / ULUS-10584)**. O projeto original também contém suporte a Eternal Punishment e outras variantes, mas a aplicação de perfis de fontes deste fluxo está limitada a IS US.

## Conteúdo do jogo e diretório local

**O usuário deve fornecer sua própria ISO original, obtida de sua cópia do jogo. O repositório não fornece o jogo.**

Todo o conteúdo de `lab/` é privado ao ambiente de trabalho e deve permanecer fora do Git: ISO original, ISO modificada, arquivos extraídos, textos, scripts, imagens de fontes e perfis locais. Essa pasta é ignorada integralmente pelo `.gitignore` e não faz parte da distribuição do projeto.

A ferramenta inclui o perfil **`pt-br` em `fonts/pt-br/`**, usado por padrão no rebuild de IS US. Esse perfil contém os mapas, receitas e quatro páginas de fonte que testamos. Ele é a exceção explícita de assets distribuídos com a ferramenta; o restante de `lab/` continua excluído. Os TBFs contêm textos originais e não devem ser enviados ao repositório.

Mantenha os arquivos do jogo dentro de `lab/`. Não inclua esse conteúdo em commits, releases, anexos de issues ou arquivos compactados publicados junto com o código. Os exemplos de texto neste README são fictícios.

## Requisitos e instalação

No Windows, o fluxo com **Docker Desktop + PPSSPP no Windows** está descrito em [Instalação com Docker e integração com VS Code](doc/windows-docker.md). Ele dispensa Node.js no host e inclui um comando para reconstruir e abrir a ISO. A extensão e o gatilho automático ao salvar ainda serão implementados.

Instale o **Docker Desktop** para executar a ferramenta e o **PPSSPP** separadamente para testar o jogo. O guia inclui a instalação, o clone do projeto e todos os comandos em PowerShell. A pasta `lab` fica dentro da pasta clonada: abra-a no Explorer com `explorer.exe .\lab` após o setup. Coloque sua ISO original em `lab\iso\p2is.iso`; a ISO reconstruída será `lab\p2is-translated.iso`. Dentro do container, essa mesma pasta aparece como `/lab`.

### Instalação nativa (Linux/WSL)

- Node.js com npm, compatível com o alvo ES2021 do projeto.
- Git.
- Sua ISO original de Innocent Sin US, sem modificações.
- PPSSPP, para testar a ISO produzida.

Clone este fork. Se estiver usando outro fork, ajuste a URL:

```bash
git clone https://github.com/dfs-Patrick/persona2_translation_tool.git
cd persona2_translation_tool
npm ci
npx tsc
```

Execute os comandos deste documento na raiz do projeto. Os exemplos de manipulação de pastas usam Bash, disponível no Linux, WSL e Git Bash.

Use a CLI compilada deste checkout: o pacote `p2_tools` publicado pelo projeto original pode não conter estas alterações.

```bash
node dist/cli/mod.js --help
node dist/cli/mod.js rebuildTbf --help
```

## 1. Fornecer a ISO

Crie a pasta local:

```bash
mkdir -p lab/iso
```

Coloque sua ISO original em:

```text
lab/iso/p2is.iso
```

Esse arquivo é a base de extração e reconstrução. A ISO traduzida terá outro caminho.

## 2. Extrair o jogo e preparar os arquivos editáveis

```bash
node dist/cli/mod.js extractAll lab/iso/p2is.iso \
  -o lab/dump \
  --translation-output lab/translation/en \
  --game is --variant us --locale en
```

O `en` identifica a codificação de origem do jogo. A tradução pode ser em português mesmo com esse nome de pasta; o perfil de fonte é selecionado separadamente.

A extração cria esta estrutura local:

```text
lab/
├── iso/
│   └── p2is.iso                    # ISO original fornecida pelo usuário
├── dump/
│   ├── dumped_cpk/                 # ISO e arquivos internos extraídos
│   └── dumped_msg/                 # Textos e scripts extraídos
└── translation/en/
    ├── before/
    │   ├── messages/               # Cópias dos textos/scripts originais
    │   └── scripts/                # Cópias dos scripts numerados
    ├── new/
    │   ├── messages/               # Arquivos .tbf editáveis
    │   └── scripts/                # Scripts exportados para consulta
    ├── after/                     # Textos/scripts regenerados no rebuild
    └── fonts/
        ├── original/              # PNGs e mapas extraídos ou copiados da base
        └── new/                   # Perfis de fontes preparados localmente
```

**A extração deve ser feita antes de começar a tradução.** Executar `extractAll` novamente sobre a mesma pasta pode sobrescrever TBFs editados. Faça uma cópia local da tradução ou escolha outra pasta de saída se precisar extrair de novo.

## 3. Traduzir os TBFs

Edite os arquivos `*.msg.tbf` em `lab/translation/en/new/messages/`.

Cada TBF é um documento JSON com:

- `header`: caminho e nome do arquivo de origem, usados na reconstrução.
- `translation`: lista de mensagens.
- `info`: chave da mensagem e trechos necessários para reconstruir sua estrutura.
- `text.before`: texto original.
- `text.after`: tradução editável.

Exemplo fictício de um campo `text`:

```json
{
  "before": "Example text.\nSecond line.",
  "after": "Texto de exemplo.\nSegunda linha."
}
```

Preencha `text.after`. Se estiver vazio ou contiver apenas espaços, o importador mantém `text.before`.

Preserve as chaves, os metadados e os comandos presentes no texto, como `[color(...)]`, `[delay(...)]` e `[wait]`. Use `\n` para uma quebra de linha dentro de uma string JSON. Aspas internas precisam ser escapadas como `\"`.

Os arquivos `*.ef.tbf` e `*.cf.tbf` representam blocos de comentário dos scripts exportados. **Os diálogos efetivamente compilados vêm dos `.msg.tbf`.** Traduzir apenas o comentário de um script não substitui sua mensagem. A pasta `new/scripts` não é uma entrada adicional automática do comando `rebuildTbf`.

## 4. Usar a fonte padrão ou escolher outra

**O perfil PT-BR testado já acompanha a ferramenta.** Para IS US, não é necessário criar um perfil nem passar `--font`: o rebuild aplica `fonts/pt-br/` automaticamente, incluindo acentos, conversão de caracteres e métricas. A extração continua lendo a ISO com a codificação original.

Para personalizar a fonte testada, copie o perfil para um nome próprio:

```bash
mkdir -p lab/translation/en/fonts/new/minha-fonte
cp -R fonts/pt-br/. lab/translation/en/fonts/new/minha-fonte/
```

Selecione-o com `--font minha-fonte`. Nomes explícitos são procurados primeiro em `translation/en/fonts/new/<nome>` e depois em `fonts/<nome>` da ferramenta. Sem o argumento, a cópia distribuída de `pt-br` tem prioridade até sobre uma cópia local com o mesmo nome. `--font original` desativa o perfil; outros jogos/variantes mantêm a fonte original por padrão.

Para iniciar um perfil do zero, a partir da fonte original extraída:

```bash
mkdir -p lab/translation/en/fonts/new/minha-fonte
cp -R lab/translation/en/fonts/original/. lab/translation/en/fonts/new/minha-fonte/
```

Essa cópia é apenas o ponto de partida: **ainda é necessário desenhar os caracteres e ajustar os mapas**.

O perfil deve conter:

| Arquivo | Função |
| --- | --- |
| `event.json` | Associa cada código de diálogo, escrito em hexadecimal, ao caractere correspondente. |
| `font.json` | Associa cada posição nas páginas da fonte ao caractere desenhado nela. |
| `7.png`, `8.png`, etc. | Páginas de imagem substitutas, nomeadas pelo número do GIM de destino. |
| `glyphs.json` | Opcional: receitas para compor caracteres usando pixels de outros glifos do próprio perfil. |

Em `font.json`, cada página tem uma grade de 16 × 16 caracteres. A coordenada de fonte é calculada como `página × 256 + linha × 16 + coluna` (índices começando em zero). Em IS US, a página 0 normal fica em `7.png` e sua versão estreita em `12.png`; a página seguinte fica em `8.png` e `13.png`.

Cadastre o mesmo caractere em ambos os mapas e desenhe-o nas posições corretas. O código de evento não precisa ser igual à coordenada da fonte. Reserve posições compatíveis com o renderizador inglês; para os novos glifos, use coordenadas de fonte abaixo de `0x500`. Códigos de comando de evento a partir de `0x1000` não podem ser usados como novas letras. Evite substituir posições de caracteres ainda usados pelos textos originais.

Os PNGs de fonte devem manter a grade de 256 × 256 pixels e a transparência. Para os caracteres que também usam a fonte estreita, forneça ambas as versões. Preserve os mapas de `game/is/encoding/en` e `fonts/original`: eles representam a origem, enquanto as alterações pertencem ao perfil.

No rebuild padrão de IS US, ou ao selecionar um perfil com `--font`, a ferramenta:

1. Carrega e valida o perfil e os caracteres dos TBFs.
2. Prepara as imagens, incluindo as composições opcionais.
3. Converte os PNGs em GIM, usando as características e a paleta dos arquivos originais.
4. Atualiza a tabela de conversão entre códigos de evento e posições da fonte no executável.
5. Calcula o recorte horizontal e a largura dos novos glifos e atualiza as métricas normal e estreita.

Se faltar um caractere, o rebuild informa o TBF e a mensagem afetada, em vez de substituí-lo por `@`. Um perfil inexistente ou incompatível também causa erro.

### Composição opcional de glifos

`glyphs.json` contém uma lista de receitas com coordenadas numéricas:

- `target`: posição de destino.
- `base`: posição da letra base.
- `accent`: posição que fornece o acento.
- `accentRows`: quantidade de linhas superiores copiadas do acento, entre 1 e 5.

O restante do glifo vem da letra base. As receitas são aplicadas às versões normal e estreita e atualmente aceitam posições de `0x000` a `0x1ff`. JSON usa números decimais, sem a notação `0x`. Os PNGs compostos ficam no diretório temporário do rebuild; os PNGs editáveis do perfil não são sobrescritos.

## 5. Reconstruir a ISO

O perfil PT-BR é aplicado automaticamente:

```bash
npx tsc
node dist/cli/mod.js rebuildTbf lab/iso/p2is.iso lab/translation/en \
  --game is --variant us --locale en
```

**A saída padrão é sempre `lab/p2is-translated.iso`.** Uma nova execução substitui essa saída. A ISO original permanece em `lab/iso/p2is.iso`.

Para escolher outro perfil, acrescente `--font minha-fonte`. Para reconstruir usando a fonte e a codificação originais, use explicitamente `--font original`:

```bash
node dist/cli/mod.js rebuildTbf lab/iso/p2is.iso lab/translation/en \
  --font original --game is --variant us --locale en
```

O rebuild atualiza `translation/en/after` e usa `lab/dump/rebuild` como diretório temporário. Esse diretório é removido ao concluir com sucesso e preservado quando ocorre uma falha. Caso use `-o` para escolher outro diretório de trabalho, reserve uma pasta exclusiva para ele: todo o seu conteúdo será removido no sucesso. `--iso-output` permite alterar explicitamente o destino da ISO.

## 6. Testar no jogo

Abra `lab/p2is-translated.iso` no PPSSPP e verifique acentos, maiúsculas, cores, quebras de linha e largura dos diálogos.

Depois de alterar fontes ou o executável, reinicie o jogo. Um save state antigo pode restaurar o executável e as fontes anteriores em memória; para essa validação, use uma inicialização nova ou um save normal do jogo.

Repita o ciclo: editar TBFs/perfil → executar rebuild → testar.

## Comandos auxiliares

Importar TBFs sem reconstruir a ISO:

```bash
node dist/cli/mod.js importTbf lab/translation/en/new/messages lab/import-preview
```

Exportar novamente a partir de um dump de textos para uma pasta separada:

```bash
node dist/cli/mod.js exportTbf lab/dump/dumped_msg lab/export-preview
```

Essa exportação auxiliar cria `messages/` e `scripts/` diretamente no destino. Ela não gera automaticamente toda a estrutura de trabalho de `extractAll`.

Verificar o código e executar os testes de fontes, sem precisar de uma ISO:

```bash
npx tsc --noEmit
node -r ts-node/register tests/font_profile.test.ts
```

As outras CLIs em `cli/` tratam de arquivos internos, imagens, mensagens, scripts e executáveis. Consulte `--help` na CLI compilada correspondente. O fluxo de mods original com `mod.json` e `files.json` continua disponível:

```bash
node dist/cli/mod.js lab/iso/p2is.iso lab/meu-mod \
  --output lab/mod-build --iso_output lab/p2is-translated.iso --variant us
```

O diretório `lab/meu-mod` deve conter `mod.json`. O projeto original oferece o [p2ep_template](https://github.com/eiowlta/p2ep_template) como exemplo de estrutura para EP.

Os scripts `rebuild-iso.sh` e `watch-rebuild-iso.*` são auxiliares de um fluxo anterior, baseado em `lab/p2is`. Para aplicar TBFs e perfis de fontes, use o comando `rebuildTbf` deste documento.

## Publicar as alterações como fork

Crie o fork a partir da página do [projeto original](https://github.com/eiowlta/p2_tool), usando **Fork → Create fork** na sua conta. Esse procedimento estabelece o vínculo de fork no GitHub. Consulte as [instruções oficiais de criação de forks](https://docs.github.com/en/pull-requests/how-tos/work-with-forks/fork-a-repo).

Para aproveitar um checkout existente cujo `origin` ainda aponta para `eiowlta/p2_tool`, depois de criar o fork:

```bash
git remote -v
git remote rename origin upstream
git remote add origin https://github.com/dfs-Patrick/persona2_translation_tool.git
git remote -v
```

Nesse cenário, `upstream` aponta para o projeto original e `origin` para seu fork. Se você já clonou seu fork, mantenha seu `origin` e apenas adicione o original:

```bash
git remote add upstream https://github.com/eiowlta/p2_tool.git
```

Não reinicialize o repositório nem apague `.git`: o histórico existente preserva a autoria original. A organização dos remotes segue a [documentação do GitHub](https://docs.github.com/en/pull-requests/how-tos/work-with-forks/configuring-a-remote-repository-for-a-fork).

Antes do primeiro commit das alterações, confira:

```bash
git status --short
git check-ignore lab/iso/p2is.iso lab/p2is-translated.iso
git ls-files lab
git log --all --oneline -- lab
```

Os dois últimos comandos devem ficar sem saída para um histórico sem conteúdo de `lab/`. O `.gitignore` impede a inclusão normal de arquivos novos, mas não remove arquivos já rastreados nem limpa commits anteriores. Não use `git add -f` para contornar essa exclusão.

Crie uma branch, selecione apenas código, documentação e configurações, revise o conteúdo preparado e publique no seu fork:

```bash
git switch -c translation-workflow
git add .gitignore readme.md cli lib game fonts tests package.json package-lock.json
git diff --cached --stat
git diff --cached
```

Após conferir que o commit contém somente as alterações pretendidas:

```bash
git commit -m "Add TBF translation workflow and font profiles"
git push -u origin translation-workflow
```

Mantenha os créditos ao projeto original e os avisos de autoria existentes. Este README não atribui uma nova licença ao código original nem inclui qualquer autorização de distribuição de conteúdo do jogo.
