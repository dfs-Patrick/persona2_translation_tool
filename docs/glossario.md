# Glossário

[← Sumário](../README.md)

O glossário reúne os termos de referência da tradução (nomes, lugares, golpes, expressões
recorrentes) para que saiam iguais no jogo inteiro, mesmo com várias pessoas traduzindo.

## Abrir e editar

Clique em **Glossário** na barra de cima. Ele ocupa o lugar do editor; **← Voltar ao
arquivo** retorna ao arquivo que estava aberto.

Cada termo tem:

| Campo | Exemplo | Uso |
|---|---|---|
| Inglês (original) | `Exalted One` | Como aparece no texto original. |
| Português (tradução) | `Exaltado` | Como deve ser traduzido. |
| Descrição | `Líder do Círculo Mascarado.` | Contexto, observações de estilo. Aparece ao passar o mouse. |
| Maiúsculas | desmarcado | Marcado: só encontra o termo escrito exatamente assim (ex.: `Joker`, mas não `joker`). |

- **Novo termo** cria uma linha no topo; **Remover** apaga.
- O campo de busca filtra por inglês, português ou descrição.
- **Salvar (Ctrl+S)**. O glossário também é salvo ao voltar ao arquivo ou fechar o projeto.

Atalho pelo editor: selecione um texto numa mensagem, clique com o botão direito e use
**Adicionar ao glossário…**. Se o texto veio do original, preenche o inglês; se veio da
tradução, o português.

## Como os termos são encontrados

- Como palavra inteira: `Joker` não é encontrado dentro de `Jokers`.
- Sem diferenciar maiúsculas, a não ser que **Maiúsculas** esteja marcado.
- Um espaço no termo também casa com uma quebra de linha, porque o texto do jogo é
  quebrado à mão (`Exalted⏎One` é encontrado).
- Quando dois termos se sobrepõem, vale o mais longo (`Masked Circle member` antes de
  `Masked Circle`).
- Comandos do jogo são ignorados (`[color(Joker)]` não conta).

No editor, o termo em inglês é destacado no **original** e o termo em português na
**tradução**. Se o original tem o termo e a tradução não tem o equivalente, a mensagem
mostra o aviso "Glossário: inglês → português".

As palavras do português do glossário também contam como corretas para a
[correção ortográfica](ortografia.md): nomes próprios cadastrados não ficam sublinhados.

## Arquivo

`translation/glossary.json`, dentro do projeto:

```json
{
  "terms": [
    {
      "original": "Exalted One",
      "translation": "Exaltado",
      "description": "Líder do Círculo Mascarado.",
      "matchCase": false
    }
  ]
}
```

Linhas sem inglês e sem português são descartadas ao salvar.
