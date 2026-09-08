import {
  ChatInputCommandInteraction,
  GuildMember,
  PermissionFlagsBits,
  SlashCommandBuilder,
  type RESTPostAPIChatInputApplicationCommandsJSONBody,
} from "discord.js";
import { tierByRank, type AppConfig } from "./config.js";
import { hashName, normalizeName } from "./hash.js";
import { langOf, localizations, t } from "./i18n.js";
import { log } from "./log.js";
import { buildPanelMessage } from "./panel.js";
import { describe, fmtDate, registerName, validateName } from "./register.js";
import { setupInfo, setupRoles, setupWorld, type WorldVisibility } from "./setup.js";
import { publishIfChanged, runSync, updateEffectiveRank, type SyncContext } from "./sync.js";

export function buildCommands(): RESTPostAPIChatInputApplicationCommandsJSONBody[] {
  const vrc = new SlashCommandBuilder()
    .setName("vrc")
    .setDescription(t("en", "cmd.vrc"))
    .setDescriptionLocalizations(localizations("cmd.vrc"))
    .addSubcommand((s) =>
      s
        .setName("register")
        .setDescription(t("en", "cmd.register"))
        .setDescriptionLocalizations(localizations("cmd.register"))
        .addStringOption((o) =>
          o.setName("name").setDescription(t("en", "opt.name")).setDescriptionLocalizations(localizations("opt.name")).setRequired(true),
        )
        .addBooleanOption((o) =>
          o.setName("credit").setDescription(t("en", "opt.credit")).setDescriptionLocalizations(localizations("opt.credit")),
        ),
    )
    .addSubcommand((s) =>
      s.setName("status").setDescription(t("en", "cmd.status")).setDescriptionLocalizations(localizations("cmd.status")),
    )
    .addSubcommand((s) =>
      s
        .setName("credit")
        .setDescription(t("en", "cmd.credit"))
        .setDescriptionLocalizations(localizations("cmd.credit"))
        .addBooleanOption((o) =>
          o.setName("show").setDescription(t("en", "opt.show")).setDescriptionLocalizations(localizations("opt.show")).setRequired(true),
        ),
    );

  const admin = new SlashCommandBuilder()
    .setName("vrc-admin")
    .setDescription("支援者ゲート管理（管理者用）")
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageGuild)
    .addSubcommand((s) => s.setName("setup-roles").setDescription("Supporter / Platinum / src-* ロールを作り、config 用の ID を表示する"))
    .addSubcommand((s) => s.setName("setup-info").setDescription("INFO カテゴリ（はじめに・お知らせ・登録・雑談）を作る"))
    .addSubcommand((s) =>
      s
        .setName("setup-world")
        .setDescription("ワールド用カテゴリ（ワールド・更新情報・フィードバック）を作る")
        .addStringOption((o) => o.setName("jp").setDescription("日本語名（例: さんぷるわーるど）").setRequired(true))
        .addStringOption((o) => o.setName("en").setDescription("英語名（例: Sample World）").setRequired(true))
        .addStringOption((o) =>
          o
            .setName("visibility")
            .setDescription("誰に見せるか")
            .setRequired(true)
            .addChoices(
              { name: "全員（公開ワールド）", value: "public" },
              { name: "Supporter 以上", value: "supporter" },
              { name: "Platinum のみ", value: "platinum" },
            ),
        )
        .addBooleanOption((o) => o.setName("nsfw").setDescription("年齢制限チャンネルにする（既定: しない）")),
    )
    .addSubcommand((s) => s.setName("panel").setDescription("このチャンネルに登録ボタン付きパネルを投稿する"))
    .addSubcommand((s) => s.setName("sync").setDescription("今すぐ全メンバーを同期して公開する"))
    .addSubcommand((s) => s.setName("publish").setDescription("支援者リスト JSON を強制的に再公開する"))
    .addSubcommand((s) =>
      s
        .setName("lookup")
        .setDescription("メンバーの登録状態を表示する")
        .addUserOption((o) => o.setName("user").setDescription("対象").setRequired(true)),
    )
    .addSubcommand((s) =>
      s
        .setName("setname")
        .setDescription("クールダウンを無視して DisplayName を設定する")
        .addUserOption((o) => o.setName("user").setDescription("対象").setRequired(true))
        .addStringOption((o) => o.setName("name").setDescription("DisplayName").setRequired(true)),
    )
    .addSubcommand((s) =>
      s
        .setName("grant")
        .setDescription("支援サイトと無関係にランクを手動付与する")
        .addUserOption((o) => o.setName("user").setDescription("対象").setRequired(true))
        .addIntegerOption((o) => o.setName("rank").setDescription("ランク（config.tiers の rank）").setRequired(true))
        .addIntegerOption((o) => o.setName("days").setDescription("有効日数（省略で無期限）")),
    )
    .addSubcommand((s) =>
      s
        .setName("revoke")
        .setDescription("手動付与を取り消す")
        .addUserOption((o) => o.setName("user").setDescription("対象").setRequired(true)),
    )
    .addSubcommand((s) =>
      s
        .setName("hash")
        .setDescription("名前のハッシュを表示する（Udon 側の動作確認用）")
        .addStringOption((o) => o.setName("name").setDescription("DisplayName").setRequired(true)),
    );

  return [vrc.toJSON(), admin.toJSON()];
}

