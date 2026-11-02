# Projetos

[← Sumário](../README.md)

Um projeto é uma pasta com a tradução de um jogo. A ISO original fica onde está e é só
lida; o projeto guarda o caminho e a "impressão digital" dela (tamanho e SHA-256).

## Criar

Na tela inicial, em **Novo projeto**:

| Campo | O que é |
|---|---|
| Motor (jogo) | O jogo a traduzir. A descrição embaixo diz qual ISO ele espera. |
| Nome | Nome do projeto (aparece no título e no nome sugerido para a ISO traduzida). |
| ISO original | A ISO do jogo. Precisa ser a versão que o motor suporta. |
| Pasta do projeto | Onde os arquivos de tradução ficarão. Pode ser uma pasta nova. |

Ao clicar em **Criar projeto**, a ferramenta:

1. confere se a ISO é do jogo e da versão certa (pelo código do disco, ex.: `ULUS-10584`);
2. calcula o SHA-256 da ISO;
3. lê o texto do jogo direto da ISO e cria um arquivo `.tbf` por script.

Jogos de PlayStation 1 usam o `.cue` (ou o `.bin` direto) no campo **ISO original**; a ferramenta lê os 2048 bytes úteis de cada setor bruto (MODE2/2352) e identifica o jogo pelo `SYSTEM.CNF` (ex.: `SLPS-03170`). O motor de Shin Megami Tensei (JP) já extrai os textos dos scripts de mapa (`DATA.BIN`, arquivos `smt/map_NNNN.tbf`; 1.847 mensagens). Códigos de controle aparecem como `[cXXX]` e glifos sem caractere conhecido como `{gXXX}`; a conversão é reversível. A compilação regrava os scripts traduzidos no próprio `DATA.BIN`: a tabela de mensagens é reajustada e os setores tocados têm EDC/ECC recalculados; sai um `.bin` com `.cue`. Se o texto traduzido de um script passar do espaço dele, o script cresce em setores: os arquivos seguintes avançam, a tabela do `DATA.BIN` no executável é reescrita e o espaço vem dos setores zerados no fim dos arquivos vizinhos, então o disco mantém o tamanho (a validação avisa se esse espaço acabar). O jogo empilha várias mensagens na mesma caixa (ex.: as três falas antes da entrada do nome ocupam uma linha cada); se a tradução usa mais linhas que o original, a caixa estoura, o jogo pede ▼ e o que vem depois pode travar (a entrada do nome não abre). A validação avisa toda página com mais linhas que o original. Cada `{g0}` (quebra de página/espera) do original precisa existir na tradução, na mesma quantidade, porque a tabela de mensagens do jogo aponta para a posição logo depois dele. Só aceita caracteres que existem na fonte: além da japonesa, o disco recebe glifos latinos (A–Z, a–z, 0–9, acentos do português e pontuação comum, em `data/smt/latin.json`) desenhados nos tokens em branco da fonte, então o PT-BR já pode ser escrito. O executável recebe um patch de largura variável (`SmtTextPatch`): nas caixas de mensagem cada letra fica logo após a anterior, e nos menus as duas rotinas de texto (fonte de 12 px e de 10 px) somam as larguras da linha. A fonte de 10 px dos menus não tinha minúsculas nem acentos; os glifos latinos vão para a segunda camada vazia da textura dela. Cada linha da caixa de mensagem continua com no máximo 28 caracteres (são 28 células por linha). O teclado de nome vira latino: maiúsculas, minúsculas e números/símbolos nas três páginas, com os rótulos ABC/abc/123/＜/＞/Fim. Bancos de texto que vão até o fim da entrada podem crescer em setores, como os scripts (o carregador lê o tamanho da tabela). As imagens (TIM, cruas, RLE ou em contêineres do `DATA.BIN`) aparecem na subaba Imagens com o mesmo painel de metadados do PSP. Também são traduzidos os bancos de textos de menus, itens, nomes e falas de demônios (`smt/bank_NNNN_XXXX.tbf`, chave `str_NNNNN`), regravados no lugar como os scripts. Para substituir uma imagem, indique um PNG de mesmo tamanho no `image-translation-control.json`: ao compilar, ele é convertido para o TIM original (mesmo modo e paleta; imagens indexadas usam a cor mais próxima da paleta original), recompactado e gravado no lugar; se a versão compactada passar o espaço, a compilação aponta a imagem.

Na aba de imagens, o botão **Abrir no GIMP…** (ao lado do LibreSprite) exporta o PNG de trabalho e abre o GIMP; o executável é procurado em `Program Files\GIMP*` e, se não achar, a ferramenta pede o caminho (guardado nas configurações do usuário). Depois de salvar no GIMP (Arquivo ▸ Sobrescrever), use **Atualizar prévia**.

## Abrir

