// 登録チャンネルに置くボタンパネルと、ボタン / フォーム / テキスト投稿の処理。
import {
  ActionRowBuilder,
  ButtonBuilder,
  ButtonInteraction,
  ButtonStyle,
  Message,
  MessageFlags,
  ModalBuilder,
  ModalSubmitInteraction,
  TextInputBuilder,
  TextInputStyle,
  type MessageCreateOptions,
} from "discord.js";
import { isAdmin } from "./admin.js";
import { tierByRank, type AppConfig, type MemberConfig } from "./config.js";
import { langOf, t, type Lang } from "./i18n.js";
import { log } from "./log.js";
import { describe, fmtDate, memberState, parseTextRegister, registerName } from "./register.js";
import type { MemberRecord } from "./store.js";
import { isMemberEligible, memberEligibleFrom, type SyncContext } from "./sync.js";
import { bringIntoGroup, type VrcProfile } from "./vrchat.js";

export const IDS = {
  register: "sg:register",
  status: "sg:status",
  creditOn: "sg:credit:on",
  creditOff: "sg:credit:off",
  member: "sg:member",
  memberAgree: "sg:member:agree",
  memberLeave: "sg:member:leave",
  /** 申請の認定・見送り（管理者用）。後ろに、申請した人の Discord の ID が付く */
  memberApprove: "sg:member:approve:",
  memberDecline: "sg:member:decline:",
  group: "sg:group",
  modal: "sg:register-modal",
  modalName: "name",
} as const;

export interface PanelDeps extends SyncContext {
  requestPublish: () => void;
}

