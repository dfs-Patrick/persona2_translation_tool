# Desenvolvimento

[← Sumário](../README.md)

## Estrutura

```
GameTranslationEngine.slnx
src/
  GameTranslationEngine.Core/    formatos e etapas, sem interface gráfica
    Engines/      IGameEngine, registro de motores, motor P2IS
    Projects/     projeto, lista de arquivos, busca global
    Tbf/          leitura e gravação dos TBF (formato idêntico ao da v2)
    Editing/      glossário, ortografia, análise das mensagens para o realce
    Iso/ Cpk/ Archives/ Eboot/ Images/ IO/   formatos de arquivo (ISO 9660, CPK, BNP/PAR/EAR, executável PSP, GIM, PNG)
    Text/ Scripts/ Extraction/ Build/ Fonts/ GameData/   etapas do motor P2IS
  GameTranslationEngine.App/     interface Avalonia (MVVM com CommunityToolkit)
    Views/ ViewModels/ Controls/ Services/
tests/
  GameTranslationEngine.Tests/   xUnit
data/
  is/             o que o motor P2IS sabe do jogo: tabelas, catálogos, perfil de fonte, arte
  dictionaries/   dicionário pt-BR da ortografia
scripts/          scripts auxiliares (ex.: gen_text_sources.py, que gera data/is/text_sources.json)
MAPA.md           mapa das etapas do motor P2IS
docs/             esta documentação
```

Regras da divisão:

- **Core não depende da interface**: tudo o que precisa de teste fica no Core.
- **O que é de um jogo fica no motor** ([Motores](motores.md)). Editor, projeto, glossário,
  ortografia e busca não podem saber qual é o jogo.
- Arquivos de `data/` (`.json`, `.png`, `.dic`, `.aff`) são embutidos no Core e lidos com
  `GameResources.Open(...)`: a ferramenta publicada é um executável só, sem pasta de dados.

As versões anteriores ficam fora do repositório, como referência: a v2 (TypeScript) é a
[tag `1.0`](https://github.com/dfs-Patrick/persona2_translation_tool/tree/1.0) deste mesmo repositório, e a v1 é o [p2_tool de eiowlta](https://github.com/eiowlta/p2_tool).

## Compilar e rodar

```
dotnet build GameTranslationEngine.slnx
dotnet run --project src/GameTranslationEngine.App
```

## Testes

```
dotnet test tests/GameTranslationEngine.Tests
```

Os testes marcados com `[IsoFact]` usam a ISO original do P2IS, que não faz parte do
repositório. Eles a procuram em `lab/p2is.iso` (subindo a partir da pasta dos testes) ou
no caminho da variável de ambiente `P2_ISO`, e são **pulados** quando ela não existe. Com a
ISO, a suíte inteira leva cerca de 15 segundos, incluindo uma compilação completa.

## Publicar

```
dotnet publish src/GameTranslationEngine.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publicar
```

O resultado é um único `publicar/GameTranslationEngine.exe` (~105 MB), portátil: não
precisa do .NET instalado nem de outros arquivos ao lado, e roda com dois cliques. Os
`.pdb` que aparecem junto são símbolos de depuração das bibliotecas nativas e não vão na
release. Para Linux, use `-r linux-x64`.

A versão vem de `Directory.Build.props`, na raiz.

## Convenções

- Comentários do código em inglês; textos da interface e mensagens de erro em português.
- Mensagens de erro dizem o arquivo e a mensagem do jogo envolvidos, para quem traduz
  conseguir agir.
- O trabalho de quem traduz nunca é sobrescrito: gerar, importar e migrar só preenchem o
  que está vazio ou faltando.
- Toda gravação na ISO é relida e conferida (etapa C9 do motor P2IS).
- Mudanças no processo do P2IS são registradas no [MAPA.md](../MAPA.md), com o porquê.
