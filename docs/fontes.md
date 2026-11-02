# Fontes e caracteres

[← Sumário](../README.md)

O jogo só mostra os caracteres que a fonte dele tem. Um **perfil de fonte** acrescenta
os que faltam (no P2IS, os acentos do português) e é instalado na ISO pela compilação.

## Quais caracteres posso usar?

Os do jogo original mais os do perfil escolhido. No perfil **pt-br** do P2IS:

- letras acentuadas: `á à â ã ç è é ê í ó ô õ ú ü ñ` e as maiúsculas `Á À Â Ã Ç È É Ê Í Ó Ô Õ Ú Ü Ñ`;
- `"` (aspas retas), `_`, `°`, e `º` / `ª` (desenhados para o PT-BR).

Fora disso, o **Validar** aponta `Caractere X não existe na fonte`. Substituições comuns:

| Em vez de | Use |
|---|---|
| `“` | `"` ou `”` |
| `—` (travessão) | `―` |
| `𝅘𝅥𝅮` e outros símbolos musicais | `♪` |
| tabulação | espaço |

## Escolher o perfil

Campo `font` do `translation-project.json`:

| Valor | Resultado |
|---|---|
| `pt-br` | Perfil PT-BR que vem com a ferramenta (padrão). |
| `original` | Fonte do jogo, sem mudanças. |
| `fonts/pt-br` (ou outra pasta) | Um perfil editado pela equipe, numa pasta relativa ao projeto. |

## Editar o perfil

1. Na aba **ISO**, clique em **Exportar fontes…**. A ferramenta cria:
   - `fonts/original/`: as páginas de fonte do jogo, em PNG (para referência);
   - `fonts/pt-br/`: uma cópia editável do perfil PT-BR (arquivos existentes não são sobrescritos).
2. Edite a cópia e troque `font` para `"fonts/pt-br"` no `translation-project.json`.
3. Compile e teste.

Arquivos de um perfil:

| Arquivo | Conteúdo |
|---|---|
| `event.json` | Código de cada caractere nas mensagens: `"0a07": "á"`. Os códigos novos reaproveitam códigos de kanji que o jogo em inglês não usa. |
| `font.json` | A grade de cada página da fonte: que caractere está em cada célula de 16×16. |
| `7.png`, `8.png` | Páginas da fonte normal (falas). |
| `12.png`, `13.png` | Páginas da fonte estreita (interface: nomes de local, menus). |
| `glyphs.json` | Receitas opcionais que montam um glifo juntando partes de outros (ex.: o acento de um com a letra de outro). |

Para acrescentar um caractere: escolha um código livre em `event.json`, uma célula livre
em `font.json` (de um caractere que o jogo em inglês nunca mostra) e desenhe o glifo nas
páginas normal **e** estreita, em branco com transparência, como os vizinhos.

## Como o perfil é instalado

- A página gravada na ISO é sempre a **original do jogo**. Do perfil entram só as células
  dos caracteres que ele acrescenta.
- Se as páginas do perfil estiverem deslocadas em relação às do jogo, o deslocamento é
  detectado e corrigido sozinho. As páginas herdadas da v2 tinham todos os glifos fora do
  lugar (a fonte estreita 3 px à direita), o que cortava a primeira letra das palavras no
  nome do local ao entrar numa sala.
- A largura de cada glifo novo é medida na página e gravada no executável do jogo.
- O arquivo de fontes do jogo mantém exatamente o tamanho original, como o executável exige.

Detalhes técnicos na etapa [C4 do Mapa](../MAPA.md#c4--gerar-a-fonte-pt-br).
