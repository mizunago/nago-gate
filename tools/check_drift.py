#!/usr/bin/env python3
"""各ワールドのプロジェクトに入っている com.nagonago.* が、公開した版と同じ中身かを調べる。

    python tools/check_drift.py <プロジェクトのフォルダ> [<プロジェクトのフォルダ> ...]

パッケージの修正は、このリポジトリで行って版を上げるのが決まり。
プロジェクト側で直接直した物が残っていないかを、これで見つける（見つけたら、このリポジトリへ取り込んで版を上げる）。
比べる相手は gh-pages の vpm/<名前>-<版>.zip（origin/gh-pages から読む。先に git fetch しておく）。
"""
import io
import json
import subprocess
import sys
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
# Unity が各プロジェクトで書き換える種類（U# のプログラムアセット）。違っていても修正とは限らない
UNITY_REWRITES = {".asset"}


def published_zip(name: str, version: str):
    ref = f"origin/gh-pages:vpm/{name}-{version}.zip"
    res = subprocess.run(["git", "-C", str(REPO), "show", ref], capture_output=True)
    if res.returncode != 0:
        return None
    return zipfile.ZipFile(io.BytesIO(res.stdout))


def normalize(data: bytes) -> bytes:
    return data.replace(b"\r\n", b"\n")


def latest_version(name: str) -> str:
    path = REPO / "Packages" / name / "package.json"
    return json.loads(path.read_text(encoding="utf-8"))["version"] if path.exists() else "?"


def check_package(pkg_dir: Path) -> bool:
    name = pkg_dir.name
    version = json.loads((pkg_dir / "package.json").read_text(encoding="utf-8"))["version"]
    latest = latest_version(name)
    head = f"  {name} {version}" + ("" if version == latest else f"（最新は {latest}）")
    zf = published_zip(name, version)
    if zf is None:
        print(head + ": 公開した版に同じ番号がありません（公開前の物か、手で版を変えた物）")
        return False

    published = {i.filename: zf.read(i) for i in zf.infolist() if not i.is_dir()}
    changed, eol_only, missing, unity = [], [], [], []
    for rel, data in sorted(published.items()):
        local = pkg_dir / rel
        if not local.exists():
            missing.append(rel)
            continue
        mine = local.read_bytes()
        if mine == data:
            continue
        if Path(rel).suffix in UNITY_REWRITES:
            unity.append(rel)
        elif normalize(mine) == normalize(data):
            eol_only.append(rel)
        else:
            changed.append(rel)
    extra = sorted(
        str(p.relative_to(pkg_dir)).replace("\\", "/")
        for p in pkg_dir.rglob("*")
        if p.is_file() and str(p.relative_to(pkg_dir)).replace("\\", "/") not in published
    )

    clean = not (changed or missing or extra)
    print(head + (": 公開した版と同じ" if clean else ": 違いあり"))
    for label, items in (("中身が違う", changed), ("無い", missing), ("公開した版に無いファイル", extra)):
        for rel in items:
            print(f"    {label}: {rel}")
    if eol_only:
        print(f"    行末だけ違う: {len(eol_only)} 件（問題なし）")
    if unity:
        print(f"    Unity が書き換えたプログラムアセット: {len(unity)} 件（問題なし）")
    return clean


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    all_clean = True
    for arg in sys.argv[1:]:
        project = Path(arg)
        packages = sorted(p for p in (project / "Packages").glob("com.nagonago.*") if (p / "package.json").exists())
        print(f"{project}")
        if not packages:
            print("  com.nagonago.* は入っていません")
            continue
        for pkg in packages:
            all_clean = check_package(pkg) and all_clean
    return 0 if all_clean else 1


if __name__ == "__main__":
    sys.exit(main())
