import { createHash } from "node:crypto";
import type { Guild, GuildMember } from "discord.js";
import type { AppConfig, TierConfig } from "./config.js";
import { displaySafeName, hashName } from "./hash.js";
import { encryptText, keyedHashName, listKeyId } from "./protect.js";
import { publishJson } from "./publish.js";
import type { VrcClient } from "./vrchat.js";
import type { MemberRecord, Store } from "./store.js";

export interface SyncContext {
  config: AppConfig;
  store: Store;
  githubToken: string | null;
  /** 鍵つきのリストの鍵（複数可）。無ければ、鍵なしの形式で公開する */
  listKeys?: string[];
  /** 鍵つきのときも、鍵なしの部分を残すか（移行用） */
  listKeepPlain?: boolean;
  /** VRChat の API で Group へ招待できる状態なら、その入口。使えないときは null（持ち主が手で招待する） */
  vrc?: { get(): VrcGroupAccess | null };
  log: (msg: string) => void;
  /** Discord のログチャンネルに流さないログ。無ければ log を使う */
  logQuiet?: (msg: string) => void;
}

export interface VrcGroupAccess {
  client: VrcClient;
  groupId: string;
}

export interface SyncResult {
  scanned: number;
  active: number;
  inGrace: number;
  rolesAdded: number;
  rolesRemoved: number;
  /** メンバーの条件を満たしている人数 */
  members: number;
  published: boolean;
  publishUrl: string | null;
  errors: string[];
}

/** 公開 JSON のスキーマ。Udon 側 SupporterRegistry.cs と対応 */
export interface SupportersJson {
  /** 1 = 鍵なし、2 = 鍵つき（keyed に、鍵ごとの区画が入る） */
  v: 1 | 2;
  generatedAt: string;
  tiers: { id: string; rank: number; label: string; color: string }[];
  /** 正規化名の SHA-256 → ランク */
  access: Record<string, number>;
  /** クレジット表示用（表示同意した人のみ） */
  credits: { n: string; r: number }[];
  /** メンバー（支援とは別の軸）。正規化名の SHA-256 → 1。名前は載せない。メンバー登録を使うときだけ出す */
  members?: Record<string, number>;
  /** 案内用のリンク（設定に 1 つでもあるときだけ出す） */
  links?: Record<string, string>;
  /** 鍵つきのときだけ: 鍵なしの部分（access・credits・members）に中身があるか。false なら、鍵を持つワールドだけが読める */
  plain?: boolean;
  /** 鍵つきのときだけ: 鍵ごとの区画。ワールドは、自分の鍵の番号（k）の区画だけを読む */
  keyed?: KeyedSection[];
}

export interface KeyedSection {
  /** 鍵の番号 */
  k: string;
  /** nonce と、暗号化したクレジット（credits と同じ配列の JSON） */
  n: string;
  c: string;
  /** 鍵を混ぜたハッシュ → ランク */
  access: Record<string, number>;
  /** 鍵を混ぜたハッシュ → 1 */
  members?: Record<string, number>;
}

/** リストの形式を決める設定 */
export interface ListProtection {
  /** 鍵の並び。空なら鍵なし。複数あれば、それぞれの区画を載せる（鍵を入れ替えている間）*/
  keys: string[];
  /** 鍵つきのときも、鍵なしの部分を残すか（鍵をまだ入れていないワールドがある間） */
  keepPlain: boolean;
}

function linksOf(config: AppConfig): { links?: Record<string, string> } {
  return Object.keys(config.links).length > 0 ? { links: config.links } : {};
}

function nowIso(): string {
  return new Date().toISOString();
}

function addDays(date: Date, days: number): Date {
  return new Date(date.getTime() + days * 86_400_000);
}

/** 支援サイト Bot が付けたロールから現在ランクを算出 */
export function rankFromRoles(config: AppConfig, roleIds: Iterable<string>): number {
  const ids = new Set(roleIds);
  let rank = 0;
  for (const tier of config.tiers) {
    if (tier.sourceRoleIds.some((id) => ids.has(id)) && tier.rank > rank) rank = tier.rank;
  }
  return rank;
}

