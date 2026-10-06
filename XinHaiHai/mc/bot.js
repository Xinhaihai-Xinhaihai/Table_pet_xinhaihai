/**
 * 心海海 MC 机器人(mineflayer,原版协议,无 mod)
 *
 * 环境变量:
 *   MC_HOST  服务器地址(默认 127.0.0.1)
 *   MC_PORT  端口(默认 25565)
 *   MC_NAME  游戏内名字(默认 XinHaiHai)
 *
 * 与 C# 端(McLink)约定:
 *   stdout: SPAWN / KICK <原因> / ERR <原因> / EVENT death / MSG <聊天原文>
 *   stdin : 一行一条,以 / 开头=当原版指令执行,否则当聊天发送
 *
 * 注意: 1.20.2+ 官方要求 Mojang 登录,离线模式只支持 ≤1.20.1。
 *       因此不指定 version,让 mineflayer 与服务器握手时自动选兼容版本;
 *       连上 1.20.2+ 的服会报 auth 错误,属正常,提示用户换离线服。
 */
const mineflayer = require('mineflayer');

const host = process.env.MC_HOST || '127.0.0.1';
const port = parseInt(process.env.MC_PORT || '25565', 10) || 25565;
const name = (process.env.MC_NAME || 'XinHaiHai').slice(0, 16) || 'XinHaiHai';

let bot = null;
let reconnectTimer = null;
let fatal = false; // 被踢/永久错误:不再重连

function log(prefix, line) {
  console.log(prefix + (line == null ? '' : ' ' + String(line).replace(/\r?\n/g, ' ')));
}

function cleanup(b) {
  try { b.removeAllListeners(); } catch (e) {}
  try { if (b !== bot) b.quit(); } catch (e) {}
}

function makeBot() {
  if (fatal || bot) return;
  let b;
  try {
    b = mineflayer.createBot({
      host,
      port,
      username: name,
      auth: 'offline', // 离线模式,只连 online-mode=false 的服务器
    });
  } catch (e) {
    log('ERR', e && e.message ? e.message : String(e));
    return;
  }
  bot = b;

  b.on('spawn', () => log('SPAWN'));
  b.on('messagestr', (msg) => log('MSG', msg));
  b.on('whisper', (from, msg) => log('MSG', '<' + from + '> ' + msg));
  b.on('death', () => log('EVENT', 'death'));

  // 被踢:告知 C# 端并退出(不重连)
  b.on('kicked', (reason) => {
    log('KICK', reason);
    fatal = true;
    cleanup(b);
    bot = null;
    clearTimeout(reconnectTimer);
    process.exit(0);
  });

  // 连接错误:网络类可重连,协议/auth 类退出
  b.on('error', (err) => {
    const m = (err && (err.message || String(err))) || '';
    log('ERR', m);
    if (/auth|unsupported|invalid|kicked|version/i.test(m)) {
      fatal = true;
      cleanup(b);
      bot = null;
      clearTimeout(reconnectTimer);
      process.exit(0);
    }
  });

  b.on('end', (reason) => {
    if (bot !== b) return;
    bot = null;
    cleanup(b);
    if (process.env.MC_SHUTDOWN === '1' || fatal) return;
    // 普通断线:5 秒后重连
    clearTimeout(reconnectTimer);
    reconnectTimer = setTimeout(() => { try { makeBot(); } catch (e) { log('ERR', e.message); } }, 5000);
  });

  return b;
}

process.stdin.setEncoding('utf8');
let pending = '';
process.stdin.on('data', (chunk) => {
  pending += chunk;
  let idx;
  while ((idx = pending.indexOf('\n')) >= 0) {
    const line = pending.slice(0, idx).trim();
    pending = pending.slice(idx + 1);
    if (!line || !bot) continue;
    try { bot.chat(line); } catch (e) { log('ERR', 'send failed: ' + e.message); }
  }
});

function shutdown() {
  process.env.MC_SHUTDOWN = '1';
  fatal = true;
  clearTimeout(reconnectTimer);
  try { if (bot) bot.quit(); } catch (e) {}
  process.exit(0);
}
process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);

makeBot();
