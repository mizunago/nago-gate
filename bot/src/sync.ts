import { createHash } from "node:crypto";
import type { Guild, GuildMember } from "discord.js";
import type { AppConfig, TierConfig } from "./config.js";
import { hashName } from "./hash.js";
import { publishJson } from "./publish.js";
import type { MemberRecord, Store } from "./store.js";

export interface SyncContext {
  config: AppConfig;
  store: Store;
  githubToken: string | null;
  log: (msg: string) => void;
  /** Discord のログチャンネルに流さないログ。無ければ log を使う */
  logQuiet?: (msg: string) => void;
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
  v: 1;
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
 * 通常は、同意・名前の登録・在籍日数。管理者が手動で認定した人は、名前の登録とサーバーへの在籍だけでよい
 */
export function isMemberEligible(config: AppConfig, rec: MemberRecord, now: Date): boolean {
  if (!config.member || !rec.vrcName) return false;
  if (rec.memberManual) return rec.joinedAt !== null;
  if (!rec.memberConsentAt) return false;
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

export function buildSupportersJson(config: AppConfig, store: Store): SupportersJson {
  const access: Record<string, number> = {};
  const credits: { n: string; r: number }[] = [];
  for (const rec of store.all()) {
    if (rec.effectiveRank <= 0 || !rec.vrcName) continue;
    access[hashName(rec.vrcName)] = rec.effectiveRank;
    if (rec.showCredit) credits.push({ n: rec.vrcName, r: rec.effectiveRank });
  }
  credits.sort((a, b) => b.r - a.r || a.n.localeCompare(b.n, "ja"));

  let members: Record<string, number> | null = null;
  if (config.member) {
    members = {};
    for (const rec of store.all()) {
      if (rec.memberActive && rec.vrcName) members[hashName(rec.vrcName)] = 1;
    }
  }

  return {
    v: 1,
    generatedAt: nowIso(),
    tiers: config.tiers.map((t: TierConfig) => ({ id: t.id, rank: t.rank, label: t.label, color: t.color })),
    access,
    credits,
    ...(members ? { members } : {}),
    ...linksOf(config),
  };
}

function digestOf(json: SupportersJson): string {
  const { generatedAt: _ignored, ...rest } = json;
  return createHash("sha256").update(JSON.stringify(rest)).digest("hex");
}

/** JSON を（変化があれば）公開する */
export async function publishIfChanged(ctx: SyncContext, force: boolean): Promise<{ published: boolean; url: string | null }> {
  const json = buildSupportersJson(ctx.config, ctx.store);
  const digest = digestOf(json);
  if (!force && digest === ctx.store.lastPublishedDigest) return { published: false, url: null };
  const url = await publishJson(ctx.config.publish, JSON.stringify(json, null, 1), ctx.githubToken);
  ctx.store.markPublished(digest);
  ctx.store.save();
  const memberCount = json.members ? `, members=${Object.keys(json.members).length}` : "";
  ctx.log(`公開しました: ${url} (access=${Object.keys(json.access).length}, credits=${json.credits.length}${memberCount})`);
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
    if (rec.effectiveRank > 0) result.active++;
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

    await applyRoles(ctx.config, member, rec.effectiveRank, rec.memberActive, result, ctx.log);
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
    if (rec.effectiveRank > 0) result.active++;
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