/** /vrc-admin panel で投稿する固定メッセージ（共有メッセージなので多言語併記） */
export function buildPanelMessage(config: AppConfig): MessageCreateOptions {
  const lines = [
    "## VRChat 支援者登録 / Supporter Registration / 支持者注册",
    "",
    "🇯🇵 **登録** ボタンを押して、VRChat の表示名（プロフィールに出ている名前）を入力してください。",
    "　　表示名を変えたら再度登録してください（30 日に 1 回）。",
    "🇬🇧 Press **Register** and enter your VRChat display name (the name shown on your profile).",
    "　　Register again if you change your display name (once every 30 days).",
    "🇨🇳 点击 **注册**，输入你的 VRChat 显示名称（个人资料上显示的名字）。更改名称后请重新注册（每 30 天一次）。",
    "🇰🇷 **등록** 버튼을 누르고 VRChat 표시 이름(프로필에 표시되는 이름)을 입력하세요. 이름을 바꾸면 다시 등록하세요(30일에 1회).",
  ];
  if (config.member?.mode === "apply") {
    lines.push(
      "",
      "🇯🇵 **メンバー** ボタン: メンバー限定の案内は、申請して、確認が済んだ方に見せています（18 歳以上の方のみ）。",
      "🇬🇧 **Membership** button: the member-only area is shown to those who apply and are approved (18+ only).",
      "🇨🇳 **成员** 按钮：成员限定区域仅向提出申请并通过确认的人开放（仅限 18 岁以上）。",
      "🇰🇷 **멤버** 버튼: 멤버 전용 안내는 신청 후 확인이 끝난 분께만 보입니다 (18세 이상).",
    );
  } else if (config.member) {
    const d = config.member.minDays;
    lines.push(
      "",
      `🇯🇵 **メンバー** ボタン: サーバーに参加して ${d} 日以上の方は、支援の有無に関係なく、メンバー限定の案内を見られます（18 歳以上の方のみ）。`,
      `🇬🇧 **Membership** button: after ${d} days on this server, you can see the member-only area, whether or not you are a supporter (18+ only).`,
      `🇨🇳 **成员** 按钮：加入本服务器满 ${d} 天后，无论是否支持，都可以查看成员限定区域（仅限 18 岁以上）。`,
      `🇰🇷 **멤버** 버튼: 서버 참가 후 ${d}일이 지나면 후원 여부와 관계없이 멤버 전용 안내를 볼 수 있습니다 (18세 이상).`,
    );
  }
  if (config.group) {
    lines.push(
      "",
      "👥 **グループ / Group** ボタン: 支援者とメンバーの方は、VRChat の Group に参加できます。フレンドでなくても、Group のインスタンスで一緒に遊べます。 / Supporters and members can join our VRChat Group and play together without being friends.",
    );
  }
  // 共有のお願いは、支援者にもメンバーにも共通。全員が読む場所なので、ここにも出す
  lines.push(
    "",
    "🇯🇵 **共有のお願い**: スクリーンショットや動画の投稿はかまいませんが、**ワールドを特定できる情報（ワールド名・リンク・ID・招待リンク）は載せないでください**。知り合いに見せるときも、ワールドの情報は別に伝えてください。**限定のワールドへのポータルを、パブリックのワールドで出さないでください**。",
    "🇬🇧 **Sharing**: you may post screenshots and videos, but **never include anything that identifies the world (name, link, ID, or invite link)**. Even with people you know, give the world information separately. **Never drop a portal to the private worlds in a public world.**",
    "🇨🇳 **分享**：可以发布截图和视频，但**请勿包含能识别世界的信息（名称、链接、ID、邀请链接）**。即使分享给认识的人，也请另行告知世界信息。**请勿在公开世界放置通往限定世界的传送门**。",
    "🇰🇷 **공유**: 스크린샷과 영상은 올려도 되지만, **월드를 특정할 수 있는 정보(이름, 링크, ID, 초대 링크)는 포함하지 마세요**. 아는 사람에게도 월드 정보는 따로 전달해 주세요. **공개 월드에서 한정 월드로 가는 포털을 열지 마세요.**",
  );
  lines.push(
    "",
    "-# スラッシュコマンド `/vrc register` も使えますが、コピペでは動きません。入力欄で `/` を打って候補から選んでください。",
    "-# `/vrc register` also works, but only when picked from the popup after typing `/` (pasting the text does nothing).",
  );

  const rows = [
    new ActionRowBuilder<ButtonBuilder>().addComponents(
      new ButtonBuilder().setCustomId(IDS.register).setLabel("登録 / Register").setStyle(ButtonStyle.Primary).setEmoji("🧾"),
      new ButtonBuilder().setCustomId(IDS.status).setLabel("状態 / Status").setStyle(ButtonStyle.Secondary),
      new ButtonBuilder().setCustomId(IDS.creditOn).setLabel("クレジット ON / Credits ON").setStyle(ButtonStyle.Success),
      new ButtonBuilder().setCustomId(IDS.creditOff).setLabel("クレジット OFF / Credits OFF").setStyle(ButtonStyle.Secondary),
    ),
  ];
  // メンバーとグループのボタンは、2 段目に並べる（設定されているものだけ）
  const second: ButtonBuilder[] = [];
  if (config.member) second.push(new ButtonBuilder().setCustomId(IDS.member).setLabel("メンバー / Membership").setStyle(ButtonStyle.Secondary).setEmoji("🔑"));
  if (config.group) second.push(new ButtonBuilder().setCustomId(IDS.group).setLabel("グループ / Group").setStyle(ButtonStyle.Secondary).setEmoji("👥"));
  if (second.length > 0) rows.push(new ActionRowBuilder<ButtonBuilder>().addComponents(...second));
  return { content: lines.join("\n"), components: rows };
}

/** 見送りのあと、次に申請できる日時。見送られていなければ null */
function reapplyFrom(mc: MemberConfig, rec: MemberRecord): Date | null {
  if (!rec.memberDeclinedAt) return null;
  return new Date(new Date(rec.memberDeclinedAt).getTime() + mc.reapplyDays * 86_400_000);
}

/** 申請制のとき: 今は申請できない理由（申請できるなら null） */
function applyBlocked(config: AppConfig, rec: MemberRecord, lang: Lang, now: Date): string | null {
  const mc = config.member;
  if (!mc) return t(lang, "member.unavailable");
  const again = reapplyFrom(mc, rec);
  if (again && again > now) return t(lang, "member.declinedWait", { date: fmtDate(again.toISOString()) });
  const from = memberEligibleFrom(config, rec);
  if (from && from > now) return t(lang, "member.applyTooEarly", { days: mc.minDays, date: fmtDate(from.toISOString()) });
  return null;
}

