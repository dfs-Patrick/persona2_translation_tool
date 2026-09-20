'use strict';
const vscode = require('vscode');
const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { randomBytes } = require('node:crypto');
const { parse, edit, BuildQueue } = require('./model');

function activate(context) {
  const output = vscode.window.createOutputChannel('Persona 2');
  const events = new vscode.EventEmitter();
  let child, timer, extracting = false, saveAndRun = false;
  const cfg = () => vscode.workspace.getConfiguration('p2');
  const root = () => {
    const folders = vscode.workspace.workspaceFolders;
    if (!folders || folders.length !== 1) throw new Error('Abra um único projeto por janela.');
    return folders[0].uri.fsPath;
  };
  const translation = () => path.resolve(root(), cfg().get('translationRoot'));
  const report = error => { output.appendLine(String(error.stack || error)); output.show(true); vscode.window.showErrorMessage(`Persona 2: ${error.message || error}`); };
  const guard = action => async (...args) => { try { return await action(...args); } catch (e) { report(e); } };
  const status = value => { output.appendLine(value); events.fire(value); };
  async function host(command) {
    if (!(await vscode.commands.getCommands()).includes(command)) {
      throw new Error('Instale Persona 2 — PPSSPP Host no VS Code do Windows e configure os caminhos antes de executar.');
    }
    const result = await vscode.commands.executeCommand(command);
    if (!result || !result.ok) throw new Error(result?.error || 'PPSSPP: operação cancelada.');
  }
  function tool(args) {
    if (!vscode.workspace.isTrusted) throw new Error('Confie no workspace antes de executar comandos.');
    if (process.platform !== 'linux' || !fs.existsSync('/.dockerenv')) throw new Error('Use Dev Containers: Reopen in Container para executar a ferramenta.');
    if (child) throw new Error('Outra operação já está em execução.');
    const cli = cfg().get('cliPath');
    if (!fs.existsSync(cli)) throw new Error(`CLI não encontrada: ${cli}. Reconstrua o Dev Container.`);
    output.show(true);
    output.appendLine(`\n> node ${cli} ${args.join(' ')}`);
    return new Promise((resolve, reject) => {
      const running = spawn('node', [cli, ...args], { cwd: root(), shell: false, stdio: ['ignore', 'pipe', 'pipe'] });
      child = running;
      running.stdout.on('data', data => output.append(data.toString()));
      running.stderr.on('data', data => output.append(data.toString()));
      running.on('error', reject);
      running.on('close', code => {
        if (child === running) child = undefined;
        code === 0 ? resolve() : reject(new Error(`Ferramenta terminou com código ${code}. Consulte a saída.`));
      });
    });
  }
  const queue = new BuildQueue(async () => {
    const launch = saveAndRun; saveAndRun = false;
    status('Compilando ISO…');
    try {
      if (launch) await host('p2.host.prepare');
      else if ((await vscode.commands.getCommands()).includes('p2.host.stop')) await host('p2.host.stop');
      const args = ['rebuildTbf', 'lab/iso/p2is.iso', translation(), '--game', 'is', '--variant', 'us', '--locale', 'en'];
      const font = cfg().get('font'); if (font) args.push('--font', font);
      await tool(args);
      if (launch && !queue.pending) await host('p2.host.run');
      status('ISO compilada: lab/p2is-translated.iso');
    } finally { events.fire('Pronto'); }
  }, report);
  async function rebuild(run = false) {
    if (extracting) throw new Error('Aguarde a extração terminar.');
    if (!(await vscode.workspace.saveAll(false))) throw new Error('Não foi possível salvar os arquivos.');
    clearTimeout(timer); saveAndRun ||= run; return queue.request();
  }
  const treeChanged = new vscode.EventEmitter();
  const tree = {
    onDidChangeTreeData: treeChanged.event,
    getTreeItem: item => item,
    async getChildren(parent) {
      if (!vscode.workspace.workspaceFolders) return [];
      const uri = parent?.resourceUri || vscode.Uri.file(path.resolve(root(), cfg().get('translationDirectory')));
      let entries;
      try { entries = await vscode.workspace.fs.readDirectory(uri); } catch { return []; }
      return entries.filter(([name, type]) => type === vscode.FileType.Directory || name.endsWith('.tbf'))
        .sort((a, b) => (b[1] === vscode.FileType.Directory) - (a[1] === vscode.FileType.Directory) || a[0].localeCompare(b[0], undefined, { numeric: true }))
        .map(([name, type]) => {
          const directory = type === vscode.FileType.Directory;
          const item = new vscode.TreeItem(name, directory ? vscode.TreeItemCollapsibleState.Collapsed : vscode.TreeItemCollapsibleState.None);
          item.resourceUri = vscode.Uri.joinPath(uri, name);
          item.iconPath = new vscode.ThemeIcon(directory ? 'folder' : 'file-text');
          if (!directory) item.command = { command: 'vscode.openWith', title: 'Abrir TBF', arguments: [item.resourceUri, 'p2.tbf'] };
          return item;
        });
    }
  };
  const watcher = vscode.workspace.createFileSystemWatcher('**/lab/translation/**');
  let refreshTimer;
  const refresh = () => { clearTimeout(refreshTimer); refreshTimer = setTimeout(() => treeChanged.fire(), 250); };
  context.subscriptions.push(output, events, treeChanged, watcher, watcher.onDidCreate(refresh), watcher.onDidDelete(refresh),
    vscode.window.registerTreeDataProvider('p2.files', tree),
    vscode.commands.registerCommand('p2.refresh', refresh),
    vscode.commands.registerCommand('p2.logs', () => output.show()),
    vscode.commands.registerCommand('p2.rebuild', guard(() => rebuild())),
    vscode.commands.registerCommand('p2.rebuildAndRun', guard(() => rebuild(true))),
    vscode.commands.registerCommand('p2.toggleOnSave', guard(async () => {
      const enabled = !cfg().get('runOnSave');
      await cfg().update('runOnSave', enabled, vscode.ConfigurationTarget.Workspace);
      if (!enabled) clearTimeout(timer);
      events.fire('Pronto');
    })),
    vscode.commands.registerCommand('p2.extract', guard(async () => {
      if (child || queue.active || extracting) throw new Error('Aguarde a operação atual terminar.');
      const destination = path.join(translation(), 'new', 'messages');
      if (fs.existsSync(destination)) throw new Error('Já existem TBFs neste projeto. Use outro workspace para uma nova extração.');
      extracting = true; status('Extraindo ISO…');
      try {
        await tool(['extractAll', 'lab/iso/p2is.iso', '-o', 'lab/dump', '--translation-output', translation(), '--game', 'is', '--variant', 'us', '--locale', 'en']);
        refresh(); vscode.window.showInformationMessage('Extração concluída. Abra um TBF na aba Persona 2.');
      } finally { extracting = false; events.fire('Pronto'); }
    })),
    vscode.workspace.onDidSaveTextDocument(document => {
      if (!cfg().get('runOnSave') || extracting) return;
      const relative = path.relative(translation(), document.uri.fsPath).split(path.sep).join('/');
      if (!((relative.startsWith('new/messages/') && relative.endsWith('.tbf')) || relative.startsWith('fonts/new/'))) return;
      clearTimeout(timer);
      timer = setTimeout(() => { saveAndRun = true; queue.request(); }, Math.max(100, cfg().get('saveDelay')));
    }),
    vscode.workspace.onDidChangeConfiguration(event => { if (event.affectsConfiguration('p2')) { refresh(); events.fire('Pronto'); } }),
    { dispose() { clearTimeout(timer); clearTimeout(refreshTimer); queue.dispose(); child?.kill('SIGTERM'); } }
  );

  context.subscriptions.push(vscode.window.registerCustomEditorProvider('p2.tbf', {
    async resolveCustomTextEditor(document, panel) {
      const webview = panel.webview;
      webview.options = { enableScripts: true, localResourceRoots: [vscode.Uri.joinPath(context.extensionUri, 'media')] };
      const resource = name => webview.asWebviewUri(vscode.Uri.joinPath(context.extensionUri, 'media', name));
      const nonce = randomBytes(16).toString('hex');
      webview.html = `<!DOCTYPE html><html lang="pt-BR"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource}; style-src ${webview.cspSource}; script-src 'nonce-${nonce}';"><link rel="stylesheet" href="${resource('editor.css')}"></head><body>
    <header><section class="metadata"><div class="brand"><img src="${resource('logo.svg')}" alt=""><h1 id="filename">Editor TBF</h1></div><label>Diretório original<input id="directory" readonly></label><label>Arquivo original<input id="original" readonly></label><label>Contexto (objeto JSON)<textarea id="context" rows="2" spellcheck="false"></textarea></label></section>
<nav aria-label="Comandos"><button data-command="p2.extract">Extrair</button><button data-command="p2.rebuild">Compilar</button><button data-command="p2.rebuildAndRun">Compilar e executar</button><button id="save">Salvar</button><label class="toggle"><input id="onsave" type="checkbox"> Executar ao salvar</label><button data-command="p2.logs">Ver saída</button><span id="status" role="status">Pronto</span></nav></header>
<p id="error" role="alert" hidden></p><div class="columns"><h2>Antes · original</h2><h2>Depois · tradução</h2></div><main id="messages"></main><script nonce="${nonce}" src="${resource('editor.js')}"></script></body></html>`;
      let chain = Promise.resolve(), applying = false, disposed = false;
      const send = () => {
        if (disposed) return;
        try { webview.postMessage({ type: 'document', value: parse(document.getText()), filename: path.basename(document.uri.fsPath) }); }
        catch (e) { webview.postMessage({ type: 'error', message: `${e.message} Use “Reabrir editor com → Editor de texto” para corrigir o JSON.` }); }
      };
      const state = message => webview.postMessage({ type: 'status', message, onSave: cfg().get('runOnSave') });
      const subscriptions = [
        vscode.workspace.onDidChangeTextDocument(event => { if (event.document.uri.toString() === document.uri.toString() && !applying) send(); }),
        events.event(state),
        webview.onDidReceiveMessage(message => {
          chain = chain.then(async () => {
            if (disposed) return;
            if (message.type === 'ready') { send(); state('Pronto'); }
            else if (message.type === 'edit') {
              const text = edit(document.getText(), message);
              const workspaceEdit = new vscode.WorkspaceEdit();
              workspaceEdit.replace(document.uri, new vscode.Range(document.positionAt(0), document.positionAt(document.getText().length)), text);
              applying = true;
              try { if (!(await vscode.workspace.applyEdit(workspaceEdit))) throw new Error('Não foi possível aplicar a edição.'); }
              finally { applying = false; }
              webview.postMessage({ type: 'ack', id: message.id });
            } else if (message.type === 'save') { await document.save(); }
            else if (message.type === 'undo' || message.type === 'redo') { await vscode.commands.executeCommand(message.type); }
            else if (message.type === 'command' && ['p2.extract', 'p2.rebuild', 'p2.rebuildAndRun', 'p2.toggleOnSave', 'p2.logs'].includes(message.command)) {
              // Commands may last minutes; do not block further text edits.
              vscode.commands.executeCommand(message.command);
            }
          }).catch(e => { webview.postMessage({ type: 'error', message: e.message }); send(); });
        })
      ];
      panel.onDidDispose(() => { disposed = true; subscriptions.forEach(d => d.dispose()); });
    }
  }, { supportsMultipleEditorsPerDocument: false, webviewOptions: { retainContextWhenHidden: true } }));
}
module.exports = { activate };
