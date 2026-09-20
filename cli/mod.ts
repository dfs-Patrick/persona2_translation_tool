#!/usr/bin/env node
import yargs from "yargs";
import { hideBin } from "yargs/helpers";
import { rm } from "fs/promises";
import * as vm2 from "vm2";
import {
  MIPS,
  RelocInfo,
  RelocList,
  exportedFuncs,
  freg,
  ireg,
} from "../lib/mips/mips";
import {
  basename,
  copyFile,
  closeFile,
  dirname,
  exists,
  fromTools,
  joinPath,
  mkdir,
  openFileWrite,
  openFileRead,
  readBinaryFile,
  readBinaryFileSync,
  readDir,
  readDirRecursive,
  readDirSync,
  readTextFile,
  readTextFileSync,
  withoutExtension,
  writeBinaryFile,
  writeTextFile,
} from "../lib/util/filesystem";
import { parseElf } from "../lib/elf/types";
import { buildRelocTable } from "../lib/mips/reloc";
import { PSP_BASE, patchFileLoading } from "../lib/elf/atlus_eboot";
import { align } from "../lib/util/misc";
import { insertSection } from "../lib/elf/insert_mem_section";
import { loadLocale } from "../lib/util/encoding";
import {
  Constants,
  Game,
  GameContext,
  loadScriptConstants,
} from "../lib/util/context";
import { MessageManager } from "../lib/mod/msg_manager";
import { importObj } from "../lib/mips/objimport";
import {
  ModFileEntry,
  augmentInfo,
  buildIsoDir,
  buildMod,
  extractAll,
  extractISO,
  parseModInfo,
  patchEboot,
} from "../lib/mod/modlib";
import { VFS } from "../lib/mod/vfs";
import { createIso, readTOC } from "../lib/iso/iso";
import { CDXAApplicationData } from "../lib/iso/iso_types";
import { toDataView, toStructBuffer } from "../lib/util/structlib";
import { applyAfterTranslations, clearGeneratedAfter, exportScriptFiles, exportTbfFiles, importTbfFiles, validateTbfEncoding } from "../lib/msg/tbf";
import { loadFontProfile, patchEventFontTable, patchFontMetrics, prepareFontImages, resolveFontProfile } from "../lib/mod/font_profile";
import { decrypt_eboot } from "../lib/decrypt/eboot";

