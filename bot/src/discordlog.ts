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
    // ISO 時刻を時刻だけに短縮し、レベルに印を付ける
    const short = line.replace(/^\d{4}-\d{2}-\d{2}T(\d{2}:\d{2}:\d{2})\.\d{3}Z /, "$1 ");
    const mark = level === "ERROR" ? "❌ " : level === "WARN" ? "⚠️ " : "";
    buffer.push((mark + short).slice(0, MAX_LEN));
    if (!timer) timer = setTimeout(flush, FLUSH_MS);
  });
  return true;
}
