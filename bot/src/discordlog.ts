// ログ行を Discord のログチャンネルへまとめて投稿する（管理者がリモートで確認する用）。
// 2 秒ごとにバッファをまとめ、1 メッセージ 1900 文字以内のコードブロックで送る。
import type { Client, TextBasedChannel } from "discord.js";
import { setLogSink } from "./log.js";

const FLUSH_MS = 2000;
const MAX_LEN = 1900;
const FENCE = "```";

export async function attachDiscordLog(client: Client, channelId: string): Promise<boolean> {
  const ch = await client.channels.fetch(channelId).catch(() => null);
  if (!ch || !ch.isTextBased() || !("send" in ch)) return false;
  const channel = ch as TextBasedChannel & { send: (o: { content: string }) => Promise<unknown> };

  let buffer: string[] = [];
  let timer: NodeJS.Timeout | null = null;
  let sending = false;

  const post = async (text: string): Promise<void> => {
    await channel.send({ content: `${FENCE}\n${text}\n${FENCE}` });
  };

  const flush = async (): Promise<void> => {
    timer = null;
    if (sending || buffer.length === 0) return;
    sending = true;
    const lines = buffer;
    buffer = [];
    try {
      let chunk = "";
      for (const l of lines) {
        if (chunk.length + l.length + 1 > MAX_LEN) {
          await post(chunk);
          chunk = "";
        }
        chunk += (chunk ? "\n" : "") + l;
      }
      if (chunk) await post(chunk);
    } catch (err) {
      console.error(`[discordlog] 送信失敗: ${String(err)}`);
    } finally {
      sending = false;
      if (buffer.length > 0 && !timer) timer = setTimeout(flush, FLUSH_MS);
    }
  };

  setLogSink((line, level) => {
    buffer.push(formatForDiscord(line, level));
    if (!timer) timer = setTimeout(flush, FLUSH_MS);
  });
  return true;
}

const JST_MS = 9 * 3600_000;
const ISO = /\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?Z/g;

/** ISO の時刻（UTC）を、日本時間に直す。withDate なら "YYYY-MM-DD HH:MM"、無ければ "HH:MM:SS" */
function jst(iso: string, withDate: boolean): string {
  const t = new Date(iso).getTime();
  if (Number.isNaN(t)) return iso;
  const s = new Date(t + JST_MS).toISOString();
  return withDate ? `${s.slice(0, 10)} ${s.slice(11, 16)}` : s.slice(11, 19);
}

/**
 * ログの 1 行を、ログのチャンネル向けに直す。先頭の時刻は日本時間の時刻だけに縮め、行の途中の日時も日本時間にする
 * （ファイルとコンソールのログは UTC の ISO のまま）。レベルに印を付ける
 */
export function formatForDiscord(line: string, level: "INFO" | "WARN" | "ERROR"): string {
  const head = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z) /.exec(line);
  let body = head ? `${jst(head[1], false)} ${line.slice(head[0].length)}` : line;
  body = body.replace(ISO, (iso) => jst(iso, true));
  const mark = level === "ERROR" ? "❌ " : level === "WARN" ? "⚠️ " : "";
  return (mark + body).slice(0, MAX_LEN);
}
