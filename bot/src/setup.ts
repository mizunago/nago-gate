// サーバー構成の自動作成（/vrc-admin setup-*）。docs/discord-structure.md の設計をそのまま作る。
// すべて「同名があれば再利用」なので何度実行しても増えない。
import {
  ChannelType,
  PermissionFlagsBits,
  type CategoryChannel,
  type Guild,
  type GuildBasedChannel,
  type OverwriteResolvable,
  type Role,
} from "discord.js";
import type { AppConfig } from "./config.js";

const P = PermissionFlagsBits;

export const ROLE_NAMES = {
  supporter: "Supporter",
  platinum: "Platinum",
  sources: ["src-patreon-supporter", "src-patreon-platinum", "src-cien-supporter", "src-cien-platinum"],
} as const;

async function ensureRole(guild: Guild, name: string, opts: { color?: number; hoist?: boolean }): Promise<{ role: Role; created: boolean }> {
  const existing = guild.roles.cache.find((r) => r.name === name);
  if (existing) return { role: existing, created: false };
  const role = await guild.roles.create({
    name,
    color: opts.color ?? 0,
    hoist: opts.hoist ?? false,
    mentionable: false,
    permissions: [],
    reason: "SupporterGate setup",
  });
  return { role, created: true };
}

/** 出力ロールと入力ロールを作り、config.jsonc に貼る tiers スニペットを返す */
export async function setupRoles(guild: Guild): Promise<string> {
  await guild.roles.fetch();
  const lines: string[] = [];
  const sup = await ensureRole(guild, ROLE_NAMES.supporter, { color: 0xf5c542, hoist: true });
  const pla = await ensureRole(guild, ROLE_NAMES.platinum, { color: 0x8fd3ff, hoist: true });
  const src: Record<string, Role> = {};
  for (const n of ROLE_NAMES.sources) {
    const r = await ensureRole(guild, n, {});
    src[n] = r.role;
    lines.push(`${r.created ? "作成" : "既存"}: ${n}`);
  }
  lines.unshift(`${sup.created ? "作成" : "既存"}: ${ROLE_NAMES.supporter}`, `${pla.created ? "作成" : "既存"}: ${ROLE_NAMES.platinum}`);

  // 並び順: Bot の直下に Platinum, Supporter, src-*
  const me = guild.members.me;
  const top = me ? me.roles.highest.position : guild.roles.cache.size;
  const order = [pla.role, sup.role, ...ROLE_NAMES.sources.map((n) => src[n])];
  try {
    await guild.roles.setPositions(order.map((r, i) => ({ role: r, position: Math.max(1, top - 1 - i) })));
  } catch {
    lines.push("（並び替えは権限不足で省略。Bot のロールを一番上に置いてから再実行してください）");
  }

  const snippet = [
    "```jsonc",
    '"tiers": [',
    `  { "id": "supporter", "rank": 1, "label": "Supporter", "color": "#F5C542",`,
    `    "roleId": "${sup.role.id}",`,
    `    "sourceRoleIds": ["${src["src-patreon-supporter"].id}", "${src["src-cien-supporter"].id}"] },`,
    `  { "id": "platinum", "rank": 2, "label": "Platinum", "color": "#8FD3FF",`,
    `    "roleId": "${pla.role.id}",`,
    `    "sourceRoleIds": ["${src["src-patreon-platinum"].id}", "${src["src-cien-platinum"].id}"] }`,
    "]",
    "```",
  ].join("\n");
  return `${lines.join("\n")}\n\nconfig.jsonc に貼る内容:\n${snippet}\n次に Patreon / Ci-en の連携画面で src-* ロールをプランに割り当ててください。`;
}

function findRole(guild: Guild, config: AppConfig, rank: number, fallbackName: string): Role | null {
  const tier = config.tiers.find((t) => t.rank === rank);
  const byId = tier ? guild.roles.cache.get(tier.roleId) : undefined;
  return byId ?? guild.roles.cache.find((r) => r.name === fallbackName) ?? null;
}

