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
| `instance/.env` | `DISCORD_TOKEN` と `GITHUB_TOKEN`。鍵つきのリストを使うなら `LIST_KEY`（と、移行中だけ `LIST_KEEP_PLAIN`）も。秘密情報はここ以外に置かない |
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
- `label` / `color` はワールド内の表示（クレジット）に使われます
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
3b. `/vrc-admin setup-community` — 雑談（日本語・英語・中国語・韓国語）と写真チャンネル（sfw / nsfw）のカテゴリを作る
4. `登録-register` で `/vrc-admin panel` — ボタンパネルを投稿してピン留め
5. `/vrc-admin setup-world jp:<公開ワールド名> en:<English name> visibility:全員` — 公開ワールドのカテゴリ
6. `/vrc-admin setup-world jp:<限定ワールド名> en:<English name> visibility:Supporter` — 限定ワールドのカテゴリ（必要なら `nsfw:true`）

7. 任意: 管理者だけが見えるカテゴリにテキストチャンネルを作り、その ID を `discord.logChannelId` に入れて再起動すると、Bot の操作ログ（ロール付与、登録、セットアップ、エラー）がそこに流れます。リモートからの動作確認に使えます

どのコマンドも同名があれば作り直さず権限だけ揃えるので、何度実行しても増えません。
`はじめに-start-here` の本文とウェルカム画面だけは手で書きます。

ワールドのカテゴリは、権限をカテゴリに付けます。フォーラムと雑談はカテゴリの権限に従う（同期）ので、見える人を変えるときはカテゴリだけ変えれば足ります。
ワールドの案内のチャンネル（言語ごとに 4 つ。`ワールド-jp` / `world-en` / `世界-zh` / `월드-ko`）と `更新情報-updates` だけは、サーバーの持ち主専用にするためにチャンネル独自の権限（送信の拒否）を持ちます。Discord は同期していないチャンネルにカテゴリの権限を重ねないので、これらは `setup-world` を再実行して揃えます。

案内のチャンネルを言語ごとに分けているのは、1 つのチャンネルに 4 か国語を並べると長くなり、読みたい言語にたどり着きにくいためです。前の版で作った `ワールド-world` があるカテゴリは、`setup-world` を再実行すると、それが `ワールド-jp` に名前を変えて使い続けられます（投稿と権限は残ります）。

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
| `/vrc-admin setup-info` | INFO カテゴリ（はじめに・お知らせ・登録）を作成 |
| `/vrc-admin setup-community` | コミュニティカテゴリ（雑談 jp/en/zh/ko、sfw-photo、nsfw-photo）を作成 |
| `/vrc-admin setup-world jp: en: visibility: [nsfw:]` | ワールド用カテゴリ（案内 jp/en/zh/ko・更新情報・フィードバック・雑談の lounge）を作成。visibility は 全員 / Supporter / Platinum / Member |
| `/vrc-admin panel` | 実行したチャンネルに登録ボタン付きパネルを投稿（登録チャンネルで実行してピン留め）。既にパネルがあれば、新しく投稿せずに書き換える |
| `/vrc-admin sync` | 今すぐ同期＋公開 |
| `/vrc-admin publish` | JSON を強制再公開 |
| `/vrc-admin lookup user:@x` | 登録状態の確認 |
| `/vrc-admin whois name:<表示名>` | VRChat の表示名から、登録した Discord の人と状態を調べる。Group の参加申請を承認してよいかも出る |
| `/vrc-admin setname user:@x name:<表示名>` | クールダウン無視で名前を設定 |
| `/vrc-admin grant user:@x rank:<n> [days:<n>]` | 手動でランク付与（支援サイトを使わない特例など） |
| `/vrc-admin revoke user:@x` | 手動付与の取り消し |
| `/vrc-admin member-grant user:@x` | メンバーに手動で認定する。在籍日数と同意を待たない（表示名の登録は必要） |
| `/vrc-admin member-revoke user:@x` | メンバーの手動の認定を取り消す（本人のメンバー登録も取り消す） |
| `/vrc-admin ban user:@x [reason:<理由>]` | 支援者・メンバーのリストから外し、Bot が付けるロールを外す（下の「BAN」を参照） |
| `/vrc-admin unban user:@x` | ban を解除する |
| `/vrc-admin hash name:<表示名>` | ハッシュ値の確認（Udon 側デバッグ用） |

