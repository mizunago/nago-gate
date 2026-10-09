# SupporterGate — 支援者限定 VRChat ワールド基盤

Patreon / Ci-en の支援者に Discord ロールを付け、その支援者だけが入れる
VRChat ワールド（または支援者特典付きの公開ワールド）を作るための一式です。

```
sien/
├─ bot/       Discord Bot（Node.js / TypeScript）
├─ Packages/  Unity 側の VPM パッケージ（UdonSharp）
│   ├─ com.nagonago.supporter-gate   支援者ゲート
│   └─ com.nagonago.notice           共通の通知（ゲート以外のワールドでも使える）
├─ unity-test/  パッケージを実際に動かして確かめる Unity プロジェクト（SDK は含まない）
├─ tools/     VPM の一覧と zip を作る、動作を確かめる、各プロジェクトとのずれを調べるスクリプト
├─ docs/      セットアップガイド
└─ private/   非公開メモ（.gitignore 済み）
```

## 全体の流れ

```
支援サイト公式 Bot ──ロール付与──▶ Discord サーバー
                                     │
                     自作 Bot が監視 │  /vrc register で VRChat 名を自己申告
                                     ▼
                       supporters.json（名前のハッシュ + クレジット名）
                                     │  GitHub Pages に公開
                                     ▼
                VRChat ワールド (SupporterRegistry が取得 → SupporterGate が判定)
```

1. 支援サイトの公式 Discord 連携が、支援者に入力ロール（`src-patreon-*` / `src-cien-*`）を付ける
2. 自作 Bot がそれらを監視し、共通ロール `Supporter`（上位ティアなら `Platinum` も）を付与・剥奪する
3. 支援者は Discord で `/vrc register name:<VRChat の表示名>` を実行する
4. Bot が `supporters.json` を生成して GitHub Pages に公開する（名前は SHA-256 ハッシュ化。クレジット表示に同意した名前のみ平文）
5. ワールド内の `SupporterRegistry` が JSON を取得し、在室プレイヤーの表示名をハッシュ化して照合する
6. `SupporterGate` がモードに応じて入場を制御する

## 決定済みの仕様

| 項目 | 決定 |
|---|---|
| アクセス期間 | 支援中のみ ＋ 支援停止後 31 日の猶予（`graceDays`） |
| 名前登録 | Discord で自己申告。変更は 30 日に 1 回（`nameChangeCooldownDays`）。登録・変更から 60 分のうちは打ち間違いを何度でも直せる（`nameFixMinutes`、30 日の数え始めは変わらない）。変更すると旧名は即無効 |
| Discord | 必須 |
| ティア | 複数対応。`config.tiers` に追加すれば上位ティアを増やせる。ゲートは `requiredRank` 以上を支援者扱い |
| 非支援者の扱い | 在室中の支援者が個別に許可（そのインスタンス滞在中のみ有効。入り直すと無効） |
| 支援者退室後 | `noSupporterGraceSeconds`（既定 120 秒）後に非支援者をロビーへ戻す。残り時間を予告し、戻した理由を通知する |
| 持ち主 | Gate の `ownerDisplayNames` に入れた表示名は、リストに載せずに支援者扱い（クレジットには出ない） |
| クレジット | 支援者は `/vrc credit show:false` で非表示にできる |
| Bot の言語 | ユーザーのクライアント言語に自動追従（ja / en / zh-CN / zh-TW / ko）。管理コマンドは日本語 |

## セットアップ手順

1. [docs/discord-setup.md](docs/discord-setup.md) — 支援サイト連携・Bot の起動
2. [docs/unity-setup.md](docs/unity-setup.md) — VPM パッケージの入れ方とワールドへの組み込み
3. [docs/hosting.md](docs/hosting.md) — Bot をどこで動かすか

運用方針やサーバー設計のメモは `private/`（リポジトリ外）に置く。

## クイックスタート（Bot）

```bash
cd bot
cp -r instance.example instance     # instance/.env にトークン、instance/config.jsonc にロール ID などを記入
npm install && npm run build
npm start                           # Windows は bot
un.bat をダブルクリックでも可 / サーバーは docker compose up -d --build
```

設定はすべて `bot/instance/` に集約しています。

| ファイル | 中身 | 扱い |
|---|---|---|
| `instance/.env` | Discord トークン、GitHub トークン | 秘密。共有・コミット禁止 |
| `instance/config.jsonc` | サーバー ID、ロール ID、ティア、猶予日数、公開先 | 秘密ではない。コメント付きで編集できる |
| `instance/data/db.json` | Bot が生成する登録データ | バックアップ対象 |

## クイックスタート（Unity）

1. VCC の Settings > Packages > Add Repository に `https://mizunago.github.io/nago-gate/vpm.json` を入れる
2. プロジェクトに **Nago Supporter Gate** を足す（共通の通知 **Nago Notice** も一緒に入る）
3. メニュー `Tools > SupporterGate > Create Scene Setup`
4. `Registry` の **Data Url** に Bot が公開した JSON の URL を入れる
5. `Gate` の **Mode** を選び、`ContentRoot` の下にワールド本体を入れる

通知だけを使うワールドは、**Nago Notice** だけを足して `Tools > Nago Notice > Add Notice Hub To Scene`（[使い方](Packages/com.nagonago.notice/README.md)）。入退室の通知を足すときは `Tools > Nago Notice > Add Join-Leave Notice To Scene`。

## パッケージを公開する（保守する人向け）

パッケージを直すのは、このリポジトリだけです。各ワールドのプロジェクトに入っている物は直接直しません。直し方・確かめ方・ずれの調べ方は [docs/development.md](docs/development.md) にあります。

1. `Packages/<名前>/package.json` の `version` を上げる
2. `python tools/run_unity_tests.py` で、検証用の Unity プロジェクトを通す
3. gh-pages ブランチの作業ツリーを用意して、一覧と zip を作る

```bash
git worktree add ../nago-gate-pages gh-pages
python tools/build_vpm.py ../nago-gate-pages --existing ../nago-gate-pages/vpm.json
cd ../nago-gate-pages && git add vpm.json vpm && git commit -m "vpm: <名前> <版>" && git push
```

gh-pages には Bot が `supporters.json` を書いている。`vpm.json` と `vpm/` だけを足し、ほかのファイルは触らない。
