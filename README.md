<p align="center"><img src="docs/images/icon.png" width="128" alt="Ícone do Game Translation Engine"></p>

# Game Translation Engine

Ferramenta para traduzir jogos: lê o texto direto da ISO original, oferece um editor
pensado para a tradução (glossário, correção ortográfica, realce dos comandos do jogo,
busca em todos os arquivos) e compila a tradução de volta numa ISO nova.

Ela nasceu como **P2Tool**, para a tradução PT-BR de *Persona 2: Innocent Sin* (PSP).
Desde que tudo o que é específico de um jogo foi separado num **motor**, a mesma
interface pode servir para outros jogos: basta escrever o motor deles.

| Motor | Jogo | Situação |
|---|---|---|
| `p2is-psp-us` | Persona 2: Innocent Sin (PSP, US — `ULUS-10584`) | Completo: eventos, dungeons, mapas, demos de persona e loja de CDs, com fonte PT-BR |

A ISO original nunca é alterada: a ferramenta só a lê e grava a ISO traduzida em outro
arquivo.

---

## Sumário

**Usando a ferramenta**

1. [Instalação e execução](docs/instalacao.md)
2. [Projetos: criar, abrir e o que fica na pasta](docs/projetos.md)
3. [O editor de tradução](docs/editor.md)
4. [Glossário](docs/glossario.md)
5. [Correção ortográfica](docs/ortografia.md)
6. [Busca global](docs/busca.md)
7. [Validar e compilar a ISO](docs/compilacao.md)
8. [Fontes e caracteres](docs/fontes.md)

**Referência**

9. [Formato dos arquivos de tradução (TBF)](docs/formato-tbf.md)
10. [Motor Persona 2: Innocent Sin](docs/motor-p2is.md): comandos, cores, caracteres e limites
11. [Mapa do processo do P2IS](MAPA.md): cada etapa de extração e compilação, com o porquê

**Desenvolvimento**

12. [Motores: como a ferramenta separa o que é de cada jogo](docs/motores.md)
13. [Desenvolvimento: código, testes e publicação](docs/desenvolvimento.md)

---

## Guia rápido

### 1. Abrir a ferramenta

Com o [.NET 10 SDK](https://dotnet.microsoft.com/download) instalado, na pasta do repositório:

```
dotnet run --project src/GameTranslationEngine.App
```

Ou use o `GameTranslationEngine.exe` de uma versão publicada (não precisa do .NET).

### 2. Criar um projeto

Na tela inicial, em **Novo projeto**:

1. **Motor (jogo)**: escolha o jogo. A descrição embaixo diz qual ISO ele espera.
2. **Nome**, **ISO original** e **Pasta do projeto**.
3. **Criar projeto**. A ferramenta confere a ISO e gera um arquivo de tradução (`.tbf`)
   para cada script do jogo. No P2IS são 617 arquivos, em poucos segundos.

Projetos já criados aparecem na lista **Abrir projeto**.

### 3. Traduzir

- Na aba **Tradução** (à esquerda), escolha um arquivo. Cada mensagem aparece com o
  **original** à esquerda e a **tradução** à direita.
- Escreva a tradução. Deixar vazio mantém o texto original no jogo.
- Os **comandos do jogo** (`[color(name_green)]`, `[delay(30)]`…) aparecem em roxo: mantenha-os.
- Palavras com **sublinhado vermelho** não estão no dicionário: clique com o botão
  direito para ver sugestões ou adicioná-las ao dicionário do projeto.
- Termos do **glossário** aparecem em azul; passe o mouse para ver a tradução combinada.
- No topo do arquivo ficam o **local** e o **contexto** da cena, anotados pela equipe.
- **Ctrl+S** salva. Trocar de arquivo também salva o anterior.

### 4. Consultar

- **Glossário** (barra de cima): termos de referência da tradução.
- Aba **Busca**: procura um texto em todos os arquivos, no original, na tradução ou nos dois.

### 5. Testar no jogo

- **Validar**: confere toda a tradução sem gravar nada (caracteres, comandos, mensagens).
- **Compilar ISO…**: gera a ISO traduzida e confere o resultado. Abra-a num emulador
  (PPSSPP, no caso do PSP).

---

## Créditos e licenças

- Formatos do Persona 2 estudados a partir do [p2_tool original de eiowlta](https://github.com/eiowlta/p2_tool) (v1)
  e da versão em TypeScript ([v2, tag `1.0` deste repositório](https://github.com/dfs-Patrick/persona2_translation_tool/tree/1.0)).
- Dicionário pt-BR: [VERO](data/dictionaries/pt-br/README_pt_BR.txt), do LibreOffice
  (LGPLv3 / MPL), usado pela correção ortográfica.
- Logo e imagem de fundo do motor P2IS: © ATLUS / Index Corporation; usados só como
  identificação do jogo na interface.
