// 登録チャンネルに置くボタンパネルと、ボタン / フォーム / テキスト投稿の処理。
import {
  ActionRowBuilder,
  ButtonBuilder,
  ButtonInteraction,
  ButtonStyle,
  GuildMember,
  Message,
  MessageFlags,
  ModalBuilder,
  ModalSubmitInteraction,
  TextInputBuilder,
  TextInputStyle,
  type Guild,
  type MessageCreateOptions,
} from "discord.js";
import { isAdmin } from "./admin.js";
import { channelRefs, hasOwnChannel, PANEL_KINDS, panelChannelId, type PanelKind } from "./channels.js";
import { tierByRank, type AppConfig, type MemberConfig } from "./config.js";
import { langOf, t, type Lang } from "./i18n.js";
import { log } from "./log.js";
import { describe, fmtDate, memberState, parseTextRegister, registerName } from "./register.js";
import { personById, resolvePerson, resolvedReport, tierMention } from "./report.js";
import type { MemberRecord, Store } from "./store.js";
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
  /** 管理者用: 人を調べる（ボタンとフォーム）。申請のメッセージの「くわしく」は、後ろに Discord の ID が付く */
  adminLookup: "sg:admin:lookup",
  adminLookupModal: "sg:admin:lookup-modal",
  adminLookupQuery: "query",
  adminLookupUser: "sg:admin:lookup:",
} as const;

export interface PanelDeps extends SyncContext {
  requestPublish: () => void;
}

/** パネルごとの、言語ごとの文 */
type Lines = Record<Lang, string[]>;
const PANEL_LANGS: { lang: Lang; head: string }[] = [
  { lang: "ja", head: "### 🇯🇵 日本語" },
  { lang: "en", head: "### 🇬🇧 English" },
  { lang: "zh-CN", head: "### 🇨🇳 中文" },
  { lang: "ko", head: "### 🇰🇷 한국어" },
];
const PANEL_TITLE: Record<PanelKind, string> = {
  register: "## 🧾 登録 / Register",
  status: "## 🔍 状態とクレジット / Status and credits",
  resident: "## 🏠 住人 / Resident",
  group: "## 👥 VRChat の Group / VRChat Group",
};

function statusLines(): Lines {
  return {
    ja: [
      "- **状態**: 登録した表示名、支援者かどうか、住人の申請がどこまで進んだかを見られます",
      "- **クレジット ON / OFF**: ワールドの中の支援者のボードに、名前を出すかを選べます\n  - 最初は ON です\n  - 支援者の方にだけ関係します",
    ],
    en: [
      "- **Status**: see your registered display name, whether you are a supporter, and how far your resident application has gone",
      "- **Credits ON / OFF**: choose whether your name appears on the supporter board in the worlds. It starts as ON. This only matters for supporters",
    ],
    "zh-CN": [
      "- **Status**：查看已注册的显示名称、是否为支持者，以及居民申请的进度",
      "- **Credits ON / OFF**：选择是否在世界内的支持者名单板上显示你的名字\n  - 默认为 ON\n  - 仅与支持者有关",
    ],
    "zh-TW": [],
    ko: [
      "- **Status**: 등록한 표시 이름, 후원자인지, 주민 신청이 어디까지 진행되었는지 볼 수 있습니다",
      "- **Credits ON / OFF**: 월드 안의 후원자 보드에 이름을 표시할지 고를 수 있습니다. 처음에는 ON입니다. 후원자에게만 해당합니다",
    ],
  };
}

function residentLines(config: AppConfig, refs: (lang: Lang) => Record<PanelKind, string>): Lines {
  const mc = config.member!;
  if (mc.mode !== "apply") {
    const d = mc.minDays;
    return {
      ja: [`- **🏠 住人**: サーバーに参加して ${d} 日以上の方は、支援の有無に関係なく、住人になれます（18 歳以上の方のみ）`],
      en: [`- **🏠 Resident**: after ${d} days on this server, you can become a resident, whether or not you are a supporter (18+ only)`],
      "zh-CN": [`- **🏠 Resident**：加入本服务器满 ${d} 天后，无论是否支持，都可以成为居民（仅限 18 岁以上）`],
      "zh-TW": [],
      ko: [`- **🏠 Resident**: 서버 참가 후 ${d}일이 지나면 후원 여부와 관계없이 주민이 될 수 있습니다 (18세 이상)`],
    };
  }
  return {
    ja: [
      "- 住人になると、VRChat の Group に参加できます\n  - 支援は要りません\n  - Patreon の会員（メンバーシップ）とは別のものです",
      "- 申請できるのは 18 歳以上の方です\n  - 確認のために、VRChat の公開プロフィールを拝見します",
      `- 先に ${refs("ja").register} で表示名を登録してから、**🏠 住人** を押してください`,
    ],
    en: [
      "- Residents can join our VRChat Group. No support is needed. This is separate from Patreon membership",
      "- You can apply if you are 18 or older. To review your application, we look at your public VRChat profile",
      `- Register your display name in ${refs("en").register} first, then press **🏠 Resident**`,
    ],
    "zh-CN": [
      "- 成为居民后，可以加入 VRChat Group\n  - 无需支持\n  - 这与 Patreon 的会员资格无关",
      "- 年满 18 岁即可申请\n  - 为了确认，我们会查看你公开的 VRChat 个人资料",
      `- 请先在 ${refs("zh-CN").register} 注册显示名称，然后点击 **🏠 Resident**`,
    ],
    "zh-TW": [],
    ko: [
      "- 주민이 되면 VRChat Group에 참가할 수 있습니다. 후원은 필요 없습니다. Patreon 멤버십과는 다릅니다",
      "- 18세 이상이면 신청할 수 있습니다. 확인을 위해 공개된 VRChat 프로필을 봅니다",
      `- 먼저 ${refs("ko").register}에서 표시 이름을 등록한 뒤 **🏠 Resident**를 눌러 주세요`,
    ],
  };
}

