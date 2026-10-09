// 協力者の枠。デバッグなどを手伝ってくれる、支援者ではない人を、期間を決めて支援者と同じにワールドへ入れる。
// 支援者のボードには載せず、Supporter・Platinum のロールも付けない（支援者向けのチャンネルは見えない）。
// config.tester.roleId があれば、誰が協力者か分かるように、そのロールだけ付ける。
// 管理者のコマンド（/vrc-admin tester-grant・tester-revoke・testers）と、人を調べた結果のボタンの両方から使う。
import { ActionRowBuilder, ButtonBuilder, ButtonStyle, type Guild } from "discord.js";
import type { AppConfig } from "./config.js";
import { tierByRank } from "./config.js";
import { log } from "./log.js";
import { fmtDate } from "./register.js";
import type { ResolvedPerson } from "./report.js";
import { tierMention } from "./report.js";
import type { Store } from "./store.js";
import { isTesterActive, updateEffectiveRank } from "./sync.js";

export const TESTER_IDS = {
  /** 協力者の操作を出す（後ろに userId）。人を調べた結果には、このボタンだけを置く（誤って押しても、すぐには何も変わらない） */
  menu: "sg:admin:tester-menu:",
  /** 協力者にする（後ろに "<rank>:<userId>"）。押すと、日数を入れる欄が出る */
  grant: "sg:admin:tester:",
  /** 日数を入れる欄（後ろに "<rank>:<userId>"） */
  modal: "sg:admin:tester-modal:",
  modalDays: "days",
  /** 協力者を外す（後ろに userId） */
  revoke: "sg:admin:untester:",
} as const;

export const TESTER_MAX_DAYS = 365;

export interface TesterResult {
  ok: boolean;
  message: string;
}

/** 協力者にする（もう協力者なら、ランクと期間を今日から付け直す） */
export async function grantTester(
  config: AppConfig,
  store: Store,
  guild: Guild,
  userId: string,
  rank: number,
  days: number,
  by: string,
  now: Date = new Date(),
): Promise<TesterResult> {
  const tier = tierByRank(config, rank);
  if (!tier) return { ok: false, message: `ランク ${rank} は config.tiers にありません。` };
  if (!Number.isInteger(days) || days < 1 || days > TESTER_MAX_DAYS) return { ok: false, message: `日数は 1 から ${TESTER_MAX_DAYS} の整数にしてください。` };
  const member = await guild.members.fetch(userId).catch(() => null);
  const rec = store.getOrCreate(userId);
  if (rec.banned) return { ok: false, message: `<@${userId}> は BAN 中です。\n先に \`/vrc-admin unban\` で解除してください。` };
  if (member) rec.discordTag = member.user.tag;
  rec.testerRank = rank;
  rec.testerUntil = new Date(now.getTime() + days * 86_400_000).toISOString();
  updateEffectiveRank(config, rec, rec.activeRank, now);
  store.save();
  log.info(`管理者 協力者にする ${rec.discordTag ?? "-"} (${userId}) rank=${rank} days=${days} until=${rec.testerUntil} by ${by}`);
  await setTesterRole(config, guild, userId, true);

  const lines = [
    `<@${userId}> を協力者にしました（${tierMention(config, rank)} 相当、${fmtDate(rec.testerUntil)} まで）。`,
    "ワールドには、支援者と同じに入れます。",
    "支援者のボードには載らず、支援者のロールも付きません。",
  ];
  if (rec.supportRank >= rank) lines.push(`支援のランク（${tierMention(config, rec.supportRank)}）のほうが高いか同じなので、入れる場所は変わりません。`);
  if (!rec.vrcName) lines.push("VRChat の表示名が、まだ登録されていません。\n本人が登録するか、`/vrc-admin setname` で設定すると、ワールドに反映されます。");
  else lines.push("ワールドへの反映は、最長 10 分ほどです。");
  return { ok: true, message: lines.join("\n") };
}