function isAdmin(config: AppConfig, member: GuildMember): boolean {
  if (member.permissions.has(PermissionFlagsBits.ManageGuild)) return true;
  return config.adminRoleIds.some((id) => member.roles.cache.has(id));
}

export interface CommandDeps extends SyncContext {
  requestPublish: () => void;
}

export async function handleInteraction(deps: CommandDeps, interaction: ChatInputCommandInteraction): Promise<void> {
  const { config, store } = deps;
  const lang = langOf(interaction.locale);
  if (!interaction.inCachedGuild() || interaction.guildId !== config.guildId) {
    await interaction.reply({ content: t(lang, "err.wrongServer"), ephemeral: true });
    return;
  }
  const member = interaction.member;
  const sub = interaction.options.getSubcommand();

  if (interaction.commandName === "vrc") {
    if (sub === "register") {
      const r = registerName(config, store, {
        discordId: member.id,
        discordTag: member.user.tag,
        name: interaction.options.getString("name", true),
        credit: interaction.options.getBoolean("credit"),
        lang,
      });
      if (r.changed) deps.requestPublish();
      await interaction.reply({ content: r.message, ephemeral: true });
      return;
    }
    if (sub === "status") {
      await interaction.reply({ content: describe(config, store.get(member.id), lang), ephemeral: true });
      return;
    }
    if (sub === "credit") {
      const rec = store.get(member.id);
      if (!rec) {
        await interaction.reply({ content: t(lang, "err.noRecord"), ephemeral: true });
        return;
      }
      rec.showCredit = interaction.options.getBoolean("show", true);
      rec.updatedAt = new Date().toISOString();
      store.save();
      deps.requestPublish();
      await interaction.reply({ content: t(lang, "credit.set", { state: t(lang, rec.showCredit ? "on" : "off") }), ephemeral: true });
      return;
    }
  }

  if (interaction.commandName === "vrc-admin") {
    if (!isAdmin(config, member)) {
      await interaction.reply({ content: "管理者権限が必要です", ephemeral: true });
      return;
    }
    if (sub === "setup-roles" || sub === "setup-info" || sub === "setup-world") {
      await interaction.deferReply({ ephemeral: true });
      try {
        let result: string;
        if (sub === "setup-roles") result = await setupRoles(interaction.guild);
        else if (sub === "setup-info") result = await setupInfo(interaction.guild, config);
        else {
          result = await setupWorld(
            interaction.guild,
            config,
            interaction.options.getString("jp", true).trim(),
            interaction.options.getString("en", true).trim(),
            interaction.options.getString("visibility", true) as WorldVisibility,
            interaction.options.getBoolean("nsfw") ?? false,
          );
        }
        await interaction.editReply(result.slice(0, 1900));
      } catch (err) {
        log.error(`${sub} 失敗: ${String(err)}`);
        await interaction.editReply(`失敗しました: ${String(err)}\nBot に「ロールの管理」「チャンネルの管理」権限があるか、Bot のロールが一番上にあるか確認してください。`);
      }
      return;
    }
    if (sub === "panel") {
      const channel = interaction.channel;
      if (!channel || !channel.isTextBased() || !("send" in channel)) {
        await interaction.reply({ content: "このチャンネルには投稿できません", ephemeral: true });
        return;
      }
      await channel.send(buildPanelMessage());
      log.info(`パネル投稿 channel=${interaction.channelId} by ${member.user.tag}`);
      const hint = config.registerChannelId === interaction.channelId
        ? "" : `\n（テキスト投稿の自動処理を有効にするには config.jsonc の discord.registerChannelId に \`${interaction.channelId}\` を設定）`;
      await interaction.reply({ content: "パネルを投稿しました。ピン留めしておくと見つけやすくなります。" + hint, ephemeral: true });
      return;
    }
    if (sub === "sync") {
      await interaction.deferReply({ ephemeral: true });
      const r = await runSync(deps, interaction.guild, `manual by ${member.user.tag}`);
      await interaction.editReply(
        `同期完了: 走査 ${r.scanned} / 有効 ${r.active} / 猶予中 ${r.inGrace} / ロール +${r.rolesAdded} -${r.rolesRemoved} / 公開 ${r.published ? "あり" : "変更なし"}` +
          (r.publishUrl ? `\n${r.publishUrl}` : "") +
          (r.errors.length ? `\nエラー:\n${r.errors.slice(0, 5).join("\n")}` : ""),
      );
      return;
    }
    if (sub === "publish") {
      await interaction.deferReply({ ephemeral: true });
      try {
        const r = await publishIfChanged(deps, true);
        await interaction.editReply(`公開しました: ${r.url ?? "(URL 不明)"}`);
      } catch (err) {
        await interaction.editReply(`公開に失敗: ${String(err)}`);
      }
      return;
    }
    if (sub === "lookup") {
      const user = interaction.options.getUser("user", true);
      await interaction.reply({ content: `<@${user.id}>\n${describe(config, store.get(user.id), "ja")}`, ephemeral: true });
      return;
    }
    if (sub === "setname") {
      const user = interaction.options.getUser("user", true);
      const v = validateName(config, interaction.options.getString("name", true), "ja");
      if (!v.ok) {
        await interaction.reply({ content: `設定できません: ${v.reason}`, ephemeral: true });
        return;
      }
      const dup = store.findByNormalizedName(normalizeName(v.name), normalizeName);
      if (dup && dup.discordId !== user.id) {
        await interaction.reply({ content: `その名前は <@${dup.discordId}> が登録済みです`, ephemeral: true });
        return;
      }
      const rec = store.getOrCreate(user.id);
      rec.vrcName = v.name;
      rec.nameChangedAt = null;
      rec.updatedAt = new Date().toISOString();
      store.save();
      log.info(`管理者 setname ${user.tag} (${user.id}) -> ${v.name} by ${member.user.tag}`);
      deps.requestPublish();
      await interaction.reply({ content: `<@${user.id}> の名前を **${v.name}** に設定しました`, ephemeral: true });
      return;
    }
    if (sub === "grant") {
      const user = interaction.options.getUser("user", true);
      const rank = interaction.options.getInteger("rank", true);
      const days = interaction.options.getInteger("days");
      if (!tierByRank(config, rank)) {
        await interaction.reply({ content: `ランク ${rank} は config.tiers に存在しません`, ephemeral: true });
        return;
      }
      const rec = store.getOrCreate(user.id);
      const now = new Date();
      rec.manualRank = rank;
      rec.manualUntil = days ? new Date(now.getTime() + days * 86_400_000).toISOString() : null;
      updateEffectiveRank(config, rec, rec.activeRank, now);
      store.save();
      log.info(`管理者 grant ${user.tag} (${user.id}) rank=${rank} days=${days ?? "∞"} by ${member.user.tag}`);
      deps.requestPublish();
      await interaction.reply({ content: `<@${user.id}> にランク ${rank} を手動付与しました（${days ? `${days} 日間` : "無期限"}）。ロール反映は次回同期時です。`, ephemeral: true });
      return;
    }
    if (sub === "revoke") {
      const user = interaction.options.getUser("user", true);
      const rec = store.get(user.id);
      if (!rec) {
        await interaction.reply({ content: "登録情報がありません", ephemeral: true });
        return;
      }
      rec.manualRank = 0;
      rec.manualUntil = null;
      updateEffectiveRank(config, rec, rec.activeRank, new Date());
      store.save();
      log.info(`管理者 revoke ${user.tag} (${user.id}) by ${member.user.tag}`);
      deps.requestPublish();
      await interaction.reply({ content: `<@${user.id}> の手動付与を取り消しました`, ephemeral: true });
      return;
    }
    if (sub === "hash") {
      const name = interaction.options.getString("name", true);
      await interaction.reply({ content: `\`${normalizeName(name)}\` → \`${hashName(name)}\``, ephemeral: true });
      return;
    }
  }

  await interaction.reply({ content: t(lang, "err.unknown"), ephemeral: true });
}