function groupLines(config: AppConfig, refs: (lang: Lang) => Record<PanelKind, string>): Lines {
  const resident = config.member !== null;
  return {
    ja: [
      `- ${resident ? "支援者と住人" : "支援者"}の方は、VRChat の Group に参加できます\n  - フレンドでなくても、Group のインスタンスで一緒に遊べます`,
      "- **👥 グループ** を押すと、登録した表示名の VRChat アカウントに、Group の招待が届きます\n  - VRChat の通知から承諾してください",
      ...(resident ? [`- 支援者でも住人でもない方は、先に ${refs("ja").resident} で住人の申請をしてください`] : []),
    ],
    en: [
      `- ${resident ? "Supporters and residents" : "Supporters"} can join our VRChat Group, and play together in Group instances even if you are not friends`,
      "- Press **👥 Group** and a Group invite is sent to the VRChat account with your registered name. Accept it from your VRChat notifications",
      ...(resident ? [`- If you are neither a supporter nor a resident, apply as a resident in ${refs("en").resident} first`] : []),
    ],
    "zh-CN": [
      `- ${resident ? "支持者和居民" : "支持者"}可以加入 VRChat Group\n  - 即使不是好友，也可以在 Group 实例中一起游玩`,
      "- 点击 **👥 Group** 后，会向你注册的显示名称的 VRChat 账号发送 Group 邀请\n  - 请在 VRChat 的通知中接受",
      ...(resident ? [`- 既不是支持者也不是居民的话，请先在 ${refs("zh-CN").resident} 申请成为居民`] : []),
    ],
    "zh-TW": [],
    ko: [
      `- ${resident ? "후원자와 주민은" : "후원자는"} VRChat Group에 참가할 수 있습니다. 친구가 아니어도 Group 인스턴스에서 함께 놀 수 있습니다`,
      "- **👥 Group**을 누르면 등록한 표시 이름의 VRChat 계정으로 Group 초대가 전송됩니다. VRChat 알림에서 수락해 주세요",
      ...(resident ? [`- 후원자도 주민도 아니라면, 먼저 ${refs("ko").resident}에서 주민 신청을 해 주세요`] : []),
    ],
  };
}

/** 登録のパネルに並べるときの、1 ボタン 1 行の説明（専用のチャンネルが無いボタン） */
function shortLines(config: AppConfig, kind: "status" | "resident" | "group"): Lines {
  const apply = config.member?.mode === "apply";
  const d = config.member?.minDays ?? 0;
  const resident = config.member !== null;
  if (kind === "status") {
    return {
      ja: ["- **状態**: 登録と支援の状態を見られます", "- **クレジット ON / OFF**: ワールドの支援者のボードに、名前を出すかを選べます"],
      en: ["- **Status**: see your registration and support status. **Credits ON / OFF**: choose whether your name appears on the supporter board in the worlds"],
      "zh-CN": ["- **Status**：查看注册与支持状态", "- **Credits ON / OFF**：选择是否在世界内的支持者名单板上显示名字"],
      "zh-TW": [],
      ko: ["- **Status**: 등록과 후원 상태를 볼 수 있습니다. **Credits ON / OFF**: 월드 안의 후원자 보드에 이름을 표시할지 고를 수 있습니다"],
    };
  }
  if (kind === "resident") {
    return apply
      ? {
          ja: ["- **🏠 住人**: 住人の申請（18 歳以上。支援は要りません）\n  - 住人になると、VRChat の Group に参加できます"],
          en: ["- **🏠 Resident**: apply as a resident (18+, no support needed). Residents can join our VRChat Group"],
          "zh-CN": ["- **🏠 Resident**：申请成为居民（18 岁以上，无需支持）\n  - 居民可以加入 VRChat Group"],
          "zh-TW": [],
          ko: ["- **🏠 Resident**: 주민 신청 (18세 이상, 후원 불필요). 주민은 VRChat Group에 참가할 수 있습니다"],
        }
      : {
          ja: [`- **🏠 住人**: サーバーに参加して ${d} 日以上の方は、支援の有無に関係なく、住人になれます（18 歳以上の方のみ）`],
          en: [`- **🏠 Resident**: after ${d} days on this server, you can become a resident, whether or not you are a supporter (18+ only)`],
          "zh-CN": [`- **🏠 Resident**：加入本服务器满 ${d} 天后，无论是否支持，都可以成为居民（仅限 18 岁以上）`],
          "zh-TW": [],
          ko: [`- **🏠 Resident**: 서버 참가 후 ${d}일이 지나면 후원 여부와 관계없이 주민이 될 수 있습니다 (18세 이상)`],
        };
  }
  return {
    ja: [`- **👥 グループ**: ${resident ? "支援者と住人" : "支援者"}の方は、VRChat の Group に招待してもらえます`],
    en: [`- **👥 Group**: ${resident ? "supporters and residents" : "supporters"} can get an invite to our VRChat Group`],
    "zh-CN": [`- **👥 Group**：${resident ? "支持者和居民" : "支持者"}可以收到 VRChat Group 的邀请`],
    "zh-TW": [],
    ko: [`- **👥 Group**: ${resident ? "후원자와 주민은" : "후원자는"} VRChat Group 초대를 받을 수 있습니다`],
  };
}

