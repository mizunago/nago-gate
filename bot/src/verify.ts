// 住人の申請の本人確認と、表示名の登録のやり直し（どちらも管理者がボタンで始める）。
//
// 本人確認: 申請した人の Discord と VRChat の名前がかけ離れていて、同じ人か分からないときに使う。
//   管理者が「🔑 本人確認を頼む」を押すと、Bot が確認の文字（nago-XXXX）を決めて、本人に送る文を出す（コピー用と、「DM で送る」）。
//   本人が VRChat のプロフィールの「ステータス」か「自己紹介」にその文字を入れ、住人のボタンから「確かめる」を押すと、
//   Bot が VRChat のプロフィールを読み、文字があれば本人確認が済んだとして、申請のチャンネルで管理者に知らせる。
//   VRChat の API は、本人がボタンを押したときだけ使う（1 人 30 秒に 1 回まで）。
// 登録のやり直し: 表示名を間違えて登録した人（30 日に 1 回の制限にかかる）を、すぐに登録し直せるようにする。
//
// 管理者のボタンは、どれも押すと説明が出るだけで、そこでもう一度押して初めて動く（閉じるときに誤って押しても困らないように）。
import { randomInt } from "node:crypto";
import { ActionRowBuilder, ButtonBuilder, ButtonStyle, type ButtonInteraction, type Guild } from "discord.js";
import { channelRefs } from "./channels.js";
import type { AppConfig } from "./config.js";
import { t, type Lang } from "./i18n.js";
import { log } from "./log.js";
import { fmtDate } from "./register.js";
import type { Store } from "./store.js";
import type { VrcGroupAccess } from "./sync.js";
import { TESTER_IDS } from "./tester.js";

export const VERIFY_IDS = {
  /** 管理者: 本人確認の説明と、本人に送る文を出す（後ろに userId） */
  menu: "sg:admin:verify-menu:",
  /** 管理者: 本人確認のお願いを、Bot から DM で送る（後ろに userId） */
  dm: "sg:admin:verify-dm:",
  /** 本人: 確かめる（VRChat のプロフィールを読む） */
  check: "sg:verify:check",
} as const;

export const RENAME_IDS = {
  /** 管理者: 表示名の登録の制限を見る（後ろに userId） */
  menu: "sg:admin:rename-menu:",
  /** 管理者: すぐに登録し直せるようにする（後ろに userId） */
  reset: "sg:admin:rename-reset:",
  /** 管理者: 登録し直せることを、Bot から DM で知らせる（後ろに userId） */
  dm: "sg:admin:rename-dm:",
} as const;

/** 本人が「確かめる」を押せる間隔 */
const CHECK_GAP_MS = 30_000;
/** 確認の文字に使う字（見分けにくい 0・O・1・I・L は使わない） */
const CODE_CHARS = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

export interface AdminReply {
  content: string;
  components: ActionRowBuilder<ButtonBuilder>[];
}

/** 入口のボタンの名前（何をするかが分かる名前にする。説明は adminActionLegend の文で添える） */
export const ACTION_LABELS = { tester: "協力者にする", verify: "本人確認を頼む", rename: "名前の登録をやり直させる" } as const;

/** 管理者の操作の入口のボタン（人を調べた結果と、申請のメッセージに付ける）。どれも押すと説明が出るだけ */
export function adminActionRow(userId: string, withTester: boolean): ActionRowBuilder<ButtonBuilder> {
  const row = new ActionRowBuilder<ButtonBuilder>();
  if (withTester) row.addComponents(new ButtonBuilder().setCustomId(`${TESTER_IDS.menu}${userId}`).setLabel(ACTION_LABELS.tester).setStyle(ButtonStyle.Secondary).setEmoji("🧪"));
  row.addComponents(
    new ButtonBuilder().setCustomId(`${VERIFY_IDS.menu}${userId}`).setLabel(ACTION_LABELS.verify).setStyle(ButtonStyle.Secondary).setEmoji("🔑"),
    new ButtonBuilder().setCustomId(`${RENAME_IDS.menu}${userId}`).setLabel(ACTION_LABELS.rename).setStyle(ButtonStyle.Secondary).setEmoji("🔄"),
  );
  return row;
}

