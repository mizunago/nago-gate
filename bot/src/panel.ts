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
import { langOf, t } from "./i18n.js";
import { log } from "./log.js";
import { describe, parseTextRegister, registerName } from "./register.js";
import type { SyncContext } from "./sync.js";

export const IDS = {
  register: "sg:register",
  status: "sg:status",
  creditOn: "sg:credit:on",
  creditOff: "sg:credit:off",
  modal: "sg:register-modal",
  modalName: "name",
} as const;

export interface PanelDeps extends SyncContext {
  requestPublish: () => void;
}

/** /vrc-admin panel で投稿する固定メッセージ（共有メッセージなので多言語併記） */
export function buildPanelMessage(): MessageCreateOptions {
  const content = [
    "## VRChat 支援者登録 / Supporter Registration / 支持者注册",
    "",
    "🇯🇵 **登録** ボタンを押して、VRChat の表示名（プロフィールに出ている名前）を入力してください。",
    "　　表示名を変えたら再度登録してください（30 日に 1 回）。",
    "🇬🇧 Press **Register** and enter your VRChat display name (the name shown on your profile).",
    "　　Register again if you change your display name (once every 30 days).",
    "🇨🇳 点击 **注册**，输入你的 VRChat 显示名称（个人资料上显示的名字）。更改名称后请重新注册（每 30 天一次）。",
    "🇰🇷 **등록** 버튼을 누르고 VRChat 표시 이름(프로필에 표시되는 이름)을 입력하세요. 이름을 바꾸면 다시 등록하세요(30일에 1회).",
    "",
    "-# スラッシュコマンド `/vrc register` も使えますが、コピペでは動きません。入力欄で `/` を打って候補から選んでください。",
    "-# `/vrc register` also works, but only when picked from the popup after typing `/` (pasting the text does nothing).",
  ].join("\n");

  const row = new ActionRowBuilder<ButtonBuilder>().addComponents(
    new ButtonBuilder().setCustomId(IDS.register).setLabel("登録 / Register").setStyle(ButtonStyle.Primary).setEmoji("🧾"),
    new ButtonBuilder().setCustomId(IDS.status).setLabel("状態 / Status").setStyle(ButtonStyle.Secondary),
    new ButtonBuilder().setCustomId(IDS.creditOn).setLabel("クレジット ON / Credits ON").setStyle(ButtonStyle.Success),
    new ButtonBuilder().setCustomId(IDS.creditOff).setLabel("クレジット OFF / Credits OFF").setStyle(ButtonStyle.Secondary),
  );
  return { content, components: [row] };
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
