# Motor Persona 2: Innocent Sin (`p2is-psp-us`)

[← Sumário](../README.md)

## ISO suportada

*Shin Megami Tensei: Persona 2 — Innocent Sin*, PSP, versão americana **`ULUS-10584`**.
As versões europeia (`ULES-01557`) e japonesa (`ULJM-05884`) são reconhecidas, mas recusadas.

## O que é traduzível

| Grupo no editor | Origem no jogo | Arquivos |
|---|---|---|
| Eventos | `event.bin` (eventos `e*`, cenas de mapa `m*`) | 398 |
| Dungeons | `DNGSCN.BNP` | 160 |
| Mapas | `MMAP01.BNP` … `MMAP06.BNP` | 6 |
| Personas (demo) | `P_DEMO.BNP` (falas das personas ao despertar) | 1 |
| F_BE | `F_BE.BNP` (textos de teste em japonês) | 51 |
| Loja de CDs | `CD_SHOP.BIN` | 1 |

Ainda **não** traduzíveis:

- textos dentro do executável (menus, itens, nomes de personas e habilidades);
- imagens com texto (títulos, menus desenhados);
- scripts de contato com demônios (`TM_EVE.BNP`, `EVTUNIT2.BNP`).

## Comandos

Comandos aparecem entre colchetes no texto. Mantenha os do original; mude de lugar só
quando a frase traduzida pedir (ex.: uma pausa `[delay(30)]` no ponto certo da frase).

| Comando | Uso |
|---|---|
| `[color(cor)]` | Muda a cor do texto a seguir. Os nomes de quem fala usam `[color(name_green)]`; `[color(white)]` volta ao normal. |
| `[delay(n)]` | Pausa na escrita do texto (n = duração). |
| `[wait]` | Espera o jogador apertar o botão. |
| `[clear]` | Limpa a caixa de diálogo (começa uma nova "página"). |
| `[end]` | Fim da mensagem. Normalmente está nos códigos finais, que a ferramenta mantém sozinha. |
| `[sync]` | Sincroniza o texto com a cena. |
| `[tatsuya]`, `[suou]`, `[tatsu]` | Formas do nome do protagonista, escolhido pelo jogador. Mantenha: o jogo põe o nome escolhido. |
| `[choice(n)]` … `[end_choice]` | Menu de escolha com n opções, uma por linha. |
| `[if(BIT)]`, `[if_not(BIT)]` | A linha seguinte só aparece se a condição do jogo (BIT) for verdadeira/falsa. |
| `[var(msg_var_N)]` | Insere um valor do jogo (ex.: um número). |
| `[item_with_type(item_N)]` | Insere o nome de um item. |
| `[sym(nome)]` | Símbolo especial: `heart` (♥), `lquote`/`rqoute` (aspas do jogo) e outros `unkN`. |
| `[theater_rank]` | Insere o ranking do teatro. |
| `[space]`, `[half_tab]`, `[dbl_tab]` | Espaçamentos. |

Cores aceitas em `[color(…)]`: `name_green`, `white`, `white1`, `white_1`, `white_5`,
`default`, `light_blue`, `lime`, `lime2`, `orange`, `yellow`, `teal`, `fuschia`,
`light_gray`, `green`, `black`, `blue`, `pink`, `rank`.

Os nomes seguem os da v2, para que TBF antigos continuem válidos.

## Caracteres

Os do jogo em inglês mais os do perfil de fonte **pt-br** (acentos, `"`, `_`, `°`, `º`, `ª`).
Veja [Fontes e caracteres](fontes.md).

## Como funciona por dentro

O [Mapa do processo](../MAPA.md) descreve cada etapa (ler a ISO, abrir os pacotes, decifrar o
executável, decodificar os scripts, remontar e conferir), com o motivo de cada uma e o que
se aprendeu com a v1 e a v2.
