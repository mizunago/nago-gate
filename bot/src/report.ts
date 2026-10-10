// 持ち主向けの確認。1 人について、手続きのどこまで済んでいて、何がまだかを並べる（読むだけ。Discord には書き込まない）。
// 管理の「人を調べる」ボタン、申請のメッセージの「くわしく」ボタン、/vrc-admin lookup から使う。
import type { Guild, GuildMember } from "discord.js";
import { channelRefs } from "./channels.js";
import { tierByRank, type AppConfig } from "./config.js";
import { normalizeName } from "./hash.js";
import { fmtDate, fmtTime, nameChangeTimes } from "./register.js";
import type { MemberRecord, Store } from "./store.js";
import { desiredRoleIds, isMemberEligible, isTesterActive, rankFromRoles } from "./sync.js";

/** サーバーにいる本人の、今のロール */
export interface LivePerson {
  roleIds: string[];
  joinedAt: Date | null;
  roleName: (id: string) => string;
}

const DONE = "✅";
const WAIT = "⏳";
const TODO = "❌";
const NONE = "➖";

/** 支援のティアの書き方。ロールのメンションにすると、Discord がロールの色で出す（通知は飛ばさない設定で送る） */
export function tierMention(config: AppConfig, rank: number): string {
  const tier = tierByRank(config, rank);
  return tier ? `<@&${tier.roleId}>` : "なし";
}

