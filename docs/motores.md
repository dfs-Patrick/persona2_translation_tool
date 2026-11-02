# Motores

[← Sumário](../README.md)

Um **motor** reúne tudo o que é próprio de um jogo. O resto da ferramenta é comum a todos
os jogos: projetos, editor, glossário, ortografia, busca e interface.

| Comum (não depende do jogo) | Do motor |
|---|---|
| Projeto e `translation-project.json` | Reconhecer a ISO certa |
| Formato TBF e o editor | Onde está o texto e como lê-lo (gerar os TBF) |
| Glossário, ortografia, busca | Como compilar a tradução de volta na ISO |
| Realce (a partir dos nomes de comando do motor) | Nomes dos comandos das mensagens |
| Interface e arte (a partir das imagens do motor) | Como agrupar e nomear os arquivos no editor |
| | Fontes (opcional) e logo/fundo (opcional) |

## A interface `IGameEngine`

Em `src/GameTranslationEngine.Core/Engines/IGameEngine.cs`:

| Membro | Para quê |
|---|---|
| `Id` | Identificador estável, gravado no projeto (`"engine"`). Nunca mude depois de publicado. |
| `Name`, `Description` | Nome e descrição na lista **Motor (jogo)** da tela inicial. |
| `ExpectedIso`, `Supports(IsoIdentity)`, `DescribeGame(gameId)` | Conferir a ISO ao criar o projeto e explicar a recusa. |
| `GenerateTranslationArea(iso, pasta, …)` | Ler o texto da ISO e criar os TBF que faltam, **sem alterar os existentes**. |
| `CreateCompiler(projeto)` | Devolve um `IProjectCompiler` com `Validate` e `Build` (gerar a ISO traduzida). |
| `DescribeTbf(originalDirectory, originalFilename)` | Grupo e nome de cada arquivo na lista do editor. |
| `CommandNames` | Nomes de comando válidos nas mensagens, para o realce. |
| `Fonts` | Ferramentas de fonte (`IFontTools`), ou `null` se o motor não tiver. |
| `OpenArtwork(Artwork.Logo / Background)` | Logo (claro, fundo transparente) e imagem de fundo em PNG, ou `null`. |

`IsoIdentity` (código do jogo, título, volume, tamanho) é lido de forma genérica para
qualquer ISO de PSP, pelo `IsoVerifier`.

## Registrar um motor

Os motores conhecidos ficam em `GameEngines.All`, no mesmo arquivo:

```csharp
public static readonly IReadOnlyList<IGameEngine> All = [new InnocentSinEngine(), new MeuJogoEngine()];
```

O primeiro da lista é o padrão: é o usado por projetos antigos, sem o campo `engine`.

## Criar um motor novo

1. Crie a classe em `src/GameTranslationEngine.Core/Engines/`, implementando `IGameEngine`.
   Use `InnocentSinEngine.cs` como modelo.
2. Ponha o que o motor sabe do jogo (tabelas de caracteres, catálogos, arte) em
   `data/<jogo>/`. Arquivos `.json`, `.png`, `.dic` e `.aff` de `data/` são embutidos na
   ferramenta e lidos com `GameResources.Open("<jogo>/…")`.
3. Gere TBF no mesmo formato ([Formato TBF](formato-tbf.md)): é o que dá ao jogo novo o
   editor, a busca, o glossário e a ortografia sem trabalho extra.
4. A compilação deve conferir o que gravou, como o P2IS faz na etapa C9. Uma ISO quebrada
   só é descoberta no emulador, muito mais tarde.
5. Registre em `GameEngines.All` e escreva testes em `tests/GameTranslationEngine.Tests`.
6. Documente o processo do jogo como o [Mapa do P2IS](../MAPA.md) e acrescente uma página
   de referência como a do [motor P2IS](motor-p2is.md).

As peças de formato que já existem podem ser reaproveitadas por outros jogos de PSP: ISO
9660 (`Iso/`), pacotes CPK da CRI (`Cpk/`), decifragem de executável PSP (`Eboot/`),
imagens GIM (`Images/`).
