// サーバー構成の自動作成（/vrc-admin setup-*）。サーバー設計メモ（private/discord-structure.md）の構成をそのまま作る。
// すべて「同名があれば再利用」なので何度実行しても増えない。
import {
  ChannelType,
  PermissionFlagsBits,
  type CategoryChannel,
  type Guild,
  type GuildBasedChannel,
  type GuildMember,
  type OverwriteResolvable,
  type Role,
} from "discord.js";
import type { AppConfig } from "./config.js";
import { log } from "./log.js";

const P = PermissionFlagsBits;

/** Bot が実際に持っている権限だけに絞る（持っていない権限は他者に付与できず 50013 になる） */
function onlyHeld(me: GuildMember, bits: bigint[]): bigint[] {
  const held = bits.filter((b) => me.permissions.has(b));
  const missing = bits.filter((b) => !me.permissions.has(b));
  if (missing.length) log.warn(`setup: Bot が持っていない権限を上書きから除外: ${missing.map((b) => permName(b)).join(",")}`);
  return held;
}

function permName(bit: bigint): string {
  for (const [k, v] of Object.entries(PermissionFlagsBits)) if (v === bit) return k;
  return bit.toString();
}

export const ROLE_NAMES = {
  supporter: "Supporter",
  platinum: "Platinum",
  /** 住人のロール（2026-10-06 に Member から名前を変えた） */
  member: "Resident",
  memberOld: "Member",
  /** 協力者のロール（誰が協力者か分かるようにするだけ。チャンネルの権限には使わない） */
  tester: "Tester",
  sources: ["src-patreon-supporter", "src-patreon-platinum", "src-cien-supporter", "src-cien-platinum"],
} as const;

async function ensureRole(guild: Guild, name: string, opts: { color?: number; hoist?: boolean; oldName?: string }): Promise<{ role: Role; created: boolean }> {
  const existing = guild.roles.cache.find((r) => r.name === name) ?? (opts.oldName ? guild.roles.cache.find((r) => r.name === opts.oldName) : undefined);
  if (existing) {
    log.info(`setup-roles: 既存ロール再利用 ${name} (${existing.id})`);
    return { role: existing, created: false };
  }
  const role = await guild.roles.create({
    name,
    color: opts.color ?? 0,
    hoist: opts.hoist ?? false,
    mentionable: false,
    permissions: [],
    reason: "SupporterGate setup",
  });
  log.info(`setup-roles: ロール作成 ${name} (${role.id})`);
  return { role, created: true };
}

