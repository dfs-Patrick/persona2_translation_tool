import { PNG } from "pngjs";
import { Locale, loadLocale } from "../util/encoding";
import { exists, fromTools, joinPath, readBinaryFile, readDir, readTextFile, writeBinaryFile } from "../util/filesystem";

export const resolveFontProfile = async (
  translationRoot: string, requested: string | undefined, game: string, variant: string
): Promise<{ name: string; path?: string }> => {
  const supported = game === "is" && variant === "us";
  const name = requested ?? (supported ? "pt-br" : "original");
  if (!/^[a-zA-Z0-9][a-zA-Z0-9_-]*$/.test(name)) throw new Error(`Invalid font profile name: ${name}`);
  if (name === "original") return { name };
  if (!supported) throw new Error("Font profiles currently support Innocent Sin US only");

  const bundled = fromTools("fonts", name);
  // An implicit default always uses the tested, bundled profile. Explicit names
  // allow a project-local profile to override a bundled one.
  const candidates = requested
    ? [joinPath(translationRoot, "fonts", "new", name), bundled]
    : [bundled];
  for (const path of candidates) {
    if (await exists(path)) return { name, path };
  }
  throw new Error(`Font profile ${JSON.stringify(name)} not found. Searched: ${candidates.join(", ")}`);
};

export interface FontMapping {
  character: string;
  event: number;
  font: number;
}

// IS US: estr -> fstr table, identified by its original first 32 entries.
// Do not use a fixed ELF offset: unsupported executables must fail closed.
const tablePrefix = [0x35, 0x36, 1, 5, 0x112b, 0x5ea, 0xe31, 0x9cf,
  0x9ec, 0x8dc, 0x115a, 0xa35, 0xeaa, 0x823, 0xa9c, 0x120,
  0x122, 0x124, 0x126, 0x128, 0x129, 0x12b, 0x12d, 0x12f,
  0x131, 0x133, 0x135, 0x137, 0x139, 0x13b, 0x13d, 0x13f];

export const findEventFontTable = (eboot: Uint8Array): number => {
  const data = Buffer.from(eboot);
  if (data.length < 4 || data.readUInt32LE(0) !== 0x464c457f) throw new Error("Font patch requires a decrypted ELF");
  const signature = Buffer.alloc(tablePrefix.length * 2);
  tablePrefix.forEach((v, i) => signature.writeUInt16LE(v, i * 2));
  const offset = data.indexOf(signature);
  if (offset < 0 || data.indexOf(signature, offset + 1) >= 0 || offset + 0x2000 > data.length) {
    throw new Error("Cannot identify the IS US event/font conversion table uniquely");
  }
  return offset;
};

export const patchEventFontTable = (eboot: Uint8Array, mappings: FontMapping[]) => {
  const offset = findEventFontTable(eboot);
  const view = new DataView(eboot.buffer, eboot.byteOffset, eboot.byteLength);
  for (const mapping of mappings) {
    // The English renderer substitutes @ for font positions >= 0x501.
    if (!Number.isInteger(mapping.event) || mapping.event < 32 || mapping.event >= 0x1000 ||
        !Number.isInteger(mapping.font) || mapping.font < 0 || mapping.font >= 0x501) {
      throw new Error(`Unsupported IS US font mapping for ${JSON.stringify(mapping.character)}`);
    }
  }
  for (const mapping of mappings) view.setUint16(offset + mapping.event * 2, mapping.font, true);
  return offset;
};

export const findFontMetricTables = (eboot: Uint8Array) => {
  const data = Buffer.from(eboot);
  const find = (signature: string, size: number) => {
    const bytes = Buffer.from(signature, "hex");
    const offset = data.indexOf(bytes);
    if (offset < 0 || data.indexOf(bytes, offset + 1) >= 0 || offset + size > data.length) {
      throw new Error("Cannot identify the IS US font metric tables uniquely");
    }
    return offset;
  };
  return {
    // Each normal glyph has two bytes: texture X offset and visible width.
    regular: find("00080206050506040603060306030603020b0308010602050704070506060606", 0x500 * 2),
    // Narrow glyphs store the same fields as two little-endian 32-bit integers.
    narrow: find("0000000008000000010000000400000001000000040000000100000004000000", 0x11f * 8),
  };
};

export const glyphBounds = (image: PNG, code: number) => {
  if (image.width !== 256 || image.height !== 256) throw new Error("Expected a 256x256 font page");
  let left = 16, right = -1;
  for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) {
    const offset = (((code >> 4 & 15) * 16 + y) * 256 + (code & 15) * 16 + x) * 4;
    if (image.data[offset + 3] > 0) {
      left = Math.min(left, x);
      right = Math.max(right, x);
    }
  }
  if (right < left) throw new Error(`Empty glyph at font position ${code.toString(16)}`);
  return { left, width: right - left + 1 };
};