function registerLines(config: AppConfig, refs: (lang: Lang) => Record<PanelKind, string>): Lines {
  const ownStatus = hasOwnChannel(config, "status");
  const status = shortLines(config, "status");
  const lines: Lines = {
    ja: [
      "- **🧾 登録**: VRChat の表示名（プロフィールに出ている名前）を入れると、Discord と VRChat のアカウントがつながります\n  - 支援者の方も、住人の申請をする方も、最初にこれを押してください",
      ...(ownStatus ? ["- **状態**: つながったか、支援者として確認できたかを見られます"] : status.ja),
      "- 表示名を変えたら、登録し直してください（30 日に 1 回まで）",
    ],
    en: [
      "- **Register**: enter your VRChat display name (the name on your profile) to link your Discord account to your VRChat account. Press this first, whether you are a supporter or want to apply as a resident",
      ...(ownStatus ? ["- **Status**: check that the link worked and whether your supporter role was detected"] : status.en),
      "- If you change your display name, register again (once every 30 days)",
    ],
    "zh-CN": [
      "- **Register**：输入你的 VRChat 显示名称（个人资料上显示的名字），即可把 Discord 账号和 VRChat 账号关联起来\n  - 无论是支持者还是想申请成为居民，都请先点这里",
      ...(ownStatus ? ["- **Status**：查看是否已关联、是否已确认为支持者"] : status["zh-CN"]),
      "- 更改显示名称后请重新注册（每 30 天一次）",
    ],
    "zh-TW": [],
    ko: [
      "- **Register**: VRChat 표시 이름(프로필에 표시되는 이름)을 입력하면 Discord 계정과 VRChat 계정이 연결됩니다. 후원자도, 주민 신청을 하려는 분도 먼저 이것을 눌러 주세요",
      ...(ownStatus ? ["- **Status**: 연결되었는지, 후원자로 확인되었는지 볼 수 있습니다"] : status.ko),
      "- 표시 이름을 바꾸면 다시 등록해 주세요(30일에 1회)",
    ],
  };
  // 専用のチャンネルが無いボタンは、ここに説明を並べる
  const add = (more: Lines): void => {
    for (const { lang } of PANEL_LANGS) lines[lang].push(...more[lang]);
  };
  if (config.member && !hasOwnChannel(config, "resident")) add(shortLines(config, "resident"));
  if (config.group && !hasOwnChannel(config, "group")) add(shortLines(config, "group"));
  // 専用のチャンネルがあるボタンは、場所だけを書く
  const elsewhere = (lang: Lang, words: Record<"status" | "resident" | "group", string>, sep: string, head: string): string | null => {
    const r = refs(lang);
    const items = [
      ownStatus ? `${words.status}${r.status}` : "",
      config.member && hasOwnChannel(config, "resident") ? `${words.resident}${r.resident}` : "",
      config.group && hasOwnChannel(config, "group") ? `${words.group}${r.group}` : "",
    ].filter(Boolean);
    return items.length > 0 ? `- ${head}${items.join(sep)}` : null;
  };
  const where = {
    ja: elsewhere("ja", { status: "クレジットは ", resident: "住人の申請は ", group: "VRChat の Group は " }, "、", "ほかのボタンの場所: "),
    en: elsewhere("en", { status: "credits in ", resident: "resident application in ", group: "VRChat Group in " }, ", ", "Other buttons: "),
    "zh-CN": elsewhere("zh-CN", { status: "致谢名单在 ", resident: "居民申请在 ", group: "VRChat Group 在 " }, "，", "其他按钮："),
    ko: elsewhere("ko", { status: "크레딧은 ", resident: "주민 신청은 ", group: "VRChat Group은 " }, ", ", "다른 버튼: "),
  };
  for (const { lang } of PANEL_LANGS) {
    const w = where[lang as keyof typeof where];
    if (w) lines[lang].push(w);
  }
  // 共有のお願い（くわしい文は、登録の返事と、住人の案内に出る）。全員が読む場所なので、短くここにも置く。
  // スラッシュコマンドの注意は置かない（このチャンネルに文字を書き込んだ人には、自動の案内が出る）
  lines.ja.push("- **共有のお願い**: 写真や動画に、限定のワールドを特定できる情報（名前・リンク・招待）を載せないでください");
  lines.en.push("- **Sharing**: never post anything that identifies the limited worlds (name, link, or invite) with photos or videos");
  lines["zh-CN"].push("- **分享**：发布照片或视频时，请勿附上能识别限定世界的信息（名称、链接、邀请）");
  lines.ko.push("- **공유**: 사진이나 영상에 한정 월드를 특정할 수 있는 정보(이름, 링크, 초대)를 올리지 마세요");
  return lines;
}

