"""Generates data/<game>/text_sources.json from the v2 game description.

The v2 file (old_versions/v2/game/<game>/iso.json) describes every file of
the ISO. The translation only needs the files that hold text, so this keeps
just the path to each one: CPK entry -> containers -> script.

Usage: python scripts/gen_text_sources.py [is|ep]   (default: is)
"""

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
GAME = sys.argv[1] if len(sys.argv) > 1 else "is"
SOURCE = ROOT / "old_versions" / "v2" / "game" / GAME / "iso.json"
TARGET = ROOT / "data" / GAME / "text_sources.json"
CPK_PATH = "PSP_GAME/USRDIR/pack/P2PT_ALL.cpk"
TEXT_TYPES = {"efb", "pdemo"}
CONTAINERS = {"ear", "par", "bnp"}


def find(node, name):
    if node["path"] == name:
        return node
    for child in node.get("fileList", []):
        found = find(child, name)
        if found:
            return found
    return None


def step(node, index):
    item = {"name": node["path"], "type": node["type"]}
    if index is not None:
        item["index"] = index
    for key in ("cpkId", "group", "bnpType", "compressed"):
        if key in node:
            item[key] = node[key]
    return item


def walk(node, chain, out):
    if node["type"] in TEXT_TYPES:
        out.append({"type": node["type"], "chain": chain})
        return
    if node["type"] not in CONTAINERS:
        return
    for index, child in enumerate(node.get("fileList", [])):
        walk(child, chain + [step(child, index)], out)


def main():
    iso = json.loads(SOURCE.read_text(encoding="utf-8"))
    cpk = find(iso, "P2PT_ALL.cpk")
    sources = []
    for entry in cpk["fileList"]:
        walk(entry, [step(entry, None)], sources)
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_text(
        json.dumps({"cpk": CPK_PATH, "sources": sources}, indent=1) + "\n",
        encoding="utf-8",
    )
    counts = {}
    for source in sources:
        counts[source["chain"][0]["name"]] = counts.get(source["chain"][0]["name"], 0) + 1
    print(f"{len(sources)} fontes de texto em {TARGET.relative_to(ROOT)}")
    for name, count in sorted(counts.items()):
        print(f"  {name}: {count}")


if __name__ == "__main__":
    main()
