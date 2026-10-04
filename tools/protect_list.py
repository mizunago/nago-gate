#!/usr/bin/env python3
"""鍵なしの見本のリストから、鍵つきのリストを作る（検証用）。Bot（bot/src/protect.ts）と同じ規則の、別の実装。

    python tools/protect_list.py <鍵[,鍵2,...]> <元のリスト.json> <出力.json> [--keep-plain]
    python tools/protect_list.py --decrypt <鍵> <鍵つきのリスト.json>     その鍵の区画のクレジットを、元に戻して表示する

元のリストは、access を作るために名前が要るので、credits と、見本用の "_names"（{"名前": ランク}）と
"_memberNames"（名前の配列）を読む。"_" で始まる項目は出力に入れない。
--keep-plain を付けると、鍵なしの部分（access・credits・members）も残す（移行中の形）。

規則:
    鍵の番号     sha256(鍵 + "\\nid") の先頭 16 文字（区画の "k"）
    ハッシュ     sha256(鍵 + "\\n" + 正規化した名前)。正規化は、前後の空白を除いて小文字にする
    鍵の流れ     ブロック i（0 から）= sha256(鍵 + "\\n" + nonce + "\\n" + i) の 32 バイト
    暗号文       UTF-8 の平文と、鍵の流れの XOR。16 進（小文字）
    nonce       sha256(鍵 + "\\nnonce\\n" + 平文) の先頭 16 文字
"""
import hashlib
import io
import json
import sys


def sha(text: str) -> bytes:
    return hashlib.sha256(text.encode("utf-8")).digest()


def normalize(name: str) -> str:
    return name.strip().lower()


def plain_hash(name: str) -> str:
    return sha(normalize(name)).hex()


def key_id(key: str) -> str:
    return sha(key + "\nid").hex()[:16]


def keyed_hash(key: str, name: str) -> str:
    return sha(key + "\n" + normalize(name)).hex()


def xor_keystream(key: str, nonce: str, data: bytes) -> bytes:
    out = bytearray(len(data))
    block = 0
    while block * 32 < len(data):
        stream = sha(key + "\n" + nonce + "\n" + str(block))
        for i in range(32):
            at = block * 32 + i
            if at >= len(data):
                break
            out[at] = data[at] ^ stream[i]
        block += 1
    return bytes(out)


def encrypt_text(key: str, text: str) -> dict:
    nonce = sha(key + "\nnonce\n" + text).hex()[:16]
    return {"n": nonce, "c": xor_keystream(key, nonce, text.encode("utf-8")).hex()}


def decrypt_section(key: str, data: dict):
    for section in data.get("keyed", []):
        if section.get("k") == key_id(key):
            return xor_keystream(key, section["n"], bytes.fromhex(section["c"])).decode("utf-8")
    return None


def protect(keys: list, plain: dict, keep_plain: bool) -> dict:
    names = dict(plain.get("_names", {}))
    for c in plain.get("credits", []):
        names.setdefault(c["n"], c["r"])
    member_names = plain.get("_memberNames")
    credits = plain.get("credits", [])
    out = {k: v for k, v in plain.items() if not k.startswith("_")}
    out["v"] = 2
    out["access"] = {plain_hash(n): r for n, r in names.items()} if keep_plain else {}
    out["credits"] = credits if keep_plain else []
    if member_names is not None:
        out["members"] = {plain_hash(n): 1 for n in member_names} if keep_plain else {}
    out["plain"] = keep_plain
    # Bot の JSON.stringify と同じ詰め方（区切りの空白なし、日本語はそのまま）
    text = json.dumps(credits, ensure_ascii=False, separators=(",", ":"))
    out["keyed"] = []
    for key in keys:
        section = {"k": key_id(key)}
        section.update(encrypt_text(key, text))
        section["access"] = {keyed_hash(key, n): r for n, r in names.items()}
        if member_names is not None:
            section["members"] = {keyed_hash(key, n): 1 for n in member_names}
        out["keyed"].append(section)
    return out


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    args = sys.argv[1:]
    if len(args) == 3 and args[0] == "--decrypt":
        data = json.load(io.open(args[2], encoding="utf-8"))
        text = decrypt_section(args[1], data)
        print("この鍵の区画がありません" if text is None else text)
        return 0 if text is not None else 1
    keep_plain = "--keep-plain" in args
    args = [a for a in args if a != "--keep-plain"]
    if len(args) != 3:
        print(__doc__)
        return 2
    plain = json.load(io.open(args[1], encoding="utf-8"))
    with io.open(args[2], "w", encoding="utf-8", newline="\n") as f:
        json.dump(protect(args[0].split(","), plain, keep_plain), f, ensure_ascii=False, indent=1)
    return 0


if __name__ == "__main__":
    sys.exit(main())