/** 出力ロールと入力ロールを作り、config.jsonc に貼る tiers スニペットを返す */
export async function setupRoles(guild: Guild): Promise<string> {
  log.info(`setup-roles 開始 guild=${guild.name} (${guild.id})`);
  await guild.roles.fetch();
  const lines: string[] = [];
  const sup = await ensureRole(guild, ROLE_NAMES.supporter, { color: 0xf5c542, hoist: true });
  const pla = await ensureRole(guild, ROLE_NAMES.platinum, { color: 0x8fd3ff, hoist: true });
  // 住人は支援とは別の軸（申請と認定で付く）。一覧では分けて見せない
  const mem = await ensureRole(guild, ROLE_NAMES.member, { oldName: ROLE_NAMES.memberOld });
  const tes = await ensureRole(guild, ROLE_NAMES.tester, { color: 0x4fc3b5 });
  const src: Record<string, Role> = {};
  for (const n of ROLE_NAMES.sources) {
    const r = await ensureRole(guild, n, {});
    src[n] = r.role;
    lines.push(`${r.created ? "作成" : "既存"}: ${n}`);
  }
  lines.unshift(
    `${sup.created ? "作成" : "既存"}: ${ROLE_NAMES.supporter}`,
    `${pla.created ? "作成" : "既存"}: ${ROLE_NAMES.platinum}`,
    `${mem.created ? "作成" : "既存"}: ${ROLE_NAMES.member}`,
    `${tes.created ? "作成" : "既存"}: ${ROLE_NAMES.tester}`,
  );

  // 並び順: 管理対象外のロールを下に、その上に src-* → Tester → Resident → Supporter → Platinum。Bot のロール（managed）は動かさない
  const ourIds = new Set([pla.role.id, sup.role.id, mem.role.id, tes.role.id, ...ROLE_NAMES.sources.map((n) => src[n].id)]);
  const others = [...guild.roles.cache.values()]
    .filter((r) => r.id !== guild.id && !r.managed && !ourIds.has(r.id))
    .sort((a, b) => a.position - b.position);
  const ascending = [...others, ...ROLE_NAMES.sources.slice().reverse().map((n) => src[n]), tes.role, mem.role, sup.role, pla.role];
  const me = guild.members.me;
  // Discord は「自分の最上位ロール以上の位置」への移動を拒否するため、Bot 自身のロールを最後尾（最上位）に含めて一括指定する
  const botRole = me && me.roles.highest.id !== guild.id ? me.roles.highest : null;
  const sequence = botRole ? [...ascending, botRole] : ascending;
  try {
    await guild.roles.setPositions(sequence.map((r, i) => ({ role: r, position: i + 1 })));
    log.info(`setup-roles: 並び替え完了 (下から: ${sequence.map((r) => r.name).join(" < ")})`);
  } catch (err) {
    log.warn(`setup-roles: 並び替え失敗 ${String(err)}`);
    lines.push("（並び替えは失敗。サーバー設定 → ロールで Bot を一番上にしてから再実行してください）");
  }
  // 既存ロールの見た目も揃える
  for (const [r, color, hoist] of [[sup.role, 0xf5c542, true], [pla.role, 0x8fd3ff, true]] as [Role, number, boolean][]) {
    if (r.color !== color || r.hoist !== hoist) {
      await r.edit({ color, hoist, reason: "SupporterGate setup" });
      log.info(`setup-roles: 見た目更新 ${r.name} color=#${color.toString(16)} hoist=${hoist}`);
    }
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
    "],",
    "// 住人を使う場合だけ（支援とは別に、申請と認定で付くロール）",
    `"member": { "roleId": "${mem.role.id}", "minDays": 7 },`,
    "// 協力者（期間を決めて支援者と同じに入れる人）に付けるロール。付けないなら書かない",
    `"tester": { "roleId": "${tes.role.id}", "defaultDays": 30 }`,
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
    log.info(`setup: 既存カテゴリ再利用・権限更新 ${name} (${existing.id}) overwrites=${overwrites.length}`);
    return { cat: existing, created: false };
  }
  const cat = await guild.channels.create({ name, type: ChannelType.GuildCategory, permissionOverwrites: overwrites, reason: "SupporterGate setup" });
  log.info(`setup: カテゴリ作成 ${name} (${cat.id}) overwrites=${overwrites.length}`);
  return { cat, created: true };
}

interface ChannelSpec {
  name: string;
  type: ChannelType.GuildText | ChannelType.GuildForum;
  topic?: string;
  nsfw?: boolean;
  overwrites?: OverwriteResolvable[];
  /** true ならチャンネル独自の権限を持たせず、カテゴリの権限に従わせる（同期）。overwrites より優先 */
  sync?: boolean;
  tags?: string[];
}

async function ensureChannel(guild: Guild, cat: CategoryChannel, spec: ChannelSpec): Promise<{ ch: GuildBasedChannel; created: boolean }> {
  const existing = cat.children.cache.find((c) => c.name === spec.name && c.type === spec.type);
  if (existing) {
    if (spec.sync) await existing.lockPermissions();
    else if (spec.overwrites && "permissionOverwrites" in existing) await existing.permissionOverwrites.set(spec.overwrites, "SupporterGate setup");
    // 年齢制限は、付ける方向にだけ揃える（nsfw を付け忘れて再実行しても、既に付いている制限は外さない）
    let nsfwNote = "";
    if (spec.nsfw && "nsfw" in existing && !existing.nsfw) {
      await existing.edit({ nsfw: true, reason: "SupporterGate setup" });
      nsfwNote = " 年齢制限を付与";
    }
    log.info(`setup: 既存チャンネル再利用・権限更新 ${cat.name}/${spec.name} (${existing.id})${spec.sync ? " カテゴリと同期" : ""}${nsfwNote}`);
    return { ch: existing, created: false };
  }
  const overwrites = spec.sync ? undefined : spec.overwrites;
  if (spec.type === ChannelType.GuildForum) {
    const ch = await guild.channels.create({
      name: spec.name,
      type: ChannelType.GuildForum,
      parent: cat,
      topic: spec.topic,
      nsfw: spec.nsfw ?? false,
      permissionOverwrites: overwrites,
      availableTags: (spec.tags ?? []).map((t) => ({ name: t })),
      reason: "SupporterGate setup",
    });
    if (spec.sync) await ch.lockPermissions();
    log.info(`setup: フォーラム作成 ${cat.name}/${spec.name} (${ch.id}) tags=${(spec.tags ?? []).join(",")} nsfw=${spec.nsfw ?? false}${spec.sync ? " カテゴリと同期" : ""}`);
    return { ch, created: true };
  }
  const ch = await guild.channels.create({
    name: spec.name,
    type: ChannelType.GuildText,
    parent: cat,
    topic: spec.topic,
    nsfw: spec.nsfw ?? false,
    permissionOverwrites: overwrites,
    reason: "SupporterGate setup",
  });
  if (spec.sync) await ch.lockPermissions();
  log.info(`setup: テキストチャンネル作成 ${cat.name}/${spec.name} (${ch.id}) nsfw=${spec.nsfw ?? false}${spec.sync ? " カテゴリと同期" : ""}`);
  return { ch, created: true };
}

