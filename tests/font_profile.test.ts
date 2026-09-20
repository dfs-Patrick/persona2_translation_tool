import assert from "node:assert/strict";
import { test } from "node:test";
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PNG } from "pngjs";
import { findEventFontTable, loadFontProfile, patchEventFontTable, patchFontMetrics, glyphBounds, prepareFontImages, resolveFontProfile } from "../lib/mod/font_profile";
import { loadEventEncoding, loadFontEncoding, EncodingScheme, loadLocale } from "../lib/util/encoding";
import { fromTools } from "../lib/util/filesystem";
import { messageToBin, parseMessage } from "../lib/msg/msg";
import { Game } from "../lib/util/context";
import { validateTbfEncoding } from "../lib/msg/tbf";

const prefix = [0x35, 0x36, 1, 5, 0x112b, 0x5ea, 0xe31, 0x9cf,
  0x9ec, 0x8dc, 0x115a, 0xa35, 0xeaa, 0x823, 0xa9c, 0x120,
  0x122, 0x124, 0x126, 0x128, 0x129, 0x12b, 0x12d, 0x12f,
  0x131, 0x133, 0x135, 0x137, 0x139, 0x13b, 0x13d, 0x13f];
const executable = () => {
  const data = Buffer.alloc(0x3000);
  data.writeUInt32LE(0x464c457f);
  prefix.forEach((v, i) => data.writeUInt16LE(v, 0x100 + i * 2));
  data.writeUInt16LE(0x115c, 0x100 + 0xa0d * 2);
  return data;
};
const locale = { event: loadEventEncoding({ "0040": "@", "0a0d": "é" }), font: loadFontEncoding({}) };
const context = { game: Game.IS, locale, constants: {}, file: "dialogue.msg", base: 0,
  encoding: EncodingScheme.event, terminator: 0x1103, strictEncoding: true };

test("default uses the bundled PT-BR profile even with a project-local copy", async () => {
  const dir = await mkdtemp(join(tmpdir(), "p2-profile-test-"));
  try {
    const local = join(dir, "fonts", "new", "pt-br");
    await mkdir(local, { recursive: true });
    assert.deepEqual(await resolveFontProfile(dir, undefined, "is", "us"), {
      name: "pt-br", path: fromTools("fonts", "pt-br"),
    });
    assert.deepEqual(await resolveFontProfile(dir, "pt-br", "is", "us"), { name: "pt-br", path: local });
    await mkdir(join(dir, "fonts", "new", "custom"));
    assert.equal((await resolveFontProfile(dir, "custom", "is", "us")).path, join(dir, "fonts", "new", "custom"));
    assert.deepEqual(await resolveFontProfile(dir, "original", "is", "us"), { name: "original" });
    assert.deepEqual(await resolveFontProfile(dir, undefined, "ep", "us"), { name: "original" });
    assert.deepEqual(await resolveFontProfile(dir, undefined, "is", "eu"), { name: "original" });
    await assert.rejects(resolveFontProfile(dir, "missing", "is", "us"), /not found/);
    await assert.rejects(resolveFontProfile(dir, "pt-br", "is", "eu"), /Innocent Sin US only/);
    await assert.rejects(resolveFontProfile(dir, "../pt-br", "is", "us"), /Invalid/);
  } finally { await rm(dir, { recursive: true }); }
});

test("bundled profile renders all 30 accents without lab or an ISO", async () => {
  const dir = await mkdtemp(join(tmpdir(), "p2-bundled-test-"));
  try {
    const selected = await resolveFontProfile(dir, "pt-br", "is", "us");
    const profile = await loadFontProfile(selected.path!, await loadLocale(fromTools("game/is/encoding/en")));
    assert.equal(profile.mappings.length, 30);
    assert.deepEqual((await prepareFontImages(selected.path!, dir, profile.mappings)).sort((a, b) => a - b), [7, 8, 12, 13]);
    for (const mapping of profile.mappings) {
      for (const start of [7, 12]) {
        const image = PNG.sync.read(await readFile(join(dir, `${start + (mapping.font >> 8)}.png`)));
        assert(glyphBounds(image, mapping.font).width > 0, mapping.character);
      }
    }
  } finally { await rm(dir, { recursive: true }); }
});

test("dialogue bytes reach the accent glyph through the patched executable table", () => {
  const data = executable(), original = Buffer.from(data);
  const offset = patchEventFontTable(data, [{ character: "é", event: 0xa0d, font: 0x71 }]);
  const words = messageToBin(parseMessage("é[end]", context), context);
  assert.deepEqual(words, [0xa0d, 0x1103]);
  assert.equal(data.readUInt16LE(offset + words[0] * 2), 0x71);
  original.writeUInt16LE(0x71, offset + 0xa0d * 2);
  assert.deepEqual(data, original, "other executable bytes must remain intact");
});

test("unsupported glyph positions and unrecognized executables fail before mutation", () => {
  const data = executable(), original = Buffer.from(data);
  assert.throws(() => patchEventFontTable(data, [{ character: "é", event: 0xa0d, font: 0xa0d }]), /Unsupported/);
  assert.deepEqual(data, original);
  assert.throws(() => findEventFontTable(Buffer.alloc(0x3000)), /decrypted ELF/);
  prefix.forEach((v, i) => data.writeUInt16LE(v, 0x200 + i * 2));
  assert.throws(() => findEventFontTable(data), /uniquely/);
});

