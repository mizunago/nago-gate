# Discord 側セットアップ

## 1. Discord サーバーを作る

サーバーを新規作成するだけです。ロールとチャンネルは Bot が作るので、この時点では何も足しません。

## 2. Bot アプリの作成

1. https://discord.com/developers/applications で New Application
2. Bot → Reset Token でトークン取得（`instance/.env` の `DISCORD_TOKEN`）
3. Bot → Privileged Gateway Intents で **SERVER MEMBERS INTENT** を ON（必須）。登録チャンネルの自動処理を使うなら **MESSAGE CONTENT INTENT** も ON
4. OAuth2 → URL Generator: scope `bot` + `applications.commands`、権限 `Manage Roles` + `Manage Channels` + `Manage Messages` + `Send Messages` + `View Channels`
5. 生成 URL でサーバーに招待し、サーバー設定 → ロールで **Bot のロールを一番上へ** 移動（Bot は自分より下のロールしか作成・付与できない）

## 3. 公開先の準備（GitHub Pages、同じリポジトリの gh-pages ブランチ）

VRChat が設定変更なしで読めるドメインは限られます（`*.github.io`, `gist.githubusercontent.com`, `pastebin.com`, `*.vrcdn.cloud`, `*.disbridge.com`）。
`raw.githubusercontent.com` は対象外なので Pages を使います。リポジトリを増やさないため、Bot 自身の公開リポジトリ `nago-gate` の `gh-pages` ブランチに書きます。Bot はそのブランチにしか触らないので `main` は汚れません。

1. Fine-grained Personal Access Token を作る: Repository access で `nago-gate` だけを選び、Permissions → Contents を Read and write。`instance/.env` の `GITHUB_TOKEN` に
2. `config.jsonc` の `publish` はサンプルのまま（owner `mizunago`, repo `nago-gate`, branch `gh-pages`）
3. Bot を起動して `/vrc-admin publish` を 1 回実行。`gh-pages` ブランチが無ければ Bot が `supporters.json` だけを含むブランチを自動で作る
4. GitHub の Settings → Pages → Source「Deploy from a branch」、Branch `gh-pages` / `/ (root)` で保存（1 回だけ）
5. Udon に設定する URL: `https://mizunago.github.io/nago-gate/supporters.json`

Pages は push から反映まで 1〜2 分、さらに CDN キャッシュで最大 10 分ほど遅れます。支援者の反映に数分かかるのはこのためです。

## 4. 設定ファイル（bot/instance/）

```bash
cd bot
cp -r instance.example instance
```

| ファイル | 書くもの |
|---|---|
| `instance/.env` | `DISCORD_TOKEN` と `GITHUB_TOKEN` だけ。秘密情報はここ以外に置かない |
| `instance/config.jsonc` | それ以外すべて。コメント付きなので項目の説明はファイル内を参照 |

`config.jsonc` の構成:

```jsonc
{
  "discord": { "guildId": "...", "adminRoleIds": [] },
  "tiers":   [ { "id": "supporter", "rank": 1, "label": "Supporter", "color": "#F5C542",
                 "roleId": "<共通ロール>", "sourceRoleIds": ["<Patreon ティアロール>", "<Ci-en プランロール>"] } ],
  "rules":   { "graceDays": 31, "nameChangeCooldownDays": 30, "maxNameLength": 32 },
  "sync":    { "intervalMinutes": 10 },
  "publish": { "type": "gist", "gistId": "...", "fileName": "supporters.json" }
}
```

- `rank` は大きいほど上位。上位ティアの人には下位の共通ロールも付きます
- `label` / `color` はワールド内の表示（クレジット・頭上タグ）に使われます
- ティアを増やすときは `tiers` に追加するだけです。Udon 側の変更は不要
- Bot のアプリケーション ID はトークンから自動取得するので設定不要です

## 5. 起動とサーバー構築

最初は `tiers` のロール ID が仮のままで構いません（Bot は起動します）。

```bash
npm install && npm run build
npm start
```

起動したら Discord で順に実行します（管理者のみ）。

1. `/vrc-admin setup-roles` — `Supporter` / `Platinum` / `src-*` を作り、`config.jsonc` に貼る `tiers` を表示する。貼って Bot を再起動
2. Patreon / Ci-en の連携画面で、各プランに `src-*` ロールを割り当てる
3. `/vrc-admin setup-info` — INFO カテゴリを作る。表示された登録チャンネル ID を `discord.registerChannelId` に入れて再起動
4. `登録-register` で `/vrc-admin panel` — ボタンパネルを投稿してピン留め
5. `/vrc-admin setup-world jp:<公開ワールド名> en:<English name> visibility:全員` — 公開ワールドのカテゴリ
6. `/vrc-admin setup-world jp:<限定ワールド名> en:<English name> visibility:Supporter` — 限定ワールドのカテゴリ（必要なら `nsfw:true`）