const FEEDBACK_TAGS = ["Bug", "Request", "JP", "EN", "ZH"];

/**
 * ワールドの案内（リンク・入るのに要るもの・概要）のチャンネル。言語ごとに 1 つずつ作る。
 * 1 つのチャンネルに 4 か国語を並べると長くなり、読みたい言語にたどり着きにくいため
 */
export const WORLD_INFO_CHANNELS: { lang: string; name: string; topic: (jp: string, en: string) => string }[] = [
  { lang: "ja", name: "🔗ワールド-jp", topic: (jp) => `${jp}: ワールドのリンクと概要` },
  { lang: "en", name: "🔗world-en", topic: (_jp, en) => `${en}: world link and overview` },
  { lang: "zh", name: "🔗世界-zh", topic: (_jp, en) => `${en}: 世界链接与简介` },
  { lang: "ko", name: "🔗월드-ko", topic: (_jp, en) => `${en}: 월드 링크와 개요` },
];
/** 前の版の、案内のチャンネルの名前（1 つだけだった）。あれば、日本語のチャンネルとして使い続ける */
const OLD_WORLD_INFO_NAME = "🔗ワールド-world";

/**
 * はじめに（最初に読む案内）のチャンネル。言語ごとに 1 つずつ作る（ワールドの案内と同じ理由）
 */
export const START_HERE_CHANNELS: { lang: string; name: string; topic: string }[] = [
  { lang: "ja", name: "📖はじめに-jp", topic: "まずはこちらです。登録の手順" },
  { lang: "en", name: "📖start-here-en", topic: "Start here: how to register" },
  { lang: "zh", name: "📖开始之前-zh", topic: "请先阅读：注册步骤" },
  { lang: "ko", name: "📖시작하기-ko", topic: "먼저 읽어 주세요: 등록 방법" },
];
/** 前の版の、はじめにのチャンネルの名前（1 つだけだった）。あれば、日本語のチャンネルとして使い続ける */
const OLD_START_HERE_NAME = "📖はじめに-start-here";

/** ボタンだけを置くチャンネル（状態・住人・グループ）。設定の discord の項目名と、チャンネルの名前 */
export const PANEL_CHANNELS: { key: "statusChannelId" | "residentChannelId" | "groupChannelId"; name: string; topic: string }[] = [
  { key: "statusChannelId", name: "🔍状態-status", topic: "登録の状態の確認と、クレジットの ON / OFF / Check your status and turn credits on or off" },
  { key: "residentChannelId", name: "🏠住人-resident", topic: "住人の申請。支援は要りません / Apply as a resident. No support needed" },
  { key: "groupChannelId", name: "👥vrc-group", topic: "VRChat の Group への参加 / Join our VRChat Group" },
];

