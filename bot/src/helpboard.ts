// 使えるコマンドの一覧を、管理用のチャンネルに 1 つのメッセージ（長ければ複数）として出しておく。
// コマンドの定義（buildCommands）から作るので、Bot を更新して起動し直すたびに、今の内容に書き換わる。
import { ActionRowBuilder, ButtonBuilder, ButtonStyle, type Client, type Message } from "discord.js";
import { hasOwnChannel, panelChannelId } from "./channels.js";
import { buildCommands } from "./commands.js";
import type { AppConfig } from "./config.js";
import { log } from "./log.js";
import { LIST_IDS } from "./lists.js";
import { IDS } from "./panel.js";

const HEADER = "## コマンド一覧";
const CONTINUED = "-# コマンド一覧（続き）";
const MAX_LEN = 1900;
const SUBCOMMAND = 1;

interface OptionJson {
  type: number;
  name: string;
  description: string;
  description_localizations?: Record<string, string> | null;
  required?: boolean;
  options?: OptionJson[];
}

function describeOf(o: { description: string; description_localizations?: Record<string, string> | null }): string {
  return o.description_localizations?.ja ?? o.description;
}

/** 一覧の本文（行の並び）。管理者向けを先に出す */
export function buildCommandHelpLines(config: AppConfig): string[] {
  const lines: string[] = [HEADER, "Bot が起動するたびに、今の内容へ書き換えます。", "`[ ]` は省略できる項目です。"];
  const commands = buildCommands() as unknown as (OptionJson & { options?: OptionJson[] })[];
  const order = [...commands].sort((a, b) => (a.name === "vrc-admin" ? -1 : 0) - (b.name === "vrc-admin" ? -1 : 0));
  for (const cmd of order) {
    lines.push("", cmd.name === "vrc-admin" ? "**管理者向け**（サーバーの管理権限が必要）" : "**参加者・支援者向け**（返答は、その人の Discord の言語で出る）");
    for (const sub of cmd.options ?? []) {
      if (sub.type !== SUBCOMMAND) continue;
      const args = (sub.options ?? []).map((o) => (o.required ? `${o.name}:` : `[${o.name}:]`)).join(" ");
      lines.push(`\`/${cmd.name} ${sub.name}${args ? " " + args : ""}\` … ${describeOf(sub)}`);
    }
  }
  // ボタンの場所。専用のチャンネルが無いボタンは、登録のチャンネルに並ぶ
  const at = (kind: "register" | "status" | "resident" | "group"): string => {
    const id = panelChannelId(config, kind);
    return id ? `<#${id}>` : "登録のチャンネル";
  };
  lines.push("", "**ボタンの場所と、することの中身**");
  lines.push(`${at("register")}: \`登録\` … Discord と VRChat のアカウントをつなぐ（VRChat の表示名を入れる、30 日に 1 回まで変えられる）`);
  lines.push("　支援者も、住人の申請をする人も、最初にこれ");
  lines.push(`　登録から ${config.nameFixMinutes} 分のうちは、打ち間違いを何度でも直せる（30 日の数え始めは変わらない）`);
  lines.push(`${at("register")}${hasOwnChannel(config, "status") ? `・${at("status")}` : ""}: \`状態\` … 本人が、自分の登録・支援・住人の申請の状態を見る`);
  lines.push(`${at("status")}: \`クレジット ON / OFF\` … ワールドの支援者のボードに、名前を出すかを選ぶ（支援者にだけ関係する）`);
  if (config.member) {
    const how = config.member.mode === "apply" ? "住人の申請（支援は要らない）\n　申請のチャンネルに届いたら認定か見送りを決め、そのあと本人が案内に同意すると住人になる" : `住人の登録（18 歳以上の確認と同意、在籍 ${config.member.minDays} 日で有効）`;
    lines.push(`${at("resident")}: \`住人\` … ${how}`);
  }
  if (config.group) lines.push(`${at("group")}: \`グループ\` … VRChat の Group に招待する（支援者か住人の人だけ）`);
  lines.push("", "**人を調べる**");
  lines.push("下の `🔎 人を調べる` を押して、Discord のユーザー名か VRChat の表示名を入れると、その人の手続きがどこまで済んでいて、何がまだかを出します（自分にだけ見える）。");
  lines.push("`/vrc-admin lookup` でも同じものが出ます。");
  lines.push("", "**一覧**（下のボタン。自分にだけ見える）");
  lines.push("`📨 申請中の人` … まだ決めていない住人の申請と、認定済みで本人の同意待ちの人（申請のメッセージへのリンクつき）。");
  lines.push("　`/vrc-admin applications` でも出ます。");
  lines.push("`🧾 登録がまだの支援者` … 支援サイトのロールはあるが、VRChat の表示名を登録していない人（まだワールドに入れない）。");
  lines.push("　`/vrc-admin unregistered` でも出ます。");
  lines.push("", "**本人確認と、表示名の登録のやり直し**（申請のメッセージと、人を調べた結果のボタン）");
  lines.push("`🔑 本人確認を頼む` … Discord と VRChat の名前がかけ離れていて、同じ人か分からないときに使う。");
  lines.push("　Bot が確認の文字を決め、本人に送る文（コピー用）と「DM で送る」を出す。");
  lines.push("　本人が VRChat のプロフィールに文字を入れて「確かめる」を押すと、Bot が読んで確かめ、済んだら申請のチャンネルに知らせる。");
  lines.push("`🔄 名前の登録をやり直させる` … 表示名を間違えて登録した人を、30 日を待たずに登録し直せるようにする。");
  lines.push("　本人に送る文（コピー用）と「DM で知らせる」も出す。");
  lines.push("", "**協力者**（デバッグなどを手伝ってくれる、支援者ではない人）");
  lines.push("人を調べた結果の下の `🧪 協力者にする` を押し、ランクを選んで日数を入れると、その期間だけ、ワールドに支援者と同じに入れます。");
  lines.push("支援者のボードには載らず、Supporter・Platinum のロールも付きません。");
  lines.push("期限が来ると自動で外れます。\n一覧は `/vrc-admin testers`、コマンドでは `/vrc-admin tester-grant` と `tester-revoke` です。");
  return lines;
}

