# Busca global

[← Sumário](../README.md)

A aba **Busca**, à esquerda, procura um texto em todos os arquivos de tradução do projeto.

## Usar

1. Digite o texto (a partir de 2 caracteres). A busca roda sozinha quando você para de digitar.
2. Escolha onde procurar:
   - **Original e tradução**
   - **Só o original (antes)**
   - **Só a tradução (depois)**
3. Marque **Diferenciar maiúsculas** se quiser.

O resumo mostra quantas mensagens e arquivos têm o texto. Cada resultado mostra
`grupo · arquivo · chave · lado` (com `(3×)` quando o texto aparece mais de uma vez) e um
trecho da frase com o texto destacado; `⏎` marca as quebras de linha.

**Clicar num resultado** abre o arquivo já na mensagem. Enquanto a busca estiver ativa,
o texto fica com fundo amarelo nas caixas do editor, no lado em que se buscou.

## Regras

- O texto é buscado ao pé da letra, sem curingas.
- **Um espaço também casa com uma quebra de linha.** O texto do jogo é quebrado à mão,
  então buscar `Masked Circle` também encontra `Masked⏎Circle` (no P2IS, isso acontece em
  46 mensagens).
- Comandos fazem parte do texto: buscar `[tatsuya]` encontra o comando.
- Mostra até 2.000 resultados; o resumo avisa quando há mais.
- Edições ainda não salvas do arquivo aberto são salvas antes de buscar, para entrarem
  no resultado.

A busca lê os arquivos a cada vez, sem índice: no P2IS (617 arquivos, 13 mil mensagens)
leva cerca de 50 ms.