/** 入口のボタンの説明（ボタンの名前だけでは、押すと何が起こるか分かりにくいため。2026-10-10 発注者） */
export function adminActionLegend(withTester: boolean): string {
  return [
    "**下のボタン**（押すと説明が出るだけで、すぐには何も変わりません）",
    ...(withTester ? [`🧪 ${ACTION_LABELS.tester}: 期間を決めて、ワールドに支援者と同じに入れる（ボードには載らない）`] : []),
    `🔑 ${ACTION_LABELS.verify}: 名前がかけ離れていて本人か分からないとき、VRChat のプロフィールに文字を入れてもらって確かめる`,
    `🔄 ${ACTION_LABELS.rename}: 表示名を間違えて登録した人を、30 日を待たずに登録し直せるようにする`,
  ].join("\n");
}

function newCode(): string {
  let s = "nago-";
  for (let i = 0; i < 4; i++) s += CODE_CHARS[randomInt(CODE_CHARS.length)];
  return s;
}

function dmText(config: AppConfig, key: "verify.dm" | "rename.dm", vars: Record<string, string>): string {
  return (["ja", "en"] as Lang[]).map((l) => t(l, key, { ...channelRefs(config, l), ...vars })).join("\n\n");
}

// ---- 本人確認（管理者） ----

/** 「🔑 本人確認を頼む」を押したとき。確認の文字が無ければ決めて、本人に送る文（コピー用）と「DM で送る」を出す */
export function verifyMenu(config: AppConfig, store: Store, userId: string, now: Date = new Date()): AdminReply {
  const rec = store.get(userId);
  if (!rec || !rec.vrcName) return { content: `<@${userId}> は、まだ VRChat の表示名を登録していません。\n本人確認は、表示名の登録のあとに頼めます。`, components: [] };
  const head = `<@${userId}>（${rec.discordTag ?? "-"}、VRChat: **${rec.vrcName}**）`;
  if (rec.verifiedAt) return { content: `✅ ${head} の本人確認は済んでいます（${fmtDate(rec.verifiedAt)}、確認の文字 ${rec.verifyCode ?? "-"}）。`, components: [] };
  if (!rec.verifyCode) {
    rec.verifyCode = newCode();
    rec.verifyRequestedAt = now.toISOString();
    rec.updatedAt = now.toISOString();
    store.save();
    log.info(`本人確認の文字を決めた ${rec.discordTag ?? "-"} (${userId}): ${rec.verifyCode}`);
  }
  const content = [
    `${head} に、本人確認を頼みます。`,
    `VRChat のプロフィールに入れてもらう文字: **${rec.verifyCode}**`,
    "本人が住人のボタンから「確かめる」を押すと、Bot が VRChat のプロフィールを読んで確かめ、済んだら申請のチャンネルに知らせます。",
    "下の「DM で送る」で、Bot から本人に頼めます。",
    "自分で送るときは、次の文をコピーしてください。",
    "```",
    dmText(config, "verify.dm", { code: rec.verifyCode }),
    "```",
  ].join("\n");
  const row = new ActionRowBuilder<ButtonBuilder>().addComponents(
    new ButtonBuilder().setCustomId(`${VERIFY_IDS.dm}${userId}`).setLabel("DM で送る").setStyle(ButtonStyle.Primary).setEmoji("✉️"),
  );
  return { content, components: [row] };
}

/** 「DM で送る」を押したとき */
export async function sendVerifyDm(config: AppConfig, store: Store, guild: Guild, userId: string, by: string): Promise<string> {
  const rec = store.get(userId);
  if (!rec || !rec.verifyCode) return "確認の文字がまだありません。\nもう一度「🔑 本人確認を頼む」を押してください。";
  if (rec.verifiedAt) return `<@${userId}> の本人確認は、もう済んでいます。`;
  const member = await guild.members.fetch(userId).catch(() => null);
  if (!member) return `<@${userId}> は、もうサーバーにいません。`;
  try {
    await member.send({ content: dmText(config, "verify.dm", { code: rec.verifyCode }) });
  } catch {
    log.info(`本人確認の DM が届かない ${rec.discordTag ?? "-"} (${userId}) by ${by}`);
    return `<@${userId}> に DM が届きませんでした（受け取らない設定）。\n前の返事の文をコピーして、ほかの方法で送ってください。`;
  }
  log.info(`本人確認の DM を送った ${rec.discordTag ?? "-"} (${userId}) code=${rec.verifyCode} by ${by}`);
  return `<@${userId}> に、本人確認のお願いを DM で送りました（確認の文字 ${rec.verifyCode}）。\n本人が「確かめる」を押して確認が済むと、申請のチャンネルに知らせます。`;
}

// ---- 本人確認（本人） ----

