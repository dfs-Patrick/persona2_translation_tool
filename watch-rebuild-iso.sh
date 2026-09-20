#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli="$project_dir/dist/cli/iso.js"

cd "$project_dir"
if [[ ! -f "$cli" ]] || find "$project_dir/cli" "$project_dir/lib" "$project_dir/main.ts" -type f -newer "$cli" -print -quit | grep -q .; then
  npx tsc
fi

exec node "$project_dir/watch-rebuild-iso.js"