/** 手動付与を含めた有効ランクを更新する（猶予処理込み） */
export function updateEffectiveRank(config: AppConfig, rec: MemberRecord, activeRank: number, now: Date): void {
  const prevActive = rec.activeRank;
  rec.activeRank = activeRank;

  if (activeRank > 0) {
    rec.lastActiveAt = now.toISOString();
    rec.graceUntil = null;
    rec.graceRank = 0;
  } else if (prevActive > 0 && rec.graceUntil === null) {
    // 今回初めて支援が途切れた: 猶予開始（直前のランクを維持）
    rec.graceUntil = addDays(now, config.graceDays).toISOString();
    rec.graceRank = prevActive;
  }

  let effective = activeRank;
  if (activeRank === 0 && rec.graceUntil !== null) {
    if (new Date(rec.graceUntil) > now) {
      effective = rec.graceRank;
    } else {
      rec.graceUntil = null;
      rec.graceRank = 0;
    }
  }

  const manualValid = rec.manualRank > 0 && (rec.manualUntil === null || new Date(rec.manualUntil) > now);
  if (!manualValid && rec.manualRank > 0) {
    rec.manualRank = 0;
    rec.manualUntil = null;
  }
  if (manualValid && rec.manualRank > effective) effective = rec.manualRank;

  rec.effectiveRank = effective;
  rec.updatedAt = now.toISOString();
}

/** メンバーの在籍日数の条件を満たす日時。サーバーにいない、または機能が無効なら null */
export function memberEligibleFrom(config: AppConfig, rec: MemberRecord): Date | null {
  if (!config.member || !rec.joinedAt) return null;
  return addDays(new Date(rec.joinedAt), config.member.minDays);
}

/**
 * メンバーの条件を満たしているか。支援の有無は見ない。
 * 通常は、同意・名前の登録・在籍日数。管理者が手動で認定した人（申請制で認定された人も同じ）は、名前の登録とサーバーへの在籍だけでよい
 */
export function isMemberEligible(config: AppConfig, rec: MemberRecord, now: Date): boolean {
  if (!config.member || !rec.vrcName || rec.banned) return false;
  if (rec.memberManual) return rec.joinedAt !== null;
  if (!rec.memberConsentAt) return false;
  // 申請制: 申請中の人は、管理者が認定するまでメンバーではない（認定されると memberManual が立つ）。
  // 申請制にする前に自分で登録した人は、申請の記録が無いので、前の決まり（同意と在籍日数）のまま扱う
  if (config.member.mode === "apply" && rec.memberAppliedAt) return false;
  const from = memberEligibleFrom(config, rec);
  return from !== null && from <= now;
}

/** 有効ランクに応じて付与すべき共通ロール（ランク以下のティア全部） */
export function desiredRoleIds(config: AppConfig, effectiveRank: number): Set<string> {
  const set = new Set<string>();
  for (const tier of config.tiers) if (tier.rank <= effectiveRank) set.add(tier.roleId);
  return set;
}

function roleName(member: GuildMember, id: string): string {
  return member.guild.roles.cache.get(id)?.name ?? id;
}

async function applyRoles(
  config: AppConfig,
  member: GuildMember,
  effectiveRank: number,
  memberActive: boolean,
  result: SyncResult,
  log: (m: string) => void,
): Promise<void> {
  const desired = desiredRoleIds(config, effectiveRank);
  const managed = new Set(config.tiers.map((t) => t.roleId));
  if (config.member) {
    managed.add(config.member.roleId);
    if (memberActive) desired.add(config.member.roleId);
  }
  const current = new Set(member.roles.cache.keys());
  const toAdd = [...desired].filter((id) => !current.has(id));
  const toRemove = [...managed].filter((id) => current.has(id) && !desired.has(id));
  if (toAdd.length === 0 && toRemove.length === 0) return;
  try {
    if (toAdd.length > 0) {
      await member.roles.add(toAdd, "SupporterGate sync");
      result.rolesAdded += toAdd.length;
      log(`ロール付与 ${member.user.tag} (${member.id}) rank=${effectiveRank}: +${toAdd.map((id) => roleName(member, id)).join(",")}`);
    }
    if (toRemove.length > 0) {
      await member.roles.remove(toRemove, "SupporterGate sync");
      result.rolesRemoved += toRemove.length;
      log(`ロール剥奪 ${member.user.tag} (${member.id}) rank=${effectiveRank}: -${toRemove.map((id) => roleName(member, id)).join(",")}`);
    }
  } catch (err) {
    const msg = `ロール更新失敗 ${member.user.tag}: ${String(err)}`;
    result.errors.push(msg);
    log(msg);
  }
}

