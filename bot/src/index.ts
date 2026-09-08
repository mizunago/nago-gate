import { Client, Events, GatewayIntentBits, REST, Routes } from "discord.js";
import { buildCommands, handleInteraction } from "./commands.js";
import { langOf, t } from "./i18n.js";
import { handleButton, handleModal, handleRegisterChannelMessage } from "./panel.js";
import { loadInstance } from "./config.js";
import { initLog, log as L } from "./log.js";
import { Store } from "./store.js";
import { publishIfChanged, runSync, type SyncContext } from "./sync.js";

function log(msg: string): void {
  L.info(msg);
}

async function main(): Promise<void> {
  const inst = loadInstance();
  initLog(inst.dir);
  const { config } = inst;
  const store = new Store(inst.dataPath);
  const ctx: SyncContext = { config, store, githubToken: inst.secrets.githubToken, log };
  log(`設定フォルダ: ${inst.dir}`);

  const intents = [GatewayIntentBits.Guilds, GatewayIntentBits.GuildMembers];
  if (config.registerChannelId) intents.push(GatewayIntentBits.GuildMessages, GatewayIntentBits.MessageContent);
  const client = new Client({ intents });

  let syncRunning = false;
  let syncQueued = false;
  const triggerSync = (reason: string): void => {
    if (syncRunning) {
      syncQueued = true;
      return;
    }
    syncRunning = true;
    void (async () => {
      try {
        const guild = await client.guilds.fetch(config.guildId);
        await runSync(ctx, guild, reason);
      } catch (err) {
        L.error(`sync 失敗: ${String(err)}`);
      } finally {
        syncRunning = false;
        if (syncQueued) {
          syncQueued = false;
          triggerSync("queued");
        }
      }
    })();
  };

  // 名前登録などの直後は 15 秒まとめてから公開する（連続操作の抑制）
  let publishTimer: NodeJS.Timeout | null = null;
  const requestPublish = (): void => {
    if (publishTimer) clearTimeout(publishTimer);
    publishTimer = setTimeout(() => {
      publishTimer = null;
      publishIfChanged(ctx, false).catch((err) => L.error(`公開失敗: ${String(err)}`));
    }, 15_000);
  };

  let memberTimer: NodeJS.Timeout | null = null;
  const scheduleMemberSync = (reason: string): void => {
    if (memberTimer) clearTimeout(memberTimer);
    memberTimer = setTimeout(() => {
      memberTimer = null;
      triggerSync(reason);
    }, 20_000);
  };

  client.once(Events.ClientReady, async (c) => {
    log(`ログイン: ${c.user.tag}`);
    const applicationId = c.application?.id ?? c.user.id;
    const rest = new REST().setToken(inst.secrets.discordToken);
    await rest.put(Routes.applicationGuildCommands(applicationId, config.guildId), {
      body: buildCommands(),
    });
    log("スラッシュコマンドを登録しました");
    triggerSync("startup");
    setInterval(() => triggerSync("interval"), config.syncIntervalMinutes * 60_000);
  });

  client.on(Events.GuildMemberUpdate, (oldM, newM) => {
    if (newM.guild.id !== config.guildId) return;
    if (oldM.roles.cache.size === newM.roles.cache.size && oldM.roles.cache.every((r) => newM.roles.cache.has(r.id))) return;
    scheduleMemberSync(`role change: ${newM.user.tag}`);
  });
  client.on(Events.GuildMemberAdd, (m) => {
    if (m.guild.id === config.guildId) scheduleMemberSync(`join: ${m.user.tag}`);
  });
  client.on(Events.GuildMemberRemove, (m) => {
    if (m.guild.id === config.guildId) scheduleMemberSync(`leave: ${m.user.tag}`);
  });

  client.on(Events.InteractionCreate, async (interaction) => {
    const deps = { ...ctx, requestPublish };
    if (interaction.isChatInputCommand()) {
      const sub = interaction.options.getSubcommand(false);
      L.info(`コマンド /${interaction.commandName}${sub ? " " + sub : ""} by ${interaction.user.tag} (${interaction.user.id}) locale=${interaction.locale}`);
    } else if (interaction.isButton() || interaction.isModalSubmit()) {
      L.info(`UI ${interaction.customId} by ${interaction.user.tag} (${interaction.user.id}) locale=${interaction.locale}`);
    }
    try {
      if (interaction.isChatInputCommand()) await handleInteraction(deps, interaction);
      else if (interaction.isButton()) await handleButton(deps, interaction);
      else if (interaction.isModalSubmit()) await handleModal(deps, interaction);
      else return;
    } catch (err) {
      L.error(`コマンド処理エラー: ${String(err)}`);
      const msg = { content: t(langOf(interaction.locale), "err.internal"), ephemeral: true as const };
      if (!interaction.isRepliable()) return;
      if (interaction.deferred || interaction.replied) await interaction.editReply(msg).catch(() => undefined);
      else await interaction.reply(msg).catch(() => undefined);
    }
  });

  if (config.registerChannelId) {
    client.on(Events.MessageCreate, async (msg) => {
      if (msg.channelId !== config.registerChannelId) return;
      try {
        await handleRegisterChannelMessage({ ...ctx, requestPublish }, msg);
      } catch (err) {
        L.error(`登録チャンネル処理エラー: ${String(err)}`);
      }
    });
  }

  process.on("unhandledRejection", (err) => L.error(`unhandledRejection: ${String(err)}`));
  process.on("SIGINT", () => { L.info("停止 (SIGINT)"); client.destroy(); process.exit(0); });
  process.on("SIGTERM", () => { L.info("停止 (SIGTERM)"); client.destroy(); process.exit(0); });
  L.info("起動中...");
  await client.login(inst.secrets.discordToken);
}

main().catch((err) => {
  L.error(`起動失敗: ${err instanceof Error ? err.stack ?? err.message : String(err)}`);
  process.exit(1);
});
