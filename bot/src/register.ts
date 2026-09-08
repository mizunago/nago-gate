// 登録処理の本体。スラッシュコマンド / ボタン+フォーム / テキスト投稿の 3 経路から共通で呼ぶ。
import type { AppConfig } from "./config.js";
import { normalizeName } from "./hash.js";
import { t, type Lang } from "./i18n.js";
import type { MemberRecord, Store } from "./store.js";
import { tierByRank } from "./config.js";

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

export function describe(config: AppConfig, rec: MemberRecord | null, lang: Lang): string {
  if (!rec) return t(lang, "status.none");
  const tier = tierByRank(config, rec.effectiveRank);
  const lines = [
    `${t(lang, "status.name")}: ${rec.vrcName ? `**${rec.vrcName}**` : t(lang, "status.unregistered")}`,
    `${t(lang, "status.rank")}: ${tier ? `${tier.label} (${tier.rank})` : t(lang, "status.rankNone")}`,
    `${t(lang, "status.link")}: ${rec.activeRank > 0 ? t(lang, "status.active") : t(lang, "status.inactive")}`,
    `${t(lang, "status.credit")}: ${t(lang, rec.showCredit ? "on" : "off")}`,
  ];
  if (rec.graceUntil) lines.push(`${t(lang, "status.grace")}: ${fmtDate(rec.graceUntil)}`);
  if (rec.manualRank > 0) {
    const v = rec.manualUntil
      ? t(lang, "status.manualUntil", { rank: rec.manualRank, date: fmtDate(rec.manualUntil) })
      : t(lang, "status.manualForever", { rank: rec.manualRank });
    lines.push(`${t(lang, "status.manual")}: ${v}`);
  }
  if (rec.nameChangedAt) {
    const next = new Date(new Date(rec.nameChangedAt).getTime() + config.nameChangeCooldownDays * 86_400_000);
    lines.push(`${t(lang, "status.nextChange")}: ${next > new Date() ? fmtDate(next.toISOString()) : t(lang, "status.now")}`);
  }
  return lines.join("\n");
}

export function validateName(config: AppConfig, raw: string, lang: Lang): { ok: true; name: string } | { ok: false; reason: string } {
  const name = raw.trim();
  if (name.length === 0) return { ok: false, reason: t(lang, "err.name.empty") };
  if (name.length > config.maxNameLength) return { ok: false, reason: t(lang, "err.name.tooLong", { max: config.maxNameLength }) };
  if (/[\r\n\t]/.test(name)) return { ok: false, reason: t(lang, "err.name.badChars") };
  return { ok: true, name };
}

/** 名前を登録する。戻り値の message はそのままユーザーへ返せる */
export function registerName(config: AppConfig, store: Store, input: RegisterInput): RegisterResult {
  const { lang } = input;
  const v = validateName(config, input.name, lang);
  if (!v.ok) return { ok: false, changed: false, message: t(lang, "err.cannotRegister", { reason: v.reason }) };

  const rec = store.getOrCreate(input.discordId);
  rec.discordTag = input.discordTag;
  const normalized = normalizeName(v.name);
  const dup = store.findByNormalizedName(normalized, normalizeName);
  if (dup && dup.discordId !== input.discordId) {
    return { ok: false, changed: false, message: t(lang, "err.duplicateName") };
  }

  const now = new Date();
  const changed = !rec.vrcName || normalizeName(rec.vrcName) !== normalized;
  if (changed && rec.vrcName && rec.nameChangedAt) {
    const next = new Date(new Date(rec.nameChangedAt).getTime() + config.nameChangeCooldownDays * 86_400_000);
    if (next > now) {
      return {
        ok: false,
        changed: false,
        message: t(lang, "err.cooldown", { days: config.nameChangeCooldownDays, date: fmtDate(next.toISOString()) }),
      };
    }
  }

  rec.vrcName = v.name;
  if (changed) rec.nameChangedAt = now.toISOString();
  if (input.credit !== null) rec.showCredit = input.credit;
  rec.updatedAt = now.toISOString();
  store.save();

  const message =
    `${t(lang, "registered")}\n${describe(config, rec, lang)}\n` +
    t(lang, rec.effectiveRank > 0 ? "registered.active" : "registered.inactive");
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
