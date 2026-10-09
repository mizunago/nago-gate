// 管理者用の一覧。申請のメッセージはほかのログで流れてしまうので、まとめて見られるようにする（2026-10-10 発注者）。
//   📨 申請中の人: まだ決めていない住人の申請と、認定済みで本人の同意待ちの人
//   🧾 登録がまだの支援者: 支援サイトのロールは付いているが、VRChat の表示名を登録していない人（ワールドに入れない）
// コマンドの一覧のチャンネルのボタンと、/vrc-admin applications・unregistered から出す。返事は押した人にだけ見える。
import type { Guild } from "discord.js";
import { tierByRank, type AppConfig } from "./config.js";
import { fmtDate } from "./register.js";
import type { MemberRecord, Store } from "./store.js";

export const LIST_IDS = {
  applications: "sg:admin:list:applications",
  unregistered: "sg:admin:list:unregistered",
} as const;

/** 1 つの返事の長さ（2000 文字まで。少し余らせる） */
const MAX_LEN = 1900;

export interface ListReply {
  /** 返事を、2000 文字に収まるように分けたもの（1 つ目を返事、残りを続きとして送る） */
  chunks: string[];
  /** 名前で出すために、メンションの相手に入れる人（返事は押した人にだけ見えるので、通知は飛ばない） */
  userIds: string[];
}

function daysAgo(iso: string, now: Date): number {
  return Math.max(0, Math.floor((now.getTime() - new Date(iso).getTime()) / 86_400_000));
}

function byTime(key: (r: MemberRecord) => string | null): (a: MemberRecord, b: MemberRecord) => number {
  return (a, b) => (key(a) ?? "").localeCompare(key(b) ?? "");
}

function chunk(lines: string[]): string[] {
  const out: string[] = [];
  let cur = "";
  for (const line of lines) {
    if (cur.length + line.length + 1 > MAX_LEN) {
      out.push(cur);
      cur = "";
    }
    cur += (cur ? "\n" : "") + line;
  }
  if (cur) out.push(cur);
  return out;
}

/** 申請のチャンネルで Bot が出した申請のメッセージを探す（申請した人の ID → メッセージの URL。直近 100 件の中の、新しい物） */
async function reviewLinks(config: AppConfig, guild: Guild): Promise<Map<string, string>> {
  const links = new Map<string, string>();
  const channelId = config.member?.reviewChannelId ?? config.commandsChannelId ?? config.logChannelId;
  if (!channelId) return links;
  const ch = await guild.channels.fetch(channelId).catch(() => null);
  if (!ch || !ch.isTextBased() || !("messages" in ch)) return links;
  const msgs = await ch.messages.fetch({ limit: 100 }).catch(() => null);
  for (const m of msgs?.values() ?? []) {
    const hit = /申請した人: <@(\d+)>/.exec(m.content);
    if (hit && !links.has(hit[1])) links.set(hit[1], m.url);
  }
  return links;
}

/** 📨 申請中の人（まだ決めていない申請と、認定済みで本人の同意待ち） */
export async function applicationsList(config: AppConfig, store: Store, guild: Guild, now: Date = new Date()): Promise<ListReply> {
  const recs = store.all().filter((r) => !r.banned);
  const pending = recs.filter((r) => r.memberAppliedAt && !r.memberApprovedAt).sort(byTime((r) => r.memberAppliedAt));
  const waiting = recs.filter((r) => r.memberApprovedAt && !r.memberConsentAt && !r.memberManual).sort(byTime((r) => r.memberApprovedAt));
  const links = await reviewLinks(config, guild);
  const away = (r: MemberRecord): string => (r.joinedAt === null ? "、サーバーにいない" : "");

  const lines = [`## 📨 まだ決めていない住人の申請（${pending.length} 人。古い順）`];
  if (pending.length === 0) lines.push("ありません。");
  for (const r of pending) {
    const verify = r.verifiedAt ? "、本人確認: 済み" : r.verifyCode ? `、本人確認: 頼んだ（${r.verifyCode}）` : "";
    const link = links.get(r.discordId);
    lines.push(
      `・<@${r.discordId}>（${r.discordTag ?? "-"}）VRChat: **${r.vrcName ?? "未登録"}**、申請 ${fmtDate(r.memberAppliedAt)}（${daysAgo(r.memberAppliedAt!, now)} 日前）${verify}${away(r)}${link ? `　[申請のメッセージ](${link})` : ""}`,
    );
  }
  lines.push("", `## ⏳ 認定済みで、本人の同意待ち（${waiting.length} 人。古い順）`);
  if (waiting.length === 0) lines.push("ありません。");
  else lines.push("本人が「🏠 住人」から案内に同意すると、住人になります。");
  for (const r of waiting) {
    lines.push(`・<@${r.discordId}>（${r.discordTag ?? "-"}）VRChat: **${r.vrcName ?? "未登録"}**、認定 ${fmtDate(r.memberApprovedAt)}（${daysAgo(r.memberApprovedAt!, now)} 日前）${away(r)}`);
  }
  return { chunks: chunk(lines), userIds: [...pending, ...waiting].map((r) => r.discordId) };
}

/** 🧾 登録がまだの支援者（支援サイトのロールはあるが、VRChat の表示名を登録していない人） */
export function unregisteredList(config: AppConfig, store: Store, now: Date = new Date()): ListReply {
  const recs = store
    .all()
    .filter((r) => !r.banned && r.activeRank > 0 && !r.vrcName)
    .sort(byTime((r) => r.joinedAt));
  const lines = [
    `## 🧾 表示名の登録がまだの支援者（${recs.length} 人。参加の古い順）`,
    "支援サイトのロールは付いているが、VRChat の表示名を登録していない人です。",
    "ワールドが読むリストに載らないので、まだワールドに入れません。",
  ];
  if (recs.length === 0) lines.push("ありません。");
  for (const r of recs) {
    const tier = tierByRank(config, r.activeRank);
    const joined = r.joinedAt ? `、サーバーに参加 ${fmtDate(r.joinedAt)}（${daysAgo(r.joinedAt, now)} 日前）` : "、サーバーにいない";
    lines.push(`・<@${r.discordId}>（${r.discordTag ?? "-"}）${tier ? tier.label : `ランク ${r.activeRank}`}${joined}`);
  }
  lines.push("", "支援サイトで支援していても、Discord をつないでいない人は、ここに出ません（Bot からは見えないため）。");
  return { chunks: chunk(lines), userIds: recs.map((r) => r.discordId) };
}

/** 一覧を返す（先に「考え中」にしてから、1 つ目を返事に、残りを続きとして送る。どれも押した人にだけ見える） */
export async function sendList(
  interaction: { editReply(o: object): Promise<unknown>; followUp(o: object): Promise<unknown> },
  list: ListReply,
): Promise<void> {
  const allowedMentions = { users: list.userIds.slice(0, 100) };
  await interaction.editReply({ content: list.chunks[0] ?? "ありません。", allowedMentions });
  for (const more of list.chunks.slice(1)) await interaction.followUp({ content: more, ephemeral: true, allowedMentions });
}
