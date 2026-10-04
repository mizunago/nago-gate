# パッケージの直し方と確かめ方

`com.nagonago.notice` と `com.nagonago.supporter-gate` は、複数のワールドのプロジェクトから使われます。中身がプロジェクトごとにずれないように、直す場所と確かめる場所を決めています。

## 決まり

1. **直すのは、このリポジトリだけ**。各ワールドのプロジェクトに入っている `Packages/com.nagonago.*` は、直接書き換えない
2. ワールド側で不具合や足りない機能を見つけたら、このリポジトリに伝える。直して版を上げたものを、VCC で入れ直す
3. パッケージの動きは、このリポジトリの検証用プロジェクト（`unity-test/`）で確かめてから公開する。ワールド側で確かめるのは、そのワールドへの組み込み（配線、見た目、そのワールドのギミックとの組み合わせ）だけ
4. 急ぎでワールド側を直接直した場合は、直した内容をすぐこのリポジトリへ伝える。取り込んで版を上げるまでは、ずれた状態として扱う

## 確かめ方

```
python tools/run_unity_tests.py            全部（8 分ほど）
python tools/run_unity_tests.py member     名前に member を含む場面だけ
```

Unity をバッチモードで動かし、次を確かめます。Unity の場所は、環境変数 `UNITY_EXE` で変えられます。

| 段階 | 確かめること |
|---|---|
| コンパイルと配線 | UdonSharp のコンパイル、プログラムアセット、通知のプレハブ、`Create Scene Setup` と `Wire Notices` の配線 |
| ClientSim での再生 | 場面ごとに実際に再生し、出た表示・プレイヤーの位置・入場の可否を、期待と照らす。Udon の例外が 1 つでも出たら失敗 |

今ある場面:

| 場面 | 見ていること |
|---|---|
| presence/GuestLocal | 通知の積み上げと言語の切り替え、支援者の退出後のカウントダウン、理由つきでロビーへ戻る、ボードの本人向けの表示と招待 URL |
| member/Paula | メンバー限定のゲートに、メンバーが入れる。「支援者」が「メンバー」に言い換わる |
| member/Dave | メンバー限定のゲートに、支援者だがメンバーでない人は入れない |
| convert/GuestLocal | `Convert Existing World` で、スポーン地点の引き継ぎ・入口の部屋・リスポーンの高さが正しく設定され、メンバーが入場できる |
| convert/Dave | 変換したワールドで、入れない人が入口の部屋に戻る |

支援者リストは `unity-test/TestData/supporters.json` を、スクリプトが `127.0.0.1` で配ります。場面を足すときは、`unity-test/Assets/Editor/PlaySmoke.cs`（シーンの組み立て）、`unity-test/Assets/Test/NoticeSmoke.cs`（再生中の操作とログ）、`tools/run_unity_tests.py` の `SCENARIOS`（期待する表示）を直します。

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
