// 登録処理の本体。スラッシュコマンド / ボタン+フォーム / テキスト投稿の 3 経路から共通で呼ぶ。
import { channelRefs } from "./channels.js";
import type { AppConfig } from "./config.js";
import { hasUnsafeNameChars, normalizeName } from "./hash.js";
import { t, type Lang } from "./i18n.js";
import type { MemberRecord, Store } from "./store.js";
import { tierByRank } from "./config.js";
import { log } from "./log.js";
import { isTesterActive, memberEligibleFrom } from "./sync.js";

export interface RegisterInput {
  discordId: string;
  discordTag: string;
  name: string;
  credit: boolean | null;
  lang: Lang;
}

export interface RegisterResult {
  ok: boolean;
  message: string;
  changed: boolean;
}

export function fmtDate(iso: string | null): string {
  if (!iso) return "-";
  return `<t:${Math.floor(new Date(iso).getTime() / 1000)}:D>`;
}

/** 時刻だけ（見る人の時間帯で出る） */
export function fmtTime(d: Date): string {
  return `<t:${Math.floor(d.getTime() / 1000)}:t>`;
}

/**
 * 表示名を次に変えられる日と、打ち間違いを直せる期限。
 * 登録・変更から nameFixMinutes 分のうちは、何度でも直せる（登録の直後に打ち間違いに気づく人が多いため。2026-10-10 発注者）。
 * 直しても 30 日の数え始めは動かさないので、直せる時間は延びない。期限を過ぎていれば fixUntil は null
 */
export function nameChangeTimes(config: AppConfig, rec: MemberRecord, now: Date = new Date()): { next: Date | null; fixUntil: Date | null } {
  if (!rec.nameChangedAt) return { next: null, fixUntil: null };
  const at = new Date(rec.nameChangedAt).getTime();
  const fixUntil = new Date(at + config.nameFixMinutes * 60_000);
  return { next: new Date(at + config.nameChangeCooldownDays * 86_400_000), fixUntil: fixUntil > now ? fixUntil : null };
}

/** 住人の状態を 1 行で表す */
export function memberState(config: AppConfig, rec: MemberRecord, lang: Lang): string {
  if (rec.banned) return t(lang, "member.state.none");
  if (rec.memberManual) return t(lang, rec.memberActive ? "member.state.manual" : "member.state.manualNeedName");
  if (config.member?.mode === "apply") {
    if (rec.memberApprovedAt && !rec.memberConsentAt) return t(lang, "member.state.approved", channelRefs(config, lang));
    if (rec.memberAppliedAt) return t(lang, "member.state.applied");
    if (rec.memberDeclinedAt && !rec.memberConsentAt) {
      const again = new Date(new Date(rec.memberDeclinedAt).getTime() + config.member.reapplyDays * 86_400_000);
      if (again > new Date()) return t(lang, "member.state.declined", { date: fmtDate(again.toISOString()) });
    }
  }
  if (!rec.memberConsentAt) return t(lang, "member.state.none");
  if (rec.memberActive) return t(lang, "member.state.active");
  const from = memberEligibleFrom(config, rec);
  return t(lang, "member.state.pending", { date: from ? fmtDate(from.toISOString()) : "-" });
}

export function describe(config: AppConfig, rec: MemberRecord | null, lang: Lang): string {
  if (!rec) return t(lang, "status.none");
  const tier = tierByRank(config, rec.effectiveRank);
  const lines = [
    `${t(lang, "status.name")}: ${rec.vrcName ? `**${rec.vrcName}**` : t(lang, "status.unregistered")}`,
    `${t(lang, "status.rank")}: ${tier ? `${tier.label} (${tier.rank})` : t(lang, "status.rankNone")}`,
    `${t(lang, "status.link")}: ${rec.activeRank > 0 ? t(lang, "status.active") : t(lang, "status.inactive")}`,
    `${t(lang, "status.credit")}: ${t(lang, rec.showCredit ? "on" : "off")}`,
  ];
  if (config.member) lines.push(`${t(lang, "member.label")}: ${memberState(config, rec, lang)}`);
  if (rec.groupRequestedAt) lines.push(`${t(lang, "group.label")}: ${t(lang, "group.requested", { date: fmtDate(rec.groupRequestedAt) })}`);
  if (rec.banned) lines.push(`BAN: ${fmtDate(rec.bannedAt)}${rec.banReason ? ` (${rec.banReason})` : ""}`);
  if (rec.graceUntil) lines.push(`${t(lang, "status.grace")}: ${fmtDate(rec.graceUntil)}`);
  if (rec.manualRank > 0) {
    const v = rec.manualUntil
      ? t(lang, "status.manualUntil", { rank: rec.manualRank, date: fmtDate(rec.manualUntil) })
      : t(lang, "status.manualForever", { rank: rec.manualRank });
    lines.push(`${t(lang, "status.manual")}: ${v}`);
  }
  if (!rec.banned && isTesterActive(rec, new Date())) {
    const testerTier = tierByRank(config, rec.testerRank);
    lines.push(`${t(lang, "status.tester")}: ${t(lang, "status.testerUntil", { tier: testerTier ? testerTier.label : String(rec.testerRank), date: fmtDate(rec.testerUntil) })}`);
  }
  const { next, fixUntil } = nameChangeTimes(config, rec);
  if (next) lines.push(`${t(lang, "status.nextChange")}: ${next > new Date() ? fmtDate(next.toISOString()) : t(lang, "status.now")}`);
  if (fixUntil) lines.push(t(lang, "status.fixUntil", { time: fmtTime(fixUntil) }));
  return lines.join("\n");
}

