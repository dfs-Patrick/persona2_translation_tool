#!/usr/bin/env node
const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");

const projectDir = __dirname;
const sourceDir = path.join(projectDir, "lab", "p2is");
const outputIso = path.join(projectDir, "lab", "iso", "p2is-mod.iso");
const temporaryIso = `${outputIso}.tmp`;
const cli = path.join(projectDir, "dist", "cli", "iso.js");
let timer;
let rebuilding = false;
let pending = false;

const rebuild = () => {
  if (rebuilding) {
    pending = true;
    return;
  }

  rebuilding = true;
  console.log("MSG alterado. Reconstruindo ISO...");
  const child = spawn(process.execPath, [cli, "make", sourceDir, "-o", temporaryIso, "--gameID", "ULUS-10584"], {
    cwd: projectDir,
    stdio: "inherit",
  });

  child.on("exit", (code) => {
    if (code === 0) {
      fs.renameSync(temporaryIso, outputIso);
      console.log(`ISO atualizada: ${outputIso}`);
    } else {
      console.error(`Falha ao reconstruir a ISO (codigo ${code ?? "desconhecido"}).`);
      if (fs.existsSync(temporaryIso)) fs.rmSync(temporaryIso);
    }

    rebuilding = false;
    if (pending) {
      pending = false;
      rebuild();
    }
  });
};

if (!fs.existsSync(cli)) {
  console.error("CLI compilado nao encontrado. Execute: npx tsc");
  process.exit(1);
}

console.log(`Monitorando arquivos .msg em ${sourceDir}`);
console.log("Pressione Ctrl+C para encerrar.");
fs.watch(sourceDir, { recursive: true }, (_event, filename) => {
  if (!filename || !filename.toString().toLowerCase().endsWith(".msg")) return;
  clearTimeout(timer);
  timer = setTimeout(rebuild, 300);
});

rebuild();