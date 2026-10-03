#!/usr/bin/env python3
"""VPM の一覧（vpm.json）とパッケージの zip を作る。

使い方:
    python tools/build_vpm.py <出力フォルダ> [--existing <今の vpm.json>]

- Packages/ の下の各パッケージを <出力フォルダ>/vpm/<名前>-<版>.zip にまとめる
- <出力フォルダ>/vpm.json に、パッケージごとの版の一覧を書く
- --existing を渡すと、そこに載っている過去の版を残したまま、今の版を足す（同じ版は上書き）

出力フォルダは gh-pages ブランチの作業ツリーを想定している。
公開 URL: https://mizunago.github.io/nago-gate/vpm.json
"""
import argparse
import hashlib
import json
import os
import sys
import zipfile

BASE_URL = "https://mizunago.github.io/nago-gate"
LISTING = {
    "name": "Nagonago Packages",
    "id": "com.nagonago.vpm",
    "author": "nagonago",
    "url": BASE_URL + "/vpm.json",
}
# zip を毎回同じ中身にするための固定の日時（同じソースなら同じハッシュになる）
FIXED_DATE = (2026, 1, 1, 0, 0, 0)


def build_zip(package_dir: str, zip_path: str) -> str:
    """package_dir の中身を zip にする（package.json が zip の直下に来る）。SHA-256 を返す。"""
    files = []
    for root, dirs, names in os.walk(package_dir):
        dirs.sort()
        for name in sorted(names):
            full = os.path.join(root, name)
            rel = os.path.relpath(full, package_dir).replace(os.sep, "/")
            files.append((rel, full))
    os.makedirs(os.path.dirname(zip_path), exist_ok=True)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for rel, full in files:
            info = zipfile.ZipInfo(rel, FIXED_DATE)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            with open(full, "rb") as f:
                zf.writestr(info, f.read())
    with open(zip_path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("out_dir")
    parser.add_argument("--existing", help="今公開している vpm.json（過去の版を残す）")
    parser.add_argument("--packages", default=os.path.join(os.path.dirname(__file__), "..", "Packages"))
    args = parser.parse_args()

    packages_root = os.path.abspath(args.packages)
    listing = dict(LISTING)
    listing["packages"] = {}
    if args.existing and os.path.exists(args.existing):
        with open(args.existing, encoding="utf-8") as f:
            listing["packages"] = json.load(f).get("packages", {})

    for name in sorted(os.listdir(packages_root)):
        package_dir = os.path.join(packages_root, name)
        manifest_path = os.path.join(package_dir, "package.json")
        if not os.path.isfile(manifest_path):
            continue
        with open(manifest_path, encoding="utf-8") as f:
            manifest = json.load(f)
        if manifest.get("name") != name:
            print(f"ERROR: {name}: package.json の name がフォルダ名と違います", file=sys.stderr)
            return 1
        missing = [
            os.path.relpath(os.path.join(root, n), package_dir)
            for root, dirs, names in os.walk(package_dir)
            for n in names + dirs
            if not n.endswith(".meta") and not os.path.exists(os.path.join(root, n + ".meta"))
        ]
        if missing:
            print(f"ERROR: {name}: .meta が無いファイルがあります: {missing}", file=sys.stderr)
            return 1

        version = manifest["version"]
        zip_name = f"{name}-{version}.zip"
        sha = build_zip(package_dir, os.path.join(args.out_dir, "vpm", zip_name))
        entry = dict(manifest)
        entry["url"] = f"{BASE_URL}/vpm/{zip_name}"
        entry["zipSHA256"] = sha
        listing["packages"].setdefault(name, {"versions": {}})["versions"][version] = entry
        print(f"{name} {version}: {zip_name} sha256={sha[:16]}…")

    with open(os.path.join(args.out_dir, "vpm.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(listing, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print("wrote", os.path.join(args.out_dir, "vpm.json"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