/** INFO カテゴリ（全員向け） */
export async function setupInfo(guild: Guild, config: AppConfig): Promise<string> {
  log.info(`setup-info 開始 guild=${guild.name} (${guild.id})`);
  await guild.channels.fetch();
  const everyone = guild.roles.everyone;
  const me = guild.members.me;
  const botOw: OverwriteResolvable[] = me ? [{ id: me.id, allow: onlyHeld(me, [P.ViewChannel, P.SendMessages, P.ManageMessages]) }] : [];
  const { cat, created } = await ensureCategory(guild, "📌 INFO", []);
  const out: string[] = [`${created ? "作成" : "既存"}: ${cat.name}`];

  // 前の版で作ったカテゴリ: 1 つだけだった はじめに を、日本語のチャンネルにする（投稿と権限はそのまま残る）
  const oldStart = cat.children.cache.find((c) => c.name === OLD_START_HERE_NAME && c.type === ChannelType.GuildText);
  if (oldStart && !cat.children.cache.some((c) => c.name === START_HERE_CHANNELS[0].name)) {
    await oldStart.edit({ name: START_HERE_CHANNELS[0].name, topic: START_HERE_CHANNELS[0].topic, reason: "SupporterGate setup" });
    log.info(`setup: 名前を変更 ${cat.name}/${OLD_START_HERE_NAME} -> ${START_HERE_CHANNELS[0].name} (${oldStart.id})`);
    out.push(`名前を変更: ${OLD_START_HERE_NAME} → ${START_HERE_CHANNELS[0].name}`);
  }

  const readOnly: OverwriteResolvable[] = [{ id: everyone.id, deny: [P.SendMessages, P.CreatePublicThreads, P.CreatePrivateThreads] }, ...botOw];
  // お知らせは、読む人がリアクションで反応できるようにする（書き込みはできない）
  const announce: OverwriteResolvable[] = [
    { id: everyone.id, allow: [P.AddReactions, P.ReadMessageHistory], deny: [P.SendMessages, P.CreatePublicThreads, P.CreatePrivateThreads] },
    ...botOw,
  ];
  const specs: ChannelSpec[] = [
    ...START_HERE_CHANNELS.map((s): ChannelSpec => ({ name: s.name, type: ChannelType.GuildText, topic: s.topic, overwrites: readOnly })),
    { name: "📢お知らせ-announcements", type: ChannelType.GuildText, overwrites: announce },
    {
      name: "🧾登録-register",
      type: ChannelType.GuildText,
      topic: "Discord と VRChat をつなぐ（VRChat の表示名の登録） / Link Discord to VRChat (register your display name)",
      // 書き込みはできない（2026-10-06。ボタンで足りるため）。スラッシュコマンドは使える。
      // 文字で「/vrc register」と書いた人を受け付けたいときは、このチャンネルの権限で送信を許可する
      overwrites: [
        { id: everyone.id, allow: [P.UseApplicationCommands], deny: [P.SendMessages, P.CreatePublicThreads, P.CreatePrivateThreads, P.AttachFiles, P.EmbedLinks] },
        ...botOw,
      ],
    },
    // ボタンだけのチャンネル。説明を分けて、1 つのパネルの文を短くする（2026-10-06）
    ...PANEL_CHANNELS.map((c): ChannelSpec => ({ name: c.name, type: ChannelType.GuildText, topic: c.topic, overwrites: readOnly })),
  ];
  let registerId: string | null = null;
  const panelIds: Record<string, string> = {};
  const ordered: GuildBasedChannel[] = [];
  for (const spec of specs) {
    const r = await ensureChannel(guild, cat, spec);
    ordered.push(r.ch);
    out.push(`${r.created ? "作成" : "既存"}: ${spec.name}`);
    if (spec.name === "🧾登録-register") registerId = r.ch.id;
    const panel = PANEL_CHANNELS.find((c) => c.name === spec.name);
    if (panel) panelIds[panel.key] = r.ch.id;
  }
  // 並びを揃える: はじめに（言語ごと）→ お知らせ → 登録
  await guild.channels
    .setPositions(ordered.map((ch, i) => ({ channel: ch.id, position: i })))
    .catch((err) => log.warn(`setup-info: 並び替えに失敗: ${String(err)}`));
  if (registerId) {
    out.push("");
    out.push(`登録チャンネル ID: \`${registerId}\``);
    if (config.registerChannelId !== registerId) {
      out.push(`config.jsonc の discord.registerChannelId に \`${registerId}\` を設定して Bot を再起動し、そのチャンネルで /vrc-admin panel を実行してください。`);
    }
  }
  const unset = PANEL_CHANNELS.filter((c) => panelIds[c.key] && config[c.key] !== panelIds[c.key]);
  if (unset.length > 0) {
    out.push("");
    out.push("config.jsonc の discord に、次を足して Bot を再起動し、/vrc-admin panel を実行してください（ボタンが、それぞれのチャンネルに分かれます）:");
    for (const c of unset) out.push(`\`"${c.key}": "${panelIds[c.key]}"\``);
  }
  return out.join("\n");
}

