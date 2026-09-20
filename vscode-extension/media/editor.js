'use strict';
const vscode = acquireVsCodeApi();
const byId = id => document.getElementById(id);
let serial = 0;
const pending = new Set();
function error(message) { byId('error').textContent = message; byId('error').hidden = !message; }
function submit(field, index, value, previous) {
  if (value === previous) return;
  const id = ++serial; pending.add(id);
  vscode.postMessage({ type: 'edit', id, field, index, value, previous });
}
function sizePair(row) {
  const areas = row.querySelectorAll('textarea');
  // The browser computes text wrapping; rows remain paired and equally tall.
  for (const area of areas) area.rows = 3;
  const lines = Math.max(...Array.from(areas, area => Math.ceil(area.scrollHeight / parseFloat(getComputedStyle(area).lineHeight))));
  for (const area of areas) area.rows = Math.max(3, lines);
}
function render(value, filename) {
  const active = document.activeElement;
  const activeKey = active?.dataset.key;
  const selection = active?.selectionStart;
  const end = active?.selectionEnd;
  const scroll = window.scrollY;
  byId('filename').textContent = filename;
  byId('directory').value = value.header.originalDirectory;
  byId('original').value = value.header.originalFilename;
  const context = byId('context');
  context.value = JSON.stringify(value.header.context ?? {}, null, 2);
  context.dataset.previous = JSON.stringify(value.header.context ?? {});
  const fragment = document.createDocumentFragment();
  value.translation.forEach((item, index) => {
    const row = document.createElement('section'); row.className = 'message-row';
    for (const field of ['before', 'after']) {
      const label = document.createElement('label');
      const caption = document.createElement('span'); caption.textContent = item.info.msg_key;
      const area = document.createElement('textarea');
      area.value = item.text[field]; area.dataset.key = `${index}:${field}`;
      area.setAttribute('aria-label', `${item.info.msg_key} — ${field === 'before' ? 'original' : 'tradução'}`);
      area.spellcheck = field === 'after'; area.rows = 3;
      if (field === 'after') area.placeholder = 'Vazio mantém o texto original';
      let previous = item.text[field];
      area.addEventListener('input', () => {
        submit(field, index, area.value, previous); previous = area.value;
        sizePair(row);
      });
      label.append(caption, area); row.append(label);
    }
    fragment.append(row);
  });
  if (!value.translation.length) {
    const empty = document.createElement('p'); empty.textContent = 'Este TBF não contém mensagens editáveis. Os diálogos ficam nos arquivos .msg.tbf.'; fragment.append(empty);
  }
  byId('messages').replaceChildren(fragment);
  document.querySelectorAll('.message-row').forEach(sizePair);
  const target = activeKey ? Array.from(document.querySelectorAll('textarea')).find(a => a.dataset.key === activeKey) : active?.id === 'context' ? context : null;
  if (target) { target.focus({ preventScroll: true }); target.setSelectionRange(selection, end); }
  window.scrollTo(0, scroll);
}
byId('context').addEventListener('change', event => {
  try {
    const value = JSON.parse(event.target.value);
    if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Contexto deve ser um objeto JSON.');
    submit('context', null, JSON.stringify(value), event.target.dataset.previous);
    event.target.dataset.previous = JSON.stringify(value); error('');
  } catch (e) { error(e.message); }
});
function save() {
  document.activeElement?.blur();
  if (!byId('error').hidden) return;
  vscode.postMessage({ type: 'save' });
}
byId('save').addEventListener('click', save);
byId('onsave').addEventListener('change', () => vscode.postMessage({ type: 'command', command: 'p2.toggleOnSave' }));
document.querySelectorAll('[data-command]').forEach(button => button.addEventListener('click', () => {
  document.activeElement?.blur();
  vscode.postMessage({ type: 'command', command: button.dataset.command });
}));
document.addEventListener('keydown', event => {
  if (!(event.ctrlKey || event.metaKey)) return;
  if (event.key.toLowerCase() === 's') { event.preventDefault(); save(); }
  if (['z', 'y'].includes(event.key.toLowerCase())) {
    event.preventDefault();
    vscode.postMessage({ type: event.shiftKey || event.key.toLowerCase() === 'y' ? 'redo' : 'undo' });
  }
});
window.addEventListener('message', event => {
  const message = event.data;
  if (message.type === 'document') { pending.clear(); render(message.value, message.filename); }
  else if (message.type === 'ack') { pending.delete(message.id); }
  else if (message.type === 'error') error(message.message);
  else if (message.type === 'status') { byId('status').textContent = message.message; byId('onsave').checked = message.onSave; }
});
new ResizeObserver(() => document.querySelectorAll('.message-row').forEach(sizePair)).observe(byId('messages'));
vscode.postMessage({ type: 'ready' });
