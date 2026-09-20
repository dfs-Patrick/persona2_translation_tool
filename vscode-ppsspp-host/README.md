# Persona 2 — PPSSPP Host

No fluxo com Dev Container, este VSIX é instalado automaticamente pelo comando
pós-conexão. Se a instalação automática não estiver disponível, instale-o
**localmente no VS Code do Windows**. A extensão TBF principal fica no Dev
Container e executa a ferramenta em Linux. Este componente local somente inicia
e encerra o PPSSPP, sem PowerShell, rede ou servidores HTTP.

Execute **Persona 2: Configurar PPSSPP no Windows** e informe o executável e o
caminho Windows para `lab/p2is-translated.iso` do projeto aberto. Ajuste esse
caminho ao trocar de projeto. Caminhos UNC do WSL também são aceitos.

O emulador reinicia após cada compilação solicitada por “Compilar e executar”
ou “Executar ao salvar”. Salve seu progresso no jogo antes de editar: a instância
iniciada pela extensão é encerrada antes do rebuild. Instâncias manuais não são
encerradas. Não são carregados save states automaticamente.