export function buildSupportersJson(config: AppConfig, store: Store, protection: ListProtection = { keys: [], keepPlain: false }): SupportersJson {
  // 載せる人を先に集める。ハッシュは、鍵なしの部分と、鍵ごとの区画とで、別々に作る
  const supporters: { name: string; rank: number }[] = [];
  const credits: { n: string; r: number }[] = [];
  for (const rec of store.all()) {
    // BAN 中の人は、支援が続いていても載せない
    if (rec.effectiveRank <= 0 || !rec.vrcName || rec.banned) continue;
    // 判定用のハッシュは、登録された名前そのままで作る。表示用の名前だけ、表示を乱す文字を落とす
    supporters.push({ name: rec.vrcName, rank: rec.effectiveRank });
    const shown = displaySafeName(rec.vrcName);
    if (rec.showCredit && shown) credits.push({ n: shown, r: rec.effectiveRank });
  }
  credits.sort((a, b) => b.r - a.r || a.n.localeCompare(b.n, "ja"));

  const memberNames: string[] | null = config.member ? [] : null;
  if (memberNames) {
    for (const rec of store.all()) {
      if (rec.memberActive && rec.vrcName) memberNames.push(rec.vrcName);
    }
  }

  const accessOf = (hashOf: (name: string) => string): Record<string, number> => {
    const access: Record<string, number> = {};
    for (const s of supporters) access[hashOf(s.name)] = s.rank;
    return access;
  };
  const membersOf = (hashOf: (name: string) => string): { members?: Record<string, number> } => {
    if (!memberNames) return {};
    const members: Record<string, number> = {};
    for (const name of memberNames) members[hashOf(name)] = 1;
    return { members };
  };

  const keyed = protection.keys.length > 0;
  // 鍵なしの部分に中身を入れるのは、鍵を使わないときと、移行中（keepPlain）だけ
  const plain = !keyed || protection.keepPlain;
  const creditsJson = JSON.stringify(credits);
  return {
    v: keyed ? 2 : 1,
    generatedAt: nowIso(),
    tiers: config.tiers.map((t: TierConfig) => ({ id: t.id, rank: t.rank, label: t.label, color: t.color })),
    access: plain ? accessOf(hashName) : {},
    credits: plain ? credits : [],
    ...(plain ? membersOf(hashName) : memberNames ? { members: {} } : {}),
    ...linksOf(config),
    ...(keyed
      ? {
          plain,
          keyed: protection.keys.map((key) => ({
            k: listKeyId(key),
            ...encryptText(key, creditsJson),
            access: accessOf((name) => keyedHashName(key, name)),
            ...membersOf((name) => keyedHashName(key, name)),
          })),
        }
      : {}),
  };
}

function digestOf(json: SupportersJson): string {
  const { generatedAt: _ignored, ...rest } = json;
  return createHash("sha256").update(JSON.stringify(rest)).digest("hex");
}

/** JSON を（変化があれば）公開する */
export async function publishIfChanged(ctx: SyncContext, force: boolean): Promise<{ published: boolean; url: string | null }> {
  const json = buildSupportersJson(ctx.config, ctx.store, { keys: ctx.listKeys ?? [], keepPlain: ctx.listKeepPlain ?? false });
  const digest = digestOf(json);
  if (!force && digest === ctx.store.lastPublishedDigest) return { published: false, url: null };
  const url = await publishJson(ctx.config.publish, JSON.stringify(json, null, 1), ctx.githubToken);
  ctx.store.markPublished(digest);
  ctx.store.save();
  // 人数は、中身のある部分から数える（鍵つきだけのときは、最初の鍵の区画）
  const keyedOnly = json.keyed !== undefined && !json.plain;
  const counted = keyedOnly && json.keyed ? json.keyed[0] : json;
  const memberCount = counted.members ? `, members=${Object.keys(counted.members).length}` : "";
  const mode = json.keyed ? `, 鍵 ${json.keyed.length} 本${json.plain ? "＋鍵なしの部分" : ""}` : "";
  ctx.log(`公開しました: ${url} (access=${Object.keys(counted.access).length}, credits=${keyedOnly ? "暗号化" : json.credits.length}${memberCount}${mode})`);
  return { published: true, url };
}

