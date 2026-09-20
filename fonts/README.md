# Perfis distribuídos com a ferramenta

`pt-br/` contém o perfil testado para Innocent Sin US: mapas, receitas de
composição e as quatro páginas necessárias (`7.png`, `8.png`, `12.png`,
`13.png`). As demais páginas continuam vindo da ISO fornecida pelo usuário.

O rebuild sem `--font` usa este perfil, independentemente de uma cópia local
em `lab/translation/en/fonts/new/pt-br`. A extração continua usando a
codificação original para ler corretamente a ISO de origem.

Para selecionar outro perfil, use `--font nome-do-perfil`. Um nome explícito
é procurado primeiro em `<tradução>/fonts/new/<nome>` e depois em
`fonts/<nome>` desta ferramenta. `--font original` mantém a fonte e a
codificação de origem. Perfis personalizados só são aplicados a IS US;
os demais jogos/variantes mantêm a fonte original quando a opção é omitida.

Este diretório é a exceção explícita para o perfil que acompanha a ferramenta.
ISOs, dumps, textos, scripts do jogo e o restante de `lab/` não devem ser
copiados para cá nem incluídos no pacote.
