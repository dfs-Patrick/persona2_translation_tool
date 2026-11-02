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
│   ├── glossary.json          glossário (veja Glossário)
│   └── dictionary.txt         palavras aceitas pela ortografia (veja Correção ortográfica)
├── fonts/                     criada por "Exportar fontes…" (opcional)
├── build/                     ISOs compiladas (sugestão padrão)
└── cache/
```

`translation/` é tudo o que a equipe produz: os TBF, o glossário e o dicionário. As outras
pastas podem ser recriadas pela ferramenta.

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
