#!/usr/bin/env python3
"""パッケージを、検証用の Unity プロジェクト（unity-test/）で実際に動かして確かめる。

    python tools/run_unity_tests.py            全部
    python tools/run_unity_tests.py member     名前に member を含む場面だけ

やること:
  1. U# のコンパイルと、プログラムアセット・プレハブ・セットアップの配線の確認（PackageBatch.BuildAll）
  2. ClientSim で場面ごとに再生し、ログに出た表示と位置を、期待と照らす（PlaySmoke.Run）

Unity の場所は、環境変数 UNITY_EXE で変えられる。支援者リストは unity-test/TestData を
このスクリプトが 127.0.0.1:8765 で配る（外には出ない）。VR 実機でしか分からないことは、ここでは確かめられない。
"""
import functools
import http.server
import os
import subprocess
import sys
import threading
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
PROJECT = REPO / "unity-test"
UNITY = os.environ.get("UNITY_EXE", r"C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe")
PORT = 8765

# ClientSim がバッチモードで出す、入力まわりの例外（中身と関係ない）
NOISE = ("ClientSimPlayerController", "NullReferenceException: Object reference not set")

# 場面: (名前, 環境変数, 期待する文字列のリスト)。"A && B" は「A と B を両方含む行がある」
SCENARIOS = [
    ("presence/GuestLocal（支援者が在室の間だけ開く。本人はメンバー）", {}, [
        "credits(ja)= && Discord に参加  discord.gg/testInvite && あなたはメンバーです && Paula && Dave",
        "支援者が退出しました。 && でロビーに戻ります",
        "t=19  && allowed=False && 支援者が退出してから時間が経ったため",
        "credits(late)= && あなたはこのワールドにアクセスする権限を持っていません",
    ]),
    ("member/Paula（メンバー限定。本人はプラチナかつメンバー）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Paula"}, [
        "credits(ja)= && あなたはプラチナサポーター・メンバーです",
        "credits(en)= && You are: Platinum Supporter / Member",
        "t=19  && allowed=True inside=True",
        "status(late)=モード: メンバー限定/在室メンバー: 1/あなた: メンバー/入場: 可",
    ]),
    ("member/Dave（メンバー限定。本人は支援者だがメンバーではない）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Dave"}, [
        "credits(ja)= && あなたはサポーターです && あなたはこのワールドにアクセスする権限を持っていません",
        "t=9  && allowed=False inside=False && This area is for members only.",
        "status(late)=モード: メンバー限定/在室メンバー: 0/あなた: 一般/入場: 不可",
    ]),
    ("convert/GuestLocal（ゲートの無いワールドを変換。本人はメンバー）", {"SG_SMOKE_CONVERT": "1", "SG_SMOKE_VARIANT": "member"}, [
        "convert report: 入口の部屋とゲートを足しました。入れるのは、メンバーだけです。",
        "convert again: このシーンには既に SupporterGate があります",
        "convert result: spawns=1 spawn0=LobbySpawn && parent=LobbyRoom && respawnY=-64 && contentSpawn=(3.00, 0.00, -4.00) rotY=90 && approvalActive=False",
        "gate mode=1 useMemberList=True",
        "t=2  && pos=(0.0, -43.4, 0.0)",
        "t=9  && pos=(3.0, 0.0, -4.0) && allowed=True inside=True",
    ]),
    ("convert/Dave（ゲートの無いワールドを変換。本人はメンバーではない）", {"SG_SMOKE_CONVERT": "1", "SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Dave"}, [
        "t=9  && allowed=False inside=False && This area is for members only.",
        "t=19  && pos=(0.0, -44.0, 0.0) && allowed=False",
    ]),
]


def run_unity(method: str, log_name: str, env_extra: dict, quit_after: bool) -> int:
    cmd = [UNITY, "-batchmode", "-projectPath", str(PROJECT), "-executeMethod", method, "-logFile", str(PROJECT / log_name)]
    if quit_after:
        cmd.append("-quit")
    return subprocess.run(cmd, env={**os.environ, **env_extra}).returncode


def check(lines: list, expectations: list) -> list:
    failures = []
    for exp in expectations:
        parts = [p.strip(" ") if not p.startswith("t=") else p for p in exp.split(" && ")]
        if not any(all(p in line for p in parts) for line in lines):
            failures.append(exp)
    return failures


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    only = sys.argv[1] if len(sys.argv) > 1 else ""
    if not Path(UNITY).exists():
        print(f"Unity が見つかりません: {UNITY}（環境変数 UNITY_EXE で指定してください）")
        return 2

    class QuietHandler(http.server.SimpleHTTPRequestHandler):
        def log_message(self, *args):   # アクセスのログは出さない
            pass

    handler = functools.partial(QuietHandler, directory=str(PROJECT / "TestData"))
    server = http.server.ThreadingHTTPServer(("127.0.0.1", PORT), handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()

    failed = 0
    try:
        print("== コンパイルと配線の確認 ==")
        run_unity("PackageBatch.BuildAll", "unity-build.log", {}, True)
        result = (PROJECT / "batch-result.txt").read_text(encoding="utf-8", errors="replace") if (PROJECT / "batch-result.txt").exists() else ""
        build_ok = "PROGRAMS_OK" in result and "BUILD_DONE" in result and "EXCEPTION" not in result and "VERIFY_GATE_DONE" in result
        print("  " + ("ok" if build_ok else "FAIL（unity-test/batch-result.txt と unity-build.log を見る）"))
        if not build_ok:
            return 1

        for name, env, expectations in SCENARIOS:
            if only and only not in name:
                continue
            print(f"== {name} ==")
            run_unity("PlaySmoke.Run", "unity-smoke.log", env, False)
            path = PROJECT / "smoke-result.txt"
            raw = path.read_text(encoding="utf-8", errors="replace").split("\n") if path.exists() else []
            lines = [l for l in raw if not any(n in l for n in NOISE) and l.strip()]
            failures = check(lines, expectations)
            udon_errors = [l for l in lines if "halted" in l or "<Exception>" in l or "<Error>" in l]
            ended = any("SMOKE_END" in l for l in lines)
            if failures or udon_errors or not ended:
                failed += 1
                for f in failures:
                    print(f"  FAIL 期待した表示が無い: {f}")
                for l in udon_errors[:5]:
                    print(f"  FAIL 例外やエラー: {l[:200]}")
                if not ended:
                    print("  FAIL 最後まで再生されなかった")
            else:
                print(f"  ok（{len(expectations)} 項目、例外なし）")
    finally:
        server.shutdown()

    print("\nすべて通過" if failed == 0 else f"\n失敗した場面: {failed}")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