/**
 * パネルの固定メッセージ。全員が読む場所なので 4 言語を載せ、言語ごとの節に分ける
 * （話題ごとに 4 言語を並べると、1 行ごとに言語が入れ替わって読みにくい）。
 * ボタンのラベルは日本語と英語の併記なので、中国語と韓国語の節では、英語のラベルでボタンを指す。
 * kind を省くと登録のパネル。専用のチャンネルが無いボタンは、登録のパネルに並ぶ
 */
export function buildPanelMessage(config: AppConfig, kind: PanelKind = "register"): MessageCreateOptions {
  const refs = (lang: Lang): Record<PanelKind, string> => channelRefs(config, lang);
  const body: Lines =
    kind === "status" ? statusLines() : kind === "resident" ? residentLines(config, refs) : kind === "group" ? groupLines(config, refs) : registerLines(config, refs);
  const lines = [PANEL_TITLE[kind], ...PANEL_LANGS.flatMap(({ lang, head }) => [head, ...body[lang]])];

  const registerBtn = new ButtonBuilder().setCustomId(IDS.register).setLabel("登録 / Register").setStyle(ButtonStyle.Primary).setEmoji("🧾");
  const statusBtn = new ButtonBuilder().setCustomId(IDS.status).setLabel("状態 / Status").setStyle(ButtonStyle.Secondary);
  const creditOn = new ButtonBuilder().setCustomId(IDS.creditOn).setLabel("クレジット ON / Credits ON").setStyle(ButtonStyle.Success);
  const creditOff = new ButtonBuilder().setCustomId(IDS.creditOff).setLabel("クレジット OFF / Credits OFF").setStyle(ButtonStyle.Secondary);
  const residentBtn = new ButtonBuilder().setCustomId(IDS.member).setLabel("住人 / Resident").setStyle(ButtonStyle.Secondary).setEmoji("🏠");
  const groupBtn = new ButtonBuilder().setCustomId(IDS.group).setLabel("グループ / Group").setStyle(ButtonStyle.Secondary).setEmoji("👥");

  const rows: ActionRowBuilder<ButtonBuilder>[] = [];
  if (kind === "status") rows.push(new ActionRowBuilder<ButtonBuilder>().addComponents(statusBtn, creditOn, creditOff));
  else if (kind === "resident") rows.push(new ActionRowBuilder<ButtonBuilder>().addComponents(residentBtn));
  else if (kind === "group") rows.push(new ActionRowBuilder<ButtonBuilder>().addComponents(groupBtn));
  else {
    // 状態は、専用のチャンネルがあっても登録のパネルに残す（Patreon の手順が「register のチャンネルで Status を押す」と書いているため）
    const first = hasOwnChannel(config, "status") ? [registerBtn, statusBtn] : [registerBtn, statusBtn, creditOn, creditOff];
    rows.push(new ActionRowBuilder<ButtonBuilder>().addComponents(...first));
    const second: ButtonBuilder[] = [];
    if (config.member && !hasOwnChannel(config, "resident")) second.push(residentBtn);
    if (config.group && !hasOwnChannel(config, "group")) second.push(groupBtn);
    if (second.length > 0) rows.push(new ActionRowBuilder<ButtonBuilder>().addComponents(...second));
  }
  return { content: lines.join("\n"), components: rows };
}

/** パネルの見分け方: そのパネルにだけあるボタン */
const PANEL_MARKER: Record<PanelKind, string> = { register: IDS.register, status: IDS.creditOn, resident: IDS.member, group: IDS.group };

/** 置くべきパネル（専用のチャンネルがあり、機能が設定されているもの。登録は常に） */
export function panelsToPlace(config: AppConfig): PanelKind[] {
  return PANEL_KINDS.filter((kind) => {
    if (kind === "register") return config.registerChannelId !== null;
    if (!hasOwnChannel(config, kind)) return false;
    if (kind === "resident") return config.member !== null;
    if (kind === "group") return config.group !== null;
    return true;
  });
}

