// 持ち主向けの確認。支援者がワールドに入れるまでの段を上から順に確かめ、止まっている段と、次にやることを返す。
// /vrc-admin lookup から使う。Discord には書き込まない（読むだけ）。
import { tierByRank, type AppConfig } from "./config.js";
import type { MemberRecord } from "./store.js";
import { desiredRoleIds, isMemberEligible, rankFromRoles } from "./sync.js";

export interface LiveMember {
  /** サーバーで今付いているロールの ID */
  roleIds: string[];
  /** ロールの ID から名前を引く（表示用） */
  roleName: (id: string) => string;
}

const OK = "✅";
const NG = "❌";
const SKIP = "➖";

/**
 * 入場までの段を確かめた結果（Discord に返す文）。
 * live が null なら、本人はサーバーにいない。
 */
export function diagnose(config: AppConfig, rec: MemberRecord | null, live: LiveMember | null, now: Date): string {
  const lines: string[] = ["**入場までの確認**（上から順に。❌ が止まっている所）"];
  let next = "";
  const stop = (hint: string): void => {
    if (!next) next = hint;
  };

  // 1. サーバーにいるか
  if (!live) {
    lines.push(`${NG} 1. サーバーにいません`);
    stop("本人に、Patreon の設定の「接続中のアプリ（Apps）」で Discord をつなぎ、「サーバーに参加する」を押してもらう。入れなければ招待リンクから入ってもらう");
    lines.push(`${SKIP} 2〜5. サーバーに入ってから確かめます`);
    return lines.join("\n") + `\n**次にやること**: ${next}`;
  }
  lines.push(`${OK} 1. サーバーにいます`);

  // 2. 支援サイトの Bot が付けるロール
  const sourceIds = new Set(config.tiers.flatMap((t) => t.sourceRoleIds));
  const liveSource = live.roleIds.filter((id) => sourceIds.has(id));
  const liveRank = rankFromRoles(config, live.roleIds);
  const graceActive = !!rec && rec.graceUntil !== null && new Date(rec.graceUntil) > now && rec.graceRank > 0;
  const manualActive = !!rec && rec.manualRank > 0 && (rec.manualUntil === null || new Date(rec.manualUntil) > now);
  if (liveRank > 0) {
    lines.push(`${OK} 2. 支援サイトのロール: ${liveSource.map(live.roleName).join("・")}`);
  } else if (graceActive || manualActive) {
    const why = [graceActive ? "猶予中" : "", manualActive ? "手動の付与" : ""].filter(Boolean).join("・");
    lines.push(`${SKIP} 2. 支援サイトのロールはありません（${why}なので、支援者として扱います）`);
  } else {
    lines.push(`${NG} 2. 支援サイトのロールがありません（Patreon や Ci-en の Bot が、まだ付けていません）`);
    stop(
      "支援サイトの側を確かめる。(a) Patreon の Audience で、その人が入会していて、支払いが確定しているか。(b) Patreon の設定の Apps > Discord に赤字のエラーが無いか。あれば Update を押し直す。(c) そのうえで本人に、Patreon の「接続中のアプリ」で「サーバーを離れる」を押してから「サーバーに参加する」を押し直してもらう",
    );
  }

  // 3. この Bot が付ける共通のロール
  if (rec?.banned) {
    lines.push(`${NG} 3. BAN 中です（どのリストにも載せません）`);
    stop("入れるようにするなら `/vrc-admin unban` を打つ");
  } else {
    const effective = rec?.effectiveRank ?? 0;
    const want = [...desiredRoleIds(config, effective)];
    const missing = want.filter((id) => !live.roleIds.includes(id));
    const tier = tierByRank(config, effective);
    if (effective > 0 && missing.length === 0) {
      lines.push(`${OK} 3. Bot のロール: ${want.map(live.roleName).join("・")}（${tier ? tier.label : `rank ${effective}`}）`);
    } else if (effective > 0) {
      lines.push(`${NG} 3. Bot のロールが、まだ付いていません: ${missing.map(live.roleName).join("・")}`);
      stop("`/vrc-admin sync` を打つ。それでも付かなければ、この Bot のロールが Supporter・Platinum より上にあるかを見る");
    } else if (liveRank > 0) {
      lines.push(`${NG} 3. 支援サイトのロールはありますが、Bot がまだ読み取っていません`);
      stop("`/vrc-admin sync` を打つ");
    } else {
      lines.push(`${SKIP} 3. Bot のロールは付けません（支援者として確認できていないため）`);
    }
  }

  // 4. VRChat の表示名
  if (rec?.vrcName) {
    lines.push(`${OK} 4. VRChat の表示名: **${rec.vrcName}**`);
  } else {
    lines.push(`${NG} 4. VRChat の表示名が未登録です`);
    stop("本人に、登録のチャンネルで Register を押して、VRChat の表示名を入れてもらう");
  }

  // 5. ワールドが読むリストに載るか
  const listed = !!rec && !rec.banned && rec.effectiveRank > 0 && !!rec.vrcName;
  const member = !!rec && isMemberEligible(config, rec, now);
  if (listed) {
    lines.push(`${OK} 5. 支援者のリストに載ります${member ? "（メンバーのリストにも載ります）" : ""}`);
  } else if (member) {
    lines.push(`${SKIP} 5. 支援者のリストには載りません。メンバーのリストには載ります`);
  } else {
    lines.push(`${NG} 5. リストに載りません`);
  }

  if (!next) {
    next = listed
      ? "Discord の側は整っています。ワールドへの反映は最長 10 分ほど。入り直すと早い。それでも入れなければ、VRChat の表示名が登録した名前と同じかを本人に確かめる（記号や空白の違い。大文字と小文字の違いは問わない）"
      : "上の ❌ を解消する";
  }
  return lines.join("\n") + `\n**次にやること**: ${next}`;
}
