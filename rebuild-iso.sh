#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source_dir="$project_dir/lab/p2is"
output_iso="$project_dir/lab/iso/p2is-mod.iso"

if [[ ! -d "$source_dir" ]]; then
  printf 'Diretorio extraido nao encontrado: %s\n' "$source_dir" >&2
  exit 1
fi

cd "$project_dir"
cli="$project_dir/dist/cli/iso.js"
if [[ ! -f "$cli" ]] || find "$project_dir/cli" "$project_dir/lib" "$project_dir/main.ts" -type f -newer "$cli" -print -quit | grep -q .; then
  npx tsc
fi

node "$cli" make "$source_dir" \
  --output "$output_iso" \
  --gameID ULUS-10584

printf '\nISO criada: %s\n' "$output_iso"

if [[ "${1:-}" == "--run" ]]; then
  exec PPSSPPSDL "$output_iso"
fi