export function validateName(config: AppConfig, raw: string, lang: Lang): { ok: true; name: string } | { ok: false; reason: string } {
  const name = raw.trim();
  if (name.length === 0) return { ok: false, reason: t(lang, "err.name.empty") };
  if (name.length > config.maxNameLength) return { ok: false, reason: t(lang, "err.name.tooLong", { max: config.maxNameLength }) };
  if (hasUnsafeNameChars(name)) return { ok: false, reason: t(lang, "err.name.badChars") };
  return { ok: true, name };
}

/** 名前を登録する。戻り値の message はそのままユーザーへ返せる */
export function registerName(config: AppConfig, store: Store, input: RegisterInput): RegisterResult {
  const { lang } = input;
  const v = validateName(config, input.name, lang);
  if (!v.ok) {
    log.warn(`登録拒否 ${input.discordTag} (${input.discordId}): ${v.reason} name=${JSON.stringify(input.name)}`);
    return { ok: false, changed: false, message: t(lang, "err.cannotRegister", { reason: v.reason }) };
  }

  // BAN 中の人は登録できない（Discord 側の BAN だけが解かれて、入り直した場合）
  if (store.get(input.discordId)?.banned) {
    log.warn(`登録拒否 ${input.discordTag} (${input.discordId}): BAN 中 name=${JSON.stringify(input.name)}`);
    return { ok: false, changed: false, message: t(lang, "err.banned") };
  }

  const rec = store.getOrCreate(input.discordId);
  rec.discordTag = input.discordTag;
  const normalized = normalizeName(v.name);
  const dup = store.findByNormalizedName(normalized, normalizeName);
  if (dup && dup.discordId !== input.discordId) {
    log.warn(`登録拒否 ${input.discordTag} (${input.discordId}): 重複 name=${v.name} 既存=${dup.discordTag ?? dup.discordId}`);
    return { ok: false, changed: false, message: t(lang, "err.duplicateName") };
  }

  const now = new Date();
  const changed = !rec.vrcName || normalizeName(rec.vrcName) !== normalized;
  const old = rec.vrcName;
  let fixing = false;
  if (changed && rec.vrcName && rec.nameChangedAt) {
    const { next, fixUntil } = nameChangeTimes(config, rec, now);
    if (fixUntil) fixing = true;
    else if (next && next > now) {
      // 入力の間違いをすぐ直そうとした人もここで止まる。管理者が気づけるよう、外し方も書く
      log.warn(
        `表示名の変更を断った（${config.nameChangeCooldownDays} 日に 1 回の制限） ${input.discordTag} (${input.discordId}): ${rec.vrcName} -> ${v.name}、次に変えられるのは ${next.toISOString()}。` +
          "入力の間違いの直しなら、人を調べて「🔄 名前の登録をやり直させる」で制限を外せる",
      );
      return {
        ok: false,
        changed: false,
        message: t(lang, "err.cooldown", { days: config.nameChangeCooldownDays, date: fmtDate(next.toISOString()) }),
      };
    }
  }

  rec.vrcName = v.name;
  // 打ち間違いの直しでは、30 日の数え始めを動かさない
  if (changed && !fixing) rec.nameChangedAt = now.toISOString();
  if (input.credit !== null) rec.showCredit = input.credit;
  rec.updatedAt = now.toISOString();
  store.save();
  const fix = fixing ? ` （登録から ${config.nameFixMinutes} 分のうちの、打ち間違いの直し: ${old} -> ${v.name}）` : "";
  log.info(`登録 ${input.discordTag} (${input.discordId}): name=${v.name} changed=${changed} credit=${rec.showCredit} effectiveRank=${rec.effectiveRank}${fix}`);
  if (changed && old && rec.memberAppliedAt && !rec.memberApprovedAt) {
    // 申請のメッセージには、申請したときの表示名が出たまま。認定すると、今の表示名がワールドのリストに載る
    log.warn(`住人の申請中の人が表示名を変えた ${input.discordTag} (${input.discordId}): ${old} -> ${v.name}。申請のメッセージの表示名は古いままで、認定すると新しい名前でリストに載る`);
  }

  const message =
    `${t(lang, "registered")}\n${describe(config, rec, lang)}\n` +
    t(lang, rec.effectiveRank > 0 ? "registered.active" : "registered.inactive") +
    `\n\n${t(lang, "share.notice")}`;
  return { ok: true, changed: true, message };
}

/** テキスト投稿から「/vrc register name:xxx credit:true」風の記述を抜き出す */
export function parseTextRegister(text: string): { name: string; credit: boolean | null } | null {
  const m = text.trim().match(/^\/?\s*vrc\s+register\s+(.+)$/i);
  if (!m) return null;
  let rest = m[1].trim();
  let credit: boolean | null = null;
  const cm = rest.match(/\bcredit\s*[:=]\s*(true|false)\s*$/i);
  if (cm) {
    credit = cm[1].toLowerCase() === "true";
    rest = rest.slice(0, cm.index).trim();
  }
  rest = rest.replace(/^name\s*[:=]\s*/i, "").trim();
  if (!rest) return null;
  return { name: rest, credit };
}
