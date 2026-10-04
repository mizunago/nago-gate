// 鍵つきのリスト。
// 公開する JSON を見ただけでは、誰が支援者・メンバーなのかを確かめられないようにする。
//   - 判定用のハッシュは、鍵を混ぜて作る（鍵を知らないと、名前から同じハッシュを作れない）
//   - クレジットの名前は、鍵で暗号化する（ワールドが同じ鍵で元に戻して表示する）
// 鍵は Bot（instance/.env の LIST_KEY）とワールド（SupporterRegistry の List Key）の両方に入れる。
// ワールドのデータを解析できる人には鍵を取り出せるので、強い秘密にはならない。
//
// 鍵は複数を同時に使える（LIST_KEY にカンマ区切りで並べる）。リストには鍵ごとの区画が入り、
// ワールドは自分の鍵の区画だけを読む。鍵を替えるときは、新旧の両方を並べておけば、
// ワールドを上げ直す時期がずれても、上げ直す前と後のどちらのワールドも動く。
//
// Udon 側（SupporterRegistry.cs）と同じ規則にすること:
//   鍵の番号     sha256(鍵 + "\nid") の先頭 16 文字（区画の "k"。ワールドが自分の区画を探すのに使う）
//   ハッシュ     sha256(鍵 + "\n" + 正規化した名前)
//   鍵の流れ     ブロック i（0 から）= sha256(鍵 + "\n" + nonce + "\n" + i) の 32 バイト
//   暗号文       UTF-8 の平文と、鍵の流れの XOR。16 進（小文字）
//   nonce       sha256(鍵 + "\nnonce\n" + 平文) の先頭 16 文字（同じ内容なら同じ暗号文になり、無駄な再公開をしない）
import { createHash } from "node:crypto";
import { normalizeName } from "./hash.js";

export interface EncryptedText {
  /** nonce */
  n: string;
  /** 暗号文（16 進） */
  c: string;
}

function sha(text: string): Buffer {
  return createHash("sha256").update(text, "utf8").digest();
}

/** 鍵として使える形か。印字できる ASCII（空白とカンマを除く）で 12〜64 文字 */
export function isValidListKey(key: string): boolean {
  return /^[\x21-\x2b\x2d-\x7e]{12,64}$/.test(key);
}

/** LIST_KEY の値（カンマ区切り）を、鍵の並びにする。使えない鍵や重複があれば例外 */
export function parseListKeys(raw: string | null | undefined): string[] {
  const keys = (raw ?? "").split(",").map((k) => k.trim()).filter((k) => k.length > 0);
  for (const key of keys) {
    if (!isValidListKey(key)) throw new Error("LIST_KEY の鍵は、空白とカンマを含まない半角の英数字・記号で 12〜64 文字にしてください（複数のときはカンマで区切る）");
  }
  if (new Set(keys).size !== keys.length) throw new Error("LIST_KEY に同じ鍵が 2 回書かれています");
  return keys;
}

/** 鍵の番号。リストの区画に付け、ワールドが自分の鍵の区画を探すのに使う */
export function listKeyId(key: string): string {
  return sha(`${key}\nid`).toString("hex").slice(0, 16);
}

/** 鍵つきのリスト用のハッシュ（小文字 hex） */
export function keyedHashName(key: string, name: string): string {
  return sha(`${key}\n${normalizeName(name)}`).toString("hex");
}

function xorKeystream(key: string, nonce: string, data: Buffer): Buffer {
  const out = Buffer.alloc(data.length);
  for (let block = 0; block * 32 < data.length; block++) {
    const stream = sha(`${key}\n${nonce}\n${block}`);
    for (let i = 0; i < 32 && block * 32 + i < data.length; i++) out[block * 32 + i] = data[block * 32 + i] ^ stream[i];
  }
  return out;
}

export function encryptText(key: string, text: string): EncryptedText {
  const nonce = sha(`${key}\nnonce\n${text}`).toString("hex").slice(0, 16);
  return { n: nonce, c: xorKeystream(key, nonce, Buffer.from(text, "utf8")).toString("hex") };
}

/** 元の文に戻す（鍵が違えば、意味のない文字の並びになる。鍵が合っているかは、区画の鍵の番号で確かめる） */
export function decryptText(key: string, enc: EncryptedText): string {
  return xorKeystream(key, enc.n, Buffer.from(enc.c, "hex")).toString("utf8");
}
