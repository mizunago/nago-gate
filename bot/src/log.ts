// コンソールと instance/logs/bot-YYYY-MM-DD.log の両方に書くロガー。
// 動作確認・障害調査のため、ロール操作・チャンネル作成・登録・公開・エラーはすべてここを通す。
import { appendFileSync, mkdirSync, readdirSync, unlinkSync } from "node:fs";
import path from "node:path";

type Level = "INFO" | "WARN" | "ERROR";

let logDir: string | null = null;
const KEEP_DAYS = 30;

export function initLog(instanceDir: string): void {
  logDir = path.join(instanceDir, "logs");
  mkdirSync(logDir, { recursive: true });
  pruneOldLogs();
}

function fileFor(now: Date): string {
  const d = now.toISOString().slice(0, 10);
  return path.join(logDir ?? ".", `bot-${d}.log`);
}

function write(level: Level, msg: string): void {
  const now = new Date();
  const line = `${now.toISOString()} [${level}] ${msg}`;
  if (level === "ERROR") console.error(line);
  else console.log(line);
  if (logDir) {
    try {
      appendFileSync(fileFor(now), line + "\n", "utf8");
    } catch {
      // ログ書き込み失敗で本体を止めない
    }
  }
}

export const log = {
  info: (msg: string): void => write("INFO", msg),
  warn: (msg: string): void => write("WARN", msg),
  error: (msg: string): void => write("ERROR", msg),
};

function pruneOldLogs(): void {
  if (!logDir) return;
  const cutoff = Date.now() - KEEP_DAYS * 86_400_000;
  try {
    for (const f of readdirSync(logDir)) {
      const m = f.match(/^bot-(\d{4}-\d{2}-\d{2})\.log$/);
      if (!m) continue;
      if (new Date(m[1]).getTime() < cutoff) unlinkSync(path.join(logDir, f));
    }
  } catch {
    // 無視
  }
}
