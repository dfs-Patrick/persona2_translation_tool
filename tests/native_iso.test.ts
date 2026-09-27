import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createIso, readTOC, extractFile } from '../lib/iso/iso';
import { openFileRead, openFileWrite, closeFile } from '../lib/util/filesystem';

test('ISO round trip in a workspace with spaces and Unicode', async () => {
  const root = await mkdtemp(join(tmpdir(), 'p2 tradução '));
  try {
    const input = join(root, 'original files');
    await mkdir(join(input, 'PSP_GAME', 'USRDIR'), { recursive: true });
    const bytes = Buffer.alloc(70001);
    for (let i = 0; i < bytes.length; i++) bytes[i] = i % 251;
    await writeFile(join(input, 'PSP_GAME', 'USRDIR', 'TEST.BIN'), bytes);
    await writeFile(join(input, 'UMD_DATA.BIN'), 'test');
    const iso = join(root, 'test.iso');
    const output = await openFileWrite(iso);
    try { await createIso(output, input, {}); }
    finally { await closeFile(output); }
    const source = await openFileRead(iso);
    try { await extractFile(source, await readTOC(source), join(root, 'extracted')); }
    finally { await closeFile(source); }
    assert.deepEqual(await readFile(join(root, 'extracted', 'PSP_GAME', 'USRDIR', 'TEST.BIN')), bytes);
    assert.equal(await readFile(join(root, 'extracted', 'UMD_DATA.BIN'), 'utf8'), 'test');
  } finally { await rm(root, { recursive: true, force: true }); }
});
