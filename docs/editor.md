# O editor de tradução

[← Sumário](../README.md)

## Escolher um arquivo

A aba **Tradução**, à esquerda, lista os arquivos do projeto agrupados pelo tipo de
script (no P2IS: Eventos, Dungeons, Mapas, Personas (demo), Loja de CDs, F_BE). O número
ao lado de cada arquivo é o progresso: mensagens traduzidas / total, em verde quando
completo. O campo **Filtrar arquivos…** filtra pelo nome ou grupo.

## A tela do arquivo

No topo:

- **Grupo · arquivo** e o caminho do script dentro do jogo.
- **Local da cena** e **Contexto**: anotações da equipe sobre a cena (onde acontece, o que
  acontece, quem fala). São editáveis e ficam salvas no próprio arquivo.
- O progresso do arquivo, uma busca dentro dele (na chave, no original ou na tradução) e o
  filtro **Todas / Pendentes / Traduzidas**.
- **Salvar (Ctrl+S)**, com o aviso "● alterações não salvas" quando há algo pendente.

Cada mensagem é um cartão:

| Parte | O que é |
|---|---|
| `#3 msg_2` | Número e chave da mensagem. |
| `maior linha: 40 → 38` | A linha mais longa do original e da tradução. A caixa de diálogo do jogo tem largura fixa: use isso para não estourar. |
| **Copiar original** | Copia o texto original para a tradução (bom ponto de partida: os comandos já vêm no lugar). |
| **Limpar** | Esvazia a tradução, ou seja, volta ao texto original no jogo. |
| Original / Tradução | As duas caixas de texto. A borda do cartão fica verde quando a mensagem está traduzida. |
| Códigos finais | Comandos que fecham a mensagem (ex.: `[wait][clear][end]`). A ferramenta os mantém sozinha; não precisam ser escritos. |

**Tradução vazia = texto original no jogo.** Não é preciso copiar mensagens que não mudam
(como `......?`).

## O que as cores querem dizer

| Aparência | Significado |
|---|---|
| Roxo | Comando do jogo, ex.: `[color(name_green)]`, `[delay(30)]`. Mantenha-o: ele controla cor, pausa, nomes etc. |
| Vermelho sublinhado | Comando desconhecido, ou `[` sem `]`. O jogo não aceita a mensagem assim. |
| Lilás claro | Sequência de escape, ex.: `\[` (um colchete literal). |
| Azul com fundo azul | Termo do [glossário](glossario.md). Passe o mouse para ver a tradução combinada e a descrição. |
| Sublinhado ondulado vermelho | Palavra fora do dicionário (só na tradução). Veja [Correção ortográfica](ortografia.md). |
| Fundo amarelo | Texto da [busca global](busca.md). |

Embaixo de uma mensagem pode aparecer, em azul, **"Glossário: crap → porcaria"**: o
original usa um termo do glossário e a tradução não usa o equivalente combinado.

## Menu do botão direito

- Sobre uma palavra sublinhada: sugestões de correção e **Adicionar ao dicionário do projeto**.
- Com um texto selecionado: **Adicionar ao glossário…** (abre o glossário com o termo preenchido).
- Recortar, Copiar, Colar, Selecionar tudo.

## Escrevendo a tradução

- Quebras de linha são quebras de linha no jogo: o texto não é quebrado automaticamente.
- Os comandos usam a forma `[nome]` ou `[nome(argumento)]`. A lista de cada jogo está na
  referência do motor ([P2IS](motor-p2is.md#comandos)).
- Para escrever um `[` literal, use `\[`; para uma barra invertida, `\\`.
- Use só caracteres que a fonte do jogo tem. Os que não existem são apontados pelo
  **Validar** (veja [Fontes e caracteres](fontes.md)).

## Salvar

- **Ctrl+S** ou **Salvar** grava o arquivo.
- Trocar de arquivo, abrir o glossário, buscar, validar, compilar ou fechar o projeto
  salvam antes o que estiver pendente: edições não se perdem sem aviso.