/** パネルを、それぞれのチャンネルに置く。既にあれば書き換える（ピン留めがそのまま生きる）。結果の行を返す */
export async function placePanels(guild: Guild, config: AppConfig, botUserId: string): Promise<string[]> {
  const out: string[] = [];
  for (const kind of panelsToPlace(config)) {
    const channelId = panelChannelId(config, kind)!;
    const channel = await guild.channels.fetch(channelId).catch(() => null);
    if (!channel || !channel.isTextBased() || !("messages" in channel)) {
      out.push(`${kind}: チャンネル ${channelId} が見つからないか、投稿できません`);
      continue;
    }
    const panel = buildPanelMessage(config, kind);
    const recent = await channel.messages.fetch({ limit: 50 }).catch(() => null);
    const old = recent?.find((m) => m.author.id === botUserId && m.components.some((row) => JSON.stringify(row.toJSON()).includes(`"${PANEL_MARKER[kind]}"`)));
    if (old) {
      const same = old.content === panel.content && JSON.stringify(old.components.map((r) => r.toJSON())) === JSON.stringify((panel.components ?? []).map((r) => ("toJSON" in r ? r.toJSON() : r)));
      if (!same) await old.edit({ content: panel.content, components: panel.components });
      out.push(`${kind}: <#${channelId}> ${same ? "変更なし" : "書き換えた"}（${panel.content!.length} 文字）`);
    } else {
      await channel.send({ ...panel, allowedMentions: { parse: [] } });
      out.push(`${kind}: <#${channelId}> 新しく置いた（${panel.content!.length} 文字）。\nピン留めしておくと見つけやすい`);
    }
    log.info(`パネル ${kind} channel=${channelId} ${old ? "書き換え" : "投稿"}`);
  }
  return out;
}

/**
 * 表示名を登録した直後に、住人の条件が揃った人へロールを付ける。
 * 管理者が先に認定していた人（/vrc-admin member-grant）は、表示名の登録で条件が揃う。定期の同期まで待たせない
 */
