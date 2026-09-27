'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const { spawnSync } = require('node:child_process');
const { createIso } = require('../dist/lib/iso/iso');
const { openFileWrite, closeFile } = require('../dist/lib/util/filesystem');
async function main() {
  const target = process.env.P2_EXE_TARGET || 'node22-win-x64';
  const executable = path.resolve('release', `p2-tool-exe-${target}`, target.includes('-win-') ? 'Persona2Tool.exe' : 'Persona2Tool');
  const work = await fs.mkdtemp(path.join(os.tmpdir(), 'p2 exe teste '));
  try {
    // Only the executable is copied: no node_modules, fonts, game or dist.
    const standalone = path.join(work, path.basename(executable));
    await fs.copyFile(executable, standalone);
    await fs.chmod(standalone, 0o755);
    await fs.mkdir(path.join(work, 'input'));
    await fs.writeFile(path.join(work, 'input', 'UMD_DATA.BIN'), 'synthetic ISO');
    const output = await openFileWrite(path.join(work, 'test.iso'));
    try { await createIso(output, path.join(work, 'input'), {}); }
    finally { await closeFile(output); }
    const help = spawnSync(standalone, ['--help'], { cwd: work, encoding: 'utf8', timeout: 30000 });
    assert.equal(help.status, 0, help.stderr);
    const result = spawnSync(standalone, ['extractAll', 'test.iso', '-o', 'dump', '--game', 'is', '--variant', 'us', '--locale', 'en'], { cwd: work, encoding: 'utf8', timeout: 30000 });
    // Game archive is deliberately absent, but reading embedded metadata and
    // extracting the real ISO must succeed before that failure.
    assert.equal(await fs.readFile(path.join(work, 'dump/dumped_cpk/UMD_DATA.BIN'), 'utf8'), 'synthetic ISO');
    assert.equal(result.status, 1, result.stderr);
    assert.match(result.stderr, /ERRO \[ENOENT\]/);
    assert.ok(!result.stderr.includes('Cannot find module'), result.stderr);
    console.log('Executável isolado: CLI, recursos embutidos, extração e erro legível verificados.');
  } finally { await fs.rm(work, { recursive: true, force: true }); }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