/** 協力者の枠を外す */
export async function revokeTester(config: AppConfig, store: Store, guild: Guild, userId: string, by: string, now: Date = new Date()): Promise<TesterResult> {
  const rec = store.get(userId);
  if (!rec || rec.testerRank <= 0) return { ok: false, message: `<@${userId}> は協力者ではありません。` };
  rec.testerRank = 0;
  rec.testerUntil = null;
  updateEffectiveRank(config, rec, rec.activeRank, now);
  store.save();
  log.info(`管理者 協力者を外す ${rec.discordTag ?? "-"} (${userId}) by ${by}`);
  await setTesterRole(config, guild, userId, false);
  return { ok: true, message: `<@${userId}> を協力者から外しました。\nワールドへの反映は、最長 10 分ほどです。` };
}

/** 今の協力者の一覧 */
export function testerList(config: AppConfig, store: Store, now: Date = new Date()): string {
  const testers = store
    .all()
    .filter((r) => isTesterActive(r, now))
    .sort((a, b) => (a.testerUntil ?? "").localeCompare(b.testerUntil ?? ""));
  if (testers.length === 0) return "今、協力者はいません。";
  const lines = testers.map(
    (r) =>
      `・<@${r.discordId}>（${r.discordTag ?? "-"}、VRChat: ${r.vrcName ?? "未登録"}）: ${tierMention(config, r.testerRank)} 相当、${fmtDate(r.testerUntil)} まで${r.banned ? "（BAN 中のため無効）" : ""}`,
  );
  return `**協力者**（${testers.length} 人、期限の近い順）\n${lines.join("\n")}`;
}

/** 「🧪 協力者…」を押したときの返事（説明と、協力者にする・外すのボタン） */
export function testerMenu(config: AppConfig, store: Store, userId: string, now: Date = new Date()): { content: string; components: ActionRowBuilder<ButtonBuilder>[] } {
  const rec = store.get(userId);
  const lines = [
    `<@${userId}>（${rec?.discordTag ?? "-"}）を協力者にするときは、ランクを選んでください。`,
    "次に日数を入れると、協力者になります（日数の画面で取り消せば、何も変わりません）。",
    "協力者は、その期間だけ、ワールドに支援者と同じに入れます。",
    "支援者のボードには載らず、Supporter・Platinum のロールも付きません。",
  ];
  if (rec && isTesterActive(rec, now)) lines.unshift(`今は協力者です（${tierMention(config, rec.testerRank)} 相当、${fmtDate(rec.testerUntil)} まで）。\n選び直すと、ランクと期間を今日から付け直します。`);
  return { content: lines.join("\n"), components: testerButtons(config, [{ userId, rec, member: null }], now) };
}

/** 協力者にする・外すのボタン（「🧪 協力者…」を押したあとの返事に付ける） */
export function testerButtons(config: AppConfig, people: ResolvedPerson[], now: Date = new Date()): ActionRowBuilder<ButtonBuilder>[] {
  if (people.length !== 1) return [];
  const p = people[0];
  const row = new ActionRowBuilder<ButtonBuilder>();
  for (const tier of config.tiers.slice(0, 4)) {
    row.addComponents(
      new ButtonBuilder()
        .setCustomId(`${TESTER_IDS.grant}${tier.rank}:${p.userId}`)
        .setLabel(`協力者にする（${tier.label}）`)
        .setStyle(ButtonStyle.Secondary)
        .setEmoji("🧪"),
    );
  }
  if (p.rec && isTesterActive(p.rec, now)) {
    row.addComponents(new ButtonBuilder().setCustomId(`${TESTER_IDS.revoke}${p.userId}`).setLabel("協力者を外す").setStyle(ButtonStyle.Danger));
  }
  return row.components.length > 0 ? [row] : [];
}

/** "<rank>:<userId>" を読む */
export function parseRankUser(rest: string): { rank: number; userId: string } | null {
  const m = /^(\d+):(\w+)$/.exec(rest);
  return m ? { rank: Number(m[1]), userId: m[2] } : null;
}

/** 協力者のロール（設定があれば）を付ける・外す。失敗しても、次の同期が付け直す */
async function setTesterRole(config: AppConfig, guild: Guild, userId: string, on: boolean): Promise<void> {
  const roleId = config.tester.roleId;
  if (!roleId) return;
  const member = await guild.members.fetch(userId).catch(() => null);
  if (!member) return;
  const op = on ? member.roles.add(roleId, "SupporterGate tester") : member.roles.remove(roleId, "SupporterGate tester");
  await op.catch((err) => log.warn(`協力者のロールの更新に失敗 ${member.user.tag}: ${String(err)}`));
}
