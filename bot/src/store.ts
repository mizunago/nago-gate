import { mkdirSync, readFileSync, renameSync, writeFileSync, existsSync } from "node:fs";
import path from "node:path";

export interface MemberRecord {
  discordId: string;
  /** 登録済み VRChat DisplayName（表示用・原文） */
  vrcName: string | null;
  /** 最後に名前を登録/変更した日時 (ISO) */
  nameChangedAt: string | null;
  /** クレジット表示に載せるか */
  showCredit: boolean;
  /** 支援サイトのロールから算出した現在ランク (0 = 支援なし) */
  activeRank: number;
  /** 猶予・手動の付与・協力者の枠を含めた有効ランク。ワールドに入れるかの判定（JSON の access）はこれ */
  effectiveRank: number;
  /** 支援のランク（猶予と手動の付与を含み、協力者の枠を除く）。支援者のボードと、Supporter・Platinum のロールはこれで決める */
  supportRank: number;
  /** 支援停止後の猶予期限 (ISO)。支援中は null */
  graceUntil: string | null;
  /** 猶予中に維持するランク */
  graceRank: number;
  /** 管理者による手動付与ランク (0 = なし) */
  manualRank: number;
  /** 手動付与の期限 (ISO)。null は無期限 */
  manualUntil: string | null;
  /**
   * 協力者の枠のランク (0 = なし)。デバッグなどを手伝ってくれる、支援者ではない人を、期間を決めて支援者と同じに入れる。
   * 支援者のボードには載せず、Supporter・Platinum のロールも付けない（付けるなら config.tester.roleId のロール）
   */
  testerRank: number;
  /** 協力者の枠の期限 (ISO)。期限を過ぎると、同期のときに外れる */
  testerUntil: string | null;
  /** 最後に支援ロールを確認できた日時 */
  lastActiveAt: string | null;
  /** 最後に活動を確認した Discord ユーザー名（管理用） */
  discordTag: string | null;
  /** メンバー登録（18 歳以上の確認と注意への同意）をした日時。未登録は null */
  memberConsentAt: string | null;
  /** 管理者が手動でメンバーと認定したか（在籍日数と同意を問わない。名前の登録とサーバーへの在籍は必要） */
  memberManual: boolean;
  /** 申請制のとき: メンバーの申請を出した日時。申請していない・結果が出たあとは null */
  memberAppliedAt: string | null;
  /** 申請制のとき: 申請が見送られた日時。見送られていなければ null */
  memberDeclinedAt: string | null;
  /** 申請制のとき: 管理者が申請を認定した日時。このあと本人が案内に同意する（memberConsentAt）と、メンバーになる */
  memberApprovedAt: string | null;
  /** メンバーの条件（同意・名前の登録・在籍日数、または手動の認定）を満たしているか。同期のたびに更新 */
  memberActive: boolean;
  /** サーバーに参加した日時（在籍日数の計算用）。サーバーにいないときは null */
  joinedAt: string | null;
  /** 登録パネルのボタンで、VRChat の Group への参加を希望した日時。まだなら null */
  groupRequestedAt: string | null;
  /** 表示名から引いた VRChat のユーザー ID（Group への招待のときに分かったもの）。まだなら null */
  vrcUserId: string | null;
  /** 管理者が BAN したか。BAN 中は、支援者・メンバーのどのリストにも載せず、登録も受け付けない */
  banned: boolean;
  /** BAN した日時 (ISO) */
  bannedAt: string | null;
  /** BAN の理由（管理用のメモ） */
  banReason: string | null;
  updatedAt: string;
}

export interface StoreData {
  version: 1;
  members: Record<string, MemberRecord>;
  lastPublishedDigest: string | null;
  lastPublishedAt: string | null;
}

export class Store {
  private data: StoreData;

  constructor(private readonly filePath: string) {
    this.data = Store.load(filePath);
  }

  private static load(filePath: string): StoreData {
    if (!existsSync(filePath)) {
      return { version: 1, members: {}, lastPublishedDigest: null, lastPublishedAt: null };
    }
    const parsed = JSON.parse(readFileSync(filePath, "utf8")) as StoreData;
    if (parsed.version !== 1) throw new Error(`未対応のデータバージョン: ${String(parsed.version)}`);
    // メンバー登録より前に作られた記録には、あとから足した項目が無い
    for (const rec of Object.values(parsed.members)) {
      rec.memberConsentAt ??= null;
      rec.memberActive ??= false;
      rec.memberManual ??= false;
      rec.memberAppliedAt ??= null;
      rec.memberDeclinedAt ??= null;
      rec.memberApprovedAt ??= null;
      rec.joinedAt ??= null;
      rec.groupRequestedAt ??= null;
      rec.vrcUserId ??= null;
      rec.banned ??= false;
      rec.bannedAt ??= null;
      rec.banReason ??= null;
      rec.testerRank ??= 0;
      rec.testerUntil ??= null;
      rec.supportRank ??= rec.effectiveRank;
    }
    return parsed;
  }

  save(): void {
    mkdirSync(path.dirname(this.filePath), { recursive: true });
    const tmp = `${this.filePath}.tmp`;
    writeFileSync(tmp, JSON.stringify(this.data, null, 2), "utf8");
    renameSync(tmp, this.filePath);
  }

  get(discordId: string): MemberRecord | null {
    return this.data.members[discordId] ?? null;
  }

  getOrCreate(discordId: string): MemberRecord {
    const existing = this.data.members[discordId];
    if (existing) return existing;
    const now = new Date().toISOString();
    const rec: MemberRecord = {
      discordId,
      vrcName: null,
      nameChangedAt: null,
      showCredit: true,
      activeRank: 0,
      effectiveRank: 0,
      supportRank: 0,
      graceUntil: null,
      graceRank: 0,
      manualRank: 0,
      manualUntil: null,
      testerRank: 0,
      testerUntil: null,
      lastActiveAt: null,
      discordTag: null,
      memberConsentAt: null,
      memberActive: false,
      memberManual: false,
      memberAppliedAt: null,
      memberDeclinedAt: null,
      memberApprovedAt: null,
      joinedAt: null,
      groupRequestedAt: null,
      vrcUserId: null,
      banned: false,
      bannedAt: null,
      banReason: null,
      updatedAt: now,
    };
    this.data.members[discordId] = rec;
    return rec;
  }

  all(): MemberRecord[] {
    return Object.values(this.data.members);
  }

  /** 正規化した名前で検索（重複登録チェック用） */
  findByNormalizedName(normalized: string, normalizer: (s: string) => string): MemberRecord | null {
    for (const rec of this.all()) {
      if (rec.vrcName && normalizer(rec.vrcName) === normalized) return rec;
    }
    return null;
  }

  get lastPublishedDigest(): string | null {
    return this.data.lastPublishedDigest;
  }

  markPublished(digest: string): void {
    this.data.lastPublishedDigest = digest;
    this.data.lastPublishedAt = new Date().toISOString();
  }
}
