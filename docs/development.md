# パッケージの直し方と確かめ方

`com.nagonago.notice` と `com.nagonago.supporter-gate` は、複数のワールドのプロジェクトから使われます。中身がプロジェクトごとにずれないように、直す場所と確かめる場所を決めています。

## 決まり

1. **直すのは、このリポジトリだけ**。各ワールドのプロジェクトに入っている `Packages/com.nagonago.*` は、直接書き換えない
2. ワールド側で不具合や足りない機能を見つけたら、このリポジトリに伝える。直して版を上げたものを、VCC で入れ直す
3. パッケージの動きは、このリポジトリの検証用プロジェクト（`unity-test/`）で確かめてから公開する。ワールド側で確かめるのは、そのワールドへの組み込み（配線、見た目、そのワールドのギミックとの組み合わせ）だけ
4. 急ぎでワールド側を直接直した場合は、直した内容をすぐこのリポジトリへ伝える。取り込んで版を上げるまでは、ずれた状態として扱う

## 確かめ方

```
python tools/run_unity_tests.py            全部（20 分ほど）
python tools/run_unity_tests.py member     名前に member を含む場面だけ
```

Unity をバッチモードで動かし、次を確かめます。Unity の場所は、環境変数 `UNITY_EXE` で変えられます。

| 段階 | 確かめること |
|---|---|
| コンパイルと配線 | UdonSharp のコンパイル、プログラムアセット、通知のプレハブ、`Create Scene Setup` と `Wire Notices` の配線。テスト用のパネルの出し分け（Build & Test は出して持ち主だけ、Play モードは誰でも、公開用は取り除く）と、前の版のシーンへの後付け |
| ClientSim での再生 | 場面ごとに実際に再生し、出た表示・プレイヤーの位置・入場の可否を、期待と照らす。Udon の例外が 1 つでも出たら失敗 |

今ある場面:

| 場面 | 見ていること |
|---|---|
| presence/GuestLocal | 通知の積み上げと言語の切り替え、支援者の退出後のカウントダウン、理由つきでロビーへ戻る、ボードの本人向けの表示と招待 URL |
| member/Paula | メンバー限定のゲートに、メンバーが入れる。「支援者」が「メンバー」に言い換わる |
| member/Dave | メンバー限定のゲートに、支援者だがメンバーでない人は入れない |
| many/Nobody | 支援者が 130 人のとき、名前の一覧がページに分かれて切り替わる |
| keyed/Paula・keyed/many | 鍵つきのリストで、判定とクレジットの表示ができる（130 人でも元に戻せる） |
| keyed/plain | 鍵を入れたワールドが、鍵なしのリストを読める |
| keyed/nokey・keyed/wrongkey | 鍵が無い・合わないワールドは、取得できなかった扱いになり、誰も入れない |
| keyed/rotate-old・keyed/rotate-new | 鍵を入れ替えている間（リストに新旧 2 本の区画）、古い鍵のワールドも新しい鍵のワールドも動く |
| keyed/migrate-nokey・migrate-key・migrate-otherkey | 鍵なしから移る間（鍵なしの部分も残したリスト）、鍵の無いワールドも、鍵を入れたワールドも動く |
| convert/GuestLocal | `Convert Existing World` で、スポーン地点の引き継ぎ・入口の部屋・リスポーンの高さが正しく設定され、メンバーが入場できる |
| convert/Dave | 変換したワールドで、入れない人が入口の部屋に戻る |
| nomember/GuestLocal・nomember/Paula | ボードの `Show Member Status` を OFF にしたワールドで、メンバーに触れる表示が出ない（支援者の状態と、支援の案内だけ） |
| joinleave/on・joinleave/off | 入退室の通知が、ON のときだけ出る（入室と退室。状態の表示も切り替わる） |
| testpanel/Dave | テスト用のパネルが Play モードで出て、持ち主でない人も使える。「住人」で入れるようになり、「リストどおりに戻す」で元に戻る |
| testpanel/owner | 持ち主の名前で入っていても、上書き中は持ち主の特別枠を使わない（「ランクなし」「住人ではない」で入れなくなる） |
| joinleave/toggle・joinleave/restore | OFF から ON に切り替えると、そのあとの退室から通知が出て、設定が保存される。次に来たときに引き継ぐ（restore は、直前の toggle の保存を使うので単独では通らない） |