/** 管理者に見せる、申請の内容 */
function reviewContent(config: AppConfig, rec: MemberRecord, ownerId: string, profile: VrcProfile | null, profileNote: string, now: Date): string {
  const days = rec.joinedAt ? Math.floor((now.getTime() - new Date(rec.joinedAt).getTime()) / 86_400_000) : null;
  const tier = tierByRank(config, rec.effectiveRank);
  const lines = [
    `📨 **メンバーの申請** <@${ownerId}>`,
    `申請した人: <@${rec.discordId}>（${rec.discordTag ?? "-"}）`,
    `サーバーに参加: ${rec.joinedAt ? `${fmtDate(rec.joinedAt)}（${days} 日前）` : "不明"}`,
    `VRChat の表示名: **${rec.vrcName ?? "-"}**`,
  ];
  if (profile) {
    lines.push(`VRChat のプロフィール: https://vrchat.com/home/user/${profile.id}`);
    const facts = [
      profile.dateJoined ? `VRChat の登録日 ${profile.dateJoined}` : "",
      profile.trust ? `ランク ${profile.trust}` : "",
      profile.ageVerification ? `年齢確認 ${profile.ageVerification}` : "",
    ].filter((f) => f.length > 0);
    if (facts.length > 0) lines.push(facts.join(" / "));
    const bio = profile.bio.replace(/\s+/g, " ").trim();
    if (bio.length > 0) lines.push(`自己紹介: ${bio.slice(0, 200)}${bio.length > 200 ? "…" : ""}`);
  } else {
    lines.push(`VRChat のプロフィール: ${profileNote}`);
  }
  lines.push(`支援: ${tier ? tier.label : "なし"}`);
  lines.push("プロフィールを見て、下のボタンで決めてください。認定すると、すぐにメンバーのロールが付きます。");
  return lines.join("\n");
}

/** 申請を受け付けて、管理者のチャンネルに確認の依頼を出す */
async function submitApplication(deps: PanelDeps, interaction: ButtonInteraction<"cached">, lang: Lang, rec: MemberRecord, now: Date): Promise<void> {
  const { config, store } = deps;
  const mc = config.member;
  if (!mc) return;
  const who = `${interaction.user.tag} (${interaction.user.id})`;
  const blocked = applyBlocked(config, rec, lang, now);
  if (blocked) {
    await interaction.update({ content: blocked, components: [] });
    return;
  }
  if (rec.memberAppliedAt) {
    await interaction.update({ content: t(lang, "member.applied"), components: [] });
    return;
  }
  // VRChat への問い合わせに数秒かかるので、先に受け付けだけ返す
  await interaction.deferUpdate();
  rec.memberConsentAt = now.toISOString();
  rec.memberAppliedAt = now.toISOString();
  rec.memberDeclinedAt = null;
  rec.updatedAt = now.toISOString();
  store.save();
  log.info(`メンバーの申請 ${who}: VRChat の表示名=${rec.vrcName ?? "-"} 参加日=${rec.joinedAt ?? "-"} ランク=${rec.effectiveRank}`);

  // 申請した人の VRChat のプロフィール（公開されている範囲）を、確認用に添える
  let profile: VrcProfile | null = null;
  let profileNote = "VRChat の API を使っていないので、表示名で検索してください";
  const access = deps.vrc?.get() ?? null;
  if (access && rec.vrcName) {
    try {
      const user = await access.client.findUserByDisplayName(rec.vrcName);
      if (user) {
        if (rec.vrcUserId !== user.id) {
          rec.vrcUserId = user.id;
          store.save();
        }
        profile = await access.client.getProfile(user.id);
      } else {
        profileNote = "この表示名のユーザーが、VRChat で見つかりませんでした（表示名を変えたか、登録の間違いかもしれません）";
      }
    } catch (err) {
      profileNote = "取得できませんでした。表示名で検索してください";
      log.warn(`メンバーの申請: VRChat のプロフィールの取得に失敗 ${who}: ${String(err)}`);
    }
  }

  const channelId = mc.reviewChannelId ?? config.commandsChannelId ?? config.logChannelId;
  const channel = channelId ? await interaction.guild.channels.fetch(channelId).catch(() => null) : null;
  if (channel && channel.isTextBased()) {
    const ownerId = interaction.guild.ownerId;
    const row = new ActionRowBuilder<ButtonBuilder>().addComponents(
      new ButtonBuilder().setCustomId(IDS.memberApprove + rec.discordId).setLabel("認定する").setStyle(ButtonStyle.Success),
      new ButtonBuilder().setCustomId(IDS.memberDecline + rec.discordId).setLabel("見送る").setStyle(ButtonStyle.Secondary),
    );
    await channel
      .send({ content: reviewContent(config, rec, ownerId, profile, profileNote, now), components: [row], allowedMentions: { users: [ownerId] }, flags: MessageFlags.SuppressEmbeds })
      .catch((err) => log.warn(`メンバーの申請を、管理のチャンネルに出せませんでした ${who}: ${String(err)}。/vrc-admin member-grant で認定できます`));
  } else {
    log.warn(`メンバーの申請を出すチャンネルがありません ${who}。/vrc-admin member-grant で認定できます`);
  }
  await interaction.editReply({ content: t(lang, "member.applied"), components: [] });
}