/** 住人のボタンを押したときに、本人確認を頼まれていれば、お願いの文とボタンを返す */
export function verifyPrompt(store: Store, userId: string, lang: Lang): { text: string; row: ActionRowBuilder<ButtonBuilder> } | null {
  const rec = store.get(userId);
  if (!rec || !rec.verifyCode || rec.verifiedAt) return null;
  const row = new ActionRowBuilder<ButtonBuilder>().addComponents(
    new ButtonBuilder().setCustomId(VERIFY_IDS.check).setLabel(t(lang, "verify.button")).setStyle(ButtonStyle.Primary).setEmoji("🔑"),
  );
  return { text: t(lang, "verify.prompt", { code: rec.verifyCode }), row };
}

/** 本人が「確かめる」を押したとき。VRChat のプロフィールに確認の文字があれば、本人確認を済ませ、申請のチャンネルで知らせる */
export async function checkVerify(
  config: AppConfig,
  store: Store,
  access: VrcGroupAccess | null,
  interaction: ButtonInteraction<"cached">,
  lang: Lang,
  now: Date = new Date(),
): Promise<void> {
  const rec = store.get(interaction.user.id);
  const who = `${interaction.user.tag} (${interaction.user.id})`;
  if (!rec || !rec.verifyCode || rec.verifiedAt || !rec.vrcName) {
    await interaction.reply({ content: t(lang, "verify.already"), ephemeral: true });
    return;
  }
  if (rec.verifyCheckedAt) {
    const left = CHECK_GAP_MS - (now.getTime() - new Date(rec.verifyCheckedAt).getTime());
    if (left > 0) {
      await interaction.reply({ content: t(lang, "verify.wait", { seconds: String(Math.ceil(left / 1000)) }), ephemeral: true });
      return;
    }
  }
  if (!access) {
    log.warn(`本人確認: VRChat の API を使えないので、確かめられない ${who}`);
    await interaction.reply({ content: t(lang, "verify.noApi"), ephemeral: true });
    return;
  }
  await interaction.deferReply({ ephemeral: true });
  rec.verifyCheckedAt = now.toISOString();
  store.save();
  try {
    const user = await access.client.findUserByDisplayName(rec.vrcName);
    if (!user) {
      log.info(`本人確認: VRChat に表示名が見つからない ${who} name=${rec.vrcName}`);
      await interaction.editReply({ content: t(lang, "verify.userNotFound", { name: rec.vrcName }) });
      return;
    }
    const profile = await access.client.getProfile(user.id);
    const code = rec.verifyCode.toLowerCase();
    const found = `${profile.bio}\n${profile.statusDescription}`.toLowerCase().includes(code);
    if (!found) {
      log.info(`本人確認: まだ見つからない ${who} name=${rec.vrcName} code=${rec.verifyCode}`);
      await interaction.editReply({ content: t(lang, "verify.notFound", { code: rec.verifyCode }) });
      return;
    }
    rec.verifiedAt = now.toISOString();
    rec.vrcUserId = user.id;
    rec.updatedAt = now.toISOString();
    store.save();
    log.info(`本人確認が済んだ ${who} name=${rec.vrcName} code=${rec.verifyCode}`);
    await notifyVerified(config, interaction.guild, interaction.user.id, rec.discordTag ?? interaction.user.tag, rec.vrcName, rec.verifyCode, user.id);
    await interaction.editReply({ content: t(lang, "verify.ok") });
  } catch (err) {
    log.warn(`本人確認: VRChat のプロフィールを読めなかった ${who}: ${String(err)}`);
    await interaction.editReply({ content: t(lang, "verify.noApi") });
  }
}

async function notifyVerified(config: AppConfig, guild: Guild, userId: string, tag: string, vrcName: string, code: string, vrcUserId: string): Promise<void> {
  const channelId = config.member?.reviewChannelId ?? config.commandsChannelId ?? config.logChannelId;
  const channel = channelId ? await guild.channels.fetch(channelId).catch(() => null) : null;
  if (!channel || !channel.isTextBased()) return;
  const ownerId = guild.ownerId;
  const content = [
    `🔑 **本人確認が済みました** <@${ownerId}>`,
    `<@${userId}>（${tag}）の VRChat のプロフィールに、確認の文字「${code}」がありました。`,
    `VRChat の表示名: **${vrcName}**　https://vrchat.com/home/user/${vrcUserId}`,
    "申請のメッセージで、認定するか見送るかを決めてください。",
  ].join("\n");
  // 申請のチャンネルは管理者しか見られないので、申請した人をメンションの相手に入れても通知は飛ばない（名前で出すため）
  await channel.send({ content, allowedMentions: { users: [ownerId, userId] }, flags: 4 }).catch((err) => log.warn(`本人確認の知らせを出せなかった: ${String(err)}`));
}

