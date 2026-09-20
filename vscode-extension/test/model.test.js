const { test } = require('node:test');
const assert = require('node:assert/strict');
const { parse, edit, BuildQueue } = require('../model');
const fixture = () => JSON.stringify({ header: { originalDirectory: 'test', originalFilename: 'script.msg', context: {}, extra: 'keep' }, translation: [{ info: { msg_key: 'msg_0', after_msg: '[end]' }, text: { before: 'Olá\nMundo\n', after: '' } }], extra: 42 }, null, 2);
test('line breaks, quotes, accents and literal backslashes survive JSON round trip', () => {
  const after = 'ação\n"Texto"\nLiteral \\n e \\ caminho\n';
  const result = edit(fixture(), { field: 'after', index: 0, previous: '', value: after });
  assert.equal(parse(result).translation[0].text.after, after);
  assert(result.includes('ação\\n\\"Texto\\"'));
  assert.equal(parse(result).header.extra, 'keep');
  assert.equal(parse(result).extra, 42);
  assert.equal(parse(result).translation[0].info.after_msg, '[end]');
});
test('both columns and context editable; protected fields and stale edits rejected', () => {
  const before = edit(fixture(), { field: 'before', index: 0, previous: 'Olá\nMundo\n', value: 'Novo\n' });
  assert.equal(parse(before).translation[0].text.before, 'Novo\n');
  const context = edit(before, { field: 'context', previous: '{}', value: '{"scene":"rua"}' });
  assert.deepEqual(parse(context).header.context, { scene: 'rua' });
  assert.throws(() => edit(context, { field: 'context', previous: '{}', value: '{}' }), /mudou/);
  assert.throws(() => edit(fixture(), { field: 'originalFilename', value: 'bad' }), /inválido/);
  assert.throws(() => edit(fixture(), { field: 'after', index: 0, previous: 'stale', value: 'bad' }), /mudou/);
  assert.throws(() => edit(fixture(), { field: 'context', previous: '{}', value: '[]' }), /objeto/);
});
test('invalid documents fail without regenerating them', () => {
  assert.throws(() => parse('{broken'));
  assert.throws(() => parse('{"header":{},"translation":[]}'));
});
test('build queue coalesces saves while building without parallel processes', async () => {
  let finish, count = 0, active = 0, max = 0;
  const queue = new BuildQueue(async () => {
    count++; max = Math.max(max, ++active);
    if (count === 1) await new Promise(resolve => { finish = resolve; });
    active--;
  }, error => { throw error; });
  const done = queue.request(); queue.request(); queue.request(); finish(); await done;
  assert.equal(count, 2); assert.equal(max, 1);
});
test('failed build is reported and stops pending automatic retries', async () => {
  let finish, error;
  const queue = new BuildQueue(async () => { await new Promise(resolve => { finish = resolve; }); throw new Error('failed'); }, e => { error = e; });
  const done = queue.request(); queue.request(); finish(); await done;
  assert.equal(error.message, 'failed'); assert.equal(queue.pending, false);
});