/** 申請の認定・見送り（管理者が、申請のメッセージのボタンで行う） */
async function handleMemberReview(deps: PanelDeps, interaction: ButtonInteraction<"cached">): Promise<void> {
  const { config, store } = deps;
  const mc = config.member;
  if (!isAdmin(config, interaction.member)) {
    await interaction.reply({ content: "この操作は、管理者だけができます。", ephemeral: true });
    return;
  }
  const approve = interaction.customId.startsWith(IDS.memberApprove);
  const userId = interaction.customId.slice((approve ? IDS.memberApprove : IDS.memberDecline).length);
  const rec = store.get(userId);
  const by = interaction.user.tag;
  const now = new Date();
  // 結果を、申請のメッセージの下に書き足して、ボタンを外す
  const close = async (note: string): Promise<void> => {
    await interaction.update({ content: `${interaction.message.content}\n\n${note}`, components: [], allowedMentions: { parse: [] } });
  };
  if (!mc || !rec || !rec.memberAppliedAt) {
    await close("ℹ️ この申請は、もう処理済みか、取り下げられています。");
    return;
  }
  const who = `${rec.discordTag ?? "-"} (${userId})`;

  if (!approve) {
    rec.memberAppliedAt = null;
    rec.memberConsentAt = null;
    rec.memberDeclinedAt = now.toISOString();
    rec.memberManual = false;
    rec.memberActive = false;
    rec.updatedAt = now.toISOString();
    store.save();
    log.info(`メンバーの申請を見送り ${who} by ${by}`);
    await close(`⏸️ 見送りました（${by}、${fmtDate(now.toISOString())}）。本人には知らせていません。本人は「状態」のボタンで、見送りと、次に申請できる日を見られます。`);
    return;
  }

  if (rec.banned) {
    await interaction.reply({ content: "この人は BAN 中です。認定するなら、先に `/vrc-admin unban` で解除してください。", ephemeral: true });
    return;
  }
  const target = await interaction.guild.members.fetch(userId).catch(() => null);
  if (!target) {
    rec.memberAppliedAt = null;
    rec.memberConsentAt = null;
    rec.updatedAt = now.toISOString();
    store.save();
    await close("ℹ️ 申請した人は、もうサーバーにいません。申請を閉じました。");
    return;
  }
  // 認定は、手動の認定（member-grant）と同じ扱いにする
  rec.joinedAt = target.joinedAt ? target.joinedAt.toISOString() : rec.joinedAt;
  rec.memberManual = true;
  rec.memberAppliedAt = null;
  rec.memberDeclinedAt = null;
  rec.memberActive = isMemberEligible(config, rec, now);
  rec.updatedAt = now.toISOString();
  store.save();
  await target.roles.add(mc.roleId, `SupporterGate member approved by ${by}`).catch((err) => log.warn(`メンバーのロール付与に失敗 ${who}: ${String(err)}`));
  deps.requestPublish();
  log.info(`メンバーの申請を認定 ${who} by ${by} 有効=${rec.memberActive}`);

  // 本人に知らせる。DM を受け取らない設定なら届かないが、ロールと「状態」のボタンで分かる
  const dmText = (["ja", "en"] as Lang[]).map((l) => [t(l, "member.approvedDm"), config.group ? t(l, "member.approvedDmGroup") : ""].filter((x) => x.length > 0).join("\n")).join("\n\n");
  let dmNote = "本人に DM で知らせました。";
  try {
    await target.send({ content: dmText });
  } catch {
    dmNote = "本人への DM は届きませんでした（受け取らない設定）。ロールは付いています。";
  }
  await close(`✅ 認定しました（${by}、${fmtDate(now.toISOString())}）。${dmNote}`);
}

