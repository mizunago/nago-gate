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

/** 正規化した名前の SHA-256 (小文字 hex)。Udon 側の Sha256Hex と一致する */
export function hashName(name: string): string {
  return createHash("sha256").update(normalizeName(name), "utf8").digest("hex");
}
