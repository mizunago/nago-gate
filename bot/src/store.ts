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
  /** 猶予を含めた有効ランク。JSON に出るのはこれ */
  effectiveRank: number;
  /** 支援停止後の猶予期限 (ISO)。支援中は null */
  graceUntil: string | null;
  /** 猶予中に維持するランク */
  graceRank: number;
  /** 管理者による手動付与ランク (0 = なし) */
  manualRank: number;
  /** 手動付与の期限 (ISO)。null は無期限 */
  manualUntil: string | null;
  /** 最後に支援ロールを確認できた日時 */
  lastActiveAt: string | null;
  /** 最後に活動を確認した Discord ユーザー名（管理用） */
  discordTag: string | null;
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
      graceUntil: null,
      graceRank: 0,
      manualRank: 0,
      manualUntil: null,
      lastActiveAt: null,
      discordTag: null,
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
