'use strict';
// Run through npm run package:native on the target OS/architecture.
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '..');
const pkg = require('../package.json');
const windowsRuntime = process.env.P2_WINDOWS_RUNTIME;
const platform = windowsRuntime ? 'win32' : process.platform;
const arch = windowsRuntime ? 'x64' : process.arch;
const destination = path.join(root, 'release', `p2-tool-${platform}-${arch}`);
const runtimeRoot = windowsRuntime || path.dirname(process.execPath);
const license = [path.join(runtimeRoot, 'LICENSE'), path.join(runtimeRoot, '..', 'LICENSE'), '/usr/share/licenses/nodejs/LICENSE'].find(p => fs.existsSync(p));
if (!license) throw new Error('Licença do Node não encontrada junto ao runtime.');
if (fs.existsSync(destination)) throw new Error(`Remova ou mova o pacote anterior antes de gerar: ${destination}`);
if (!process.env.npm_execpath) throw new Error('Execute npm run package:native.');
const dependencies = execFileSync(process.execPath, [process.env.npm_execpath, 'ls', '--omit=dev', '--parseable', '--all'], { cwd: root, encoding: 'utf8' }).trim().split(/\r?\n/).filter(p => p !== root);
fs.mkdirSync(destination, { recursive: true });
function copy(source, target = source) {
  fs.cpSync(path.join(root, source), path.join(destination, target), { recursive: true });
}
for (const folder of ['dist', 'game', 'fonts']) copy(folder);
for (const dependency of new Set(dependencies)) {
  const relative = path.relative(root, dependency);
  if (!relative.startsWith(`node_modules${path.sep}`)) throw new Error(`Dependência fora do projeto: ${dependency}`);
  copy(relative);
}
copy('package.json');
copy('scripts/p2-tool.ps1');
copy('doc/windows-native.md', 'LEIA-ME.md');
fs.mkdirSync(path.join(destination, 'runtime'), { recursive: true });
const binary = platform === 'win32' ? 'node.exe' : 'node';
fs.copyFileSync(windowsRuntime ? path.join(windowsRuntime, 'node.exe') : process.execPath, path.join(destination, 'runtime', binary));
fs.copyFileSync(license, path.join(destination, 'runtime', 'LICENSE.txt'));
fs.mkdirSync(path.join(destination, 'lab', 'iso'), { recursive: true });
fs.mkdirSync(path.join(destination, '.vscode'), { recursive: true });
fs.writeFileSync(path.join(destination, '.vscode', 'settings.json'), JSON.stringify({ 'p2.cliPath': 'dist/cli/mod.js', 'p2.nodePath': `runtime/${binary}`, 'files.associations': { '*.tbf': 'tbf' } }, null, 2));
for (const [folder, name] of [['vscode-extension', 'p2-tbf-editor'], ['vscode-ppsspp-host', 'p2-ppsspp-host']]) {
  const vsix = `${folder}/${name}.vsix`;
  if (!fs.existsSync(path.join(root, vsix))) throw new Error(`Gere a extensão antes do pacote: ${vsix}`);
  copy(vsix, `extensions/${name}.vsix`);
}
fs.writeFileSync(path.join(destination, 'p2-tool.cmd'), '@echo off\r\n"%~dp0runtime\\node.exe" "%~dp0dist\\cli\\mod.js" %*\r\nexit /b %errorlevel%\r\n');
for (const [name, action] of [['Extrair', 'extract'], ['Compilar', 'rebuild']]) {
  fs.writeFileSync(path.join(destination, `${name}.cmd`), `@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\\p2-tool.ps1" -Action ${action}\r\nset "result=%errorlevel%"\r\npause\r\nexit /b %result%\r\n`);
}
fs.writeFileSync(path.join(destination, 'p2-tool'), '#!/bin/sh\nbase=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)\nexec "$base/runtime/node" "$base/dist/cli/mod.js" "$@"\n', { mode: 0o755 });
fs.writeFileSync(path.join(destination, 'build-info.json'), JSON.stringify({ version: pkg.version, platform, arch, node: windowsRuntime ? path.basename(windowsRuntime) : process.version }, null, 2));
execFileSync(windowsRuntime ? process.execPath : path.join(destination, 'runtime', binary), [path.join(destination, 'dist/cli/mod.js'), '--help'], { cwd: destination, stdio: 'pipe' });
console.log(`Pacote criado e CLI verificada no sistema de build: ${destination}`);
