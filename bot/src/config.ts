// 設定の読み込み。
//   <instance>/.env          秘密情報（DISCORD_TOKEN, GITHUB_TOKEN）
//   <instance>/config.jsonc  それ以外の設定（config.json でも可）
//   <instance>/data/db.json  Bot が生成するデータ
// <instance> は環境変数 INSTANCE_DIR、無ければカレントの ./instance

import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { parse as parseJsonc, type ParseError, printParseErrorCode } from "jsonc-parser";

export interface TierConfig {
  id: string;
  rank: number;
  label: string;
  color: string;
  roleId: string;
  sourceRoleIds: string[];
}

export type PublishConfig =
  | { type: "file"; path: string }
  | { type: "gist"; gistId: string; fileName: string }
  | { type: "github"; owner: string; repo: string; branch?: string; path: string };

/** アプリ内部で使う平坦な設定（ファイルの構造とは分離） */
export interface AppConfig {
  guildId: string;
  adminRoleIds: string[];
  /** 登録専用チャンネル。設定するとテキスト投稿を拾って案内/登録する */
  registerChannelId: string | null;
  graceDays: number;
  nameChangeCooldownDays: number;
  syncIntervalMinutes: number;
  maxNameLength: number;
  tiers: TierConfig[];
  publish: PublishConfig;
}

export interface Secrets {
  discordToken: string;
  githubToken: string | null;
}

export interface Instance {
  dir: string;
  configPath: string;
  dataPath: string;
  secrets: Secrets;
  config: AppConfig;
}

/** config.jsonc のファイル構造 */
interface ConfigFile {
  discord?: { guildId?: string; adminRoleIds?: string[]; registerChannelId?: string };
  tiers?: Partial<TierConfig>[];
  rules?: { graceDays?: number; nameChangeCooldownDays?: number; maxNameLength?: number };
  sync?: { intervalMinutes?: number };
  publish?: PublishConfig;
}

export function resolveInstanceDir(): string {
  return path.resolve(process.env.INSTANCE_DIR ?? "./instance");
}

export function loadInstance(dir: string = resolveInstanceDir()): Instance {
  if (!existsSync(dir)) {
    throw new Error(`設定フォルダがありません: ${dir}\n  cp -r instance.example instance してから編集してください`);
  }
  const secrets = loadSecrets(path.join(dir, ".env"));
  const configPath = ["config.jsonc", "config.json"].map((f) => path.join(dir, f)).find((p) => existsSync(p));
  if (!configPath) throw new Error(`${dir} に config.jsonc がありません`);
  const config = loadConfig(configPath, dir);
  return { dir, configPath, dataPath: path.join(dir, "data", "db.json"), secrets, config };
}

/** dotenv 互換の最小パーサ（KEY=VALUE、# コメント、引用符） */
function parseDotEnv(text: string): Record<string, string> {
  const out: Record<string, string> = {};
  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith("#")) continue;
    const eq = line.indexOf("=");
    if (eq <= 0) continue;
    const key = line.slice(0, eq).trim().replace(/^export\s+/, "");
    let value = line.slice(eq + 1).trim();
    const quote = value.startsWith('"') || value.startsWith("'") ? value[0] : null;
    if (quote) {
      const close = value.indexOf(quote, 1);
      value = close > 0 ? value.slice(1, close) : value.slice(1);
    } else {
      const hash = value.indexOf(" #");
      if (hash >= 0) value = value.slice(0, hash).trim();
    }
    out[key] = value;
  }
  return out;
}

function loadSecrets(envPath: string): Secrets {
  const fromFile = existsSync(envPath) ? parseDotEnv(readFileSync(envPath, "utf8")) : {};
  // .env ファイルを正とする。ファイルに無いキーだけ環境変数から補う
  // （同名の環境変数がたまたまマシンにあっても別 Bot でログインしないようにするため）
  const discordToken = fromFile.DISCORD_TOKEN || process.env.DISCORD_TOKEN || "";
  const githubToken = fromFile.GITHUB_TOKEN || process.env.GITHUB_TOKEN || null;
  if (!discordToken) throw new Error(`DISCORD_TOKEN が未設定です（${envPath} または環境変数）`);
  return { discordToken, githubToken };
}

function loadConfig(configPath: string, instanceDir: string): AppConfig {
  const errors: ParseError[] = [];
  const raw = parseJsonc(readFileSync(configPath, "utf8"), errors, { allowTrailingComma: true }) as ConfigFile;
  if (errors.length > 0) {
    const e = errors[0];
    throw new Error(`${configPath} の構文エラー: ${printParseErrorCode(e.error)} (offset ${e.offset})`);
  }
  if (!raw || typeof raw !== "object") throw new Error(`${configPath} が空です`);

  const guildId = raw.discord?.guildId;
  if (!guildId || !/^\d{5,}$/.test(guildId)) throw new Error("discord.guildId を設定してください");

  if (!Array.isArray(raw.tiers) || raw.tiers.length === 0) throw new Error("tiers を 1 件以上設定してください");
  const tiers = raw.tiers.map((t, i) => {
    if (!t.id || !t.roleId || typeof t.rank !== "number" || t.rank <= 0) {
      throw new Error(`tiers[${i}] に id / rank(>0) / roleId が必要です`);
    }
    return {
      id: t.id,
      rank: t.rank,
      label: t.label ?? t.id,
      color: t.color ?? "#FFFFFF",
      roleId: t.roleId,
      sourceRoleIds: Array.isArray(t.sourceRoleIds) ? t.sourceRoleIds : [],
    } satisfies TierConfig;
  });
  if (new Set(tiers.map((t) => t.rank)).size !== tiers.length) throw new Error("tiers の rank が重複しています");
  if (new Set(tiers.map((t) => t.id)).size !== tiers.length) throw new Error("tiers の id が重複しています");

  if (!raw.publish || !raw.publish.type) throw new Error("publish を設定してください");
  let publish: PublishConfig = raw.publish;
  if (publish.type === "file") {
    publish = { type: "file", path: path.resolve(instanceDir, publish.path) };
  } else if (publish.type === "gist") {
    if (!publish.gistId || !publish.fileName) throw new Error("publish.gistId と publish.fileName が必要です");
  } else if (publish.type === "github") {
    if (!publish.owner || !publish.repo || !publish.path) throw new Error("publish.owner / repo / path が必要です");
  } else {
    throw new Error(`publish.type が不正です: ${String((publish as { type: string }).type)}`);
  }

  return {
    guildId,
    adminRoleIds: raw.discord?.adminRoleIds ?? [],
    registerChannelId: raw.discord?.registerChannelId || null,
    graceDays: raw.rules?.graceDays ?? 31,
    nameChangeCooldownDays: raw.rules?.nameChangeCooldownDays ?? 30,
    maxNameLength: raw.rules?.maxNameLength ?? 32,
    syncIntervalMinutes: raw.sync?.intervalMinutes ?? 10,
    tiers: tiers.sort((a, b) => a.rank - b.rank),
    publish,
  };
}

export function tierByRank(config: AppConfig, rank: number): TierConfig | null {
  return config.tiers.find((t) => t.rank === rank) ?? null;
}
