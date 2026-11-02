# Validar e compilar a ISO

[← Sumário](../README.md)

## Validar

**Validar** (barra de cima) passa por toda a tradução como se fosse compilar, mas não grava
nada. No P2IS leva menos de 2 segundos. O resultado diz quantos arquivos foram lidos,
quantas mensagens estão traduzidas e lista erros e avisos.

## Compilar ISO…

**Compilar ISO…** pede onde salvar (a sugestão é `build/<nome do projeto>.iso`) e:

1. codifica as mensagens traduzidas no formato do jogo;
2. remonta os scripts e os pacotes que os contêm;
3. instala o perfil de fonte (no P2IS, os acentos);
4. ajusta o executável do jogo para os novos tamanhos e posições;
5. grava a ISO nova, uma cópia da original com as partes alteradas;
6. **confere a ISO gravada**: relê cada parte alterada e compara com o esperado.

No P2IS isso leva cerca de 7 segundos. A ISO original não é alterada.

Se houver **erros**, nenhuma ISO é gravada. Avisos não impedem a compilação.

## Mensagens do relatório

| Tipo | Exemplo | O que fazer |
|---|---|---|
| Erro | `Caractere º (U+00BA) não existe na fonte` | Troque o caractere, ou acrescente-o ao perfil de fonte (veja [Fontes](fontes.md)). |
| Erro | `Comando inválido … [choice(2)Conversar…` | Falta o `]` do comando. No editor, aparece em vermelho. |
| Erro | `Comando desconhecido [xyz]` / `Cor desconhecida [color(xyz)]` | Nome de comando ou argumento errado. Veja a lista do [motor](motor-p2is.md#comandos). |
| Erro | `Constante desconhecida do tipo msg_var: …` | Argumento de comando corrompido; compare com o original. |
| Aviso | `mensagem não existe no script original; ignorada` | O TBF tem uma entrada que o jogo não tem. Acontece em TBF vindos da v2, que cortavam mensagens em linhas terminadas em `:`. O texto da mensagem verdadeira é usado normalmente; o aviso pode ser ignorado. |
| Aviso | `mensagem repetida no TBF; vale a primeira` | Duas entradas com a mesma chave. |
| Info | `[item_with_type(item_0)] vindo(s) da v2 trocado(s)…` | Correção automática de um defeito dos TBF da v2. Nada a fazer. |

A mensagem com erro fica com o texto original na ISO; as outras seguem traduzidas, mas a
ISO só é gravada quando não há nenhum erro.

## Testar no emulador

Abra a ISO gerada no emulador (PPSSPP para o PSP). Pontos que valem a pena conferir depois
de mudanças grandes:

- a abertura do jogo e o primeiro evento traduzido;
- um evento mais adiante (as posições dos scripts mudam quando o texto cresce);
- dungeons e mapas;
- textos com acentos e com `º`, `ª`, aspas;
- textos de interface que usam a fonte estreita (ex.: o nome do local ao entrar numa sala).

Se uma linha passar da caixa de diálogo, encurte a tradução: use a dica
`maior linha: original → tradução` do editor como referência.
