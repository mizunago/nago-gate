// 使えるコマンドの一覧を、管理用のチャンネルに 1 つのメッセージ（長ければ複数）として出しておく。
// コマンドの定義（buildCommands）から作るので、Bot を更新して起動し直すたびに、今の内容に書き換わる。
import type { Client, Message } from "discord.js";
import { buildCommands } from "./commands.js";
import type { AppConfig } from "./config.js";
import { log } from "./log.js";

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
  const lines: string[] = [HEADER, "Bot が起動するたびに、今の内容へ書き換えます。`[ ]` は省略できる項目です。"];
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
  lines.push("", "**登録チャンネルのパネルのボタン**");
  lines.push("`登録` … VRChat の表示名を登録・変更する（30 日に 1 回）");
  lines.push("`状態` … 自分の登録の状態を見る");
  lines.push("`クレジット ON / OFF` … ワールドのクレジットに名前を載せるかを切り替える");
  if (config.member) lines.push(`\`メンバー\` … メンバー登録（18 歳以上の確認と同意。在籍 ${config.member.minDays} 日で有効）と、その取り消し`);
  if (config.group) lines.push("`グループ` … VRChat の Group への参加を希望する（支援者かメンバーの方。申請と承認は VRChat の側で行う）");
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
    if (i < mine.length) {
      if (mine[i].content !== chunks[i]) {
        await mine[i].edit({ content: chunks[i] });
        changed++;
      }
    } else {
      await ch.send({ content: chunks[i] });
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