支援者リストは `unity-test/TestData/supporters.json` を、スクリプトが `127.0.0.1` で配ります。場面を足すときは、`unity-test/Assets/Editor/PlaySmoke.cs`（シーンの組み立て）、`unity-test/Assets/Test/NoticeSmoke.cs`（再生中の操作とログ）、`tools/run_unity_tests.py` の `SCENARIOS`（期待する表示）を直します。

検証用のプロジェクトの Scripting Define Symbols には、実際のワールドのプロジェクトと同じ `VRC_SDK_VRCSDK3;UDON;UDONSHARP;VRC_ENABLE_PLAYER_PERSISTENCE` を入れてあります。`VRC_ENABLE_PLAYER_PERSISTENCE` が無いと、ClientSim で PlayerData の保存と `OnPlayerRestored` が動きません。ClientSim が保存したデータは `unity-test/ClientSimStorage/` に残るので、場面を始める前に消しています（`SG_SMOKE_JL_KEEP=1` のときだけ残す）。

新しい U# のスクリプトを足した直後の 1 回目は、プログラムアセットを作った同じ回で配線の確認まで進むため、`outdated script version` の例外で止まります。もう一度実行すれば通ります。

鍵つきの見本（`supporters-keyed*.json`、鍵は `TestListKey-0123` と `NewListKey-45678`）は、`python tools/protect_list.py` で作ります。Bot（`bot/src/protect.ts`）とは別の実装なので、形式を変えたときは、Bot の出力と突き合わせてください。

ここで確かめられないもの:

- VR 実機での見え方（通知の位置や大きさ、音量）
- 2 人以上での同期（承認パネルの操作が相手に届くか）
- 各ワールドへの組み込み

## 公開の手順

1. `Packages/<名前>/package.json` の `version` を上げる
2. `python tools/run_unity_tests.py` を通す
3. main に commit / push
4. `git worktree add ../nago-gate-pages gh-pages` で gh-pages を出し、`python tools/build_vpm.py ../nago-gate-pages --existing ../nago-gate-pages/vpm.json` を実行して、gh-pages に commit / push
5. 各ワールドの担当に、版と変わった点を伝える

通知のプレハブ（`NoticeHub.prefab`）は、公開したあとに作り直さない。中の ID が変わり、使う側の上書きが外れるため。

## 表示の画像を作る

```
python tools/make_screenshots.py            全部（15 分ほど）
python tools/make_screenshots.py members    名前に members を含む場面だけ
```

クレジットのボード、ロビーのパネル、承認パネル、本人の視点（通知が重なって見える）を、場面ごとに画像にします。出力は `unity-test/screenshots/` で、一覧は `unity-test/screenshots/README.md` です（リポジトリには入れません）。文言や見た目を変えたら、撮り直して目で確かめます。

## ずれの確認

```
python tools/check_drift.py <プロジェクトのフォルダ> [<プロジェクトのフォルダ> ...]
```

各プロジェクトに入っているパッケージを、公開した同じ版の zip と比べます。中身が違うファイル、足りないファイル、公開した版に無いファイルを出します。UdonSharp のプログラムアセット（`.asset`）は Unity がプロジェクトごとに書き換えるので、違っていても問題にしません。

## 検証用プロジェクトの準備

`unity-test/` には、VRChat の SDK と TextMesh Pro の素材を置いていません（再配布しないため）。初めて使うときは、VCC にこのフォルダを足して開くと、`Packages/vpm-manifest.json` に書いた SDK が入ります。TextMesh Pro の素材は、最初のバッチ実行で自動で取り込みます。2 つのパッケージは、`Packages/manifest.json` からリポジトリの `Packages/` を直接参照しています。