/** 1 人ぶんの、手続きの進み具合 */
export function personReport(config: AppConfig, rec: MemberRecord | null, live: LivePerson | null, now: Date): string {
  const ch = channelRefs(config, "ja");
  const lines: string[] = [`**手続きの進み具合**（${DONE} 済み　${WAIT} 待ち　${TODO} まだ　${NONE} 関係なし）`];
  const nexts: string[] = [];

  // サーバー
  if (live) lines.push(`${DONE} サーバー: 参加している（${live.joinedAt ? fmtDate(live.joinedAt.toISOString()) : "日時不明"}）`);
  else {
    lines.push(`${TODO} サーバー: いない`);
    nexts.push("本人にサーバーへ入ってもらう（支援者なら Patreon の Discord 連携から、それ以外は招待リンクから）");
  }

  if (rec?.banned) {
    lines.push(`${TODO} BAN 中（${fmtDate(rec.bannedAt)}${rec.banReason ? `、${rec.banReason}` : ""}）、どのリストにも載らない`);
    return lines.join("\n") + "\n**次にやること**: 入れるようにするなら `/vrc-admin unban`";
  }

  // 登録（Discord と VRChat をつなぐ）
  if (rec?.vrcName) {
    // 次に表示名を変えられる日（30 日に 1 回。登録から少しのうちは、本人が打ち間違いを直せる）。
    // 間違えて登録した人は「🔄 名前の登録をやり直させる」で、すぐに登録し直せるようにできる
    const { next: nextChange, fixUntil } = nameChangeTimes(config, rec, now);
    const fixable = fixUntil ? `（${fmtTime(fixUntil)} までは、本人が打ち間違いを直せる）` : "";
    const locked = nextChange && nextChange > now ? `、次に変えられるのは ${fmtDate(nextChange.toISOString())}${fixable}` : "";
    // URL のすぐ後ろに文を続けると、Discord が文までリンクにしてしまう。リンクの文字で囲んで行の最後に置く（<> で VRChat のカードも出さない）
    const profile = rec.vrcUserId ? `　[VRChat のプロフィール](<https://vrchat.com/home/user/${rec.vrcUserId}>)` : "";
    lines.push(`${DONE} 登録（VRChat とつなぐ）: **${rec.vrcName}**${locked}${profile}`);
  } else {
    lines.push(`${TODO} 登録（VRChat とつなぐ）: まだ`);
    nexts.push(`本人に、${ch.register} で 🧾 登録 を押して、VRChat の表示名を入れてもらう`);
  }

  // 本人確認（管理者が「🔑 本人確認を頼む」で頼んだときだけ出す）
  if (rec?.verifiedAt) lines.push(`${DONE} 本人確認: 済み（${fmtDate(rec.verifiedAt)}、VRChat のプロフィールに確認の文字 ${rec.verifyCode ?? "-"} があった）`);
  else if (rec?.verifyCode) {
    lines.push(`${WAIT} 本人確認: 頼んだ（${fmtDate(rec.verifyRequestedAt)}、確認の文字 ${rec.verifyCode}）、本人が「確かめる」を押す待ち`);
    nexts.push(`本人に、VRChat のプロフィールに ${rec.verifyCode} を入れて、${ch.resident} の 🏠 住人 から「確かめる」を押してもらう`);
  }

  // 支援（協力者の枠は含めない。Supporter・Platinum のロールは、支援のランクで付く）
  const effective = rec?.supportRank ?? 0;
  const liveRank = live ? rankFromRoles(config, live.roleIds) : 0;
  const graceActive = !!rec && rec.graceUntil !== null && new Date(rec.graceUntil) > now && rec.graceRank > 0;
  const manualActive = !!rec && rec.manualRank > 0 && (rec.manualUntil === null || new Date(rec.manualUntil) > now);
  if (effective > 0) {
    const missing = live ? [...desiredRoleIds(config, effective)].filter((id) => !live.roleIds.includes(id)) : [];
    const why = liveRank > 0 ? "支援サイトのロールあり" : graceActive ? `猶予中、${fmtDate(rec!.graceUntil)} まで` : manualActive ? "手動の付与" : "";
    if (missing.length === 0) lines.push(`${DONE} 支援: ${tierMention(config, effective)}（${why}）`);
    else {
      lines.push(`${WAIT} 支援: ${tierMention(config, effective)}、Bot のロールが、まだ付いていない（${missing.map((id) => live!.roleName(id)).join("・")}）`);
      nexts.push("`/vrc-admin sync` を打つ（それでも付かなければ、Bot のロールが Supporter・Platinum より上にあるかを見る）");
    }
  } else if (liveRank > 0) {
    lines.push(`${WAIT} 支援: 支援サイトのロールはあるが、Bot がまだ読み取っていない`);
    nexts.push("`/vrc-admin sync` を打つ");
  } else {
    lines.push(`${NONE} 支援: なし`);
  }

  // 協力者の枠（ワールドには支援者と同じに入れる。ボードには載らない）
  const tester = !!rec && !rec.banned && isTesterActive(rec, now);
  if (tester) {
    const roleNote = config.tester.roleId && live && !live.roleIds.includes(config.tester.roleId) ? "、協力者のロールがまだ付いていない（次の同期で付く）" : "";
    lines.push(`${DONE} 協力者: ${tierMention(config, rec!.testerRank)} 相当、${fmtDate(rec!.testerUntil)} まで（ボードには載らない${roleNote}）`);
  }

  // 住人
  const mc = config.member;
  if (mc && rec) {
    const resident = isMemberEligible(config, rec, now);
    if (rec.memberManual) {
      if (rec.memberActive || resident) lines.push(`${DONE} 住人: 住人（手動で認定）`);
      else {
        lines.push(`${WAIT} 住人: 手動で認定済み、表示名の登録待ち`);
      }
    } else if (rec.memberActive || resident) {
      lines.push(`${DONE} 住人: 住人（同意 ${fmtDate(rec.memberConsentAt)}）`);
    } else if (mc.mode === "apply" && rec.memberApprovedAt && !rec.memberConsentAt) {
      lines.push(`${WAIT} 住人: 認定済み（${fmtDate(rec.memberApprovedAt)}）、本人の同意待ち`);
      nexts.push(`本人に、${ch.resident} で 🏠 住人 を押して、案内に同意してもらう`);
    } else if (rec.memberAppliedAt) {
      lines.push(`${WAIT} 住人: 申請中（${fmtDate(rec.memberAppliedAt)}）、あなたの確認待ち`);
      nexts.push("申請のチャンネルのメッセージで、認定するか見送るかを決める");
    } else if (rec.memberDeclinedAt) {
      const again = new Date(new Date(rec.memberDeclinedAt).getTime() + mc.reapplyDays * 86_400_000);
      lines.push(`${NONE} 住人: 見送り（${fmtDate(rec.memberDeclinedAt)}）、${again > now ? `${fmtDate(again.toISOString())} から申請し直せる` : "もう申請し直せる"}`);
    } else {
      lines.push(`${NONE} 住人: 申請していない`);
    }
  } else if (mc) {
    lines.push(`${NONE} 住人: 申請していない`);
  }

  // グループ
  const canGroup = effective > 0 || !!rec?.memberActive;
  if (config.group) {
    if (rec?.groupRequestedAt) lines.push(`${DONE} グループ: 👥 グループ を押した（${fmtDate(rec.groupRequestedAt)}）、招待の結果は Bot のログ`);
    else if (canGroup) {
      lines.push(`${TODO} グループ: まだ押していない`);
      nexts.push(`Group に入りたいなら、本人に ${ch.group} で 👥 グループ を押してもらう`);
    } else lines.push(`${NONE} グループ: 支援者か住人になると押せる`);
  }

  // クレジットとリスト
  if (rec && effective > 0) lines.push(`${rec.showCredit ? DONE : NONE} クレジット: ${rec.showCredit ? "ON（支援者のボードに名前が出る）" : "OFF（ボードに名前を出さない）"}`);
  const supporterListed = !!rec && !rec.banned && (effective > 0 || tester) && !!rec.vrcName;
  const residentListed = !!rec && !!mc && isMemberEligible(config, rec, now);
  const lists = [supporterListed ? "支援者のリスト" : "", residentListed ? "住人のリスト" : ""].filter(Boolean);
  lines.push(`${lists.length > 0 ? DONE : NONE} ワールドのリスト: ${lists.length > 0 ? `${lists.join("と")}に載る。ワールドへの反映は最長 10 分ほど` : "載らない"}`);

  const next = nexts[0] ?? "ありません（手続きは済んでいます）";
  return lines.join("\n") + `\n**次にやること**: ${next}`;
}

