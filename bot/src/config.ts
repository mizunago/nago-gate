// 設定の読み込み。
//   <instance>/.env          秘密情報（DISCORD_TOKEN, GITHUB_TOKEN, LIST_KEY, VRC_AUTH_COOKIE）と、LIST_KEEP_PLAIN
//   <instance>/config.jsonc  それ以外の設定（config.json でも可）
//   <instance>/data/db.json  Bot が生成するデータ
// <instance> は環境変数 INSTANCE_DIR、無ければカレントの ./instance

import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { parse as parseJsonc, type ParseError, printParseErrorCode } from "jsonc-parser";
import { parseListKeys } from "./protect.js";

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

/**
 * 住人（設定やデータの中の名前は member）。支援とは別の軸。在籍日数・名前の登録・18 歳以上の確認を満たした人に
 * ロールを付け、リストの members に載せる（名前は載せず、ハッシュだけ）。
 * 表に出す呼び名は「住人 / Resident」（2026-10-06 に「メンバー」から変えた。Patreon のメンバーシップと紛れるため）
 */
export interface MemberConfig {
  roleId: string;
  /** サーバーに参加してから必要な日数（自動のとき: 認定までの日数。申請制のとき: 申請できるようになるまでの日数） */
  minDays: number;
  /** auto = 条件を満たせば自動で認定する。apply = 本人が申請し、管理者が見て認定する */
  mode: "auto" | "apply";
  /** 申請制のとき: 申請を出すチャンネル。未設定ならコマンドのチャンネル、それも無ければログのチャンネル */
  reviewChannelId: string | null;
  /** 申請制のとき: 見送りのあと、次に申請できるまでの日数 */
  reapplyDays: number;
}

/** アプリ内部で使う平坦な設定（ファイルの構造とは分離） */
export interface AppConfig {
  guildId: string;
  adminRoleIds: string[];
  /** 登録専用チャンネル。設定するとテキスト投稿を拾って案内/登録する */
  registerChannelId: string | null;
  /**
   * ボタンのパネルを分けて置くチャンネル（任意）。設定したものは、そのチャンネルに専用のパネルを置き、
   * 登録のパネルからは外す。未設定なら、そのボタンは登録のパネルに並ぶ（前の版と同じ）
   */
  statusChannelId: string | null;
  residentChannelId: string | null;
  groupChannelId: string | null;
  /** Bot の操作ログを流すチャンネル（管理者専用） */
  logChannelId: string | null;
  /** 使えるコマンドの一覧を出しておくチャンネル（管理者専用）。起動のたびに書き換える */
  commandsChannelId: string | null;
  graceDays: number;
  nameChangeCooldownDays: number;
  /** 登録・変更から、この分数のうちは表示名の打ち間違いを何度でも直せる（30 日の数え始めは動かさない） */
  nameFixMinutes: number;
  syncIntervalMinutes: number;
  maxNameLength: number;
  tiers: TierConfig[];
  publish: PublishConfig;
  /** 未設定（null）ならメンバー登録の機能は出ない */
  member: MemberConfig | null;
  /** リストに入れて、ワールドの中に出す案内用のリンク（"discord" → 招待 URL など） */
  links: Record<string, string>;
  /** VRChat の Group（未設定なら、登録パネルにグループのボタンを出さない） */
  group: GroupConfig | null;
  /** 協力者の枠（デバッグなどを手伝ってくれる人を、期間を決めて支援者と同じに入れる） */
  tester: TesterConfig;
}

export interface TesterConfig {
  /** 協力者に付ける Discord のロール（誰が協力者か分かるようにするだけ）。未設定ならロールは付けない */
  roleId: string | null;
  /** 日数を指定しなかったときの期間 */
  defaultDays: number;
}

/** VRChat の Group。支援者とメンバーが、フレンドでなくても同じインスタンスで遊べるようにするためのもの */
export interface GroupConfig {
  /** Group の名前（案内の文に出す） */
  name: string;
  /** Group のページの URL（https://vrc.group/XXXX.0000 など） */
  url: string;
  /** Group の ID（grp_...）。未設定なら、URL の短いコードから、Bot 用のアカウントが入っている Group を探す */
  id: string | null;
  /** URL の末尾の短いコード（XXXX.0000）。取り出せなければ null */
  shortCode: string | null;
}

