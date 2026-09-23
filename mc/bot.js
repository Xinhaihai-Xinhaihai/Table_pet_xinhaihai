// 心海海进 MC。原版/Fabric/Forge/NeoForge 登录协议相同,不装模组。
const mineflayer = require("mineflayer");

const host = process.env.MC_HOST || "127.0.0.1";
const port = Number(process.env.MC_PORT || 25565);
const username = process.env.MC_NAME || "XinHaiHai";

const bot = mineflayer.createBot({ host, port, username, auth: "offline", hideErrors: false });

function out(tag, text) { process.stdout.write(tag + " " + String(text).replace(/[\r\n]/g, " ").slice(0, 512) + "\n"); }

bot.once("login", () => out("LOGIN", bot.username));
bot.once("spawn", () => {
  out("SPAWN", "ok");
  setTimeout(() => { try { bot.chat("主人,吾来啦~"); } catch {} }, 1500);
});

bot.on("messagestr", (msg) => {
  if (!msg) return;
  out("MSG", msg);
});

bot.on("message", (jsonMsg) => {
  try {
    let text = jsonMsg.toString();
    if (text) out("RAW", text);
  } catch {}
});

bot.on("death", () => { out("EVENT", "death"); });

bot.on("kicked", (reason) => out("KICK", typeof reason === "string" ? reason : JSON.stringify(reason)));
bot.on("end", (reason) => out("END", reason || ""));
bot.on("error", (err) => out("ERR", err.message));

process.stdin.setEncoding("utf8");
let buf = "";
process.stdin.on("data", (chunk) => {
  buf += chunk;
  let i;
  while ((i = buf.indexOf("\n")) >= 0) {
    const line = buf.slice(0, i).trim();
    buf = buf.slice(i + 1);
    if (!line) continue;
    try { bot.chat(line); out("SENT", line); }
    catch (e) { out("SAYERR", e.message); }
  }
});
