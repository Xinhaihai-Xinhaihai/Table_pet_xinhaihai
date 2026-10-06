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
 */
const mineflayer = require('mineflayer');

const host = process.env.MC_HOST || '127.0.0.1';
const port = parseInt(process.env.MC_PORT || '25565', 10) || 25565;
const name = (process.env.MC_NAME || 'XinHaiHai').slice(0, 16) || 'XinHaiHai';

let bot = null;
let reconnectTimer = null;

function log(prefix, line) {
  console.log(prefix + (line == null ? '' : ' ' + String(line).replace(/\r?\n/g, ' ')));
}

function makeBot() {
  const b = mineflayer.createBot({
    host,
    port,
    username: name,
    auth: 'offline',      // 走线下模式,只连 online-mode=false 的服务器
    version: '1.20.1',    // 自动选兼容版本
  });
  bot = b;

  b.once('login', () => { /* 二次握手,等 spawn */ });
  b.once('spawn', () => log('SPAWN'));
  b.on('messagestr', (msg) => log('MSG', msg));
  b.on('death', () => log('EVENT', 'death'));
  b.once('kicked', (reason) => log('KICK', reason));
  b.once('error', (err) => log('ERR', err && err.message ? err.message : String(err)));

  b.on('end', (reason) => {
    bot = null;
    if (process.env.MC_SHUTDOWN === '1') return;
    log('ERR', 'disconnected: ' + (reason || 'unknown'));
    // 断线自动重连(5 秒),桌宠端看到 ERR 提示
    clearTimeout(reconnectTimer);
    reconnectTimer = setTimeout(() => { try { makeBot(); } catch (e) { log('ERR', e.message); } }, 5000);
  });

  b.on('whisper', (from, msg) => log('MSG', '<' + from + '> ' + msg));

  // 自己说过的话别回显
  const myMsg = new Set();
  b.on('message', (json, position) => { /* 忽略 */ });
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
    if (line.startsWith('/')) {
      bot.chat(line); // 原版指令(如 /tp 玩家 目的地、/time set day)
    } else {
      bot.chat(line);
    }
  }
});

process.on('SIGINT', () => { process.env.MC_SHUTDOWN = '1'; try { if (bot) bot.quit(); } catch (e) {} process.exit(0); });

makeBot();
