# Correção ortográfica

[← Sumário](../README.md)

A tradução é verificada em português do Brasil enquanto se escreve. Nada é corrigido
sozinho: a ferramenta só aponta.

## Como funciona

- Palavras fora do dicionário ganham um **sublinhado ondulado vermelho**. Só a caixa da
  tradução é verificada; o original não.
- O dicionário carrega em segundo plano ao abrir o projeto (cerca de 1 segundo). O rodapé
  mostra **Ortografia: pt-BR** quando está pronto.
- Não são verificados: comandos do jogo (`[delay(30)]`), escapes e termos do
  [glossário](glossario.md).
- Palavras com hífen (`meio-dia`) são aceitas se o conjunto ou cada parte estiver no dicionário.
- Linguagem coloquial comum já é aceita pelo dicionário (`pra`, `tá`, `né`).

## Corrigir

Clique com o **botão direito** sobre a palavra sublinhada:

- as sugestões aparecem em negrito; clicar numa delas troca a palavra;
- **Adicionar "palavra" ao dicionário do projeto** faz a palavra valer como correta no
  projeto inteiro (para nomes, gírias, onomatopeias: `Tatsuya`, `Sumaru`, `Heheh`).

## O dicionário do projeto

`translation/dictionary.txt`, uma palavra por linha. Linhas que começam com `#` são
comentários. Pode ser editado à mão e versionado junto com a tradução. A comparação não
diferencia maiúsculas.

Para nomes, prefira o glossário: além de não serem sublinhados, eles ganham destaque e
descrição.

## Dicionário usado

[VERO](../data/dictionaries/pt-br/README_pt_BR.txt) 3.2, o verificador ortográfico pt-BR do
LibreOffice (licença LGPLv3 / MPL), lido pelo
[WeCantSpell.Hunspell](https://github.com/aarondandy/WeCantSpell.Hunspell). Ele já vem
dentro da ferramenta.