test("missing characters cannot silently become @ in strict builds", () => {
  assert.throws(() => messageToBin(parseMessage("É[end]", context), context), /É.*U\+C9.*dialogue.msg/);
});

test("TBF validation reports file and message and uses before when after is empty", async () => {
  const dir = await mkdtemp(join(tmpdir(), "p2-font-test-"));
  try {
    const file = join(dir, "0000.msg.tbf");
    const tbf = { header: { originalFilename: "script.msg" }, translation: [{
      info: { msg_key: "msg_1", after_msg: "[wait][end]" }, text: { before: "é", after: "" },
    }] };
    await writeFile(file, JSON.stringify(tbf));
    await validateTbfEncoding(dir, context);
    tbf.translation[0].text.after = "É";
    await writeFile(file, JSON.stringify(tbf));
    await assert.rejects(validateTbfEncoding(dir, context), /0000.msg.tbf.*msg_1.*É/);
    await assert.rejects(loadFontProfile(dir, locale), /missing.*event.json/);
    await writeFile(join(dir, "event.json"), JSON.stringify({ "1101": "é" }));
    await writeFile(join(dir, "font.json"), "{}");
    await assert.rejects(loadFontProfile(dir, locale), /reserved event command/);
  } finally { await rm(dir, { recursive: true }); }
});

test("glyph recipes preserve source PNGs and compose normal and narrow glyphs", async () => {
  const dir = await mkdtemp(join(tmpdir(), "p2-glyph-test-"));
  try {
    const out = join(dir, "output");
    await mkdir(out);
    for (const page of [7, 8, 12, 13]) {
      const image = new PNG({ width: 256, height: 256 });
      image.data.fill(0);
      const position = page === 7 || page === 12 ? (2 * 256 + 2) * 4 : (8 * 256 + 3) * 4;
      image.data.fill(255, position, position + 4);
      await writeFile(join(dir, `${page}.png`), PNG.sync.write(image));
    }
    const original = await readFile(join(dir, "7.png"));
    await writeFile(join(dir, "glyphs.json"), JSON.stringify([{ target: 1, base: 256, accent: 0, accentRows: 5 }]));
    await prepareFontImages(dir, out, [{ character: "è", event: 0xa0c, font: 1 }]);
    for (const page of [7, 12]) {
      const image = PNG.sync.read(await readFile(join(out, `${page}.png`)));
      assert.equal(image.data[(2 * 256 + 16 + 2) * 4 + 3], 255);
      assert.equal(image.data[(8 * 256 + 16 + 3) * 4 + 3], 255);
      assert.equal(image.data[(8 * 256 + 16 + 2) * 4 + 3], 0);
    }
    assert.deepEqual(await readFile(join(dir, "7.png")), original);
  } finally { await rm(dir, { recursive: true }); }
});

test("accent metrics include the whole glyph and give each following letter enough room", async () => {
  const dir = await mkdtemp(join(tmpdir(), "p2-metrics-test-"));
  try {
    const data = Buffer.alloc(0x4000);
    Buffer.from("00080206050506040603060306030603020b0308010602050704070506060606", "hex").copy(data, 0x100);
    Buffer.from("0000000008000000010000000400000001000000040000000100000004000000", "hex").copy(data, 0x1000);
    data[0x100 + 0x70 * 2] = 6;
    data[0x100 + 0x70 * 2 + 1] = 3; // Old placeholder clipped ã to three pixels.
    const before = Buffer.from(data);
    for (const page of [7, 12]) {
      const image = new PNG({ width: 256, height: 256 });
      image.data.fill(0);
      const right = page === 7 ? 11 : 7;
      for (let x = 3; x <= right; x++) {
        image.data[((7 * 16 + 8) * 256 + x) * 4 + 3] = 255;
      }
      // The accent itself contributes to the bounds, even with low-alpha edges.
      image.data[((7 * 16 + 2) * 256 + 3) * 4 + 3] = 17;
      assert.deepEqual(glyphBounds(image, 0x70), { left: 3, width: right - 2 });
      await writeFile(join(dir, `${page}.png`), PNG.sync.write(image));
    }
    const mapping = [{ character: "ã", event: 0xa0a, font: 0x70 }];
    const tables = await patchFontMetrics(data, dir, mapping);
    assert.equal(data[tables.regular + 0x70 * 2], 3);
    assert.equal(data[tables.regular + 0x70 * 2 + 1], 9);
    assert.equal(data.readUInt32LE(tables.narrow + 0x70 * 8), 3);
    assert.equal(data.readUInt32LE(tables.narrow + 0x70 * 8 + 4), 5);
    const advance = data[tables.regular + 0x70 * 2 + 1] + 1;
    assert.equal(advance, 10, "the renderer adds one pixel after the visible width");
    before[tables.regular + 0x70 * 2] = 3;
    before[tables.regular + 0x70 * 2 + 1] = 9;
    before.writeUInt32LE(3, tables.narrow + 0x70 * 8);
    before.writeUInt32LE(5, tables.narrow + 0x70 * 8 + 4);
    assert.deepEqual(data, before, "unrelated glyph metrics must stay unchanged");
    await rm(join(dir, "12.png"));
    await assert.rejects(patchFontMetrics(data, dir, mapping));
    assert.deepEqual(data, before, "a missing font page must not partially patch metrics");
  } finally { await rm(dir, { recursive: true }); }
});
