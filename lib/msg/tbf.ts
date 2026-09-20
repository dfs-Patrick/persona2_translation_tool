import {
  basename,
  dirname,
  joinPath,
  mkdir,
  readDirWithTypes,
  readTextFile,
  writeTextFile,
} from "../util/filesystem";
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

const splitMessages = (text: string): TbfTranslation[] => {
  const lines = text.split("\n");
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
      body.push(lines[index]);
      const done = lines[index].trimEnd().endsWith("[end]");
      index++;
      if (done) break;
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