## 6b. メンバー登録（任意）

支援とは別の軸で、「サーバーに一定の日数いて、登録を済ませた人」にロールを付ける機能です。支援の有無は見ません。このロールだけに見せるカテゴリと、この人たちだけが入れるワールドを作れます。

条件は 3 つです。

- サーバーに `minDays` 日以上いる（入り直すと数え直し）
- VRChat の表示名を登録済み
- パネルの **メンバー** ボタンで、18 歳以上の確認と注意への同意を済ませた

設定の手順:

1. `/vrc-admin setup-roles` を実行する。`Member` ロールが作られ、`config.jsonc` に貼る `member` の行が表示される
2. `config.jsonc` に `member` を足して、Bot を再起動する（`instance.example/config.jsonc` に例）
3. 登録チャンネルで `/vrc-admin panel` を実行する。既にあるパネルが書き換わり、**メンバー** ボタンが付く
4. メンバーだけに見せるカテゴリは `/vrc-admin setup-world ... visibility:Member` で作る

動き:

- ボタンを押した時点で日数が足りていれば、その場でロールが付く。足りなければ、日数がたったあとの定期の同期で自動的に付く
- 同じボタンから、登録の取り消しもできる
- 管理者は `/vrc-admin member-grant` で、在籍日数と同意を待たずにメンバーに認定できる（表示名の登録は必要）
- サーバーを抜けるとメンバーではなくなる
- メンバーは、支援者と同じリストの `members` に載る（名前は載せず、ハッシュだけ）。ワールド側の使い方は [unity-setup.md](unity-setup.md) を参照

### ワールドの中に Discord の招待 URL を出す

`config.jsonc` に `"links": { "discord": "https://discord.gg/xxxxxxxx" }` を足すと、リストに URL が入り、各ワールドのクレジットのボードに表示されます。URL を変えたいときは、ここを直して Bot を再起動するだけです。招待リンクは、期限なし・回数無制限で作ってください。

### VRChat の Group への参加を受け付ける（任意）

支援者とメンバーが、フレンドでなくても同じインスタンスで遊べるように、VRChat の Group を使う場合の設定です。Group への申請と承認は VRChat の側で行い、Bot は「誰が申請してよい人か」を確かめる手伝いをします。

`config.jsonc` に `"vrcGroup": { "name": "<Group の名前>", "url": "https://vrc.group/XXXX.0000" }` を足して起動し直し、`/vrc-admin panel` でパネルを書き換えると、登録パネルに「グループ」のボタンが出ます。

- ボタンを押せるのは、表示名を登録済みの、支援者かメンバー
- 押した記録は、ログ（Discord のログチャンネルにも流れる）に「グループ参加の希望 <Discord の人>: VRChat の表示名=…」と残る
- Group の側は、参加の方法を「Request Invite」か「Invite Only」にしておく（Open にしない）

入ってもらう流れは、2 通りあります。

| | 流れ |
|---|---|
| 手で招待する（既定） | ボタンを押した人に「持ち主が招待を送ります」と返る。持ち主は、ログに出た表示名の人を VRChat で Group に招待する |
| 自動で招待する | `instance/.env` に `VRC_AUTH_COOKIE` を入れると、Bot が VRChat の API で招待を送る。申請が先に出ていた人は、承認する |

VRChat に直接届いた参加申請は、`/vrc-admin whois name:<申請者の表示名>` で、登録した Discord の人、支援とメンバーの状態、承認してよいかを確かめてから承認します。登録の無い名前や、支援者でもメンバーでもない人は、承認しません。

自動で招待するときの決まり:

