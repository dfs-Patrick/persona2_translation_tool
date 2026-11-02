# Instalação e execução

[← Sumário](../README.md)

## Usando uma versão publicada

Baixe o `GameTranslationEngine.exe` da página de
[releases](https://github.com/dfs-Patrick/persona2_translation_tool/releases), coloque-o
onde quiser e abra com dois cliques. É um arquivo único e portátil: não instala nada e não
precisa do .NET.

Na primeira execução o Windows pode mostrar o aviso do SmartScreen ("O Windows protegeu o
computador"), porque o executável não tem assinatura digital: clique em **Mais
informações › Executar assim mesmo**.

Para gerar o executável a partir do código, veja [Desenvolvimento › Publicar](desenvolvimento.md#publicar).

## Rodando a partir do código

Requisitos:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows ou Linux (a interface usa Avalonia). O uso diário foi testado no Windows.

Na pasta do repositório:

```
dotnet run --project src/GameTranslationEngine.App
```

Para abrir direto um projeto, passe a pasta do projeto ou o arquivo dele depois de `--`:

```
dotnet run --project src/GameTranslationEngine.App -- "D:\traducao\meu-projeto"
```

Depois de compilar uma vez (`dotnet build`), o executável fica em
`src/GameTranslationEngine.App/bin/Debug/net10.0/GameTranslationEngine.exe` e aceita o
mesmo argumento.

## O que você precisa ter

- A **ISO original** do jogo, na versão que o motor pede. Para o Persona 2: Innocent Sin,
  a americana `ULUS-10584`. A ferramenta confere o código do jogo ao criar o projeto e
  recusa outras versões.
- Um **emulador** para testar a ISO traduzida (PPSSPP, no caso do PSP).

## Onde a ferramenta guarda as configurações

A lista de projetos recentes fica em `%APPDATA%\GameTranslationEngine\settings.json`
(no Linux, `~/.config/GameTranslationEngine/settings.json`). Na primeira execução depois
da mudança de nome, a lista antiga (`%APPDATA%\P2Tool\settings.json`) é aproveitada.
