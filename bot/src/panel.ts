// 登録チャンネルに置くボタンパネルと、ボタン / フォーム / テキスト投稿の処理。
import {
  ActionRowBuilder,
  ButtonBuilder,
  ButtonInteraction,
  ButtonStyle,
  Message,
  ModalBuilder,
  ModalSubmitInteraction,
  TextInputBuilder,
  TextInputStyle,
  type MessageCreateOptions,
} from "discord.js";
import type { AppConfig } from "./config.js";
import { langOf, t, type Lang } from "./i18n.js";
import { log } from "./log.js";
import { describe, fmtDate, memberState, parseTextRegister, registerName } from "./register.js";
import { isMemberEligible, memberEligibleFrom, type SyncContext } from "./sync.js";

export const IDS = {
  register: "sg:register",
  status: "sg:status",
  creditOn: "sg:credit:on",
  creditOff: "sg:credit:off",
  member: "sg:member",
  memberAgree: "sg:member:agree",
  memberLeave: "sg:member:leave",
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
  if (config.member) {
    const d = config.member.minDays;
    lines.push(
      "",
      `🇯🇵 **メンバー** ボタン: サーバーに参加して ${d} 日以上の方は、支援の有無に関係なく、メンバー限定の案内を見られます（18 歳以上の方のみ）。`,
      `🇬🇧 **Membership** button: after ${d} days on this server, you can see the member-only area, whether or not you are a supporter (18+ only).`,
      `🇨🇳 **成员** 按钮：加入本服务器满 ${d} 天后，无论是否支持，都可以查看成员限定区域（仅限 18 岁以上）。`,
      `🇰🇷 **멤버** 버튼: 서버 참가 후 ${d}일이 지나면 후원 여부와 관계없이 멤버 전용 안내를 볼 수 있습니다 (18세 이상).`,
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
  if (config.member) {
    rows.push(
      new ActionRowBuilder<ButtonBuilder>().addComponents(
        new ButtonBuilder().setCustomId(IDS.member).setLabel("メンバー / Membership").setStyle(ButtonStyle.Secondary).setEmoji("🔑"),
      ),
    );
  }
  return { content: lines.join("\n"), components: rows };
}

/** メンバー登録のボタン（説明を出す・同意する・取り消す） */
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

  if (id === IDS.member) {
    if (!rec.memberConsentAt && !rec.memberManual) {
      const row = new ActionRowBuilder<ButtonBuilder>().addComponents(
        new ButtonBuilder().setCustomId(IDS.memberAgree).setLabel(t(lang, "member.agree")).setStyle(ButtonStyle.Success),
      );
      const content = [t(lang, "member.explain", { days: mc.minDays }), t(lang, "share.notice"), t(lang, "member.confirm")].join("\n\n");
      await interaction.reply({ content, components: [row], ephemeral: true });
    } else {
      const row = new ActionRowBuilder<ButtonBuilder>().addComponents(
        new ButtonBuilder().setCustomId(IDS.memberLeave).setLabel(t(lang, "member.leave")).setStyle(ButtonStyle.Secondary),
      );
      await interaction.reply({ content: `${t(lang, "member.label")}: ${memberState(config, rec, lang)}`, components: [row], ephemeral: true });
    }
    return;
  }

  const now = new Date();
  if (id === IDS.memberAgree) {
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

  // 取り消し
  const wasActive = rec.memberActive;
  rec.memberConsentAt = null;
  rec.memberManual = false;
  rec.memberActive = false;
  rec.updatedAt = now.toISOString();
  store.save();
  log.info(`メンバー登録の取り消し ${who}`);
  await interaction.member.roles.remove(mc.roleId, "SupporterGate member").catch((err) => log.warn(`メンバーのロール剥奪に失敗 ${who}: ${String(err)}`));
  if (wasActive) deps.requestPublish();
  await interaction.update({ content: t(lang, "member.left"), components: [] });
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

  if (id === IDS.member || id === IDS.memberAgree || id === IDS.memberLeave) {
    await handleMemberButton(deps, interaction, lang);
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