- VRChat の API は、公式には案内されていないもの。サポートは無く、予告なく動かなくなることがある。乱用するとアカウントを止められることがあるので、Bot 用に別の VRChat アカウントを使う（持ち主のアカウントは使わない）
- そのアカウントを Group に入れ、招待を管理できるロール（Manage Group Invites）を付ける
- `VRC_AUTH_COOKIE` には、そのアカウントでブラウザから vrchat.com にログインしたときのクッキー `auth` の値を入れる。2 段階認証がメールで届く設定でも、ログイン済みのクッキーなら使える。クッキーには期限があるので、切れたら入れ直す（切れている間は、手で招待する流れに戻る。起動のログに「VRChat の API を使えません」と出る）
- Bot が API を呼ぶのは、ボタンが押されたときだけ。定期的な問い合わせはしない。問い合わせの間隔は 1.5 秒以上空ける
- Group の ID は、`vrcGroup.url` の短いコード（`XXXX.0000`）から、そのアカウントが入っている Group を探して決める。見つからないときは `vrcGroup.id` に `grp_...` を入れる

### コマンドの一覧を管理用のチャンネルに出しておく

`config.jsonc` の `discord.commandsChannelId` に、管理者だけが見えるチャンネルの ID を入れると、使えるコマンドの一覧がそこに出ます。Bot が起動するたびに、今の内容へ書き換えます（コマンドの定義から作るので、手で直す必要はありません）。

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

## BAN

メンバーの認定を取り消すだけだと、本人が登録し直せば戻れます。戻れないようにするには `/vrc-admin ban` を使います。

Bot が行うのは、次の 3 つです。サーバーからの BAN は行いません（Bot に BAN の権限を持たせないため）。必要なら、人が Discord の画面から BAN します。

- 支援が続いていても、支援者・メンバーのどのリストにも載せない。メンバー登録と手動の付与は取り消す
- Bot が付けるロール（Member と、支援者のロール）を外す。支援サイトの Bot が付けるロールには触らない
- 本人の登録し直しを断る。登録していた表示名は記録に残すので、別の Discord アカウントで同じ表示名を登録し直すこともできない

そのほか:

- ワールドへの反映は、公開と再取得を合わせて、遅くとも 20 分ほど
- 支援サイトでの支援は止まらない。必要なら、支援サイト側でもブロックする
- `/vrc-admin lookup` に BAN の日付と理由が出る
- 解除は `/vrc-admin unban`。支援中なら次の同期で支援者に戻り、メンバーは本人が登録し直せば通常の条件で戻れる。サーバーから BAN していた場合は、Discord の画面からも解除する

## 鍵つきのリスト（任意）

公開するリストは、誰でも読めます。鍵なしの形式では、名前を知っている人なら「その人が支援者か、メンバーか」を確かめられます（名前のハッシュを作って、リストにあるかを見る）。クレジットの名前はそのまま載っています。

鍵つきにすると、リストを見ただけでは確かめられなくなります。

- 判定用のハッシュに鍵を混ぜる。鍵を知らないと、名前から同じハッシュを作れない
- クレジットの名前は鍵で暗号化する。ワールドが同じ鍵で元に戻して表示する

鍵は、Bot（`instance/.env` の `LIST_KEY`）とワールド（`SupporterRegistry` の `List Key`）の両方に入れます。鍵は、リポジトリに入るファイルや、公開される場所に書かないでください（GitHub に上げると、誰でも読めます）。ワールドのデータを解析できる人は鍵を取り出せるので、強い秘密にはなりません。「リストの URL を開いただけでは分からない」ところまでの保護です。

### ワールドの上げ直しの時期がずれても動く仕組み

Bot の設定はすぐ変えられますが、ワールドはビルドとアップロードに時間がかかり、全部が同時には切り替わりません。そこで、リストには鍵ごとの区画を並べて載せられるようにしてあります。ワールドは、自分の鍵の区画だけを読みます。

