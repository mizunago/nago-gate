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

# 場面: (名前, 環境変数, 期待する文字列のリスト[, 出てよいエラーの目印のリスト[, 出てはいけない文字列のリスト]])。"A && B" は「A と B を両方含む行がある」
SCENARIOS = [
    ("presence/GuestLocal（支援者が在室の間だけ開く。本人はメンバー）", {}, [
        "credits(ja)=<size=125%><b>Special Thanks</b></size>// && Paula && Dave",
        "info(ja)= && <color=#C9B8FF><b>あなたの状態</b></color> && あなたは<color=#9BE7A8><b>メンバー</b></color>です && <color=#C9B8FF><b>ご案内</b></color> && Discord で案内しています && <b>discord.gg/testInvite</b>",
        "支援者が退出しました。 && でロビーに戻ります",
        "t=19  && allowed=False && 支援者の退出から時間が経ち",
        "info(late)= && <color=#FF8A80>あなたはこのワールドにアクセスする権限を持っていません</color>",
    ]),
    ("member/Paula（メンバー限定。本人はプラチナかつメンバー）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Paula"}, [
        "info(ja)= && あなたは<color=#8FD3FF><b>プラチナサポーター</b></color>・<color=#9BE7A8><b>メンバー</b></color>です",
        "info(en)= && Your status && You are: <color=#8FD3FF><b>Platinum Supporter</b></color> / <color=#9BE7A8><b>Member</b></color>",
        "t=19  && allowed=True inside=True",
        "status(late)=<color=#AEB4BE>モード:</color> <b>メンバー限定</b>/<color=#AEB4BE>在室メンバー:</color> <b>1</b>/<color=#AEB4BE>あなた:</color> <color=#9BE7A8><b>メンバー</b></color>/<color=#AEB4BE>入場:</color> <color=#7CFC9A><b>可</b></color>",
    ]),
    ("member/Dave（メンバー限定。本人は支援者だがメンバーではない）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Dave"}, [
        "info(ja)= && あなたは<color=#F5C542><b>サポーター</b></color>です && あなたはこのワールドにアクセスする権限を持っていません",
        "t=9  && allowed=False inside=False && This area is for members only.",
        "status(late)=<color=#AEB4BE>モード:</color> <b>メンバー限定</b>/<color=#AEB4BE>在室メンバー:</color> <b>0</b>/<color=#AEB4BE>あなた:</color> <b>一般</b>/<color=#AEB4BE>入場:</color> <color=#FF8A80><b>不可</b></color>",
    ]),
    ("many/Nobody（支援者が 130 人。名前の一覧をページに分けて切り替える）", {"SG_SMOKE_MODE": "open", "SG_SMOKE_NAME": "Nobody", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_LIST": "supporters-130.json"}, [
        "credits(ja)=<size=125%><b>Special Thanks</b></size>// && <nobr> && / 5</color></size>",
        "credits(late)=<size=125%><b>Special Thanks</b></size>// && / 5</color></size>",
        "info(ja)= && <color=#AEB4BE>支援者・メンバーの登録は見つかりません</color>",
    ]),
    ("keyed/Paula（鍵つきのリスト。メンバー限定。本人はプラチナかつメンバー）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Paula", "SG_SMOKE_LIST": "supporters-keyed.json", "SG_SMOKE_KEY": "TestListKey-0123"}, [
        "loaded=True hasMembers=True localRank=2 localMember=True rank(Paula)=2 rank(Alice)=0",
        "credits decrypted count=2",
        "credits(ja)=<size=125%><b>Special Thanks</b></size>// && Paula && Dave",
        "info(ja)= && あなたは<color=#8FD3FF><b>プラチナサポーター</b></color>・<color=#9BE7A8><b>メンバー</b></color>です",
        "t=19  && allowed=True inside=True",
    ]),
    ("keyed/many（鍵つきのリスト。支援者が 130 人）", {"SG_SMOKE_MODE": "open", "SG_SMOKE_NAME": "Nobody", "SG_SMOKE_REMOTE": "none", "SG_SMOKE_LIST": "supporters-130-keyed.json", "SG_SMOKE_KEY": "TestListKey-0123"}, [
        "credits decrypted count=130",
        "credits(ja)=<size=125%><b>Special Thanks</b></size>// && <nobr>なごなご && / 5</color></size>",
        "info(ja)= && 支援者・メンバーの登録は見つかりません",
    ]),
    ("keyed/plain（鍵を入れたワールドが、鍵なしのリストを読む。切り替える前の状態）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Paula", "SG_SMOKE_KEY": "TestListKey-0123"}, [
        "loaded=True hasMembers=True localRank=2 localMember=True rank(Paula)=2 rank(Alice)=0",
        "credits(ja)=<size=125%><b>Special Thanks</b></size>// && Paula && Dave",
        "t=19  && allowed=True inside=True",
    ]),
    ("keyed/nokey（鍵つきのリストを、鍵の無いワールドが読む。誰も入れない）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Paula", "SG_SMOKE_LIST": "supporters-keyed.json"}, [
        "loaded=False",
        "鍵つきのリストですが、List Key が未設定です",
        "info(ja)= && <color=#FF8A80>リストを取得できませんでした</color>",
        "t=9  && allowed=False inside=False",
    ], ["List Key"]),
    ("keyed/wrongkey（ワールドの鍵が、リストの鍵と違う。誰も入れない）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Paula", "SG_SMOKE_LIST": "supporters-keyed.json", "SG_SMOKE_KEY": "WrongKey-00000000"}, [
        "loaded=False",
        "List Key が、リストのどの鍵とも一致しません",
        "t=9  && allowed=False inside=False",
    ], ["List Key"]),
    ("keyed/rotate-old（鍵を入れ替えている間。リストに新旧 2 本の区画。ワールドはまだ古い鍵）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Paula", "SG_SMOKE_LIST": "supporters-keyed-two.json", "SG_SMOKE_KEY": "TestListKey-0123"}, [
        "loaded=True hasMembers=True localRank=2 localMember=True rank(Paula)=2 rank(Alice)=0",
        "credits decrypted count=2",
        "credits(ja)=<size=125%><b>Special Thanks</b></size>// && Paula && Dave",
        "t=19  && allowed=True inside=True",
    ]),
    ("keyed/rotate-new（鍵を入れ替えている間。ワールドは新しい鍵に上げ直した）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Paula", "SG_SMOKE_LIST": "supporters-keyed-two.json", "SG_SMOKE_KEY": "NewListKey-45678"}, [
        "loaded=True hasMembers=True localRank=2 localMember=True rank(Paula)=2 rank(Alice)=0",
        "credits decrypted count=2",
        "credits(ja)=<size=125%><b>Special Thanks</b></size>// && Paula && Dave",
        "t=19  && allowed=True inside=True",
    ]),
    ("keyed/migrate-nokey（鍵なしから移る間。鍵なしの部分も残したリストを、鍵の無いワールドが読む）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Paula", "SG_SMOKE_LIST": "supporters-keyed-plain.json"}, [
        "loaded=True hasMembers=True localRank=2 localMember=True rank(Paula)=2 rank(Alice)=0",
        "credits(ja)=<size=125%><b>Special Thanks</b></size>// && Paula && Dave",
        "t=19  && allowed=True inside=True",
    ]),
    ("keyed/migrate-key（鍵なしから移る間。鍵を入れたワールドは、鍵の区画のほうを読む）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Paula", "SG_SMOKE_LIST": "supporters-keyed-plain.json", "SG_SMOKE_KEY": "TestListKey-0123"}, [
        "loaded=True hasMembers=True localRank=2 localMember=True rank(Paula)=2 rank(Alice)=0",
        "credits decrypted count=2",
        "credits(ja)=<size=125%><b>Special Thanks</b></size>// && Paula && Dave",
        "t=19  && allowed=True inside=True",
    ]),
    ("keyed/migrate-otherkey（鍵なしの部分が残っていれば、鍵が合わないワールドも鍵なしの部分で動く）", {"SG_SMOKE_VARIANT": "member", "SG_SMOKE_NAME": "Paula", "SG_SMOKE_LIST": "supporters-keyed-plain.json", "SG_SMOKE_KEY": "WrongKey-00000000"}, [
        "loaded=True hasMembers=True localRank=2 localMember=True rank(Paula)=2 rank(Alice)=0",
        "credits(ja)=<size=125%><b>Special Thanks</b></size>// && Paula && Dave",
        "t=19  && allowed=True inside=True",
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
    ("joinleave/on（入退室の通知。既定を ON にしたワールド。入室と退室が出る）", {"SG_SMOKE_JL": "on"}, [
        "[NoticeJoinLeave] && OwnerDummy && が入室しました",
        "[NoticeJoinLeave] && OwnerDummy && が退室しました",
        "joinleave(late) on=True text=入退室の通知: ON label=入退室の通知: ON",
    ]),
    ("joinleave/off（入退室の通知。既定は OFF。何も出ない）", {"SG_SMOKE_JL": "off"}, [
        "joinleave(late) on=False text=入退室の通知: OFF label=入退室の通知: OFF",
    ], [], ["[NoticeJoinLeave] && OwnerDummy"]),
    ("joinleave/toggle（入退室の通知。OFF から ON に切り替える。切り替えたあとの退室だけ出て、設定を保存する）", {"SG_SMOKE_JL": "toggle"}, [
        "joinleave toggled on=True label=入退室の通知: ON",
        "[NoticeJoinLeave] saved on=True",
        "[NoticeJoinLeave] && OwnerDummy && が退室しました",
        "joinleave(late) on=True",
    ], [], ["[NoticeJoinLeave] && が入室しました"]),
    ("joinleave/restore（入退室の通知。直前の toggle の場面で保存した ON を、次に来たときに引き継ぐ。単独では通らない）", {"SG_SMOKE_JL": "off", "SG_SMOKE_JL_KEEP": "1"}, [
        "[NoticeJoinLeave] restored on=True",
        "[NoticeJoinLeave] && OwnerDummy && が退室しました",
        "joinleave(late) on=True text=入退室の通知: ON label=入退室の通知: ON",
    ]),
]


def run_unity(method: str, log_name: str, env_extra: dict, quit_after: bool) -> int:
    cmd = [UNITY, "-batchmode", "-projectPath", str(PROJECT), "-executeMethod", method, "-logFile", str(PROJECT / log_name)]
    if quit_after:
        cmd.append("-quit")
    return subprocess.run(cmd, env={**os.environ, **env_extra}).returncode


def found(lines: list, exp: str) -> bool:
    parts = [p.strip(" ") if not p.startswith("t=") else p for p in exp.split(" && ")]
    return any(all(p in line for p in parts) for line in lines)


def check(lines: list, expectations: list) -> list:
    return [exp for exp in expectations if not found(lines, exp)]


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
        build_ok = "PROGRAMS_OK" in result and "BUILD_DONE" in result and "EXCEPTION" not in result and "VERIFY_GATE_DONE" in result and "VERIFY_BOARD_ONLY_OK" in result and "VERIFY_JOINLEAVE_OK" in result
        print("  " + ("ok" if build_ok else "FAIL（unity-test/batch-result.txt と unity-build.log を見る）"))
        if not build_ok:
            return 1

        for scenario in SCENARIOS:
            name, env, expectations = scenario[:3]
            allowed_errors = scenario[3] if len(scenario) > 3 else []
            forbidden = scenario[4] if len(scenario) > 4 else []
            if only and only not in name:
                continue
            print(f"== {name} ==")
            run_unity("PlaySmoke.Run", "unity-smoke.log", env, False)
            path = PROJECT / "smoke-result.txt"
            raw = path.read_text(encoding="utf-8", errors="replace").split("\n") if path.exists() else []
            lines = [l for l in raw if not any(n in l for n in NOISE) and l.strip()]
            failures = check(lines, expectations)
            unwanted = [f for f in forbidden if found(lines, f)]
            udon_errors = [l for l in lines if ("halted" in l or "<Exception>" in l or "<Error>" in l) and not any(a in l for a in allowed_errors)]
            ended = any("SMOKE_END" in l for l in lines)
            if failures or unwanted or udon_errors or not ended:
                failed += 1
                for f in failures:
                    print(f"  FAIL 期待した表示が無い: {f}")
                for f in unwanted:
                    print(f"  FAIL 出てはいけない表示がある: {f}")
                for l in udon_errors[:5]:
                    print(f"  FAIL 例外やエラー: {l[:200]}")
                if not ended:
                    print("  FAIL 最後まで再生されなかった")
            else:
                print(f"  ok（{len(expectations) + len(forbidden)} 項目、例外なし）")
            # 鍵つきのリストで、名前を元に戻すのにかかった時間（参考）
            for l in lines:
                if "credits decrypted" in l:
                    print("  " + l[l.index("credits decrypted"):].strip()[:120])
    finally:
        server.shutdown()

    print("\nすべて通過" if failed == 0 else f"\n失敗した場面: {failed}")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