/** 全メンバー走査 → ランク更新 → 共通ロール適用 → JSON 公開 */
export async function runSync(ctx: SyncContext, guild: Guild, reason: string): Promise<SyncResult> {
  const result: SyncResult = {
    scanned: 0, active: 0, inGrace: 0, rolesAdded: 0, rolesRemoved: 0, members: 0,
    published: false, publishUrl: null, errors: [],
  };
  const now = new Date();
  // 定期の同期は、何も変わらなければ Discord のログチャンネルに流さない（ファイルには残す）
  const routine = reason === "interval";
  const quiet = ctx.logQuiet ?? ctx.log;
  let stateChanged = false;
  (routine ? quiet : ctx.log)(`sync 開始 (${reason})`);

  const members = await guild.members.fetch();
  const seen = new Set<string>();

  for (const member of members.values()) {
    if (member.user.bot) continue;
    result.scanned++;
    const activeRank = rankFromRoles(ctx.config, member.roles.cache.keys());
    const rec = ctx.store.get(member.id) ?? (activeRank > 0 ? ctx.store.getOrCreate(member.id) : null);
    if (!rec) continue;
    seen.add(member.id);
    rec.discordTag = member.user.tag;
    const before = { active: rec.activeRank, effective: rec.effectiveRank, grace: rec.graceUntil };
    updateEffectiveRank(ctx.config, rec, activeRank, now);
    if (before.active !== rec.activeRank || before.effective !== rec.effectiveRank || before.grace !== rec.graceUntil) {
      stateChanged = true;
      ctx.log(`状態変化 ${member.user.tag} (${member.id}): active ${before.active}->${rec.activeRank}, effective ${before.effective}->${rec.effectiveRank}, grace ${before.grace ?? "-"}->${rec.graceUntil ?? "-"}`);
    }
    if (rec.effectiveRank > 0 && !rec.banned) result.active++;
    if (rec.graceUntil) result.inGrace++;

    // メンバーは支援と別の軸。在籍日数は、今サーバーにいる期間で数える
    rec.joinedAt = member.joinedAt ? member.joinedAt.toISOString() : null;
    const wasMember = rec.memberActive;
    rec.memberActive = isMemberEligible(ctx.config, rec, now);
    if (wasMember !== rec.memberActive) {
      stateChanged = true;
      ctx.log(`メンバー ${member.user.tag} (${member.id}): ${wasMember ? "有効" : "無効"} -> ${rec.memberActive ? "有効" : "無効"}`);
    }
    if (rec.memberActive) result.members++;

    // BAN 中の人がサーバーにいる場合（Discord 側の BAN だけ解かれた等）は、ロールを付けない
    await applyRoles(ctx.config, member, rec.banned ? 0 : rec.effectiveRank, rec.memberActive, result, ctx.log);
  }

  // サーバーを抜けた（支援サイト Bot にキックされた等）メンバー: ロール操作は不可、猶予だけ進める
  for (const rec of ctx.store.all()) {
    if (seen.has(rec.discordId)) continue;
    const beforeEff = rec.effectiveRank;
    updateEffectiveRank(ctx.config, rec, 0, now);
    if (beforeEff !== rec.effectiveRank) {
      stateChanged = true;
      ctx.log(`退出済みメンバー ${rec.discordTag ?? rec.discordId}: effective ${beforeEff}->${rec.effectiveRank}`);
    }
    if (rec.effectiveRank > 0 && !rec.banned) result.active++;
    if (rec.graceUntil) result.inGrace++;
    // サーバーを抜けたらメンバーではなくなる。入り直したときは、在籍日数を数え直す
    if (rec.memberActive) {
      stateChanged = true;
      ctx.log(`メンバー ${rec.discordTag ?? rec.discordId}: 有効 -> 無効（サーバーを退出）`);
    }
    rec.memberActive = false;
    rec.joinedAt = null;
  }

  ctx.store.save();

  try {
    const pub = await publishIfChanged(ctx, false);
    result.published = pub.published;
    result.publishUrl = pub.url;
  } catch (err) {
    const msg = `公開失敗: ${String(err)}`;
    result.errors.push(msg);
    ctx.log(msg);
  }

  const changed = stateChanged || result.rolesAdded > 0 || result.rolesRemoved > 0 || result.published || result.errors.length > 0;
  (routine && !changed ? quiet : ctx.log)(
    `sync 完了: scanned=${result.scanned} active=${result.active} grace=${result.inGrace} members=${result.members} ` +
      `+${result.rolesAdded}/-${result.rolesRemoved} roles, published=${result.published}`,
  );
  return result;
}
