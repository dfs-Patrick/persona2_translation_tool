'use strict';
const vscode = require('vscode');
const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');

function activate(context) {
  let player;
  const config = () => vscode.workspace.getConfiguration('p2Host');
  function validate(requireIso) {
    if (!vscode.workspace.isTrusted) throw new Error('Confie no projeto antes de iniciar o emulador.');
    if (process.platform !== 'win32') throw new Error('Instale PPSSPP Host no VS Code local do Windows, não no container.');
    const executable = config().get('executable'), iso = config().get('iso');
    if (!path.isAbsolute(executable) || !fs.existsSync(executable)) throw new Error('Configure o executável em Persona 2: Configurar PPSSPP no Windows.');
    if (!iso || !path.isAbsolute(iso) || path.basename(iso).toLowerCase() !== 'p2is-translated.iso') throw new Error('Configure o caminho absoluto da ISO lab/p2is-translated.iso deste projeto no Windows.');
    if (requireIso && !fs.existsSync(iso)) throw new Error(`ISO não encontrada no Windows: ${iso}`);
    return { executable, iso };
  }
  async function stop() {
    const owned = player;
    if (!owned || owned.exitCode !== null || owned.signalCode !== null) return;
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => { owned.removeListener('exit', done); reject(new Error('O PPSSPP não encerrou. Feche a janela antes de compilar.')); }, 5000);
      function done() { clearTimeout(timeout); resolve(); }
      owned.once('exit', done);
      // Only a process spawned by this extension is terminated; never search by name.
      if (!owned.kill()) { clearTimeout(timeout); owned.removeListener('exit', done); reject(new Error('Não foi possível fechar o PPSSPP.')); }
    });
    if (player === owned) player = undefined;
  }
  const register = (id, action) => context.subscriptions.push(vscode.commands.registerCommand(id, async () => {
    try { await action(); return { ok: true }; }
    catch (e) { vscode.window.showErrorMessage(`PPSSPP: ${e.message}`); return { ok: false, error: e.message }; }
  }));
  register('p2.host.configure', async () => {
    const executable = await vscode.window.showInputBox({ title: 'Executável PPSSPP no Windows', value: config().get('executable'), ignoreFocusOut: true });
    if (executable === undefined) return;
    const iso = await vscode.window.showInputBox({ title: 'Caminho Windows para lab\\p2is-translated.iso deste projeto', value: config().get('iso'), placeHolder: 'C:\\Projetos\\persona2_translation_tool\\lab\\p2is-translated.iso', ignoreFocusOut: true });
    if (iso === undefined) return;
    await config().update('executable', executable.trim(), vscode.ConfigurationTarget.Global);
    await config().update('iso', iso.trim(), vscode.ConfigurationTarget.Global);
    validate(false);
    vscode.window.showInformationMessage('PPSSPP configurado. Confirme que a ISO aponta para a pasta lab deste workspace.');
  });
  register('p2.host.prepare', async () => { validate(false); await stop(); });
  register('p2.host.stop', stop);
  register('p2.host.run', async () => {
    const { executable, iso } = validate(true);
    await stop();
    await new Promise((resolve, reject) => {
      const running = spawn(executable, [iso], { cwd: path.dirname(executable), shell: false, stdio: 'ignore' });
      player = running;
      running.once('spawn', resolve);
      running.once('error', reject);
      running.once('exit', () => { if (player === running) player = undefined; });
    });
  });
  context.subscriptions.push({ dispose() { player?.kill(); } });
}
module.exports = { activate };