export async function activateMemberIfReady(config: AppConfig, store: Store, member: GuildMember | null): Promise<boolean> {
  const mc = config.member;
  if (!mc || !member) return false;
  const rec = store.get(member.id);
  if (!rec || rec.memberActive) return false;
  if (member.joinedAt) rec.joinedAt = member.joinedAt.toISOString();
  const now = new Date();
  if (!isMemberEligible(config, rec, now)) return false;
  rec.memberActive = true;
  rec.updatedAt = now.toISOString();
  store.save();
  const who = `${member.user.tag} (${member.id})`;
  await member.roles.add(mc.roleId, "SupporterGate member").catch((err) => log.warn(`住人のロール付与に失敗 ${who}: ${String(err)}`));
  log.info(`住人になりました ${who}: 表示名の登録で、条件が揃いました`);
  return true;
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

/** VRChat の年齢確認の値を、読める形にする */
function ageText(v: string): string {
  if (v === "18+") return "18+（確認済み）";
  if (v === "verified") return "verified（確認済み）";
  if (v === "hidden") return "hidden（未確認か、非公開）";
  return v;
}

/** 管理者に見せる、申請の内容 */
function reviewContent(config: AppConfig, rec: MemberRecord, ownerId: string, profile: VrcProfile | null, profileNote: string, now: Date): string {
  const days = rec.joinedAt ? Math.floor((now.getTime() - new Date(rec.joinedAt).getTime()) / 86_400_000) : null;
  const lines = [
    `📨 **住人の申請** <@${ownerId}>`,
    `申請した人: <@${rec.discordId}>（${rec.discordTag ?? "-"}）`,
    `サーバーに参加: ${rec.joinedAt ? `${fmtDate(rec.joinedAt)}（${days} 日前）` : "不明"}`,
    `VRChat の表示名: **${rec.vrcName ?? "-"}**`,
  ];
  if (profile) {
    lines.push(`VRChat のプロフィール: https://vrchat.com/home/user/${profile.id}`);
    // 1 項目ずつ改行する（並べると読みにくい）
    if (profile.dateJoined) lines.push(`・VRChat の登録日: ${profile.dateJoined}`);
    if (profile.trust) lines.push(`・ランク: ${profile.trust}`);
    if (profile.vrcPlus !== null) lines.push(`・VRC+: ${profile.vrcPlus ? "会員" : "会員ではない"}`);
    if (profile.ageVerification) lines.push(`・年齢確認: ${ageText(profile.ageVerification)}`);
    const bio = profile.bio.replace(/\s+/g, " ").trim();
    if (bio.length > 0) lines.push(`・自己紹介: ${bio.slice(0, 200)}${bio.length > 200 ? "…" : ""}`);
  } else {
    lines.push(`VRChat のプロフィール: ${profileNote}`);
  }
  // ロールのメンションにすると、Discord がロールの色（Supporter は金、Platinum は水色）で出す。通知は飛ばさない
  lines.push(`支援: ${tierMention(config, rec.effectiveRank)}`);
  lines.push("本人確認: していません。\n登録した表示名が、申請した本人の VRChat アカウントかどうかは、必要なら直接たずねて確かめてください。");
  lines.push("プロフィールを見て、下のボタンで決めてください。\n認定すると、本人にくわしい案内（内容の注意と共有のお願い）が見えるようになり、本人が同意した時点で住人のロールが付きます。");
  return lines.join("\n");
}

/** 申請のメッセージに付ける「くわしく」のボタン（決めたあとも残す） */
function lookupRow(userId: string): ActionRowBuilder<ButtonBuilder> {
  return new ActionRowBuilder<ButtonBuilder>().addComponents(
    new ButtonBuilder().setCustomId(IDS.adminLookupUser + userId).setLabel("くわしく").setStyle(ButtonStyle.Secondary).setEmoji("🔎"),
  );
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
    await interaction.update({ content: t(lang, "member.applied", channelRefs(config, lang)), components: [] });
    return;
  }
  // VRChat への問い合わせに数秒かかるので、先に受け付けだけ返す
  await interaction.deferUpdate();
  rec.memberAppliedAt = now.toISOString();
  rec.memberDeclinedAt = null;
  rec.memberApprovedAt = null;
  rec.updatedAt = now.toISOString();
  store.save();
  log.info(`住人の申請 ${who}: VRChat の表示名=${rec.vrcName ?? "-"} 参加日=${rec.joinedAt ?? "-"} ランク=${rec.effectiveRank}`);

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
      profileNote = "取得できませんでした（表示名で検索してください）";
      log.warn(`住人の申請: VRChat のプロフィールの取得に失敗 ${who}: ${String(err)}`);
    }
  }

  const channelId = mc.reviewChannelId ?? config.commandsChannelId ?? config.logChannelId;
  const channel = channelId ? await interaction.guild.channels.fetch(channelId).catch(() => null) : null;
  if (channel && channel.isTextBased()) {
    const ownerId = interaction.guild.ownerId;
    const row = new ActionRowBuilder<ButtonBuilder>().addComponents(
      new ButtonBuilder().setCustomId(IDS.memberApprove + rec.discordId).setLabel("認定する").setStyle(ButtonStyle.Success),
      new ButtonBuilder().setCustomId(IDS.memberDecline + rec.discordId).setLabel("見送る").setStyle(ButtonStyle.Secondary),
      new ButtonBuilder().setCustomId(IDS.adminLookupUser + rec.discordId).setLabel("くわしく").setStyle(ButtonStyle.Secondary).setEmoji("🔎"),
    );
    await channel
      .send({ content: reviewContent(config, rec, ownerId, profile, profileNote, now), components: [row], allowedMentions: { users: [ownerId] }, flags: MessageFlags.SuppressEmbeds })
      .catch((err) => log.warn(`住人の申請を、管理のチャンネルに出せませんでした ${who}: ${String(err)}。/vrc-admin member-grant で認定できます`));
  } else {
    log.warn(`住人の申請を出すチャンネルがありません ${who}。/vrc-admin member-grant で認定できます`);
  }
  await interaction.editReply({ content: t(lang, "member.applied", channelRefs(config, lang)), components: [] });
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
  // 結果を、申請のメッセージの下に書き足して、決めるボタンを外す（「くわしく」は残す）
  const close = async (note: string): Promise<void> => {
    await interaction.update({ content: `${interaction.message.content}\n\n${note}`, components: [lookupRow(userId)], allowedMentions: { parse: [] } });
  };
  if (!mc || !rec || !rec.memberAppliedAt) {
    await close("ℹ️ この申請は、もう処理済みか、取り下げられています。");
    return;
  }
  const who = `${rec.discordTag ?? "-"} (${userId})`;

  if (!approve) {
    rec.memberAppliedAt = null;
    rec.memberConsentAt = null;
    rec.memberApprovedAt = null;
    rec.memberDeclinedAt = now.toISOString();
    rec.memberManual = false;
    rec.memberActive = false;
    rec.updatedAt = now.toISOString();
    store.save();
    log.info(`住人の申請を見送り ${who} by ${by}`);
    await close(`⏸️ 見送りました（${by}、${fmtDate(now.toISOString())}）。\n本人には知らせていません。\n本人は「状態」のボタンで、見送りと、次に申請できる日を見られます。`);
    return;
  }

  if (rec.banned) {
    await interaction.reply({ content: "この人は BAN 中です。\n認定するなら、先に `/vrc-admin unban` で解除してください。", ephemeral: true });
    return;
  }
  const target = await interaction.guild.members.fetch(userId).catch(() => null);
  if (!target) {
    rec.memberAppliedAt = null;
    rec.memberConsentAt = null;
    rec.updatedAt = now.toISOString();
    store.save();
    await close("ℹ️ 申請した人は、もうサーバーにいません。\n申請を閉じました。");
    return;
  }
  // 認定しても、ここでは住人にしない。本人が、くわしい案内を読んで同意した時点で住人になる
  rec.joinedAt = target.joinedAt ? target.joinedAt.toISOString() : rec.joinedAt;
  rec.memberApprovedAt = now.toISOString();
  rec.memberAppliedAt = null;
  rec.memberDeclinedAt = null;
  rec.memberActive = isMemberEligible(config, rec, now);
  rec.updatedAt = now.toISOString();
  store.save();
  if (rec.memberActive) {
    await target.roles.add(mc.roleId, `SupporterGate member approved by ${by}`).catch((err) => log.warn(`住人のロール付与に失敗 ${who}: ${String(err)}`));
    deps.requestPublish();
  }
  log.info(`住人の申請を認定 ${who} by ${by} 有効=${rec.memberActive}`);

  // 本人に知らせる。DM を受け取らない設定なら届かないが、「状態」のボタンで分かる。DM には、内容の説明は書かない
  const dmText = (["ja", "en"] as Lang[]).map((l) => t(l, "member.approvedDm", channelRefs(config, l))).join("\n\n");
  let dmNote = "本人に DM で知らせました。";
  try {
    await target.send({ content: dmText });
  } catch {
    dmNote = "本人への DM は届きませんでした（受け取らない設定）。\n本人は「状態」のボタンで分かります。";
  }
  const roleNote = rec.memberActive ? "住人のロールを付けました。" : "本人が、住人のボタンから案内に同意すると、住人のロールが付きます。";
  await close(`✅ 認定しました（${by}、${fmtDate(now.toISOString())}）。\n${roleNote}\n${dmNote}`);
}