export interface Secrets {
  discordToken: string;
  githubToken: string | null;
  /** 鍵つきのリストの鍵（LIST_KEY。カンマ区切りで複数）。空なら、鍵なしの今までの形式で公開する */
  listKeys: string[];
  /** 鍵つきのときも、鍵なしの部分を残すか（LIST_KEEP_PLAIN）。鍵をまだ入れていないワールドがある間の、移行用 */
  listKeepPlain: boolean;
  /** VRChat の Bot 用アカウントのクッキー（VRC_AUTH_COOKIE と、あれば VRC_TWOFACTOR_COOKIE を合わせた、送る形の文字列）。未設定なら、Group への招待は持ち主が手で行う */
  vrcAuthCookie: string | null;
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
  discord?: {
    guildId?: string;
    adminRoleIds?: string[];
    registerChannelId?: string;
    statusChannelId?: string;
    residentChannelId?: string;
    groupChannelId?: string;
    logChannelId?: string;
    commandsChannelId?: string;
  };
  tiers?: Partial<TierConfig>[];
  rules?: { graceDays?: number; nameChangeCooldownDays?: number; nameFixMinutes?: number; maxNameLength?: number };
  sync?: { intervalMinutes?: number };
  publish?: PublishConfig;
  member?: { roleId?: string; minDays?: number; mode?: string; reviewChannelId?: string; reapplyDays?: number };
  links?: Record<string, unknown>;
  vrcGroup?: { name?: string; url?: string; id?: string };
  tester?: { roleId?: string; defaultDays?: number };
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
  const listKeys = parseListKeys(fromFile.LIST_KEY || process.env.LIST_KEY);
  const listKeepPlain = /^(1|true|yes|on)$/i.test(fromFile.LIST_KEEP_PLAIN || process.env.LIST_KEEP_PLAIN || "");
  // VRChat のクッキー。auth に加えて twoFactorAuth（確認コードを済ませた印）もあれば、一緒に送る。
  // ログインしたのと別の場所（サーバー）から使うときは、twoFactorAuth が無いと断られる
  const vrcAuth = (fromFile.VRC_AUTH_COOKIE || process.env.VRC_AUTH_COOKIE || "").trim();
  const vrcTwoFactor = (fromFile.VRC_TWOFACTOR_COOKIE || process.env.VRC_TWOFACTOR_COOKIE || "").trim().replace(/^twoFactorAuth=/, "");
  const vrcAuthCookie = vrcAuth ? (vrcAuth.startsWith("auth=") ? vrcAuth : `auth=${vrcAuth}`) + (vrcTwoFactor ? `; twoFactorAuth=${vrcTwoFactor}` : "") : null;
  return { discordToken, githubToken, listKeys, listKeepPlain, vrcAuthCookie };
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

  let member: MemberConfig | null = null;
  if (raw.member?.roleId) {
    const minDays = raw.member.minDays ?? 7;
    if (typeof minDays !== "number" || minDays < 0) throw new Error("member.minDays は 0 以上の数にしてください");
    const mode = raw.member.mode ?? "auto";
    if (mode !== "auto" && mode !== "apply") throw new Error('member.mode は "auto" か "apply" にしてください');
    const reapplyDays = raw.member.reapplyDays ?? 30;
    if (typeof reapplyDays !== "number" || reapplyDays < 0) throw new Error("member.reapplyDays は 0 以上の数にしてください");
    member = { roleId: raw.member.roleId, minDays, mode, reviewChannelId: raw.member.reviewChannelId || null, reapplyDays };
  }

  const links: Record<string, string> = {};
  for (const [key, value] of Object.entries(raw.links ?? {})) {
    if (value === "" || value === null || value === undefined) continue;
    if (typeof value !== "string" || !/^https?:\/\/\S{1,200}$/.test(value)) throw new Error(`links.${key} は http(s) の URL にしてください`);
    links[key] = value;
  }

  const testerDays = raw.tester?.defaultDays ?? 30;
  if (typeof testerDays !== "number" || !Number.isInteger(testerDays) || testerDays < 1 || testerDays > 365) throw new Error("tester.defaultDays は 1 から 365 の整数にしてください");

  let group: GroupConfig | null = null;
  if (raw.vrcGroup?.url) {
    if (typeof raw.vrcGroup.url !== "string" || !/^https:\/\/\S{1,200}$/.test(raw.vrcGroup.url)) throw new Error("vrcGroup.url は https の URL にしてください");
    const code = /([A-Za-z0-9]{3,8}\.\d{4})\/?$/.exec(raw.vrcGroup.url);
    group = { name: raw.vrcGroup.name?.trim() || "VRChat Group", url: raw.vrcGroup.url, id: raw.vrcGroup.id?.trim() || null, shortCode: code ? code[1] : null };
  }

  return {
    guildId,
    adminRoleIds: raw.discord?.adminRoleIds ?? [],
    registerChannelId: raw.discord?.registerChannelId || null,
    statusChannelId: raw.discord?.statusChannelId || null,
    residentChannelId: raw.discord?.residentChannelId || null,
    groupChannelId: raw.discord?.groupChannelId || null,
    logChannelId: raw.discord?.logChannelId || null,
    commandsChannelId: raw.discord?.commandsChannelId || null,
    graceDays: raw.rules?.graceDays ?? 31,
    nameChangeCooldownDays: raw.rules?.nameChangeCooldownDays ?? 30,
    nameFixMinutes: raw.rules?.nameFixMinutes ?? 60,
    maxNameLength: raw.rules?.maxNameLength ?? 32,
    syncIntervalMinutes: raw.sync?.intervalMinutes ?? 10,
    tiers: tiers.sort((a, b) => a.rank - b.rank),
    publish,
    member,
    links,
    group,
    tester: { roleId: raw.tester?.roleId || null, defaultDays: testerDays },
  };
}

export function tierByRank(config: AppConfig, rank: number): TierConfig | null {
  return config.tiers.find((t) => t.rank === rank) ?? null;
}
