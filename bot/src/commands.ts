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
import { buildPanelMessage, IDS } from "./panel.js";
import { keyedHashName } from "./protect.js";
import { describe, fmtDate, registerName, validateName } from "./register.js";
import { setupCommunity, setupInfo, setupRoles, setupWorld, type WorldVisibility } from "./setup.js";
import { isMemberEligible, publishIfChanged, runSync, updateEffectiveRank, type SyncContext } from "./sync.js";

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
    .addSubcommand((s) => s.setName("setup-roles").setDescription("Supporter / Platinum / Member / src-* ロールを作り、config 用の ID を表示する"))
    .addSubcommand((s) => s.setName("setup-info").setDescription("INFO カテゴリ（はじめに・お知らせ・登録）を作る"))
    .addSubcommand((s) => s.setName("setup-community").setDescription("コミュニティカテゴリ（雑談 jp/en/zh/ko・sfw-photo・nsfw-photo）を作る"))
    .addSubcommand((s) =>
      s
        .setName("setup-world")
        .setDescription("ワールド用カテゴリ（案内 JP/EN/ZH/KO・更新情報・フィードバック・雑談）を作る")
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
              { name: "Member（登録メンバー。支援とは別）", value: "member" },
            ),
        )
        .addBooleanOption((o) => o.setName("nsfw").setDescription("年齢制限チャンネルにする（既定: しない）")),
    )
    .addSubcommand((s) => s.setName("panel").setDescription("このチャンネルに登録ボタン付きパネルを投稿する（既にあれば書き換える）"))
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
        .setName("member-grant")
        .setDescription("メンバーに手動で認定する（在籍日数と同意を待たない。表示名の登録は必要）")
        .addUserOption((o) => o.setName("user").setDescription("対象").setRequired(true)),
    )
    .addSubcommand((s) =>
      s
        .setName("member-revoke")
        .setDescription("メンバーの手動の認定を取り消す（本人のメンバー登録も取り消す）")
        .addUserOption((o) => o.setName("user").setDescription("対象").setRequired(true)),
    )
    .addSubcommand((s) =>
      s
        .setName("ban")
        .setDescription("支援者・メンバーのリストから外し、ロールを外す（登録し直しも防ぐ。サーバーからの BAN は Discord の画面で行う）")
        .addUserOption((o) => o.setName("user").setDescription("対象").setRequired(true))
        .addStringOption((o) => o.setName("reason").setDescription("理由（管理用のメモ。本人には送らない）")),
    )
    .addSubcommand((s) =>
      s
        .setName("unban")
        .setDescription("ban を解除する（本人が登録し直せば、通常の条件で戻れる）")
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
    if (sub === "setup-roles" || sub === "setup-info" || sub === "setup-community" || sub === "setup-world") {
      await interaction.deferReply({ ephemeral: true });
      try {
        let result: string;
        if (sub === "setup-roles") result = await setupRoles(interaction.guild);
        else if (sub === "setup-info") result = await setupInfo(interaction.guild, config);
        else if (sub === "setup-community") result = await setupCommunity(interaction.guild);
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
      const panel = buildPanelMessage(config);
      // このチャンネルに Bot のパネルが既にあれば、新しく投稿せずに書き換える（ピン留めがそのまま生きる）
      const recent = "messages" in channel ? await channel.messages.fetch({ limit: 50 }).catch(() => null) : null;
      const old = recent?.find(
        (m) => m.author.id === interaction.client.user.id && m.components.some((row) => JSON.stringify(row.toJSON()).includes(IDS.register)),
      );
      if (old) await old.edit({ content: panel.content, components: panel.components });
      else await channel.send(panel);
      log.info(`パネル${old ? "書き換え" : "投稿"} channel=${interaction.channelId} by ${member.user.tag}`);
      const hint = config.registerChannelId === interaction.channelId
        ? "" : `\n（テキスト投稿の自動処理を有効にするには config.jsonc の discord.registerChannelId に \`${interaction.channelId}\` を設定）`;
      await interaction.reply({
        content: (old ? "既にあるパネルを書き換えました。" : "パネルを投稿しました。ピン留めしておくと見つけやすくなります。") + hint,
        ephemeral: true,
      });
      return;
    }
    if (sub === "sync") {
      await interaction.deferReply({ ephemeral: true });
      const r = await runSync(deps, interaction.guild, `manual by ${member.user.tag}`);
      await interaction.editReply(
        `同期完了: 走査 ${r.scanned} / 有効 ${r.active} / 猶予中 ${r.inGrace} / メンバー ${r.members} / ロール +${r.rolesAdded} -${r.rolesRemoved} / 公開 ${r.published ? "あり" : "変更なし"}` +
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
      if (rec.banned) {
        await interaction.reply({ content: `<@${user.id}> は BAN 中です。先に \`/vrc-admin unban\` で解除してください`, ephemeral: true });
        return;
      }
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
    if (sub === "member-grant" || sub === "member-revoke") {
      const mc = config.member;
      if (!mc) {
        await interaction.reply({ content: "メンバー登録が設定されていません（config.jsonc の member）", ephemeral: true });
        return;
      }
      const user = interaction.options.getUser("user", true);
      const target = await interaction.guild.members.fetch(user.id).catch(() => null);
      const grant = sub === "member-grant";
      if (grant && !target) {
        await interaction.reply({ content: `<@${user.id}> はこのサーバーにいません`, ephemeral: true });
        return;
      }
      const rec = grant ? store.getOrCreate(user.id) : store.get(user.id);
      if (!rec) {
        await interaction.reply({ content: "登録情報がありません", ephemeral: true });
        return;
      }
      if (grant && rec.banned) {
        await interaction.reply({ content: `<@${user.id}> は BAN 中です。先に \`/vrc-admin unban\` で解除してください`, ephemeral: true });
        return;
      }
      const now = new Date();
      rec.discordTag = user.tag;
      rec.joinedAt = target?.joinedAt ? target.joinedAt.toISOString() : null;
      rec.memberManual = grant;
      if (!grant) rec.memberConsentAt = null;
      rec.memberActive = isMemberEligible(config, rec, now);
      rec.updatedAt = now.toISOString();
      store.save();
      log.info(`管理者 ${sub} ${user.tag} (${user.id}) 有効=${rec.memberActive} by ${member.user.tag}`);
      if (target) {
        const op = rec.memberActive ? target.roles.add(mc.roleId, "SupporterGate member (manual)") : target.roles.remove(mc.roleId, "SupporterGate member (manual)");
        await op.catch((err) => log.warn(`メンバーのロール更新に失敗 ${user.tag}: ${String(err)}`));
      }
      deps.requestPublish();
      let msg: string;
      if (!grant) msg = `<@${user.id}> のメンバーの認定を取り消しました。本人が登録し直せば、通常の条件（在籍 ${mc.minDays} 日と同意）でメンバーになれます。`;
      else if (rec.memberActive) msg = `<@${user.id}> をメンバーに認定しました。数分後からメンバー限定のワールドに入れます。`;
      else msg = `<@${user.id}> をメンバーに認定しましたが、VRChat の表示名がまだ登録されていません。本人が登録するか、\`/vrc-admin setname\` で設定すると有効になります。`;
      await interaction.reply({ content: msg, ephemeral: true });
      return;
    }
    if (sub === "ban" || sub === "unban") {
      // サーバーからの BAN と、その解除は、人が Discord の画面で行う（Bot には BAN の権限を付けない）。
      // ここで行うのは、リストから外すこと・Bot が付けるロールを外すこと・登録し直しを防ぐこと
      const user = interaction.options.getUser("user", true);
      const ban = sub === "ban";
      if (ban && (user.id === member.id || user.bot)) {
        await interaction.reply({ content: "自分自身と Bot は BAN できません", ephemeral: true });
        return;
      }
      await interaction.deferReply({ ephemeral: true });
      const now = new Date();

      if (!ban) {
        const rec = store.get(user.id);
        if (!rec?.banned) {
          await interaction.editReply(`<@${user.id}> は BAN されていません`);
          return;
        }
        rec.banned = false;
        rec.bannedAt = null;
        rec.banReason = null;
        rec.updatedAt = now.toISOString();
        store.save();
        deps.requestPublish();
        log.info(`管理者 unban ${user.tag} (${user.id}) by ${member.user.tag}`);
        await interaction.editReply(
          `<@${user.id}> の BAN を解除しました。支援中なら、次の同期で支援者に戻ります。メンバーは、本人が登録し直せば、通常の条件で戻れます。\n` +
            "サーバーから BAN していた場合は、Discord の「サーバー設定 → BAN」からも解除してください。",
        );
        return;
      }

      const reason = (interaction.options.getString("reason") ?? "").trim();
      const target = await interaction.guild.members.fetch(user.id).catch(() => null);
      const rec = store.getOrCreate(user.id);
      rec.discordTag = user.tag;
      rec.banned = true;
      rec.bannedAt = now.toISOString();
      rec.banReason = reason || null;
      rec.memberConsentAt = null;
      rec.memberManual = false;
      rec.memberActive = false;
      rec.manualRank = 0;
      rec.manualUntil = null;
      rec.updatedAt = now.toISOString();
      store.save();
      deps.requestPublish();

      // Bot が付けるロール（Member と、支援者のロール）を外す。失敗しても、次の同期が外し直す
      let roleNote = "サーバーにいないので、外すロールはありません。";
      if (target) {
        const managed = [...config.tiers.map((tier) => tier.roleId), ...(config.member ? [config.member.roleId] : [])].filter((id) => target.roles.cache.has(id));
        const names = managed.map((id) => interaction.guild.roles.cache.get(id)?.name ?? id).join("・");
        roleNote = managed.length > 0 ? `ロール（${names}）を外しました。` : "外すロールはありませんでした。";
        if (managed.length > 0) {
          await target.roles.remove(managed, `SupporterGate ban by ${member.user.tag}`).catch((err) => {
            log.warn(`BAN のロール剥奪に失敗 ${user.tag}: ${String(err)}`);
            roleNote = "ロールを外せませんでした（次の同期で外し直します）。";
          });
        }
      }
      log.info(`管理者 ban ${user.tag} (${user.id}) name=${rec.vrcName ?? "-"} reason=${reason || "-"} by ${member.user.tag}`);
      const nameNote = rec.vrcName ? `登録していた表示名（**${rec.vrcName}**）は、ほかのアカウントでも登録できません。` : "表示名は登録されていませんでした。";
      await interaction.editReply(
        `<@${user.id}> を支援者・メンバーのリストから外しました。${roleNote}\n` +
          `${nameNote}本人も、登録とメンバー登録をやり直せません。\n` +
          "ワールドへの反映は、遅くとも 20 分ほどです（今いるインスタンスからは、リストの読み直しのあとに出されます）。\n" +
          "**サーバーからの BAN は行っていません。** 必要なら、Discord の画面から BAN してください。支援サイトでの支援も止まりません。\n" +
          "解除は `/vrc-admin unban` です。",
      );
      return;
    }
    if (sub === "hash") {
      const name = interaction.options.getString("name", true);
      // 鍵つきのリストでは、リストに載るのは鍵を混ぜたハッシュのほう
      const keyed = deps.listKeys?.length ? `\n鍵つき: \`${keyedHashName(deps.listKeys[0], name)}\`` : "";
      await interaction.reply({ content: `\`${normalizeName(name)}\` → \`${hashName(name)}\`${keyed}`, ephemeral: true });
      return;
    }
  }

  await interaction.reply({ content: t(lang, "err.unknown"), ephemeral: true });
}