function chunk(lines: string[]): string[] {
  const out: string[] = [];
  let cur = "";
  for (const line of lines) {
    if (cur.length + line.length + 1 > MAX_LEN) {
      out.push(cur);
      cur = CONTINUED;
    }
    cur += (cur ? "\n" : "") + line;
  }
  if (cur) out.push(cur);
  return out;
}

/** 一覧の最後のメッセージに付けるボタン */
function lookupButtons(): ActionRowBuilder<ButtonBuilder>[] {
  return [
    new ActionRowBuilder<ButtonBuilder>().addComponents(
      new ButtonBuilder().setCustomId(IDS.adminLookup).setLabel("人を調べる").setStyle(ButtonStyle.Primary).setEmoji("🔎"),
      new ButtonBuilder().setCustomId(LIST_IDS.applications).setLabel("申請中の人").setStyle(ButtonStyle.Secondary).setEmoji("📨"),
      new ButtonBuilder().setCustomId(LIST_IDS.unregistered).setLabel("登録がまだの支援者").setStyle(ButtonStyle.Secondary).setEmoji("🧾"),
    ),
  ];
}

/** 一覧のメッセージを、今の内容に合わせる（あれば書き換え、足りなければ投稿、余れば消す） */
export async function updateCommandBoard(client: Client, config: AppConfig): Promise<void> {
  if (!config.commandsChannelId) return;
  const ch = await client.channels.fetch(config.commandsChannelId).catch(() => null);
  if (!ch || !ch.isTextBased() || !("send" in ch) || !("messages" in ch)) {
    log.warn(`コマンド一覧のチャンネル ${config.commandsChannelId} が見つからないか、投稿できません`);
    return;
  }
  const chunks = chunk(buildCommandHelpLines(config));
  const recent = await ch.messages.fetch({ limit: 50 });
  const mine: Message[] = [...recent.values()]
    .filter((m) => m.author.id === client.user?.id && (m.content.startsWith(HEADER) || m.content.startsWith(CONTINUED)))
    .sort((a, b) => a.createdTimestamp - b.createdTimestamp);

  let changed = 0;
  for (let i = 0; i < chunks.length; i++) {
    // 「人を調べる」のボタンは、最後のメッセージにだけ付ける
    const components = i === chunks.length - 1 ? lookupButtons() : [];
    const want = JSON.stringify(components.map((r) => r.toJSON()));
    if (i < mine.length) {
      if (mine[i].content !== chunks[i] || JSON.stringify(mine[i].components.map((r) => r.toJSON())) !== want) {
        await mine[i].edit({ content: chunks[i], components });
        changed++;
      }
    } else {
      await ch.send({ content: chunks[i], components });
      changed++;
    }
  }
  for (const extra of mine.slice(chunks.length)) {
    await extra.delete().catch(() => undefined);
    changed++;
  }
  // 変化がなければ、ログも出さない（起動のたびにログチャンネルが増えないように）
  if (changed > 0) log.info(`コマンド一覧を更新しました (${chunks.length} 件のメッセージ, channel=${config.commandsChannelId})`);
}
