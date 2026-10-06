#!/usr/bin/env python3
"""パッケージの表示を、場面ごとに画像にする（検証用の Unity プロジェクトで実際に再生して撮る）。

    python tools/make_screenshots.py            全部（15 分ほど）
    python tools/make_screenshots.py members    名前に members を含む場面だけ

出力は unity-test/screenshots/<場面>/ と、一覧の unity-test/screenshots/README.md（リポジトリには入れない）。
撮るもの: 名前の一覧のボード、本人の状態と案内のパネル、ロビーのパネル、承認パネル、本人の視点（通知が重なって見える）。
支援者リストは unity-test/TestData の見本（Dave = Supporter、Paula = Platinum かつメンバー、GuestLocal = メンバー）。
"""
import functools
import http.server
import os
import shutil
import subprocess
import sys
import threading
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
PROJECT = REPO / "unity-test"
OUT = PROJECT / "screenshots"
UNITY = os.environ.get("UNITY_EXE", r"C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe")
PORT = 8765

# (フォルダ名, 説明, 環境変数, 見るべき画像と説明のリスト)
#   a = 再生 10 秒後、b = 13 秒後、c = 20 秒後
SCENES = [
    ("01-open-general", "公開のワールド。本人は支援者でもメンバーでもない",
     {"SG_SMOKE_MODE": "open", "SG_SMOKE_NAME": "Nobody", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_NOENTER": "1"},
     [("a-InfoPanel", "本人の状態と案内。本人向けの行は出ない"), ("a-CreditsBoard", "名前の一覧（Special Thanks）"), ("a-LobbyPanel", "ロビーのパネル（公開）")]),
    ("02-open-member", "公開のワールド。本人はメンバー",
     {"SG_SMOKE_MODE": "open", "SG_SMOKE_NAME": "GuestLocal", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_NOENTER": "1"},
     [("a-InfoPanel", "本人の状態と案内。「あなたはメンバーです」"), ("a-CreditsBoard", "名前の一覧（Special Thanks）")]),
    ("03-open-supporter", "公開のワールド。本人はサポーター",
     {"SG_SMOKE_MODE": "open", "SG_SMOKE_NAME": "Dave", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_NOENTER": "1"},
     [("a-InfoPanel", "本人の状態と案内。「あなたはサポーターです」"), ("a-CreditsBoard", "名前の一覧（Special Thanks）")]),
    ("04-open-platinum-member", "公開のワールド。本人はプラチナで、メンバーでもある",
     {"SG_SMOKE_MODE": "open", "SG_SMOKE_NAME": "Paula", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_NOENTER": "1"},
     [("a-InfoPanel", "本人の状態と案内。「あなたはプラチナサポーター・メンバーです」"), ("a-CreditsBoard", "名前の一覧（Special Thanks）")]),
    ("05-approval-supporter", "支援者＋許可のワールド。本人はサポーター、支援者でない人が 1 人いる",
     {"SG_SMOKE_MODE": "approval", "SG_SMOKE_NAME": "Dave", "SG_SMOKE_REMOTE": "Hanako", "SG_SMOKE_NOENTER": "1"},
     [("a-LobbyPanel", "ロビーのパネル（入場できる）"), ("a-ApprovalPanel", "承認パネル（支援者の画面。許可のボタン）"), ("a-InfoPanel", "本人の状態と案内"), ("a-CreditsBoard", "名前の一覧（Special Thanks）"), ("a-view", "本人の視点")]),
    ("06-approval-general", "支援者＋許可のワールド。本人は支援者でなく、まだ許可されていない",
     {"SG_SMOKE_MODE": "approval", "SG_SMOKE_NAME": "Nobody", "SG_SMOKE_REMOTE": "Dave"},
     [("a-LobbyPanel", "ロビーのパネル（入場できない理由つき）"), ("a-ApprovalPanel", "承認パネル（支援者でない人の画面）"), ("a-InfoPanel", "本人の状態と案内。入れない旨の行"), ("a-CreditsBoard", "名前の一覧（Special Thanks）"), ("a-view", "中に入ろうとして戻されたときの通知")]),
    ("07-supporters-supporter", "支援者だけのワールド。本人はサポーター",
     {"SG_SMOKE_MODE": "supporters", "SG_SMOKE_NAME": "Dave", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_NOENTER": "1"},
     [("a-LobbyPanel", "ロビーのパネル"), ("a-InfoPanel", "本人の状態と案内"), ("a-CreditsBoard", "名前の一覧（Special Thanks）")]),
    ("08-supporters-general", "支援者だけのワールド。本人は支援者でない",
     {"SG_SMOKE_MODE": "supporters", "SG_SMOKE_NAME": "Nobody", "SG_SMOKE_REMOTE": "none"},
     [("a-LobbyPanel", "ロビーのパネル（入場できない）"), ("a-InfoPanel", "本人の状態と案内。入れない旨の行"), ("a-CreditsBoard", "名前の一覧（Special Thanks）"), ("a-view", "中に入ろうとして戻されたときの通知")]),
    ("09-members-member", "メンバーだけのワールド。本人はメンバー",
     {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "GuestLocal", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_NOENTER": "1"},
     [("a-LobbyPanel", "ロビーのパネル（メンバー限定・入場できる）"), ("a-InfoPanel", "本人の状態と案内。「あなたはメンバーです」"), ("a-CreditsBoard", "名前の一覧（Special Thanks）"), ("a-view", "本人の視点")]),
    ("10-members-supporter", "メンバーだけのワールド。本人はサポーターだが、メンバーではない",
     {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Dave", "SG_SMOKE_REMOTE": "none"},
     [("a-LobbyPanel", "ロビーのパネル（入場できない）"), ("a-InfoPanel", "本人の状態と案内。「あなたはサポーターです」と、入れない旨の行"), ("a-CreditsBoard", "名前の一覧（Special Thanks）"), ("a-view", "中に入ろうとして戻されたときの通知")]),
    ("11-members-general", "メンバーだけのワールド。本人は支援者でもメンバーでもない",
     {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Nobody", "SG_SMOKE_REMOTE": "none"},
     [("a-LobbyPanel", "ロビーのパネル（入場できない）"), ("a-InfoPanel", "本人の状態と案内。入れない旨の行"), ("a-CreditsBoard", "名前の一覧（Special Thanks）")]),
    ("12-presence-countdown", "支援者がいる間だけ開くワールド。支援者が退出して、時間切れで戻される",
     {"SG_SMOKE_NAME": "Nobody"},
     [("a-LobbyPanel", "支援者がいる間（入場できる）"), ("b-view", "支援者の退出後のカウントダウン"), ("c-view", "時間切れで戻されたときの通知"), ("c-LobbyPanel", "戻されたあとのパネル"), ("c-InfoPanel", "本人の状態と案内。戻されたあとのボード"), ("c-CreditsBoard", "名前の一覧（Special Thanks）")]),
    ("13-list-error", "支援者リストを取得できないとき",
     {"SG_SMOKE_MODE": "supporters", "SG_SMOKE_NAME": "Dave", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_BADURL": "1", "SG_SMOKE_NOENTER": "1"},
     [("a-LobbyPanel", "ロビーのパネル"), ("a-InfoPanel", "本人の状態と案内"), ("a-CreditsBoard", "名前の一覧（Special Thanks）")]),
    ("14-english", "英語の表示。メンバーだけのワールドで、本人はプラチナかつメンバー",
     {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Paula", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_LANG": "en", "SG_SMOKE_NOENTER": "1"},
     [("a-LobbyPanel", "ロビーのパネル"), ("a-InfoPanel", "本人の状態と案内"), ("a-CreditsBoard", "名前の一覧（Special Thanks）"), ("a-view", "本人の視点")]),
    ("15-notices", "通知の見本（ゲートが出す文を、種類ごとに出したもの）",
     {"SG_SMOKE_MODE": "open", "SG_SMOKE_NAME": "Dave", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_NOENTER": "1", "SG_SMOKE_NOTICES": "1"},
     [("a-view", "許可された / 来訪の知らせ / 許可の取り消しで戻された / 支援者限定"), ("b-view", "カウントダウン / 支援者が戻った / 時間切れで戻された / 許可が必要")]),
    ("16-converted-lobby", "ゲートの無いワールドを変換したあとの、入口の部屋。本人はメンバー",
     {"SG_SMOKE_CONVERT": "1", "SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "GuestLocal", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_NOENTER": "1"},
     [("a-view", "入口の部屋の中から見たところ"), ("a-LobbyPanel", "ロビーのパネル"), ("a-InfoPanel", "本人の状態と案内"), ("a-CreditsBoard", "名前の一覧（Special Thanks）")]),
    ("17-names-12", "支援者が 12 人（プラチナ 3 人）。1 ページに収まる",
     {"SG_SMOKE_MODE": "open", "SG_SMOKE_NAME": "Nobody", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_NOENTER": "1", "SG_SMOKE_LIST": "supporters-12.json"},
     [("a-CreditsBoard", "名前の一覧")]),
    ("18-names-45", "支援者が 45 人（プラチナ 8 人）。2 ページに分かれ、8 秒ごとに切り替わる",
     {"SG_SMOKE_MODE": "open", "SG_SMOKE_NAME": "Nobody", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_NOENTER": "1", "SG_SMOKE_LIST": "supporters-45.json"},
     [("p-CreditsBoard", "1 ページ目"), ("a-CreditsBoard", "2 ページ目")]),
    ("19-names-130", "支援者が 130 人（プラチナ 20 人）。5 ページに分かれる",
     {"SG_SMOKE_MODE": "open", "SG_SMOKE_NAME": "Nobody", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_NOENTER": "1", "SG_SMOKE_LIST": "supporters-130.json"},
     [("p-CreditsBoard", "1 ページ目（プラチナ 20 人と、サポーターの最初の 10 人）"), ("a-CreditsBoard", "2 ページ目"), ("c-CreditsBoard", "3 ページ目")]),
    ("20-approval-long-en", "長い文字の確認。英語の表示で、全角 15 文字の名前の人を許可する場面",
     {"SG_SMOKE_MODE": "approval", "SG_SMOKE_NAME": "Dave", "SG_SMOKE_REMOTE": "あいうえおかきくけこさしすせそ", "SG_SMOKE_LANG": "en", "SG_SMOKE_NOENTER": "1"},
     [("a-ApprovalPanel", "承認パネル。名前と「Not approved」が 1 行に収まる"), ("a-LobbyPanel", "ロビーのパネル")]),
    ("21-presence-long-en", "長い文字の確認。英語の表示で、支援者がいる間だけ開くワールドの承認パネル",
     {"SG_SMOKE_NAME": "Dave", "SG_SMOKE_REMOTE": "あいうえおかきくけこさしすせそ", "SG_SMOKE_LANG": "en", "SG_SMOKE_NOENTER": "1"},
     [("a-ApprovalPanel", "承認パネル。長い見出しが 1 行に収まる")]),
    ("23-owners-owner", "持ち主と、許可した人だけのワールド（実験用など）。本人は持ち主で、許可していない人が 1 人いる",
     {"SG_SMOKE_CONVERT": "1", "SG_SMOKE_OWNERS": "approval", "SG_SMOKE_NAME": "OwnerLocal", "SG_SMOKE_REMOTE": "Guest", "SG_SMOKE_NOENTER": "1"},
     [("a-ApprovalPanel", "承認パネル。「持ち主以外 1 人」と、許可するボタン"), ("a-LobbyPanel", "ロビーのパネル（持ち主）")]),
    ("24-owners-guest", "持ち主と、許可した人だけのワールド。本人は許可されていない人で、持ち主が在室している",
     {"SG_SMOKE_CONVERT": "1", "SG_SMOKE_OWNERS": "approval", "SG_SMOKE_NAME": "Guest", "SG_SMOKE_REMOTE": "OwnerLocal", "SG_SMOKE_NOENTER": "1"},
     [("a-LobbyPanel", "ロビーのパネル（持ち主の許可待ち）"), ("a-ApprovalPanel", "承認パネル（持ち主専用と出る）")]),
    ("22-test-panel", "テスト用のパネル（Play モードと Build & Test だけに出る）。住人限定のワールドで、支援者だが住人ではない Dave が「住人」を押した",
     {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Dave", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_NOENTER": "1", "SG_SMOKE_TESTPANEL": "hold"},
     [("a-TestPanel", "テスト用のパネル。上書き中で、住人として入場できる"), ("a-LobbyPanel", "ロビーのパネル（「ロビーへ戻る」は無い）")]),
]


class QuietHandler(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *args):
        pass


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    only = sys.argv[1] if len(sys.argv) > 1 else ""
    if not Path(UNITY).exists():
        print(f"Unity が見つかりません: {UNITY}（環境変数 UNITY_EXE で指定してください）")
        return 2

    handler = functools.partial(QuietHandler, directory=str(PROJECT / "TestData"))
    server = http.server.ThreadingHTTPServer(("127.0.0.1", PORT), handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    try:
        print("== コンパイル ==", flush=True)
        subprocess.run([UNITY, "-batchmode", "-projectPath", str(PROJECT), "-executeMethod", "PackageBatch.BuildAll",
                        "-logFile", str(PROJECT / "unity-build.log"), "-quit"])
        result = (PROJECT / "batch-result.txt").read_text(encoding="utf-8", errors="replace")
        if "PROGRAMS_OK" not in result or "BUILD_DONE" not in result:
            print("  FAIL（unity-test/batch-result.txt を見る）")
            return 1
        for folder, desc, env, _ in SCENES:
            if only and only not in folder:
                continue
            shutil.rmtree(OUT / folder, ignore_errors=True)
            print(f"== {folder}: {desc} ==", flush=True)
            subprocess.run([UNITY, "-batchmode", "-projectPath", str(PROJECT), "-executeMethod", "PlaySmoke.Run",
                            "-logFile", str(PROJECT / "unity-smoke.log")], env={**os.environ, **env, "SG_SHOTS": folder})
            made = sorted(p.name for p in (OUT / folder).glob("*.png")) if (OUT / folder).exists() else []
            print(f"  {len(made)} 枚", flush=True)
    finally:
        server.shutdown()

    lines = ["# 表示の一覧", "",
             "`python tools/make_screenshots.py` が、検証用の Unity プロジェクトで実際に再生して撮った画像です。",
             "支援者リストは見本です（Dave = Supporter、Paula = Platinum かつメンバー、GuestLocal = メンバー、Nobody = どれでもない）。",
             "パネルの見た目（色や枠）は、パッケージが作る初期のものです。各ワールドで差し替えられます。", ""]
    missing = 0
    for folder, desc, _, shots in SCENES:
        lines += [f"## {folder}", "", desc, ""]
        for name, caption in shots:
            path = OUT / folder / f"{name}.png"
            if path.exists():
                lines += [f"**{caption}**", "", f"![{caption}]({folder}/{name}.png)", ""]
            else:
                missing += 1
                lines += [f"**{caption}**（画像なし）", ""]
    OUT.mkdir(exist_ok=True)
    (OUT / "README.md").write_text("\n".join(lines), encoding="utf-8")
    print(f"\n一覧: {OUT / 'README.md'}（無い画像 {missing} 枚）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