/** コミュニティカテゴリ（全員向け: 雑談と写真） */
export async function setupCommunity(guild: Guild): Promise<string> {
  log.info(`setup-community 開始 guild=${guild.name} (${guild.id})`);
  await guild.channels.fetch();
  const { cat, created } = await ensureCategory(guild, "💬 コミュニティ ⁄ Community", []);
  const out: string[] = [`${created ? "作成" : "既存"}: ${cat.name}`];
  const specs: ChannelSpec[] = [
    { name: "💬雑談-jp", type: ChannelType.GuildText, topic: "日本語の雑談" },
    { name: "💬chat-en", type: ChannelType.GuildText, topic: "English chat" },
    { name: "💬闲聊-zh", type: ChannelType.GuildText, topic: "中文闲聊" },
    { name: "💬잡담-ko", type: ChannelType.GuildText, topic: "한국어 잡담" },
    { name: "📷sfw-photo", type: ChannelType.GuildText, topic: "全年齢の写真・スクショ / SFW photos & screenshots" },
    { name: "🔞nsfw-photo", type: ChannelType.GuildText, topic: "年齢制限あり / Age-restricted photos", nsfw: true },
  ];
  const ordered: GuildBasedChannel[] = [];
  for (const spec of specs) {
    const r = await ensureChannel(guild, cat, spec);
    ordered.push(r.ch);
    out.push(`${r.created ? "作成" : "既存"}: ${spec.name}${spec.nsfw ? "（年齢制限）" : ""}`);
  }
  // 並びを揃える: 雑談（言語ごと）→ 写真
  await guild.channels
    .setPositions(ordered.map((ch, i) => ({ channel: ch.id, position: i })))
    .catch((err) => log.warn(`setup-community: 並び替えに失敗: ${String(err)}`));
  return out.join("\n");
}

export type WorldVisibility = "public" | "supporter" | "platinum" | "member";