async function ensureCategory(guild: Guild, name: string, overwrites: OverwriteResolvable[]): Promise<{ cat: CategoryChannel; created: boolean }> {
  const existing = guild.channels.cache.find((c) => c.type === ChannelType.GuildCategory && c.name === name) as CategoryChannel | undefined;
  if (existing) {
    await existing.permissionOverwrites.set(overwrites, "SupporterGate setup");
    return { cat: existing, created: false };
  }
  const cat = await guild.channels.create({ name, type: ChannelType.GuildCategory, permissionOverwrites: overwrites, reason: "SupporterGate setup" });
  return { cat, created: true };
}

interface ChannelSpec {
  name: string;
  type: ChannelType.GuildText | ChannelType.GuildForum;
  topic?: string;
  nsfw?: boolean;
  overwrites?: OverwriteResolvable[];
  tags?: string[];
}

async function ensureChannel(guild: Guild, cat: CategoryChannel, spec: ChannelSpec): Promise<{ ch: GuildBasedChannel; created: boolean }> {
  const existing = cat.children.cache.find((c) => c.name === spec.name && c.type === spec.type);
  if (existing) {
    if (spec.overwrites && "permissionOverwrites" in existing) await existing.permissionOverwrites.set(spec.overwrites, "SupporterGate setup");
    return { ch: existing, created: false };
  }
  if (spec.type === ChannelType.GuildForum) {
    const ch = await guild.channels.create({
      name: spec.name,
      type: ChannelType.GuildForum,
      parent: cat,
      topic: spec.topic,
      nsfw: spec.nsfw ?? false,
      permissionOverwrites: spec.overwrites,
      availableTags: (spec.tags ?? []).map((t) => ({ name: t })),
      reason: "SupporterGate setup",
    });
    return { ch, created: true };
  }
  const ch = await guild.channels.create({
    name: spec.name,
    type: ChannelType.GuildText,
    parent: cat,
    topic: spec.topic,
    nsfw: spec.nsfw ?? false,
    permissionOverwrites: spec.overwrites,
    reason: "SupporterGate setup",
  });
  return { ch, created: true };
}

const FEEDBACK_TAGS = ["Bug", "Request", "JP", "EN", "ZH"];

/** INFO カテゴリ（全員向け） */
export async function setupInfo(guild: Guild, config: AppConfig): Promise<string> {
  await guild.channels.fetch();
  const everyone = guild.roles.everyone;
  const me = guild.members.me;
  const { cat, created } = await ensureCategory(guild, "📌 INFO", []);
  const out: string[] = [`${created ? "作成" : "既存"}: ${cat.name}`];

  const specs: ChannelSpec[] = [
    { name: "📖はじめに-start-here", type: ChannelType.GuildText, overwrites: [{ id: everyone.id, deny: [P.SendMessages, P.CreatePublicThreads, P.CreatePrivateThreads] }] },
    { name: "📢お知らせ-announcements", type: ChannelType.GuildText, overwrites: [{ id: everyone.id, deny: [P.SendMessages, P.CreatePublicThreads, P.CreatePrivateThreads] }] },
    {
      name: "🧾登録-register",
      type: ChannelType.GuildText,
      topic: "VRChat 表示名の登録 / Register your VRChat display name",
      overwrites: [
        { id: everyone.id, allow: [P.SendMessages, P.UseApplicationCommands], deny: [P.CreatePublicThreads, P.CreatePrivateThreads, P.AttachFiles, P.EmbedLinks] },
        ...(me ? [{ id: me.id, allow: [P.SendMessages, P.ManageMessages, P.ViewChannel] }] : []),
      ],
    },
    { name: "💬雑談-jp", type: ChannelType.GuildText, topic: "日本語の雑談" },
    { name: "💬chat-en", type: ChannelType.GuildText, topic: "English chat" },
  ];
  let registerId: string | null = null;
  for (const spec of specs) {
    const r = await ensureChannel(guild, cat, spec);
    out.push(`${r.created ? "作成" : "既存"}: ${spec.name}`);
    if (spec.name === "🧾登録-register") registerId = r.ch.id;
  }
  if (registerId) {
    out.push("");
    out.push(`登録チャンネル ID: \`${registerId}\``);
    if (config.registerChannelId !== registerId) {
      out.push(`config.jsonc の discord.registerChannelId に \`${registerId}\` を設定して Bot を再起動し、そのチャンネルで /vrc-admin panel を実行してください。`);
    }
  }
  return out.join("\n");
}

