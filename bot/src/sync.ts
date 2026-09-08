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
}

export interface SyncResult {
  scanned: number;
  active: number;
  inGrace: number;
  rolesAdded: number;
  rolesRemoved: number;
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
  result: SyncResult,
  log: (m: string) => void,
): Promise<void> {
  const desired = desiredRoleIds(config, effectiveRank);
  const managed = new Set(config.tiers.map((t) => t.roleId));
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
  return {
    v: 1,
    generatedAt: nowIso(),
    tiers: config.tiers.map((t: TierConfig) => ({ id: t.id, rank: t.rank, label: t.label, color: t.color })),
    access,
    credits,
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
  ctx.log(`公開しました: ${url} (access=${Object.keys(json.access).length}, credits=${json.credits.length})`);
  return { published: true, url };
}

/** 全メンバー走査 → ランク更新 → 共通ロール適用 → JSON 公開 */
export async function runSync(ctx: SyncContext, guild: Guild, reason: string): Promise<SyncResult> {
  const result: SyncResult = {
    scanned: 0, active: 0, inGrace: 0, rolesAdded: 0, rolesRemoved: 0,
    published: false, publishUrl: null, errors: [],
  };
  const now = new Date();
  ctx.log(`sync 開始 (${reason})`);

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
      ctx.log(`状態変化 ${member.user.tag} (${member.id}): active ${before.active}->${rec.activeRank}, effective ${before.effective}->${rec.effectiveRank}, grace ${before.grace ?? "-"}->${rec.graceUntil ?? "-"}`);
    }
    if (rec.effectiveRank > 0) result.active++;
    if (rec.graceUntil) result.inGrace++;
    await applyRoles(ctx.config, member, rec.effectiveRank, result, ctx.log);
  }

  // サーバーを抜けた（支援サイト Bot にキックされた等）メンバー: ロール操作は不可、猶予だけ進める
  for (const rec of ctx.store.all()) {
    if (seen.has(rec.discordId)) continue;
    const beforeEff = rec.effectiveRank;
    updateEffectiveRank(ctx.config, rec, 0, now);
    if (beforeEff !== rec.effectiveRank) ctx.log(`退出済みメンバー ${rec.discordTag ?? rec.discordId}: effective ${beforeEff}->${rec.effectiveRank}`);
    if (rec.effectiveRank > 0) result.active++;
    if (rec.graceUntil) result.inGrace++;
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

  ctx.log(
    `sync 完了: scanned=${result.scanned} active=${result.active} grace=${result.inGrace} ` +
      `+${result.rolesAdded}/-${result.rolesRemoved} roles, published=${result.published}`,
  );
  return result;
}
