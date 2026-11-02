# Formato dos arquivos de tradução (TBF)

[← Sumário](../README.md)

Cada script do jogo vira um arquivo `.tbf` em `translation/messages/<container>/`. É um JSON
com a mesma formatação da v2 (indentação de 2 espaços, chaves na mesma ordem), então TBF da
v2 são lidos sem conversão e um TBF salvo só muda nas linhas editadas.

```json
{
  "header": {
    "originalDirectory": "PSP_GAME/USRDIR/pack/P2PT_ALL.cpk$/event.bin$/e0005.bin$/8.efb$",
    "originalFilename": "script.msg",
    "context": {
      "location": "Prisão de Sumaru",
      "summary": "Curinga confronta Tatsuya…"
    }
  },
  "translation": [
    {
      "info": {
        "msg_key": "msg_1",
        "before_msg": "msg_1:",
        "after_msg": "[wait][clear][end]\n"
      },
      "text": {
        "before": "[color(name_green)]Thuggish student\nYo... Goin’ somewhere?\n",
        "after": "[color(name_green)]Valentão\nE aí... Vai pra algum lugar?\n"
      }
    }
  ]
}
```

## `header`

| Campo | Conteúdo |
|---|---|
| `originalDirectory` | Onde o script está dentro da ISO (pastas e containers terminados em `$`). Identifica o arquivo na compilação. Não edite. |
| `originalFilename` | Nome do arquivo de mensagens dentro do script (`script.msg`). Não edite. |
| `context` | Anotações livres da equipe. A ferramenta usa `location` (local da cena) e `summary` (o que acontece), editáveis no topo do editor. Outras chaves são preservadas. |

## `translation[]`: uma entrada por mensagem

| Campo | Conteúdo |
|---|---|
| `info.msg_key` | Chave da mensagem no script (`msg_0`, `msg_persona_3`…). Liga a tradução à mensagem do jogo. Não edite. |
| `info.before_msg` | Linha de cabeçalho da mensagem no formato texto da v2. Não edite. |
| `info.after_msg` | Comandos que fecham a mensagem. São acrescentados sozinhos na compilação; não repita na tradução. |
| `text.before` | Texto original (em inglês). Só leitura. |
| `text.after` | **A tradução.** Vazio = o jogo mantém o texto original. |

## Texto das mensagens

- Quebra de linha (`\n` no JSON) é quebra de linha no jogo.
- Comandos: `[nome]` ou `[nome(arg1, arg2)]`. A lista depende do motor
  ([P2IS](motor-p2is.md#comandos)).
- Escapes: `\[` é um colchete literal, `\\` uma barra invertida e `\xHHHH` um código bruto
  do jogo (quatro dígitos hexadecimais).
- `[end]` encerra a mensagem: o que vier depois é ignorado.

## Entradas "fantasmas" da v2

A v2 cortava uma mensagem em duas quando uma linha terminava em `:` (ex.: `I have my orders
from the Exalted One:`), criando uma entrada com essa linha como chave. A v3 lê esses TBF
juntando as partes de novo, como a v2 fazia ao compilar. A entrada extra gera o aviso
"mensagem não existe no script original; ignorada", que pode ser ignorado.
