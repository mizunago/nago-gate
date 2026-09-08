// 使い方: node dist/tools/hash-name.js "DisplayName"
// Udon 側 (SupporterRegistry) のハッシュ実装が Bot と一致しているか確認するためのツール
import { hashName, normalizeName } from "../hash.js";

const name = process.argv.slice(2).join(" ");
if (!name) {
  console.error('usage: node dist/tools/hash-name.js "DisplayName"');
  process.exit(1);
}
console.log(`normalized: ${normalizeName(name)}`);
console.log(`sha256    : ${hashName(name)}`);