| `.env` の書き方 | リストの中身 | 読めるワールド |
|---|---|---|
| `LIST_KEY` なし | 鍵なしの部分だけ（今までと同じ） | すべて。鍵を入れたワールドも読める |
| `LIST_KEY=鍵A` と `LIST_KEEP_PLAIN=1` | 鍵なしの部分と、鍵 A の区画 | すべて。鍵 A を入れたワールドは、鍵 A の区画を読む |
| `LIST_KEY=鍵A` | 鍵 A の区画だけ | 鍵 A を入れたワールドだけ |
| `LIST_KEY=鍵B,鍵A` | 鍵 B の区画と、鍵 A の区画 | 鍵 A か鍵 B を入れたワールド |
| `LIST_KEY=鍵B` | 鍵 B の区画だけ | 鍵 B を入れたワールドだけ |

どの段階でも、Bot の `.env` を書き換えて起動し直すだけで、次の公開から切り替わります。

### 最初に鍵つきにする手順

1. 鍵を作る。空白とカンマを含まない半角の英数字・記号で 12〜64 文字。例: `node -e "console.log(require('crypto').randomBytes(12).toString('base64url'))"`（16 文字）
2. Bot の `.env` に `LIST_KEY=<鍵>` と `LIST_KEEP_PLAIN=1` を足して、起動し直す。まだ鍵なしの部分も残っているので、どのワールドも今までどおり動く
3. ワールドを 1 つずつ、supporter-gate 0.5.0 以上にし、`SupporterRegistry` の `List Key` に鍵を入れて、アップロードし直す。上げ直したワールドは鍵の区画を読むようになるので、ここで鍵つきの動きを確かめられる
4. 全部のワールドを上げ終えたら、`.env` から `LIST_KEEP_PLAIN` を消して、起動し直す。ここから、リストを見ただけでは分からなくなる

### 鍵を入れ替える手順

1. 新しい鍵を作る
2. Bot の `.env` を `LIST_KEY=<新しい鍵>,<古い鍵>` にして、起動し直す。新旧どちらの鍵のワールドも動く
3. ワールドを 1 つずつ、`List Key` を新しい鍵に替えて、アップロードし直す。時期はばらばらでよい
4. 全部のワールドを上げ終えたら、`.env` を `LIST_KEY=<新しい鍵>` にして、起動し直す。古い鍵では読めなくなる

- 古い鍵が漏れたときは、手順 4 を済ませるまで、古い鍵でも読める状態が続く。急ぐときは、手順 2 を飛ばして新しい鍵だけにする。その場合、上げ直すまでのワールドは「支援者リストを取得できませんでした」になり、限定のワールドには誰も入れない
- 鍵つきをやめるときは、`LIST_KEY` を消して起動し直す。鍵を入れたままのワールドも、鍵なしのリストを読める
- ワールドごとに別の鍵にすることもできる（`LIST_KEY=鍵1,鍵2,鍵3`）。1 つのワールドの鍵だけを入れ替えられる代わりに、リストが鍵の本数だけ大きくなる

### そのほか

- 自分の鍵の区画が無く、鍵なしの部分も空のリストを読んだワールドは、ゲートに「支援者リストを取得できませんでした」と出して、誰も入れない状態になる（黙って全員を支援者でない扱いにはしない）。0.4.2 以前のパッケージのワールドは、鍵つきだけのリストを読むと、全員が支援者でない扱いになる
- 起動のログに、鍵の本数と番号が出る。番号は鍵から作った 16 文字で、リストの区画の `k` と同じもの（鍵そのものではない）
- `/vrc-admin hash` は、鍵つきのときは、最初の鍵を混ぜたハッシュも出す
- 形式の決まりは [bot/src/protect.ts](../bot/src/protect.ts) の先頭にある。別の実装が [tools/protect_list.py](../tools/protect_list.py) にあり、Bot の出力と突き合わせて確かめている

## 登録できる名前

改行・タブなどの制御文字と、文字の向きを変える指定（表示を乱す文字）を含む名前は登録できません。それより前に登録された名前は、クレジットに出すときにだけ、その文字を落とします（判定用のハッシュは登録された名前のまま）。`<` などの記号は登録でき、ワールド側で装飾として解釈されないようにして表示します。