/** 申請制で、認定は済んだが、本人の同意がまだの人か */
function isAwaitingConsent(config: AppConfig, rec: MemberRecord): boolean {
  return config.member?.mode === "apply" && !rec.banned && !rec.memberManual && rec.memberApprovedAt !== null && !rec.memberConsentAt;
}

/**
 * 認定された人に見せる、くわしい案内（内容の注意と共有のお願い）と、同意のボタン。認定された人にだけ見せる。
 * lead は、案内の前に置く一言
 */
function consentGuide(lang: Lang, lead: string | null): { content: string; components: ActionRowBuilder<ButtonBuilder>[] } {
  const row = new ActionRowBuilder<ButtonBuilder>().addComponents(
    new ButtonBuilder().setCustomId(IDS.memberAgree).setLabel(t(lang, "member.agree")).setStyle(ButtonStyle.Success),
    new ButtonBuilder().setCustomId(IDS.memberLeave).setLabel(t(lang, "member.withdraw")).setStyle(ButtonStyle.Secondary),
  );
  const parts = [t(lang, "member.explainApproved"), t(lang, "share.notice"), t(lang, "member.confirm")];
  return { content: (lead ? [lead, ...parts] : parts).join("\n\n"), components: [row] };
}

/** 住人のボタン（説明を出す・同意する・取り消す）。申請制のときは、同意が申請になる */
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
    await interaction.reply({ content: t(lang, "member.needName", channelRefs(config, lang)), ephemeral: true });
    return;
  }
  const who = `${interaction.user.tag} (${interaction.user.id})`;
  rec.discordTag = interaction.user.tag;
  if (interaction.member.joinedAt) rec.joinedAt = interaction.member.joinedAt.toISOString();
  const id = interaction.customId;
  const apply = mc.mode === "apply";
  const now = new Date();

  // 申請制で、認定は済んだが、本人の同意がまだの人
  const awaitingConsent = isAwaitingConsent(config, rec);
  // 申請制で、申請を出して確認を待っている人
  const pendingReview = apply && !rec.memberManual && rec.memberAppliedAt !== null && !rec.memberApprovedAt;

  if (id === IDS.member) {
    if (awaitingConsent) {
      await interaction.reply({ ...consentGuide(lang, null), ephemeral: true });
    } else if (!rec.memberConsentAt && !rec.memberManual && !pendingReview) {
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
      // 申請の段階では、住人向けの物の中身には触れない（年齢の確認と、確認のしかただけ）
      const content = apply
        ? [t(lang, "member.explainApply"), t(lang, "member.confirmApply")].join("\n\n")
        : [t(lang, "member.explain", { days: mc.minDays }), t(lang, "share.notice"), t(lang, "member.confirm")].join("\n\n");
      await interaction.reply({ content, components: [row], ephemeral: true });
    } else {
      const pending = pendingReview;
      const row = new ActionRowBuilder<ButtonBuilder>().addComponents(
        new ButtonBuilder().setCustomId(IDS.memberLeave).setLabel(t(lang, pending ? "member.withdraw" : "member.leave")).setStyle(ButtonStyle.Secondary),
      );
      await interaction.reply({ content: `${t(lang, "member.label")}: ${memberState(config, rec, lang)}`, components: [row], ephemeral: true });
    }
    return;
  }

  if (id === IDS.memberAgree) {
    if (apply && !rec.memberManual && !rec.memberApprovedAt) {
      await submitApplication(deps, interaction, lang, rec, now);
      return;
    }
    if (!rec.memberConsentAt) rec.memberConsentAt = now.toISOString();
    rec.memberActive = isMemberEligible(config, rec, now);
    rec.updatedAt = now.toISOString();
    store.save();
    log.info(`住人の同意 ${who}: 同意を記録、有効=${rec.memberActive} 参加日=${rec.joinedAt ?? "-"}`);
    let content: string;
    if (rec.memberActive) {
      // ロールはここで付ける。失敗しても次の同期が付け直す
      await interaction.member.roles.add(mc.roleId, "SupporterGate member").catch((err) => log.warn(`住人のロール付与に失敗 ${who}: ${String(err)}`));
      deps.requestPublish();
      content = t(lang, "member.granted");
      if (apply && config.group) content += `\n${t(lang, "member.approvedDmGroup", channelRefs(config, lang))}`;
    } else {
      const from = memberEligibleFrom(config, rec);
      content = t(lang, "member.pending", { date: from ? fmtDate(from.toISOString()) : "-" });
    }
    await interaction.update({ content, components: [] });
    return;
  }

  // 取り消し（申請中なら、申請の取り下げ）
  const wasActive = rec.memberActive;
  const wasPending = pendingReview || awaitingConsent;
  rec.memberConsentAt = null;
  rec.memberManual = false;
  rec.memberAppliedAt = null;
  rec.memberApprovedAt = null;
  rec.memberActive = false;
  rec.updatedAt = now.toISOString();
  store.save();
  log.info(wasPending ? `住人の申請の取り下げ ${who}` : `住人の取り消し ${who}`);
  await interaction.member.roles.remove(mc.roleId, "SupporterGate member").catch((err) => log.warn(`住人のロール剥奪に失敗 ${who}: ${String(err)}`));
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
    await interaction.reply({ content: t(lang, "group.needName", channelRefs(config, lang)), ephemeral: true });
    return;
  }
  if (rec.effectiveRank <= 0 && !rec.memberActive) {
    const key = config.member?.mode === "apply" ? "group.notEligibleApply" : "group.notEligible";
    await interaction.reply({ content: t(lang, key, channelRefs(config, lang)), ephemeral: true });
    return;
  }
  if (!rec.groupRequestedAt) {
    rec.groupRequestedAt = new Date().toISOString();
    rec.discordTag = interaction.user.tag;
    rec.updatedAt = rec.groupRequestedAt;
    store.save();
    // このログは Discord のログチャンネルにも流れる。VRChat に届いた申請と、表示名で照らせる
    log.info(`グループ参加の希望 ${interaction.user.tag} (${interaction.user.id}): VRChat の表示名=${rec.vrcName} ランク=${rec.effectiveRank} 住人=${rec.memberActive ? "有効" : "無効"}`);
  }
  const params = { name: group.name, url: group.url, vrcName: rec.vrcName, ...channelRefs(config, lang) };
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
    // 認定のあと、「住人」と間違えて「登録」を押す人がいる（先頭にある青いボタンなので）。
    // 同意がまだの人には、表示名のフォームではなく、手続きの続き（くわしい案内と同意のボタン）を見せる
    const current = store.get(interaction.user.id);
    if (current && current.vrcName && isAwaitingConsent(config, current)) {
      log.info(`UI 同意がまだの人が「登録」を押したので、住人の案内を見せます ${interaction.user.tag} (${interaction.user.id})`);
      await interaction.reply({ ...consentGuide(lang, t(lang, "member.registerRedirect", { name: current.vrcName })), ephemeral: true });
      return;
    }
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

  if (id === IDS.adminLookup || id.startsWith(IDS.adminLookupUser)) {
    if (!isAdmin(config, interaction.member)) {
      await interaction.reply({ content: "この操作は、管理者だけができます。", ephemeral: true });
      return;
    }
    if (id === IDS.adminLookup) {
      // 名前を入れてもらう
      const modal = new ModalBuilder().setCustomId(IDS.adminLookupModal).setTitle("人を調べる");
      const input = new TextInputBuilder()
        .setCustomId(IDS.adminLookupQuery)
        .setLabel("Discord のユーザー名か、VRChat の表示名")
        .setPlaceholder("例: tsubasas / 「ツバサ」")
        .setStyle(TextInputStyle.Short)
        .setRequired(true)
        .setMaxLength(64);
      modal.addComponents(new ActionRowBuilder<TextInputBuilder>().addComponents(input));
      await interaction.showModal(modal);
      return;
    }
    const userId = id.slice(IDS.adminLookupUser.length);
    const people = await personById(interaction.guild, store, userId);
    await interaction.reply({ content: resolvedReport(config, interaction.guild, userId, people, new Date()), ephemeral: true, allowedMentions: { parse: [] } });
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
  if (interaction.customId !== IDS.modal && interaction.customId !== IDS.adminLookupModal) return;
  if (!interaction.inCachedGuild() || interaction.guildId !== config.guildId) {
    await interaction.reply({ content: t(lang, "err.wrongServer"), ephemeral: true });
    return;
  }
  if (interaction.customId === IDS.adminLookupModal) {
    if (!isAdmin(config, interaction.member)) {
      await interaction.reply({ content: "この操作は、管理者だけができます。", ephemeral: true });
      return;
    }
    const query = interaction.fields.getTextInputValue(IDS.adminLookupQuery).trim();
    const people = await resolvePerson(interaction.guild, store, query);
    log.info(`人を調べる「${query}」 by ${interaction.user.tag}: ${people.length} 件`);
    await interaction.reply({ content: resolvedReport(config, interaction.guild, query, people, new Date()), ephemeral: true, allowedMentions: { parse: [] } });
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
  if (r.ok && (await activateMemberIfReady(config, store, interaction.member))) deps.requestPublish();
  if (r.changed) deps.requestPublish();
  // 申請制で、まだ申請していない人には、次に押すボタンを一言足す（登録だけで申請が済んだと思って待つ人がいる）
  let message = r.message;
  if (r.ok && config.member?.mode === "apply") {
    const rec = store.get(interaction.user.id);
    const started = !rec || rec.memberActive || rec.memberManual || rec.memberConsentAt || rec.memberAppliedAt || rec.memberApprovedAt;
    if (rec && !started && applyBlocked(config, rec, lang, new Date()) === null) message += `\n${t(lang, "member.nextApply", channelRefs(config, lang))}`;
  }
  await interaction.reply({ content: message, ephemeral: true });
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
    if (ja.ok && (await activateMemberIfReady(config, store, msg.member))) deps.requestPublish();
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