/** メンバーのボタン（説明を出す・同意する・取り消す）。申請制のときは、同意が申請になる */
async function handleMemberButton(deps: PanelDeps, interaction: ButtonInteraction<"cached">, lang: Lang): Promise<void> {
  const { config, store } = deps;
  const mc = config.member;
  if (!mc) {
    await interaction.reply({ content: t(lang, "member.unavailable"), ephemeral: true });
    return;
  }
  const rec = store.get(interaction.user.id);
  if (rec?.banned) {
    await interaction.reply({ content: t(lang, "err.banned"), ephemeral: true });
    return;
  }
  if (!rec || !rec.vrcName) {
    // ゲートは表示名で判定するので、名前の登録が先
    await interaction.reply({ content: t(lang, "member.needName"), ephemeral: true });
    return;
  }
  const who = `${interaction.user.tag} (${interaction.user.id})`;
  rec.discordTag = interaction.user.tag;
  if (interaction.member.joinedAt) rec.joinedAt = interaction.member.joinedAt.toISOString();
  const id = interaction.customId;
  const apply = mc.mode === "apply";
  const now = new Date();

  if (id === IDS.member) {
    if (!rec.memberConsentAt && !rec.memberManual) {
      if (apply) {
        const blocked = applyBlocked(config, rec, lang, now);
        if (blocked) {
          await interaction.reply({ content: blocked, ephemeral: true });
          return;
        }
      }
      const row = new ActionRowBuilder<ButtonBuilder>().addComponents(
        new ButtonBuilder().setCustomId(IDS.memberAgree).setLabel(t(lang, apply ? "member.apply" : "member.agree")).setStyle(ButtonStyle.Success),
      );
      const content = apply
        ? [t(lang, "member.explainApply"), t(lang, "share.notice"), t(lang, "member.confirmApply")].join("\n\n")
        : [t(lang, "member.explain", { days: mc.minDays }), t(lang, "share.notice"), t(lang, "member.confirm")].join("\n\n");
      await interaction.reply({ content, components: [row], ephemeral: true });
    } else {
      const pending = apply && rec.memberAppliedAt !== null && !rec.memberManual;
      const row = new ActionRowBuilder<ButtonBuilder>().addComponents(
        new ButtonBuilder().setCustomId(IDS.memberLeave).setLabel(t(lang, pending ? "member.withdraw" : "member.leave")).setStyle(ButtonStyle.Secondary),
      );
      await interaction.reply({ content: `${t(lang, "member.label")}: ${memberState(config, rec, lang)}`, components: [row], ephemeral: true });
    }
    return;
  }

  if (id === IDS.memberAgree) {
    if (apply && !rec.memberManual) {
      await submitApplication(deps, interaction, lang, rec, now);
      return;
    }
    if (!rec.memberConsentAt) rec.memberConsentAt = now.toISOString();
    rec.memberActive = isMemberEligible(config, rec, now);
    rec.updatedAt = now.toISOString();
    store.save();
    log.info(`メンバー登録 ${who}: 同意を記録、有効=${rec.memberActive} 参加日=${rec.joinedAt ?? "-"}`);
    let content: string;
    if (rec.memberActive) {
      // ロールはここで付ける。失敗しても次の同期が付け直す
      await interaction.member.roles.add(mc.roleId, "SupporterGate member").catch((err) => log.warn(`メンバーのロール付与に失敗 ${who}: ${String(err)}`));
      deps.requestPublish();
      content = t(lang, "member.granted");
    } else {
      const from = memberEligibleFrom(config, rec);
      content = t(lang, "member.pending", { date: from ? fmtDate(from.toISOString()) : "-" });
    }
    await interaction.update({ content, components: [] });
    return;
  }

  // 取り消し（申請中なら、申請の取り下げ）
  const wasActive = rec.memberActive;
  const wasPending = rec.memberAppliedAt !== null && !rec.memberManual;
  rec.memberConsentAt = null;
  rec.memberManual = false;
  rec.memberAppliedAt = null;
  rec.memberActive = false;
  rec.updatedAt = now.toISOString();
  store.save();
  log.info(wasPending ? `メンバーの申請の取り下げ ${who}` : `メンバー登録の取り消し ${who}`);
  await interaction.member.roles.remove(mc.roleId, "SupporterGate member").catch((err) => log.warn(`メンバーのロール剥奪に失敗 ${who}: ${String(err)}`));
  if (wasActive) deps.requestPublish();
  await interaction.update({ content: t(lang, wasPending ? "member.withdrawn" : "member.left"), components: [] });
}

