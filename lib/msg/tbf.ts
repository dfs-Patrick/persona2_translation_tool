import {
  basename,
  dirname,
  exists,
  joinPath,
  mkdir,
  readDirWithTypes,
  readTextFile,
  writeTextFile,
} from "../util/filesystem";
import { rm } from "fs/promises";
import { relative } from "path";
import { GameContext } from "../util/context";
import { EncodingScheme } from "../util/encoding";
import { parseMessage } from "./msg";

export interface TbfHeader {
  originalDirectory: string;
  originalFilename: string;
  context: Record<string, unknown>;
}

export interface TbfTranslation {
  info: {
    msg_key: string;
    before_msg: string;
    after_msg: string;
  };
  text: {
    before: string;
    after: string;
  };
}

export interface TbfFile {
  header: TbfHeader;
  translation: TbfTranslation[];
}

const walk = async (directory: string): Promise<string[]> => {
  const files: string[] = [];
  for (const entry of await readDirWithTypes(directory)) {
    const path = joinPath(directory, entry.name);
    if (entry.isFile) files.push(path);
    else files.push(...(await walk(path)));
  }
  return files;
};

export const splitMessages = (text: string): TbfTranslation[] => {
  const lines = text.replaceAll("\r\n", "\n").split("\n");
  const translations: TbfTranslation[] = [];
  let index = 0;

  while (index < lines.length) {
    const match = lines[index].match(/^([^:#][^:]*)\s*:\s*$/);
    if (!match) {
      index++;
      continue;
    }

    const key = match[1].trim();
    index++;
    const body: string[] = [];
    while (index < lines.length) {
      if (/^([^:#][^:]*)\s*:\s*$/.test(lines[index])) break;
      body.push(lines[index]);
      index++;
    }

    let before = body.join("\n");
    let afterMessage = "";
    const suffix = before.match(/((?:\[[^\]\r\n]+\])+\s*)$/);
    if (suffix) {
      afterMessage = suffix[1];
      before = before.slice(0, -afterMessage.length);
    }

    translations.push({
      info: {
        msg_key: key,
        before_msg: `${key}:`,
        after_msg: afterMessage,
      },
      text: {
        before,
        after: "",
      },
    });
  }

  return translations;
};

const afterColors: Record<number, string> = {
  0x11: "white1", 0x12: "white", 0x13: "lime2", 0x14: "yellow",
  0x15: "teal", 0x16: "fuschia", 0x17: "light_gray", 0x18: "green",
  0x19: "black", 0x20: "blue", 0x21: "pink", 0x32: "name_green",
};

const afterSymbols: Record<number, string> = {
  1: "unk1", 2: "unk2", 3: "unk3", 4: "unk4", 5: "unk5", 6: "unk6",
  7: "heart", 8: "unk8", 9: "unk9", 10: "unka", 11: "unkb",
  12: "unkc", 13: "unkd", 14: "unke", 15: "unkf", 16: "unk10",
  17: "unk11", 18: "unk12", 19: "lquote", 20: "rqoute", 21: "unk15",
};

export const normalizeAfterText = (text: string): string => text
  .replaceAll("[end_diag][wait]", "[wait][clear]")
  .replaceAll("[end_diag]", "")
  .replace(
  /\[([0-9a-f]+)(?:\(([^\]]*)\))?\]/gi,
  (_full, rawOpcode: string, rawArgs = "") => {
    const opcode = Number.parseInt(rawOpcode, 16);
    const args = rawArgs.split(",").map((value: string) => value.trim()).filter(Boolean);
    if (opcode === 0x31 && args.length === 3 && args[0] === "0" && args[1] === "0") {
      return afterColors[Number(args[2]) + 0x10] ? `[color(${afterColors[Number(args[2]) + 0x10]})]` : "";
    }
    if (opcode === 0x1d && args.length === 1 && afterColors[Number(args[0])]) {
      return `[color(${afterColors[Number(args[0])]})]`;
    }
    if (opcode === 0x32 && args.length === 3 && args[0] === "0" && args[1] === "0") {
      return afterSymbols[Number(args[2])] ? `[sym(${afterSymbols[Number(args[2])]})]` : "";
    }
    const names: Record<number, string> = {
      0x05: "delay", 0x06: "wait", 0x07: "sync", 0x08: "choice",
      0x09: "end_choice", 0x12: "tatsu", 0x13: "tatsuya", 0x14: "suou",
      0x1f: "dbl_tab", 0x20: "space", 0x21: "half_tab",
    };
    if (names[opcode]) return args.length ? `[${names[opcode]}(${args.join(", ")})]` : `[${names[opcode]}]`;
    return "";
  },
  );

const splitScript = (text: string): TbfTranslation[] => {
  const translations: TbfTranslation[] = [];
  const block = /\/\*+\n([\s\S]*?)\n\*+\//g;
  let cursor = 0;
  let match: RegExpExecArray | null;
  let index = 0;

  while ((match = block.exec(text)) !== null) {
    const body = match[1];
    const beforeBlock = text.slice(cursor, match.index);
    const msgMatch = beforeBlock.match(/msgShow\(([^)]+)\);[^\n]*\s*$/);
    const bodyStart = match[0].indexOf(body);
    const bodyEnd = bodyStart + body.length;
    translations.push({
      info: {
        msg_key: msgMatch?.[1] ?? `script_${index}`,
        before_msg: beforeBlock + match[0].slice(0, bodyStart),
        after_msg: match[0].slice(bodyEnd),
      },
      text: {
        before: body,
        after: "",
      },
    });
    cursor = match.index + match[0].length;
    index++;
  }

  if (!translations.length) return [];
  translations[translations.length - 1].info.after_msg += text.slice(cursor);
  return translations;
};

const renderMessages = (file: TbfFile): string =>
  file.translation
    .map((entry) => {
      const text = entry.text.after.trim().length
        ? entry.text.after
        : entry.text.before;
      return `${entry.info.before_msg}\n${text}${entry.info.after_msg}`;
    })
    .join("\n");

const afterId = (name: string, extension: string): string | undefined => {
  const match = name.match(new RegExp(`^(e[0-9a-f]+)\\${extension}$`, "i"));
  return match?.[1].toLowerCase();
};

const afterTbfFiles = async (translationRoot: string): Promise<string[]> => {
  const files = await walk(joinPath(translationRoot, "new", "messages"));
  return files.filter(source => source.endsWith(".tbf"));
};

const afterTarget = (file: TbfFile, id: string, script: boolean): boolean =>
  file.header.originalDirectory.toLowerCase().includes(`/${id}.bin$`) &&
  (script ? file.header.originalFilename === "script.ef" : file.header.originalFilename === "script.msg");

export const applyAfterTranslations = async (translationRoot: string): Promise<number> => {
  const files = await afterTbfFiles(translationRoot);
  const targets = await Promise.all(files.map(async source => ({
    source,
    file: parseTbf(await readTextFile(source)),
  })));
  let applied = 0;

  for (const directory of ["msg", "scripts"]) {
    const inputRoot = joinPath(translationRoot, "after", directory);
    if (!(await exists(inputRoot))) continue;
    const entries = await readDirWithTypes(inputRoot);
    for (const entry of entries) {
      if (!entry.isFile) continue;
      const script = directory === "scripts";
      const extension = script ? ".script" : ".msg";
      const id = afterId(entry.name, extension);
      if (!id) continue;
      const source = joinPath(inputRoot, entry.name);
      const translated = script
        ? splitScript(await readTextFile(source))
        : splitMessages(await readTextFile(source));
      const matches = targets.filter(target => afterTarget(target.file, id, script));
      if (matches.length !== 1) continue;
      const target = matches[0].file;
      const byKey = new Map(translated.map(item => [item.info.msg_key, normalizeAfterText(item.text.before)]));
      for (const item of target.translation) {
        const text = byKey.get(item.info.msg_key);
        if (text !== undefined) { item.text.after = text; applied++; }
      }
      await writeTextFile(matches[0].source, writeTbf(target));
    }
  }
  return applied;
};

export const clearGeneratedAfter = async (translationRoot: string): Promise<void> => {
  const root = joinPath(translationRoot, "after");
  if (!(await exists(root))) return;
  for (const entry of await readDirWithTypes(root)) {
    if (entry.name !== "msg" && entry.name !== "scripts") {
      await rm(joinPath(root, entry.name), { recursive: true, force: true });
    }
  }
};

export const parseTbf = (text: string): TbfFile =>
  JSON.parse(text) as TbfFile;

export const writeTbf = (file: TbfFile): string =>
  `${JSON.stringify(file, null, 2)}\n`;

export const exportTbfFiles = async (sourceRoot: string, outputRoot: string) => {
  const sources = (await walk(sourceRoot))
    .filter(
      (source) =>
        source.endsWith(".msg") ||
        source.endsWith(".ef") ||
        source.endsWith(".cf")
    )
    .sort();

  for (let index = 0; index < sources.length; index++) {
    const source = sources[index];
    const originalPath = relative(sourceRoot, source);
    const output = joinPath(
      outputRoot,
      `${index.toString().padStart(4, "0")}_${basename(originalPath)}.tbf`
    );
    const originalDirectory = dirname(originalPath).replaceAll("\\", "/");
    const file: TbfFile = {
      header: {
        originalDirectory,
        originalFilename: basename(originalPath),
        context: {},
      },
      translation: source.endsWith(".msg")
        ? splitMessages(await readTextFile(source))
        : splitScript(await readTextFile(source)),
    };
    await mkdir(outputRoot);
    await writeTextFile(output, writeTbf(file));
  }
};

export const exportScriptFiles = async (sourceRoot: string, outputRoot: string) => {
  const sources = (await walk(sourceRoot))
    .filter((source) => source.endsWith(".ef") || source.endsWith(".cf"))
    .sort();
  await mkdir(outputRoot);
  for (let index = 0; index < sources.length; index++) {
    const source = sources[index];
    const originalPath = relative(sourceRoot, source);
    const extension = originalPath.endsWith(".ef") ? ".ef" : ".cf";
    await writeTextFile(
      joinPath(outputRoot, `${index.toString().padStart(4, "0")}${extension}`),
      await readTextFile(source)
    );
  }
};

export const exportMessageFiles = async (sourceRoot: string, outputRoot: string) => {
  const sources = (await walk(sourceRoot))
    .filter(
      (source) =>
        source.endsWith(".msg") ||
        source.endsWith(".ef") ||
        source.endsWith(".cf")
    )
    .sort();
  for (const source of sources) {
    const originalPath = relative(sourceRoot, source);
    const output = joinPath(outputRoot, originalPath);
    await mkdir(dirname(output));
    await writeTextFile(output, await readTextFile(source));
  }
};

export const importTbfFiles = async (sourceRoot: string, outputRoot: string) => {
  const sources = (await walk(sourceRoot))
    .filter((source) => source.endsWith(".tbf"))
    .sort();
  for (const source of sources) {
    const file = parseTbf(await readTextFile(source));
    // An old export could not retain scripts without message comments.
    // Leave these scripts in the original archive instead of replacing code with "".
    if (!file.translation.length && /\.(ef|cf)$/.test(file.header.originalFilename)) continue;
    const output = joinPath(
      outputRoot,
      file.header.originalDirectory,
      file.header.originalFilename
    );
    await mkdir(dirname(output));
    const render = file.header.originalFilename.endsWith(".msg")
      ? renderMessages(file)
      : file.translation
          .map((entry) => {
            const text = entry.text.after.trim().length
              ? entry.text.after
              : entry.text.before;
            return `${entry.info.before_msg}${text}${entry.info.after_msg}`;
          })
          .join("");
    await writeTextFile(output, render);
  }
};

export const validateTbfEncoding = async (sourceRoot: string, context: GameContext) => {
  const errors: string[] = [];
  for (const source of (await walk(sourceRoot)).filter(p => p.endsWith(".tbf"))) {
    const file = parseTbf(await readTextFile(source));
    if (!file.header.originalFilename.endsWith(".msg")) continue;
    for (const entry of file.translation) {
      const text = entry.text.after.trim() ? entry.text.after : entry.text.before;
      const message = parseMessage(text + entry.info.after_msg, {
        ...context, file: source, base: 0, terminator: 0x1103, encoding: EncodingScheme.event,
      });
      const missing = new Set(message.data.flatMap(value => typeof value === "string"
        ? [...value].filter(c => context.locale.event.utf2bin[c] === undefined) : []));
      if (missing.size) errors.push(`${source} (${entry.info.msg_key}): ${[...missing].map(c => JSON.stringify(c)).join(", ")}`);
    }
  }
  if (errors.length) throw new Error(`Characters missing from the selected font profile:\n${errors.join("\n")}`);
};
