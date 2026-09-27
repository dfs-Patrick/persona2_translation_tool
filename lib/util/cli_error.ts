/** User-facing CLI failures, without dumping yargs help or a stack by default. */
export function formatCliError(error: unknown, debug = false): string {
  const e = error instanceof Error ? error as NodeJS.ErrnoException : new Error(String(error)) as NodeJS.ErrnoException;
  const messages: Record<string, string> = {
    EMFILE: 'O sistema atingiu o limite de arquivos abertos.',
    ENFILE: 'O sistema atingiu o limite global de arquivos abertos.',
    ENOENT: 'Um arquivo ou diretório necessário não foi encontrado.',
    EACCES: 'Sem permissão para acessar o arquivo ou diretório.',
    EPERM: 'O sistema bloqueou o acesso ao arquivo ou diretório.',
    ENOSPC: 'Não há espaço livre suficiente no disco.',
    EBUSY: 'O arquivo está sendo usado por outro programa.',
  };
  const lines = [`\nERRO${e.code ? ` [${e.code}]` : ''}: ${messages[e.code ?? ''] ?? e.message}`];
  if (e.path) lines.push(`Arquivo: ${e.path}`);
  if (e.code === 'EMFILE' || e.code === 'ENFILE') {
    lines.push('Feche outras operações da ferramenta e tente novamente com a versão atualizada.');
  }
  if (e.code === 'ENOENT') lines.push('Confira o caminho informado e se o pacote foi extraído por completo.');
  if (['EACCES', 'EPERM', 'EBUSY'].includes(e.code ?? '')) lines.push('Feche o emulador e verifique as permissões da pasta.');
  lines.push('A operação não foi concluída. Os arquivos de saída podem estar incompletos.');
  lines.push('Se foi uma extração, use uma pasta de trabalho nova. Preserve suas traduções existentes.');
  if (debug) lines.push('\nDetalhes técnicos:', e.stack ?? String(error));
  else lines.push('Para detalhes técnicos, execute novamente com P2_DEBUG=1.');
  return lines.join('\n');
}