export const patchFontMetrics = async (eboot: Uint8Array, imagesPath: string, mappings: FontMapping[]) => {
  const tables = findFontMetricTables(eboot);
  const images = new Map<number, PNG>();
  const updates: { offset: number; narrow: boolean; left: number; width: number }[] = [];
  for (const code of new Set(mappings.map(mapping => mapping.font))) {
    if (!Number.isInteger(code) || code < 0 || code >= 0x500) throw new Error(`Unsupported metric position ${code}`);
    for (const narrow of [false, true]) {
      // The narrow renderer uses its own metrics only below 0x11f.
      if (narrow && code >= 0x11f) continue;
      const page = (narrow ? 12 : 7) + (code >> 8);
      if (!images.has(page)) {
        images.set(page, PNG.sync.read(Buffer.from(await readBinaryFile(joinPath(imagesPath, `${page}.png`)))));
      }
      updates.push({
        offset: (narrow ? tables.narrow : tables.regular) + code * (narrow ? 8 : 2),
        narrow, ...glyphBounds(images.get(page)!, code),
      });
    }
  }
  // Validate all pages before changing the executable.
  const view = new DataView(eboot.buffer, eboot.byteOffset, eboot.byteLength);
  for (const update of updates) {
    if (update.narrow) {
      view.setUint32(update.offset, update.left, true);
      view.setUint32(update.offset + 4, update.width, true);
    } else {
      view.setUint8(update.offset, update.left);
      view.setUint8(update.offset + 1, update.width);
    }
  }
  return tables;
};

export const loadFontProfile = async (path: string, original: Locale) => {
  for (const file of ["event.json", "font.json"]) {
    if (!await exists(joinPath(path, file))) throw new Error(`Font profile is missing ${joinPath(path, file)}`);
  }
  const locale = await loadLocale(path);
  const mappings: FontMapping[] = [];
  for (const [code, character] of Object.entries(locale.event.bin2utf)) {
    const event = Number(code);
    if (!Number.isInteger(event) || event < 0 || event > 0xffff) {
      throw new Error(`Invalid event code for ${JSON.stringify(character)}`);
    }
    if (event >= 0x1000) {
      // Preserve existing commands (including space), but never assign glyphs to them.
      if (character !== original.event.bin2utf[event]) {
        throw new Error(`Font profile: ${JSON.stringify(character)} uses reserved event command ${event.toString(16)}`);
      }
      continue;
    }
    const font = locale.font.utf2bin[character];
    if (character === original.event.bin2utf[event] && font === original.font.utf2bin[character]) continue;
    if (!Number.isInteger(font) || font < 0 || font >= 0x501 || event < 32) {
      throw new Error(`Font profile: ${JSON.stringify(character)} has no supported font position (0000–0500)`);
    }
    mappings.push({ character, event, font });
  }
  return { locale, mappings };
};

interface GlyphRecipe {
  target: number;
  base: number;
  accent: number;
  accentRows: number;
}

// Optional recipes compose missing letters from the profile's existing pixels.
// Page numbers in recipes are font coordinates; IS US stores page 0 in 7.gim
// and its narrow counterpart in 12.gim.
export const prepareFontImages = async (profile: string, output: string, mappings: FontMapping[]) => {
  const images = new Map<number, PNG>();
  for (const name of await readDir(profile)) {
    if (/^\d+\.png$/i.test(name)) images.set(parseInt(name), PNG.sync.read(Buffer.from(await readBinaryFile(joinPath(profile, name)))));
  }
  const recipesPath = joinPath(profile, "glyphs.json");
  const recipes: GlyphRecipe[] = await exists(recipesPath) ? JSON.parse(await readTextFile(recipesPath)) : [];
  const page = (code: number, start: number) => {
    const image = images.get(start + (code >> 8));
    if (!image || image.width !== 256 || image.height !== 256) throw new Error(`Font profile requires a 256x256 page ${start + (code >> 8)}.png`);
    return image;
  };
  const pixel = (code: number, x: number, y: number) => (((code >> 4 & 15) * 16 + y) * 256 + (code & 15) * 16 + x) * 4;
  for (const recipe of recipes) {
    if (![recipe.target, recipe.base, recipe.accent].every(n => Number.isInteger(n) && n >= 0 && n < 0x200) ||
        !Number.isInteger(recipe.accentRows) || recipe.accentRows < 1 || recipe.accentRows > 5) {
      throw new Error("Invalid glyph recipe: expected positions 0000–01ff and 1–5 accent rows");
    }
    for (const start of [7, 12]) {
      const target = page(recipe.target, start), base = page(recipe.base, start), accent = page(recipe.accent, start);
      const glyph = Buffer.alloc(16 * 16 * 4);
      for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) {
        const source = y < recipe.accentRows ? accent : base;
        const code = y < recipe.accentRows ? recipe.accent : recipe.base;
        source.data.copy(glyph, (y * 16 + x) * 4, pixel(code, x, y), pixel(code, x, y) + 4);
      }
      for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) {
        glyph.copy(target.data, pixel(recipe.target, x, y), (y * 16 + x) * 4, (y * 16 + x + 1) * 4);
      }
    }
  }
  for (const mapping of mappings) {
    const image = page(mapping.font, 7);
    let visible = false;
    for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) {
      visible ||= image.data[pixel(mapping.font, x, y) + 3] > 0;
    }
    if (!visible) throw new Error(`Empty glyph for ${JSON.stringify(mapping.character)} at font position ${mapping.font.toString(16)}`);
  }
  for (const [number, image] of images) await writeBinaryFile(joinPath(output, `${number}.png`), PNG.sync.write(image));
  return [...images.keys()];
};