/** ワールドごとのカテゴリ */
export async function setupWorld(
  guild: Guild,
  config: AppConfig,
  jpName: string,
  enName: string,
  visibility: WorldVisibility,
  nsfw: boolean,
): Promise<string> {
  log.info(`setup-world 開始 guild=${guild.name} jp=${jpName} en=${enName} visibility=${visibility} nsfw=${nsfw}`);
  await guild.channels.fetch();
  await guild.roles.fetch();
  const everyone = guild.roles.everyone;
  const me = guild.members.me;
  const catName = `${jpName} ⁄ ${enName}`;

  let viewer: Role | null = null;
  if (visibility === "supporter") viewer = findRole(guild, config, 1, ROLE_NAMES.supporter);
  if (visibility === "platinum") viewer = findRole(guild, config, 2, ROLE_NAMES.platinum);
  if (visibility === "member") {
    viewer =
      (config.member ? guild.roles.cache.get(config.member.roleId) : undefined) ??
      guild.roles.cache.find((r) => r.name === ROLE_NAMES.member || r.name === ROLE_NAMES.memberOld) ??
      null;
  }
  if (visibility !== "public" && !viewer) {
    log.warn(`setup-world: 閲覧ロールが見つからない visibility=${visibility}`);
    return `ロールが見つかりません。先に /vrc-admin setup-roles を実行してください（visibility=${visibility}）`;
  }

  // 「誰が見えて書けるか」はカテゴリで決め、フォーラムと雑談はカテゴリに従わせる（同期）。
  // 持ち主だけが書くチャンネル（ワールド・更新情報）にだけ、送信の拒否を足す。
  // Discord は同期していないチャンネルにカテゴリの権限を重ねないので、この 2 つには見える人の設定も書く
  const noSend = [P.SendMessages, P.SendMessagesInThreads, P.CreatePublicThreads, P.CreatePrivateThreads];
  const base: OverwriteResolvable[] = [];
  const readOnly: OverwriteResolvable[] = [];
  // 更新情報は、見られる人がリアクションで反応できるようにする（書き込みはできない）
  const updates: OverwriteResolvable[] = [];
  if (visibility === "public") {
    // 公開ワールドは @everyone の基本権限（見る・送信）のまま使う
    readOnly.push({ id: everyone.id, deny: noSend });
    updates.push({ id: everyone.id, allow: [P.AddReactions, P.ReadMessageHistory], deny: noSend });
  } else {
    base.push({ id: everyone.id, deny: [P.ViewChannel] });
    base.push({ id: viewer!.id, allow: me ? onlyHeld(me, [P.ViewChannel, P.SendMessages, P.SendMessagesInThreads, P.CreatePublicThreads]) : [P.ViewChannel, P.SendMessages] });
    readOnly.push({ id: everyone.id, deny: [P.ViewChannel] });
    readOnly.push({ id: viewer!.id, allow: [P.ViewChannel], deny: noSend });
    updates.push({ id: everyone.id, deny: [P.ViewChannel] });
    updates.push({ id: viewer!.id, allow: [P.ViewChannel, P.AddReactions, P.ReadMessageHistory], deny: noSend });
  }
  if (me) {
    // Bot は「自分が持っていない権限」を他者に付与できないため、
    // 子チャンネルで付与する権限をすべて Bot 自身にも明示的に許可しておく
    const botAllow = onlyHeld(me, [P.ViewChannel, P.SendMessages, P.SendMessagesInThreads, P.CreatePublicThreads, P.ManageMessages]);
    base.push({ id: me.id, allow: botAllow });
    readOnly.push({ id: me.id, allow: botAllow });
    updates.push({ id: me.id, allow: botAllow });
  }

  const { cat, created } = await ensureCategory(guild, catName, base);
  const out: string[] = [`${created ? "作成" : "既存"}: ${catName}（${visibility}${nsfw ? ", 年齢制限" : ""}）`];

  // 雑談は、公開ワールドでも限定ワールドでも同じ名前で作る（公開は全員、限定は閲覧ロールだけが書ける）
  const loungeTopic =
    visibility === "public" ? `${jpName} の雑談 / ${enName} lounge` : visibility === "member" ? "住人の雑談 / Residents lounge" : "支援者雑談 / Supporter lounge";
  // 前の版で作ったカテゴリ: 1 つだけだった案内のチャンネルを、日本語のチャンネルにする（投稿と権限はそのまま残る）
  const oldInfo = cat.children.cache.find((c) => c.name === OLD_WORLD_INFO_NAME && c.type === ChannelType.GuildText);
  if (oldInfo && !cat.children.cache.some((c) => c.name === WORLD_INFO_CHANNELS[0].name)) {
    await oldInfo.setName(WORLD_INFO_CHANNELS[0].name, "SupporterGate setup");
    log.info(`setup: 名前を変更 ${catName}/${OLD_WORLD_INFO_NAME} -> ${WORLD_INFO_CHANNELS[0].name} (${oldInfo.id})`);
    out.push(`名前を変更: ${OLD_WORLD_INFO_NAME} → ${WORLD_INFO_CHANNELS[0].name}`);
  }

  const specs: ChannelSpec[] = [
    ...WORLD_INFO_CHANNELS.map((info): ChannelSpec => ({ name: info.name, type: ChannelType.GuildText, topic: info.topic(jpName, enName), nsfw, overwrites: readOnly })),
    { name: "🔧更新情報-updates", type: ChannelType.GuildText, topic: "更新ログ / Update log", nsfw, overwrites: updates },
    { name: "🐛バグ報告と要望-feedback", type: ChannelType.GuildForum, topic: "バグ報告と要望 / Bug reports & requests. タグで種別と言語を選んでください", nsfw, sync: true, tags: FEEDBACK_TAGS },
    { name: "💬さろん-lounge", type: ChannelType.GuildText, topic: loungeTopic, nsfw, sync: true },
  ];
  const ordered: GuildBasedChannel[] = [];
  for (const spec of specs) {
    const r = await ensureChannel(guild, cat, spec);
    ordered.push(r.ch);
    out.push(`${r.created ? "作成" : "既存"}: ${spec.name}`);
  }
  // 並びを揃える: 案内（言語ごと）→ 更新情報 → フォーラム → 雑談
  await guild.channels
    .setPositions(ordered.map((ch, i) => ({ channel: ch.id, position: i })))
    .catch((err) => log.warn(`setup-world: 並び替えに失敗 ${catName}: ${String(err)}`));
  log.info(`setup-world 完了 ${catName} viewer=${viewer ? viewer.name : "@everyone"}`);
  out.push("");
  out.push("フォーラムと雑談はカテゴリの権限に従います（同期）。見える人を変えるときはカテゴリを変えてください。");
  out.push("ワールドの案内（言語ごとの 4 つ）と更新情報は、サーバーオーナーだけが書けます。他の管理者にも書かせる場合はチャンネル権限で個別に許可してください。");
  return out.join("\n");
}
