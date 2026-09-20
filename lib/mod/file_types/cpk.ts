import { GameContext } from "../../util/context";
import {
  FileType,
  closeFile,
  exists,
  joinPath,
  mkdir,
  openFileRead,
  withExtension,
} from "../../util/filesystem";
import { typeLookup } from "../file_types";
import { FileInfo, TypeHandler, needBuildLastFile } from "./common";
import {
  addFileToCPK,
  buildCPK,
  createCPK,
  extractFile,
  readCPKTOC,
} from "../../cpk/cpk";
import { dirname } from "path";

let handler: TypeHandler = {
  extract: async (
    src: string,
    info: FileInfo,
    dst: string,
    gameContext: GameContext
  ) => {
    const cpk = await openFileRead(src);
    const toc = await readCPKTOC(cpk);
    await mkdir(dst);
    for (const entry of toc) {
      let name = info.fileList!.find((f) => f.cpkId == entry.ID)?.path;
      const entryInfo = info.fileList!.find((f) => f.cpkId == entry.ID);
      name ??= `${entry.FileName}`;
      const source = joinPath(dst, withExtension(name, entryInfo?.type ?? "unk"));
      await extractFile(cpk, source, entry);

      if (entryInfo?.fileList) {
        const childHandler = typeLookup[entryInfo.type];
        if (childHandler === undefined) {
          throw new Error(`Cannot extract nested CPK type ${entryInfo.type}`);
        }
        const childDir = `${source}$`;
        await mkdir(childDir);
        await childHandler.extract(source, entryInfo, childDir, gameContext);
      }
    }
    await closeFile(cpk);
  },
  build: async ({ info, input, dst }) => {
    await mkdir(dirname(dst));
    let cpk = await createCPK(dst);
    for (let i = 0; i < info.fileList!.length; i++) {
      let file = info.fileList![i];
      let name = file.cpkName!;
      let id = file.cpkId!;
      addFileToCPK(cpk, input[i].pop()!.path, name, id);
    }
    await buildCPK(cpk);
  },
  needBuild: needBuildLastFile,
};
export default handler;