export type WorldVisibility = "public" | "supporter" | "platinum";

/** ワールドごとのカテゴリ */
export async function setupWorld(
  guild: Guild,
  config: AppConfig,
  jpName: string,
  enName: string,
  visibility: WorldVisibility,
  nsfw: boolean,
): Promise<string> {
  await guild.channels.fetch();
  await guild.roles.fetch();
  const everyone = guild.roles.everyone;
  const me = guild.members.me;
  const catName = `${jpName} ⁄ ${enName}`;

  let viewer: Role | null = null;
  if (visibility === "supporter") viewer = findRole(guild, config, 1, ROLE_NAMES.supporter);
  if (visibility === "platinum") viewer = findRole(guild, config, 2, ROLE_NAMES.platinum);
  if (visibility !== "public" && !viewer) {
    return `ロールが見つかりません。先に /vrc-admin setup-roles を実行してください（visibility=${visibility}）`;
  }

  const readOnly: OverwriteResolvable[] = [];
  const canPost: OverwriteResolvable[] = [];
  const canChat: OverwriteResolvable[] = [];
  if (visibility === "public") {
    readOnly.push({ id: everyone.id, deny: [P.SendMessages, P.SendMessagesInThreads, P.CreatePublicThreads, P.CreatePrivateThreads] });
    canPost.push({ id: everyone.id, allow: [P.SendMessages, P.SendMessagesInThreads, P.CreatePublicThreads] });
  } else {
    readOnly.push({ id: everyone.id, deny: [P.ViewChannel] });
    readOnly.push({ id: viewer!.id, allow: [P.ViewChannel], deny: [P.SendMessages, P.SendMessagesInThreads, P.CreatePublicThreads, P.CreatePrivateThreads] });
    canPost.push({ id: everyone.id, deny: [P.ViewChannel] });
    canPost.push({ id: viewer!.id, allow: [P.ViewChannel, P.SendMessages, P.SendMessagesInThreads, P.CreatePublicThreads] });
    canChat.push({ id: everyone.id, deny: [P.ViewChannel] });
    canChat.push({ id: viewer!.id, allow: [P.ViewChannel, P.SendMessages, P.SendMessagesInThreads, P.CreatePublicThreads] });
  }
  if (me) {
    readOnly.push({ id: me.id, allow: [P.ViewChannel, P.SendMessages] });
    canPost.push({ id: me.id, allow: [P.ViewChannel, P.SendMessages] });
    canChat.push({ id: me.id, allow: [P.ViewChannel, P.SendMessages] });
  }

  const { cat, created } = await ensureCategory(guild, catName, readOnly);
  const out: string[] = [`${created ? "作成" : "既存"}: ${catName}（${visibility}${nsfw ? ", 年齢制限" : ""}）`];

  const specs: ChannelSpec[] = [
    { name: "🔗ワールド-world", type: ChannelType.GuildText, topic: `${jpName} / ${enName}: ワールドリンクと概要`, nsfw, overwrites: readOnly },
    { name: "🔧更新情報-updates", type: ChannelType.GuildText, topic: "更新ログ / Update log", nsfw, overwrites: readOnly },
    { name: "🐛バグ報告と要望-feedback", type: ChannelType.GuildForum, topic: "バグ報告と要望 / Bug reports & requests. タグで種別と言語を選んでください", nsfw, overwrites: canPost, tags: FEEDBACK_TAGS },
  ];
  if (visibility !== "public") {
    specs.push({ name: "💬さろん-lounge", type: ChannelType.GuildText, topic: "支援者雑談 / Supporter lounge", nsfw, overwrites: canChat });
  }
  for (const spec of specs) {
    const r = await ensureChannel(guild, cat, spec);
    out.push(`${r.created ? "作成" : "既存"}: ${spec.name}`);
  }
  out.push("");
  out.push("ワールドと更新情報はサーバーオーナーだけが書けます。他の管理者にも書かせる場合はチャンネル権限で個別に許可してください。");
  return out.join("\n");
}