/**
 * グループのボタン。VRChat の Group への参加を希望したことを記録し、申請のしかたを返す。
 * Group への申請と承認は VRChat の側で行う。ここで残した記録とログは、持ち主が申請者を確かめるのに使う
 */
async function handleGroupButton(deps: PanelDeps, interaction: ButtonInteraction<"cached">, lang: Lang): Promise<void> {
  const { config, store } = deps;
  const group = config.group;
  if (!group) {
    await interaction.reply({ content: t(lang, "group.unavailable"), ephemeral: true });
    return;
  }
  const rec = store.get(interaction.user.id);
  if (rec?.banned) {
    await interaction.reply({ content: t(lang, "err.banned"), ephemeral: true });
    return;
  }
  if (!rec || !rec.vrcName) {
    await interaction.reply({ content: t(lang, "group.needName"), ephemeral: true });
    return;
  }
  if (rec.effectiveRank <= 0 && !rec.memberActive) {
    await interaction.reply({ content: t(lang, "group.notEligible"), ephemeral: true });
    return;
  }
  if (!rec.groupRequestedAt) {
    rec.groupRequestedAt = new Date().toISOString();
    rec.discordTag = interaction.user.tag;
    rec.updatedAt = rec.groupRequestedAt;
    store.save();
    // このログは Discord のログチャンネルにも流れる。VRChat に届いた申請と、表示名で照らせる
    log.info(`グループ参加の希望 ${interaction.user.tag} (${interaction.user.id}): VRChat の表示名=${rec.vrcName} ランク=${rec.effectiveRank} メンバー=${rec.memberActive ? "有効" : "無効"}`);
  }
  const params = { name: group.name, url: group.url, vrcName: rec.vrcName };
  const access = deps.vrc?.get() ?? null;
  if (!access) {
    // API を使わないとき: 持ち主が、ログを見て手で招待する
    await interaction.reply({ content: t(lang, "group.howto", params), ephemeral: true });
    return;
  }
  // API の問い合わせは数秒かかるので、先に受け付けだけ返す
  await interaction.deferReply({ ephemeral: true });
  try {
    const r = await bringIntoGroup(access.client, access.groupId, rec.vrcName);
    if (r.user && rec.vrcUserId !== r.user.id) {
      rec.vrcUserId = r.user.id;
      store.save();
    }
    log.info(`グループへの招待 ${interaction.user.tag} (${interaction.user.id}): VRChat の表示名=${rec.vrcName} 結果=${r.outcome}`);
    await interaction.editReply({ content: t(lang, `group.${r.outcome}`, params) });
  } catch (err) {
    // API が使えなかったときは、手作業の流れに戻す（持ち主がログを見て招待する）
    log.warn(`グループへの招待に失敗 ${interaction.user.tag} (${interaction.user.id}): VRChat の表示名=${rec.vrcName} ${String(err)}。手で招待してください`);
    await interaction.editReply({ content: t(lang, "group.howto", params) });
  }
}

