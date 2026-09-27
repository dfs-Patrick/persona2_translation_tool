'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { exec } = require('@yao-pkg/pkg');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '..');
const target = process.env.P2_EXE_TARGET || 'node22-win-x64';
if (!['node22-win-x64', 'node22-linux-x64'].includes(target)) throw new Error('Destino não suportado. Use node22-win-x64 ou node22-linux-x64.');
const windows = target.includes('-win-');
const folder = path.join(root, 'release', `p2-tool-exe-${target}`);
async function main() {
  if (fs.existsSync(folder)) throw new Error(`O destino já existe: ${folder}. Mova o pacote anterior para preservá-lo.`);
  fs.mkdirSync(folder, { recursive: true });
  const binary = path.join(folder, windows ? 'Persona2Tool.exe' : 'Persona2Tool');
  await exec([path.join(root, 'package.json'), '--target', target, '--output', binary,
    '--no-bytecode', '--public-packages', '*', '--public', '--compress', 'GZip']);
  if (windows) fs.cpSync(path.join(root, 'scripts/p2-tool.ps1'), path.join(folder, 'scripts/p2-tool.ps1'));
  fs.mkdirSync(path.join(folder, 'extensions'), { recursive: true });
  for (const [source, name] of [['vscode-extension', 'p2-tbf-editor'], ['vscode-ppsspp-host', 'p2-ppsspp-host']]) {
    fs.copyFileSync(path.join(root, source, `${name}.vsix`), path.join(folder, 'extensions', `${name}.vsix`));
  }
  fs.mkdirSync(path.join(folder, 'lab/iso'), { recursive: true });
  if (windows) for (const [name, action] of [['Extrair', 'extract'], ['Compilar', 'rebuild']]) {
    fs.writeFileSync(path.join(folder, `${name}.cmd`), `@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\\p2-tool.ps1" -Action ${action}\r\nset "result=%errorlevel%"\r\npause\r\nexit /b %result%\r\n`);
  }
  fs.copyFileSync(path.join(root, windows ? 'doc/windows-exe.md' : 'doc/linux-exe.md'), path.join(folder, 'LEIA-ME.md'));
  // Preserve license notices without distributing thousands of package files.
  const licenses = [fs.readFileSync(path.join(root, 'scripts/licenses/node-v22.23.2.txt'), 'utf8')];
  function collect(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const file = path.join(dir, entry.name);
      if (entry.isDirectory()) collect(file);
      else if (/^(license|licence|copying|notice)(\.|$)/i.test(entry.name)) licenses.push(`\n--- ${path.relative(root, file)} ---\n${fs.readFileSync(file, 'utf8')}`);
    }
  }
  collect(path.join(root, 'node_modules'));
  fs.writeFileSync(path.join(folder, 'THIRD-PARTY-NOTICES.txt'), licenses.join('\n'));
  if ((windows && process.platform === 'win32') || (target.includes('-linux-') && process.platform === 'linux')) {
    execFileSync(binary, ['--help'], { cwd: folder, stdio: 'pipe' });
  }
  console.log(`Executável gerado: ${binary}`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
