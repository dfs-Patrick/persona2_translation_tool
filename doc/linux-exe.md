# Executável Linux x64

Baixe `Persona2Tool-Linux-x64.zip` em
[Releases](https://github.com/dfs-Patrick/persona2_translation_tool/releases).
Extraia o ZIP em uma pasta gravável. O executável contém o
runtime Node, dependências, fontes padrão e configurações do jogo.

```sh
chmod +x Persona2Tool
./Persona2Tool --help
./Persona2Tool extractAll lab/iso/p2is.iso -o lab/dump --translation-output lab/translation/en --game is --variant us --locale en
./Persona2Tool rebuildTbf lab/iso/p2is.iso lab/translation/en --game is --variant us --locale en
```

Coloque sua ISO original US em `lab/iso/p2is.iso`. A tradução fica em
`lab/translation/en/new/messages` e a saída em `lab/p2is-translated.iso`.
Não reaproveite dumps de extrações interrompidas; preserve traduções existentes.

Instale extensions/p2-tbf-editor.vsix no VS Code local e abra a pasta do pacote.
A integração PPSSPP Host incluída é específica do Windows; no Linux, abra o
emulador manualmente. O binário é destinado a Linux x64 com glibc, não Alpine/musl.