7. 任意: 管理者だけが見えるカテゴリにテキストチャンネルを作り、その ID を `discord.logChannelId` に入れて再起動すると、Bot の操作ログ（ロール付与、登録、セットアップ、エラー）がそこに流れます。リモートからの動作確認に使えます

どのコマンドも同名があれば作り直さず権限だけ揃えるので、何度実行しても増えません。
`はじめに-start-here` の本文とウェルカム画面だけは手で書きます。

常駐のさせ方は [hosting.md](hosting.md) を参照。データは `instance/data/db.json` に保存されます（バックアップ対象）。

## 6. コマンド

**支援者向け**（返答とコマンド説明は、そのユーザーの Discord クライアント言語に合わせて日本語 / 英語 / 簡体中文 / 繁体中文 / 韓国語で表示。他の言語は英語。文言は `bot/src/i18n.ts`）

| コマンド | 説明 |
|---|---|
| `/vrc register name:<表示名> [credit:<true/false>]` | VRChat の表示名を登録・変更 |
| `/vrc status` | 自分の状態（ランク、猶予、次回変更可能日） |
| `/vrc credit show:<true/false>` | クレジット表示の切り替え |

**管理者向け**（ManageGuild 権限または `adminRoleIds`）

| コマンド | 説明 |
|---|---|
| `/vrc-admin setup-roles` | 出力ロールと入力ロールを作成し、config 用の ID を表示 |
| `/vrc-admin setup-info` | INFO カテゴリ（はじめに・お知らせ・登録・雑談 jp/en）を作成 |
| `/vrc-admin setup-world jp: en: visibility: [nsfw:]` | ワールド用カテゴリ（ワールド・更新情報・フィードバック、限定なら lounge も）を作成 |
| `/vrc-admin panel` | 実行したチャンネルに登録ボタン付きパネルを投稿（登録チャンネルで 1 回だけ実行してピン留め） |
| `/vrc-admin sync` | 今すぐ同期＋公開 |
| `/vrc-admin publish` | JSON を強制再公開 |
| `/vrc-admin lookup user:@x` | 登録状態の確認 |
| `/vrc-admin setname user:@x name:<表示名>` | クールダウン無視で名前を設定 |
| `/vrc-admin grant user:@x rank:<n> [days:<n>]` | 手動でランク付与（支援サイトを使わない特例など） |
| `/vrc-admin revoke user:@x` | 手動付与の取り消し |
| `/vrc-admin hash name:<表示名>` | ハッシュ値の確認（Udon 側デバッグ用） |

## 7. 登録チャンネルの運用

支援者はスラッシュコマンドを貼り付けて失敗しがちなので、登録は **パネルのボタン** を主経路にします。

1. `登録-register` チャンネルで `/vrc-admin panel` を実行し、投稿されたパネルをピン留めする
2. チャンネル ID を `config.jsonc` の `discord.registerChannelId` に入れて Bot を再起動する
3. これで、このチャンネルへの通常投稿は Bot が処理します
   - `/vrc register name:xxx` をテキストで貼った → そのまま登録して結果を返す
   - それ以外 → ボタンへの案内を出す
   - どちらも元の投稿は削除し、案内は 90 秒で消える

ボタンを押すと入力フォームが開き、フォームのラベルとエラーはその人のクライアント言語で出ます。

## 8. 支援者への案内文（例）

> 1. 支援サイトの Discord 連携を完了して、このサーバーに参加してください
> 2. `#登録-register` チャンネルの **登録** ボタンを押して、VRChat の表示名を入力してください
>    （表示名はプロフィールに出ている名前です。ユーザー名やユーザー ID ではありません）
> 3. 数分後からワールドに入れます。表示名を変えたら再度 **登録** してください（30 日に 1 回）
> 4. クレジットに名前を載せたくない場合は **クレジット OFF** ボタン

## 猶予と剥奪の動き

- 支援サイトのロールが外れた時点から 31 日は有効ランクを維持します
- Ci-en の公式 Bot は退会翌月にサーバーからキックすることがあります。キックされても Bot 側の記録で猶予は続きます（再参加すれば共通ロールも戻ります）
- 猶予が切れると JSON から外れ、ワールド側は次回の再取得（既定 10 分）で反映します