export interface ResolvedPerson {
  userId: string;
  rec: MemberRecord | null;
  member: GuildMember | null;
}

/** Discord の ID が分かっているときに、その人を引く（申請のメッセージの「くわしく」、/vrc-admin lookup） */
export async function personById(guild: Guild, store: Store, userId: string): Promise<ResolvedPerson[]> {
  const member = await guild.members.fetch(userId).catch(() => null);
  const rec = store.get(userId);
  return member || rec ? [{ userId, rec, member }] : [];
}

/**
 * 入力（Discord のユーザー名・サーバーでの名前・ID・メンション、または VRChat の表示名）から、1 人を探す。
 * 見つからなければ空、何人も当たれば全員を返す
 */
export async function resolvePerson(guild: Guild, store: Store, query: string): Promise<ResolvedPerson[]> {
  const q = query.trim().replace(/^@/, "");
  if (q.length === 0) return [];
  const byId = /^<@!?(\d{15,21})>$/.exec(q)?.[1] ?? (/^\d{15,21}$/.test(q) ? q : null);
  const found = new Map<string, ResolvedPerson>();
  const add = async (userId: string, member: GuildMember | null = null): Promise<void> => {
    if (found.has(userId)) return;
    const m = member ?? (await guild.members.fetch(userId).catch(() => null));
    found.set(userId, { userId, rec: store.get(userId), member: m });
  };
  if (byId) {
    await add(byId);
    return [...found.values()];
  }
  // 記録から: Discord のユーザー名（#0 の付く古い形も）と、VRChat の表示名
  const lower = q.toLowerCase();
  const norm = normalizeName(q);
  for (const rec of store.all()) {
    const tag = (rec.discordTag ?? "").toLowerCase();
    if (tag === lower || tag.replace(/#0$/, "") === lower || (rec.vrcName && normalizeName(rec.vrcName) === norm)) await add(rec.discordId);
  }
  if (found.size > 0) return [...found.values()];
  // サーバーから: ユーザー名・表示名・ニックネームの前方一致
  const hits = await guild.members.search({ query: q, limit: 5 }).catch(() => null);
  for (const m of hits?.values() ?? []) await add(m.id, m);
  return [...found.values()];
}

/**
 * 探した結果を返すときのメンションの設定。名前が出るよう、当たった人をメンションの相手に入れる
 * （入れないと、Discord が名前を出せず「不明なユーザー」と出る）。返事は本人にだけ見えるので、当たった人に通知は飛ばない
 */
export function mentionPeople(people: ResolvedPerson[]): { users: string[] } {
  return { users: people.map((p) => p.userId).slice(0, 100) };
}

/** 探した結果を、持ち主に返す文にする */
export function resolvedReport(config: AppConfig, guild: Guild, query: string, people: ResolvedPerson[], now: Date): string {
  if (people.length === 0) return `「${query}」に当たる人は、サーバーにも記録にもいません。\nDiscord のユーザー名か、VRChat の表示名で試してください。`;
  if (people.length > 1) {
    const list = people.map((p) => `・<@${p.userId}>（${p.member?.user.username ?? p.rec?.discordTag ?? "-"}${p.rec?.vrcName ? `、VRChat: ${p.rec.vrcName}` : ""}）`).join("\n");
    return `「${query}」に当たる人が ${people.length} 人います。\nユーザー名を正確に入れ直してください。\n${list}`;
  }
  const p = people[0];
  const live: LivePerson | null = p.member
    ? { roleIds: [...p.member.roles.cache.keys()], joinedAt: p.member.joinedAt, roleName: (id) => guild.roles.cache.get(id)?.name ?? id }
    : null;
  const head = `<@${p.userId}>（${p.member?.user.username ?? p.rec?.discordTag ?? "-"}）`;
  return `${head}\n${personReport(config, p.rec, live, now)}`;
}