- Clique num projeto da lista **Abrir projeto**, ou em **Abrir projeto…** e escolha o
  arquivo do projeto.
- Projetos criados quando a ferramenta se chamava P2Tool (`p2project.json`) abrem
  normalmente e continuam usando esse arquivo.

Se a ISO não estiver mais no caminho gravado, ou tiver outro tamanho, aparece um aviso
em vermelho e os botões que leem a ISO ficam desativados. Edite o caminho em
`translation-project.json` (campo `iso.path`) para apontar para a ISO.

## O que fica na pasta

```
meu-projeto/
├── translation-project.json   configuração do projeto
├── translation/               o trabalho da equipe (vale versionar no git)
│   ├── messages/              um .tbf por script do jogo, por container
│   │   ├── event.bin/e0000.tbf
│   │   ├── DNGSCN.BNP/0_0000.tbf
│   │   └── …
│   ├── directory-active-index.json rótulos e descrições dos diretórios
│   ├── image-translation-control.json notas e caminhos das substituições de imagem
│   ├── images/                 imagens de substituição escolhidas no visualizador
│   ├── glossary.json          glossário (veja Glossário)
│   └── dictionary.txt         palavras aceitas pela ortografia (veja Correção ortográfica)
├── fonts/                     criada por "Exportar fontes…" (opcional)
├── build/                     ISOs compiladas (sugestão padrão)
└── cache/
```

`translation/` é tudo o que a equipe produz: os TBF, o glossário e o dicionário. As outras
pastas podem ser recriadas pela ferramenta.

### Índices de diretórios e imagens

Na aba **Tradução**, a subaba **Texto** e a subaba **Imagens** organizam os arquivos por
diretório. Selecione um diretório para editar seu rótulo de exibição e descrição; o rótulo
personalizado aparece no lugar do nome amigável padrão, com o caminho original em texto
menor. A descrição também aparece ao passar o mouse sobre o diretório e seus itens.
Esses dados são salvos em `directory-active-index.json`.

Na subaba **Imagens**, selecione uma imagem para comparar o original com a substituição.
**Escolher substituição…** aceita PNG e GIM e copia o arquivo para `translation/images/`;
o painel lê as informações da substituição a partir desse arquivo, separadamente das
informações da imagem original. A descrição da imagem e o caminho relativo da substituição
ficam em `image-translation-control.json`. Na compilação, PNGs e GIMs são convertidos para
o GIM original, que mantém formato, paleta e dimensões; por isso, a imagem substituta deve
ter a mesma resolução. Cores fora da paleta indexada são aproximadas para a cor disponível
mais próxima. Os arquivos PAR/BNP e o CPK que contêm a imagem são remontados na ISO gerada.

## `translation-project.json`

```json
{
  "formatVersion": 1,
  "name": "Tradução PT-BR",
  "created": "2026-11-01T23:44:23-03:00",
  "iso": {
    "path": "F:\\jogos\\p2is.iso",
    "gameId": "ULUS-10584",
    "title": "SMT: Persona 2: Innocent Sin",
    "size": 1130889216,
    "sha256": "311f97a9…"
  },
  "engine": "p2is-psp-us",
  "font": "pt-br"
}
```

| Campo | Uso |
|---|---|
| `iso.path` | Caminho da ISO: absoluto, ou relativo à pasta do projeto quando a ISO está dentro dela. |
| `engine` | O motor do jogo. Projetos sem o campo usam `p2is-psp-us`. |
| `font` | Perfil de fonte instalado na compilação (veja [Fontes](fontes.md)). |

## Botões do projeto

| Botão | O que faz |
|---|---|
| **Glossário** | Abre o glossário no lugar do editor. |
| **Gerar da ISO** | Lê o texto da ISO e cria os `.tbf` que ainda não existem. **Nunca altera** arquivos que já existem: é seguro usar a qualquer momento. |
| **Validar** | Confere toda a tradução sem gravar nada. Veja [Compilação](compilacao.md). |
| **Compilar ISO…** | Gera a ISO traduzida. Veja [Compilação](compilacao.md). |
| **Importar TBF…** | Copia `.tbf` de outra pasta (ex.: de um projeto da v2) para o projeto, sem sobrescrever os existentes. |
| **Recarregar** | Relê a lista de arquivos (útil depois de mexer na pasta por fora, ex.: um `git pull`). |
| **Fechar projeto** | Salva o que estiver pendente e volta à tela inicial. |

A aba **ISO**, à esquerda, mostra o conteúdo da ISO e do pacote de dados do jogo, e tem
o botão **Exportar fontes…** (veja [Fontes](fontes.md)).

## Trabalho em equipe

A pasta `translation/` foi pensada para ficar num repositório git: cada TBF é um JSON com
formatação estável (só muda o que foi editado), o glossário e o dicionário são arquivos
de texto. Depois de um `git pull` com a ferramenta aberta, use **Recarregar**.
