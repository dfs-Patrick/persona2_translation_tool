import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { writeBinaryFile, readBinaryFile, openFileRead, closeFile } from '../lib/util/filesystem';
import { formatCliError } from '../lib/util/cli_error';

test('hundreds of concurrent writes respect the file-operation limit, including failures', async () => {
  const root = await fs.mkdtemp(join(tmpdir(), 'p2-file-limit-'));
  const original = fs.writeFile;
  let active = 0, peak = 0;
  fs.writeFile = (async (...args: Parameters<typeof fs.writeFile>) => {
    active++;
    peak = Math.max(peak, active);
    try {
      if (active > 16) throw Object.assign(new Error('too many files'), { code: 'EMFILE' });
      await new Promise(resolve => setTimeout(resolve, 1));
      return await original(...args);
    } finally { active--; }
  }) as typeof fs.writeFile;
  try {
    const data = new Uint8Array([0, 127, 255]);
    const results = await Promise.allSettled(Array.from({ length: 240 }, (_, i) =>
      writeBinaryFile(join(root, i % 11 === 0 ? 'missing/file' : `${i}.bin`), data)));
    assert.equal(results.filter(r => r.status === 'rejected').length, 22);
    assert.ok(peak <= 16);
    assert.equal(active, 0);
    await Promise.all(Array.from({ length: 240 }, async (_, i) => {
      if (i % 11 !== 0) assert.deepEqual(await readBinaryFile(join(root, `${i}.bin`)), Buffer.from(data));
    }));
    // Failed explicit opens must also release their permits.
    await Promise.all(Array.from({ length: 120 }, () => assert.rejects(openFileRead(join(root, 'absent')))));
    const handle = await openFileRead(join(root, '1.bin'));
    await closeFile(handle);
  } finally { fs.writeFile = original; await fs.rm(root, { recursive: true, force: true }); }
});

test('CLI errors expose the cause and path, with opt-in stack traces', () => {
  const error = Object.assign(new Error('too many open files'), { code: 'EMFILE', path: 'C:\\pasta com espaços\\arquivo.bin' });
  const normal = formatCliError(error);
  assert.match(normal, /ERRO \[EMFILE\]/);
  assert.ok(normal.includes(error.path));
  assert.match(normal, /não foi concluída/);
  assert.ok(!normal.includes(error.stack!));
  assert.ok(formatCliError(error, true).includes(error.stack!));
});