// ---- 登録のやり直し（管理者） ----

/** 「🔄 名前の登録をやり直させる」を押したとき。今の制限と、外すボタンを出す */
export function renameMenu(config: AppConfig, store: Store, userId: string, now: Date = new Date()): AdminReply {
  const rec = store.get(userId);
  if (!rec || !rec.vrcName) return { content: `<@${userId}> は、まだ表示名を登録していません。\n登録の制限はかかっていません。`, components: [] };
  const head = `<@${userId}>（${rec.discordTag ?? "-"}）の表示名は **${rec.vrcName}** です。`;
  const next = rec.nameChangedAt ? new Date(new Date(rec.nameChangedAt).getTime() + config.nameChangeCooldownDays * 86_400_000) : null;
  if (!next || next <= now) {
    return {
      content: `${head}\n今すぐ登録し直せます（制限はかかっていません）。\n本人に知らせるときは、下の「DM で知らせる」か、次の文をコピーしてください。\n\`\`\`\n${dmText(config, "rename.dm", {})}\n\`\`\``,
      components: [renameDmRow(userId)],
    };
  }
  const row = new ActionRowBuilder<ButtonBuilder>().addComponents(
    new ButtonBuilder().setCustomId(`${RENAME_IDS.reset}${userId}`).setLabel("登録し直せるようにする").setStyle(ButtonStyle.Primary).setEmoji("🔄"),
  );
  return {
    content: [
      head,
      `${fmtDate(next.toISOString())} まで変えられません（${config.nameChangeCooldownDays} 日に 1 回）。`,
      "間違えて登録した場合は、下のボタンで、すぐに登録し直せるようにできます。",
      "本人が登録し直すと、ワールドのリストも新しい名前に変わります。",
    ].join("\n"),
    components: [row],
  };
}

/** 「登録し直せるようにする」を押したとき */
export function resetRename(config: AppConfig, store: Store, userId: string, by: string, now: Date = new Date()): AdminReply {
  const rec = store.get(userId);
  if (!rec || !rec.vrcName) return { content: `<@${userId}> は、まだ表示名を登録していません。`, components: [] };
  rec.nameChangedAt = null;
  rec.updatedAt = now.toISOString();
  store.save();
  log.info(`管理者 登録のやり直しを許可 ${rec.discordTag ?? "-"} (${userId}) name=${rec.vrcName} by ${by}`);
  const pending = config.member?.mode === "apply" && rec.memberAppliedAt && !rec.memberApprovedAt;
  return {
    content: [
      `<@${userId}> は、今すぐ表示名を登録し直せます。`,
      ...(pending ? ["住人の申請中なので、登録し直したあとに「くわしく」で新しい表示名を確かめてください。"] : []),
      "本人に知らせるときは、下の「DM で知らせる」か、次の文をコピーしてください。",
      "```",
      dmText(config, "rename.dm", {}),
      "```",
    ].join("\n"),
    components: [renameDmRow(userId)],
  };
}

function renameDmRow(userId: string): ActionRowBuilder<ButtonBuilder> {
  return new ActionRowBuilder<ButtonBuilder>().addComponents(
    new ButtonBuilder().setCustomId(`${RENAME_IDS.dm}${userId}`).setLabel("DM で知らせる").setStyle(ButtonStyle.Primary).setEmoji("✉️"),
  );
}

/** 「DM で知らせる」を押したとき */
export async function sendRenameDm(config: AppConfig, store: Store, guild: Guild, userId: string, by: string): Promise<string> {
  const rec = store.get(userId);
  const member = await guild.members.fetch(userId).catch(() => null);
  if (!member) return `<@${userId}> は、もうサーバーにいません。`;
  try {
    await member.send({ content: dmText(config, "rename.dm", {}) });
  } catch {
    return `<@${userId}> に DM が届きませんでした（受け取らない設定）。\n前の返事の文をコピーして、ほかの方法で送ってください。`;
  }
  log.info(`登録のやり直しの DM を送った ${rec?.discordTag ?? "-"} (${userId}) by ${by}`);
  return `<@${userId}> に、登録し直せることを DM で知らせました。`;
}