const args = yargs(hideBin(process.argv))
  .usage(`Tool for doing asm mods`)
  .command(
    "$0 <iso> <modpath>",
    false,
    (yargs) =>
      yargs
        .positional("iso", {
          type: "string",
          describe: "iso",
          demandOption: true,
          normalize: true,
        })
        .option("output", {
          type: "string",
          describe: "output",
          alias: "o",
          demandOption: true,
          normalize: true,
        })
        .positional("modpath", {
          type: "string",
          default: ".",
          normalize: true,
        })
        .option("iso_output", {
          type: "string",
          describe: "Build ISO?",
          normalize: true,
        })
        .option("variant", {
          type: "string",
          describe: "for IS, choose eu or us",
        }),
    async (args) => {
      console.log(args);
      // const input = await readBinaryFile(args.eboot);
      let vfs = new VFS<ModFileEntry>();
      let modInfo = await parseModInfo(args.modpath, vfs);
      let gameContextIso: GameContext = {
        game: modInfo.game,
        locale: await loadLocale(
          fromTools(
            `game/${modInfo.game}/encoding/${modInfo.isoLocale ?? "jp"}`
          )
        ),
        variant: args.variant,
        constants: {},
      };
      let localePath = joinPath(args.modpath, modInfo.locale);
      if (modInfo.locale.length < 3) {
        localePath = fromTools(`game/${modInfo.game}/encoding/${modInfo.locale}`);
      }
      let gameContextMod: GameContext = {
        game: modInfo.game,
        locale: await loadLocale(localePath),
        variant: args.variant,
        constants: {},
      };
      await loadScriptConstants(gameContextIso);
      await loadScriptConstants(gameContextMod);

      let isoName = basename(args.iso);
      const buildPath = joinPath(args.output, "build", isoName + "$");
      const isoPath = joinPath(args.output, "iso", isoName + "$");

      let isoInfo = JSON.parse(
        await readTextFile(
          fromTools(
            `game/${modInfo.game}/iso${args.variant ? `_${args.variant}` : ""
            }.json`
          )
        )
      );
      augmentInfo(isoInfo);

      let clean_base = await extractISO(
        args.iso,
        joinPath(args.output, "clean", isoName + "$")
      );

      await buildMod(
        vfs.root,
        isoInfo,
        "/",
        clean_base,
        buildPath,
        gameContextIso,
        gameContextMod
      );
      await buildIsoDir(isoInfo, clean_base, buildPath, isoPath);
      await patchEboot(
        joinPath(buildPath, modInfo.ebootPath),
        args.modpath,
        buildPath,
        joinPath(isoPath, modInfo.ebootPath),
        gameContextMod,
        (modInfo.asmOrder ?? []) as string[],
        joinPath(isoPath, modInfo.symname)
      );
      if (args.iso_output) {
        let fd = await openFileWrite(args.iso_output);

        //TODO: don't hardcode this here
        let gameIDs = {
          [Game.EP]: "NPHJ-50581",
          [Game.IS]: "ULUS-10584",
        };

        const applicationData = new Uint8Array(512);
        CDXAApplicationData.write(toStructBuffer(toDataView(applicationData)), {
          id: `${(gameIDs[modInfo.game as Game] ?? "").padStart(
            10
          )}|0000000000000000|0001`,
          cdxa: "CD-XA001",
        });
        await createIso(fd, isoPath, {
          systemIdentifier: "PSP GAME",
          volumeIdentifier: "PERSONA2",
          publisherIdentifier: "ATLUS",
          applicationIdentifier: "PSP GAME",
          application: [...applicationData],
        });
        // await build
      }
    }
  )
  .command(
    "extractAll <iso>",
    false,
    (yargs) =>
      yargs
        .positional("iso", {
          type: "string",
          describe: "iso",
          demandOption: true,
          normalize: true,
        })
        .option("output", {
          type: "string",
          describe: "output",
          alias: "o",
          demandOption: true,
          normalize: true,
        })
        .option("game", {
          type: "string",
          demandOption: true,
          alias: "g",
          choices: ["is", "ep"],
        })
        .option("locale", {
          type: "string",
          demandOption: false,
          default: "jp",
        })
        .option("variant", {
          type: "string",
          choices: ["us", "eu"],
          default: "us",
        })
        .option("translation-output", {
          type: "string",
          describe: "directory for separated TBF translation files",
        }),
    async (args) => {
      // const input = await readBinaryFile(args.eboot);
      let game: Game = args.game as Game;
      let gameContextIso: GameContext = {
        game,
        variant: args.variant,
        locale: await loadLocale(
          fromTools(`game/${game}/encoding/${args.locale}`)
        ),
        constants: {},
      };
      await loadScriptConstants(gameContextIso);

      // let isoName = basename(args.iso);

      let isoInfo = JSON.parse(
        await readTextFile(
          fromTools(`game/${game}/iso${args.variant == "eu" ? "_eu" : ""}.json`)
        )
      );
      augmentInfo(isoInfo);

      // let clean_base = await extractISO(args.iso, joinPath(args.output));

      await extractAll(
        isoInfo,
        args.iso,
        joinPath(args.output, "dumped_cpk"),
        gameContextIso,
        args.translationOutput ?? joinPath(dirname(args.output), "translation", args.locale),
        fromTools(`game/${game}/encoding/${args.locale}`)
      );
    }
  )
  .command(
    "exportTbf <source> <output>",
    "Convert extracted MSG/EF/CF files to TBF translation files",
    (yargs) =>
      yargs
        .positional("source", { type: "string", demandOption: true })
        .positional("output", { type: "string", demandOption: true }),
    async (args) => {
      await exportTbfFiles(args.source, joinPath(args.output, "messages"));
      await exportScriptFiles(args.source, joinPath(args.output, "scripts"));
    }
  )
  .command(
    "importTbf <source> <output>",
    "Generate MSG files from the TBF after sections",
    (yargs) =>
      yargs
        .positional("source", { type: "string", demandOption: true })
        .positional("output", { type: "string", demandOption: true }),
    async (args) => {
      await importTbfFiles(args.source, args.output);
    }
  )
  .command(
    "rebuildTbf <iso> <translation>",
    "Rebuild an ISO from imported TBF files",
    (yargs) =>
      yargs
        .positional("iso", { type: "string", demandOption: true })
        .positional("translation", { type: "string", demandOption: true })
        .option("output", {
          type: "string",
          alias: "o",
          default: "lab/dump/rebuild",
          describe: "working directory",
        })
        .option("iso-output", {
          type: "string",
          default: "lab/p2is-translated.iso",
          describe: "rebuilt ISO path",
        })
        .option("game", {
          type: "string",
          choices: ["is", "ep"],
          demandOption: true,
        })
        .option("variant", {
          type: "string",
          choices: ["us", "eu"],
          default: "us",
        })
        .option("locale", { type: "string", default: "en" })
        .option("font", {
          type: "string",
          describe: "font profile (default: bundled pt-br for IS US); use original to disable",
        })
        .option("game-id", { type: "string", default: "ULUS-10584" }),
    async (args) => {
      const game = args.game as Game;
      const work = args.output;
      const generated = joinPath(work, "tbf-files");
      const translationRoot = basename(args.translation) === "messages"
        ? (basename(dirname(args.translation)) === "new" ? dirname(dirname(args.translation)) : dirname(args.translation))
        : args.translation;
      const structuredMessages = joinPath(translationRoot, "new", "messages");
      const translationMessages = await exists(structuredMessages)
        ? structuredMessages
        : args.translation;
      const selectedFont = await resolveFontProfile(translationRoot, args.font, game, args.variant);
      const fontProfile = selectedFont.path;
      const isoName = basename(args.iso);
      const cleanBase = joinPath(work, "clean", `${isoName}$`);
      const buildPath = joinPath(work, "build", `${isoName}$`);
      const isoPath = joinPath(work, "iso", `${isoName}$`);

      try {
        const originalLocale = await loadLocale(fromTools(`game/${game}/encoding/${args.locale}`));
        console.log(`Font profile: ${selectedFont.name}${fontProfile ? ` (${fontProfile})` : ""}`);
        const profile = fontProfile ? await loadFontProfile(fontProfile, originalLocale) : undefined;
        const gameContextIso: GameContext = { game, locale: originalLocale, variant: args.variant, constants: {} };
        const gameContextMod: GameContext = {
          ...gameContextIso, locale: profile?.locale ?? originalLocale, strictEncoding: true,
        };
        await loadScriptConstants(gameContextIso);
        const appliedAfter = await applyAfterTranslations(translationRoot);
        if (appliedAfter) console.log(`Applied ${appliedAfter} translations from after/.`);
        await validateTbfEncoding(translationMessages, gameContextMod);
        await importTbfFiles(translationMessages, generated);
        if (translationMessages !== args.translation) {
          await clearGeneratedAfter(translationRoot);
          await importTbfFiles(translationMessages, joinPath(translationRoot, "after"));
        }
        if (fontProfile && profile) {
          const overrideRoot = joinPath(generated, "font-overrides");
          await mkdir(overrideRoot);
          const files: Record<string, string> = {};
          for (const gim of await prepareFontImages(fontProfile, overrideRoot, profile.mappings)) {
            const name = `${gim}.png`;
            files[name] = `PSP_GAME/USRDIR/pack/P2PT_ALL.cpk$/syscg.bin$/${gim}.gim$/image.png`;
          }
          await writeTextFile(
            joinPath(overrideRoot, "files.json"),
            JSON.stringify({ path: "/", files }, null, 2)
          );
        }
        await writeTextFile(
          joinPath(generated, "mod.json"),
          JSON.stringify({
            game,
            locale: args.locale,
            isoLocale: args.locale,
          })
        );

        const vfs = new VFS<ModFileEntry>();
        const modInfo = await parseModInfo(generated, vfs);
        const isoInfo = JSON.parse(
        await readTextFile(
          fromTools(`game/${game}/iso${args.variant == "eu" ? "_eu" : ""}.json`)
        )
        );
        augmentInfo(isoInfo);
        const clean = await extractISO(args.iso, cleanBase);
        await buildMod(
        vfs.root,
        isoInfo,
        "/",
        clean,
        buildPath,
        gameContextIso,
        gameContextMod
        );
        await buildIsoDir(isoInfo, clean, buildPath, isoPath);
        if (profile) {
          const executablePath = joinPath(isoPath, "PSP_GAME", "SYSDIR", "EBOOT.BIN");
          const original = await readBinaryFile(executablePath);
          const executable = original[0] === 0x7f ? original : await decrypt_eboot(original);
          patchEventFontTable(executable, profile.mappings);
          await patchFontMetrics(executable, joinPath(generated, "font-overrides"), profile.mappings);
          patchFileLoading(executable);
          await writeBinaryFile(executablePath, executable);
          console.log(`Applied ${profile.mappings.length} event/font mappings from ${selectedFont.name}`);
        }
        const layoutFile = await openFileRead(args.iso);
        const layout = await readTOC(layoutFile);
        await closeFile(layoutFile);
        const fd = await openFileWrite(args.isoOutput);
        const applicationData = new Uint8Array(512);
        CDXAApplicationData.write(toStructBuffer(toDataView(applicationData)), {
        id: `${args.gameId.padStart(10)}|0000000000000000|0001`,
        cdxa: "CD-XA001",
        });
        await createIso(fd, isoPath, {
        systemIdentifier: "PSP GAME",
        volumeIdentifier: "PERSONA2",
        publisherIdentifier: "ATLUS",
        applicationIdentifier: "PSP GAME",
        application: [...applicationData],
        }, layout);
        await rm(work, { recursive: true, force: true });
        console.log(`Temporary rebuild directory removed: ${work}`);
      } catch (error) {
        console.error(`Rebuild failed; temporary directory preserved: ${work}`);
        throw error;
      }
    }
  )
  .demandCommand()
  .strict()
  .showHelpOnFail(true)
  .help().argv;
