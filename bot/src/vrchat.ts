// VRChat の API（公式には案内されていないもの）を、Group への招待のためだけに使う。
//
// 使うのは、登録パネルの「グループ」のボタンが押されたときだけ。定期的な問い合わせはしない。
// VRChat の決まりに合わせて、アプリを名乗る User-Agent を付け、問い合わせの間隔を空ける。
// 認証は、Bot 用のアカウントでログインしたときのクッキー（auth）を使う（instance/.env の VRC_AUTH_COOKIE）。
// API は予告なく変わることがある。失敗したら、呼び出し側が手作業の流れ（持ち主が招待する）に戻す。

const BASE = "https://api.vrchat.cloud/api/1";
const USER_AGENT = "nago-gate-bot/0.1 (https://github.com/mizunago/nago-gate)";
/** 問い合わせと問い合わせの間に空ける時間（ミリ秒） */
const MIN_GAP_MS = 1500;

export class VrcApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
  }
}

export interface VrcUser {
  id: string;
  displayName: string;
}

type FetchLike = (url: string, init: { method: string; headers: Record<string, string>; body?: string }) => Promise<{ ok: boolean; status: number; text(): Promise<string> }>;

export class VrcClient {
  private last = 0;
  private queue: Promise<unknown> = Promise.resolve();

  constructor(
    private readonly authCookie: string,
    private readonly fetchImpl: FetchLike = fetch as unknown as FetchLike,
    private readonly gapMs: number = MIN_GAP_MS,
  ) {}

  /** 問い合わせを 1 つずつ、間隔を空けて送る */
  private call<T>(method: string, route: string, body?: unknown): Promise<T> {
    const run = async (): Promise<T> => {
      const wait = this.last + this.gapMs - Date.now();
      if (wait > 0) await new Promise((r) => setTimeout(r, wait));
      this.last = Date.now();
      // .env には「auth=...」の形でも、値だけでも書ける
      const cookie = this.authCookie.startsWith("auth=") ? this.authCookie : `auth=${this.authCookie}`;
      const res = await this.fetchImpl(BASE + route, {
        method,
        headers: { "User-Agent": USER_AGENT, Cookie: cookie, ...(body ? { "Content-Type": "application/json" } : {}) },
        body: body ? JSON.stringify(body) : undefined,
      });
      const text = await res.text();
      if (!res.ok) throw new VrcApiError(res.status, `${method} ${route} ${res.status} ${text.slice(0, 200)}`);
      return (text ? JSON.parse(text) : null) as T;
    };
    const next = this.queue.then(run, run);
    this.queue = next.catch(() => undefined);
    return next;
  }

  /** ログインしているアカウント（クッキーが有効かの確認にも使う） */
  async currentUser(): Promise<VrcUser> {
    const u = await this.call<{ id?: string; displayName?: string; requiresTwoFactorAuth?: string[] }>("GET", "/auth/user");
    if (!u?.id || !u.displayName) throw new VrcApiError(401, "ログインできていません（2 段階認証が済んでいないクッキーかもしれません）");
    return { id: u.id, displayName: u.displayName };
  }

  /** アカウントが入っている Group から、短いコード（SHADY.9404 の形）で Group の ID を探す */
  async findMyGroupId(userId: string, shortCodeWithDiscriminator: string): Promise<string | null> {
    const groups = await this.call<{ groupId?: string; shortCode?: string; discriminator?: string }[]>("GET", `/users/${encodeURIComponent(userId)}/groups`);
    const want = shortCodeWithDiscriminator.toUpperCase();
    const hit = (groups ?? []).find((g) => `${g.shortCode}.${g.discriminator}`.toUpperCase() === want);
    return hit?.groupId ?? null;
  }

  /** 表示名が完全に一致するユーザーを探す（前後の空白と、大文字・小文字の違いは無視する。検索は部分一致で返ってくるので、こちらで絞る） */
  async findUserByDisplayName(displayName: string): Promise<VrcUser | null> {
    const want = displayName.trim().toLowerCase();
    const users = await this.call<VrcUser[]>("GET", `/users?search=${encodeURIComponent(displayName.trim())}&n=50`);
    const hit = (users ?? []).find((u) => typeof u.displayName === "string" && u.displayName.trim().toLowerCase() === want);
    return hit ? { id: hit.id, displayName: hit.displayName } : null;
  }

  /**
   * 参加の申請を出している人の ID。
   * ほかの人のメンバー情報を 1 人ずつ見る問い合わせは、招待を管理する権限だけでは断られる（403）ので、一覧から探す
   */
  async pendingRequestUserIds(groupId: string): Promise<Set<string>> {
    const list = await this.call<{ userId?: string }[]>("GET", `/groups/${encodeURIComponent(groupId)}/requests?n=100`);
    return new Set((list ?? []).map((m) => m.userId ?? "").filter((id) => id.length > 0));
  }

  /** こちらから招待を送って、まだ返事が無い人の ID */
  async invitedUserIds(groupId: string): Promise<Set<string>> {
    const list = await this.call<{ userId?: string }[]>("GET", `/groups/${encodeURIComponent(groupId)}/invites?n=100`);
    return new Set((list ?? []).map((m) => m.userId ?? "").filter((id) => id.length > 0));
  }

  async inviteToGroup(groupId: string, userId: string): Promise<void> {
    await this.call("POST", `/groups/${encodeURIComponent(groupId)}/invites`, { userId, confirmOverrideBlock: true });
  }

  /** 相手が出していた参加の申請を承認する */
  async acceptJoinRequest(groupId: string, userId: string): Promise<void> {
    await this.call("PUT", `/groups/${encodeURIComponent(groupId)}/requests/${encodeURIComponent(userId)}`, { action: "accept" });
  }
}

export type GroupJoinOutcome = "invited" | "accepted" | "alreadyMember" | "alreadyInvited" | "userNotFound";

/**
 * 表示名の人を Group に入れる手続きを進める。
 * 既に申請が出ていれば承認し、無ければ招待を送る。入っている人・招待済みの人には何もしない。
 */
export async function bringIntoGroup(client: VrcClient, groupId: string, displayName: string): Promise<{ outcome: GroupJoinOutcome; user: VrcUser | null }> {
  const user = await client.findUserByDisplayName(displayName);
  if (!user) return { outcome: "userNotFound", user: null };
  if ((await client.pendingRequestUserIds(groupId)).has(user.id)) {
    await client.acceptJoinRequest(groupId, user.id);
    return { outcome: "accepted", user };
  }
  if ((await client.invitedUserIds(groupId)).has(user.id)) return { outcome: "alreadyInvited", user };
  try {
    await client.inviteToGroup(groupId, user.id);
  } catch (err) {
    // 既に入っている人・招待済みの人は、招待のときに 400 で断られる
    if (err instanceof VrcApiError && err.status === 400) {
      if (/already a member/i.test(err.message)) return { outcome: "alreadyMember", user };
      if (/already.*invit|invit.*already/i.test(err.message)) return { outcome: "alreadyInvited", user };
    }
    throw err;
  }
  return { outcome: "invited", user };
}
