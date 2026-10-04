import { createHash } from "node:crypto";

/**
 * VRChat DisplayName の正規化。
 * Udon 側 (SupporterRegistry.cs) の NormalizeName と完全に同じ規則にすること:
 *   1. 前後の空白を除去
 *   2. 小文字化 (invariant)
 */
export function normalizeName(name: string): string {
  return name.trim().toLowerCase();
}

/**
 * 表示を乱す文字: 制御文字、行・段落の区切り、文字の向きを変える指定。
 * VRChat の表示名には入らないはずの文字なので、登録では断り、クレジットに出すときは落とす
 */
const UNSAFE_RANGES: [number, number][] = [
  [0x00, 0x1f],     // 制御文字（改行・タブを含む）
  [0x7f, 0x9f],     // DEL と、C1 の制御文字
  [0x2028, 0x2029], // 行の区切り、段落の区切り
  [0x202a, 0x202e], // 文字の向きの埋め込み・上書き
  [0x2066, 0x2069], // 文字の向きの分離
];

function isUnsafeChar(ch: string): boolean {
  const cp = ch.codePointAt(0) ?? 0;
  return UNSAFE_RANGES.some(([from, to]) => cp >= from && cp <= to);
}

export function hasUnsafeNameChars(name: string): boolean {
  return [...name].some(isUnsafeChar);
}

/** クレジットに出す名前。表示を乱す文字を落とす（判定用のハッシュには使わない） */
export function displaySafeName(name: string): string {
  return [...name].filter((ch) => !isUnsafeChar(ch)).join("").trim();
}

/** 正規化した名前の SHA-256 (小文字 hex)。Udon 側の Sha256Hex と一致する */
export function hashName(name: string): string {
  return createHash("sha256").update(normalizeName(name), "utf8").digest("hex");
}
