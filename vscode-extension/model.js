'use strict';

function parse(text) {
  const value = JSON.parse(text.replace(/^\uFEFF/, ''));
  if (!value || !value.header || typeof value.header.originalDirectory !== 'string' ||
      typeof value.header.originalFilename !== 'string' || !Array.isArray(value.translation)) {
    throw new Error('TBF inválido: cabeçalho ou lista translation ausente.');
  }
  for (const row of value.translation) {
    if (!row || !row.info || typeof row.info.msg_key !== 'string' || !row.text ||
        typeof row.text.before !== 'string' || typeof row.text.after !== 'string') {
      throw new Error('TBF inválido: mensagem sem info.msg_key ou text.before/after.');
    }
  }
  return value;
}

// Only editable fields are accepted. Everything else, including unknown metadata,
// is preserved. JSON serialization escapes newlines exactly once.
function edit(text, change) {
  const value = parse(text);
  let object, key, next;
  if (change.field === 'context') {
    object = value.header;
    key = 'context';
    next = JSON.parse(change.value);
    if (!next || Array.isArray(next) || typeof next !== 'object') throw new Error('Contexto deve ser um objeto JSON.');
    if (JSON.stringify(object[key] ?? {}) !== change.previous) throw new Error('O contexto mudou em outro editor. Recarregue antes de editar.');
  } else {
    if (!Number.isInteger(change.index) || change.index < 0 || !['before', 'after'].includes(change.field) ||
        typeof change.value !== 'string' || !value.translation[change.index]) throw new Error('Campo de edição inválido.');
    object = value.translation[change.index].text;
    key = change.field;
    next = change.value;
    if (object[key] !== change.previous) throw new Error('A mensagem mudou em outro editor. Sua edição não foi aplicada.');
  }
  object[key] = next;
  const eol = text.includes('\r\n') ? '\r\n' : '\n';
  return JSON.stringify(value, null, 2).replace(/\n/g, eol) + eol;
}

class BuildQueue {
  constructor(run, onError) { this.run = run; this.onError = onError; this.pending = false; this.active = null; this.stopped = false; }
  request() {
    if (this.stopped) return Promise.resolve();
    this.pending = true;
    if (!this.active) this.active = this.drain().finally(() => { this.active = null; });
    return this.active;
  }
  async drain() {
    while (this.pending && !this.stopped) {
      this.pending = false;
      try { await this.run(); } catch (error) { this.pending = false; this.onError(error); }
    }
  }
  dispose() { this.stopped = true; this.pending = false; }
}
module.exports = { parse, edit, BuildQueue };