export async function handleButton(deps: PanelDeps, interaction: ButtonInteraction): Promise<void> {
  const { config, store } = deps;
  const lang = langOf(interaction.locale);
  if (!interaction.inCachedGuild() || interaction.guildId !== config.guildId) {
    await interaction.reply({ content: t(lang, "err.wrongServer"), ephemeral: true });
    return;
  }
  const id = interaction.customId;

  if (id === IDS.register) {
    const modal = new ModalBuilder().setCustomId(IDS.modal).setTitle(t(lang, "modal.title"));
    const input = new TextInputBuilder()
      .setCustomId(IDS.modalName)
      .setLabel(t(lang, "modal.name"))
      .setPlaceholder(t(lang, "modal.placeholder"))
      .setStyle(TextInputStyle.Short)
      .setRequired(true)
      .setMinLength(1)
      .setMaxLength(config.maxNameLength);
    modal.addComponents(new ActionRowBuilder<TextInputBuilder>().addComponents(input));
    await interaction.showModal(modal);
    return;
  }

  if (id === IDS.status) {
    await interaction.reply({ content: describe(config, store.get(interaction.user.id), lang), ephemeral: true });
    return;
  }

  if (id.startsWith(IDS.memberApprove) || id.startsWith(IDS.memberDecline)) {
    await handleMemberReview(deps, interaction);
    return;
  }

  if (id === IDS.member || id === IDS.memberAgree || id === IDS.memberLeave) {
    await handleMemberButton(deps, interaction, lang);
    return;
  }

  if (id === IDS.group) {
    await handleGroupButton(deps, interaction, lang);
    return;
  }

  if (id === IDS.creditOn || id === IDS.creditOff) {
    const rec = store.get(interaction.user.id);
    if (!rec) {
      await interaction.reply({ content: t(lang, "err.noRecord"), ephemeral: true });
      return;
    }
    rec.showCredit = id === IDS.creditOn;
    rec.updatedAt = new Date().toISOString();
    store.save();
    log.info(`クレジット表示 ${interaction.user.tag} (${interaction.user.id}): ${rec.showCredit}`);
    deps.requestPublish();
    await interaction.reply({ content: t(lang, "credit.set", { state: t(lang, rec.showCredit ? "on" : "off") }), ephemeral: true });
    return;
  }
}

export async function handleModal(deps: PanelDeps, interaction: ModalSubmitInteraction): Promise<void> {
  const { config, store } = deps;
  const lang = langOf(interaction.locale);
  if (interaction.customId !== IDS.modal) return;
  if (!interaction.inCachedGuild() || interaction.guildId !== config.guildId) {
    await interaction.reply({ content: t(lang, "err.wrongServer"), ephemeral: true });
    return;
  }
  const name = interaction.fields.getTextInputValue(IDS.modalName);
  const r = registerName(config, store, {
    discordId: interaction.user.id,
    discordTag: interaction.user.tag,
    name,
    credit: null,
    lang,
  });
  if (r.changed) deps.requestPublish();
  await interaction.reply({ content: r.message, ephemeral: true });
}

const HINT_TTL_MS = 90_000;

/**
 * 登録チャンネルへの通常投稿を処理する。
 * 「/vrc register ...」をテキストで貼った場合は登録として処理し、それ以外はボタンへ誘導する。
 * 投稿は削除し、案内は一定時間後に消す。
 */
export async function handleRegisterChannelMessage(deps: PanelDeps, msg: Message): Promise<void> {
  const { config, store } = deps;
  if (msg.author.bot || !msg.inGuild()) return;
  const mention = `<@${msg.author.id}>`;
  let content: string;

  const parsed = parseTextRegister(msg.content);
  log.info(`登録チャンネル投稿 ${msg.author.tag} (${msg.author.id}): ${parsed ? "テキスト登録として処理" : "案内して削除"} content=${JSON.stringify(msg.content.slice(0, 80))}`);
  if (parsed) {
    // テキストからは言語が分からないので日英で返す
    const ja = registerName(config, store, {
      discordId: msg.author.id, discordTag: msg.author.tag, name: parsed.name, credit: parsed.credit, lang: "ja",
    });
    if (ja.changed) deps.requestPublish();
    const enMsg = ja.ok
      ? t("en", "registered")
      : registerName(config, store, { discordId: msg.author.id, discordTag: msg.author.tag, name: parsed.name, credit: parsed.credit, lang: "en" }).message;
    content = `${mention}\n${ja.message}\n\n${enMsg}`;
  } else {
    content = `${mention}\n${t("ja", "hint.plainText")}\n${t("en", "hint.plainText")}`;
  }

  await msg.delete().catch(() => undefined);
  const sent = await msg.channel.send({ content, allowedMentions: { users: [msg.author.id] } }).catch(() => null);
  if (sent) setTimeout(() => sent.delete().catch(() => undefined), HINT_TTL_MS);
}
