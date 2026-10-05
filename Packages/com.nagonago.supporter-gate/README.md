# Nago Supporter Gate

支援者だけが入れる VRChat ワールド用のゲートです。プレイヤーの表示名のハッシュを、公開した支援者リスト（JSON）と照合します。

- モード: 公開 / 支援者のみ / 支援者＋支援者が許可した人 / 支援者が在室している間は誰でも
- 承認パネル: 在室中の支援者が、支援者でない人を個別に許可・取り消し
- 通知（Nago Notice を使用）: ロビーへ戻した理由、支援者の退出後のカウントダウン、許可・取り消し、支援者への入室の知らせ
- 住人（支援とは別の軸。設定やリストの中の名前は member）で判定するゲートも作れる。支援者でも、住人でなければ入れない
- クレジットのボード: 支援者の名前の一覧、見ている本人の状態（「あなたはサポーターです」など）、Discord の招待 URL
- Discord の招待 URL のコピー欄と QR コード（VRChat の中ではリンクを開けないため。PC はコピー、スマホは QR）
- 文言は日本語・英語・韓国語・中国語（簡体・繁体）

対象: VRChat Worlds SDK 3.10 以降 / Unity 2022.3。Nago Notice（`com.nagonago.notice`）に依存します。

## 入れ方

1. VCC の Settings > Packages > Add Repository に次の URL を入れる
   `https://mizunago.github.io/nago-gate/vpm.json`
2. プロジェクトの Manage Project で **Nago Supporter Gate** を足す（Nago Notice も一緒に入る）
3. Unity のメニュー `Tools > SupporterGate > Create Scene Setup` を実行する

既にゲートを置いてあるシーンは、`Tools > SupporterGate > Wire Notices (existing scene)` で通知と文言の表を足せます。ボードにゲートもつながります。

ゲートの無い既存のワールドには、`Tools > SupporterGate > Convert Existing World` で、入口の部屋とゲートを後から足せます。

## 版の履歴

- 0.6.0: 「メンバー」の呼び名を「住人 / Resident」に変えた（支援サイトの「メンバーシップ」と紛れるため）。ワールドの表示（「あなたは住人です」、住人だけのゲートの文など）が変わる。設定とコードの名前（`Use Member List`、リストの `members`、文言のキーの `.member`）はそのまま。案内のパネルに「URL をコピー・QR」のボタンを足した。押すと、Discord の招待 URL を入れた入力欄（PC で Ctrl+A、Ctrl+C でコピーできる）、QR コード（スマホで読める）、Discord での開き方（ブラウザに貼る、アプリの「＋」から「サーバーに参加」）が出る。新しく作る一式には付く。既にあるシーンには `Tools > SupporterGate > Add Discord Copy Panel (existing scene)` で足せる（案内の文は、ボタンの分だけ上に詰める）。QR の画像は、エディタがリストの `links.discord` を読んで作る（`Assets/NagoSupporterGate/DiscordInviteQR.png`）。招待 URL を変えたら `Tools > SupporterGate > Update Discord QR` で作り直す。リストの URL と QR の URL が違うときは、古い QR を出さない。メニューの `Convert Existing World` の「メンバーだけが入れるワールドにする」は「住人だけが入れるワールドにする」になった
- 0.5.2: `Wire Notices (existing scene)` が、ゲートの無いシーンでも動くようにした。公開のワールドに Registry とボードだけを置いた場合も、ボードに文言の表・通知・文字の自動縮小を配線する（前の版は「シーンに SupporterGate がありません」と出して止まっていた）
- 0.5.1: 承認パネルの文字が枠からはみ出すのを修正。見出し・名前・状態・ボタンは 1 行で出し、枠の幅に収まるまで自動で小さくする（英語の長い見出しや、全角 15 文字の名前で 2 行になり、ボタンや行に重なっていた）。ロビーのボタンの文字も同じ扱いにした。上げたあとに `Wire Notices (existing scene)` をもう一度実行すると、既にあるシーンにも効く
- 0.5.0: 鍵つきのリストに対応。Bot に `LIST_KEY` を設定すると、公開するリストの判定用のハッシュに鍵が混ざり、クレジットの名前が暗号化される（リストを見ただけでは、誰が支援者・住人かを確かめられなくなる）。ワールド側は、`SupporterRegistry` の `List Key` に同じ鍵を入れる。リストには鍵ごとの区画が入るので、鍵を入れ替えるときに、ワールドを上げ直す時期がずれても動く。`List Key` を入れたワールドは、鍵なしのリストもそのまま読める。`List Key` を空のまま使う分には、今までと動きは変わらない。クレジットを自分で並べるギミック向けに `_AreCreditsReady()` を追加
- 0.4.2: 本人の状態と Discord の案内を、名前の一覧（Special Thanks）とは別のパネル（InfoPanel）に分けた。既にあるシーンには `Tools > SupporterGate > Add Info Panel (existing scene)` で足せる。通知の文の改行も直した（日本語と中国語で、1〜2 文字だけ次の行に落ちる折り返しがあった）。ロビーの「入場する」「ロビーへ戻る」のボタンを今の言語で出す。メンバーで判定するゲートでは、承認パネルの文も「メンバー」に言い換える。本人向けの表示（ボードの「あなたは○○です」と、ロビーのパネルの「あなた」）の役割の名前に色を付けた（支援者はティアの色、メンバーは緑）。名前の一覧は、人数が多いとページに分けて自動で切り替える（既定は 1 ページ 30 人、8 秒ごと）。名前の途中では折り返さず、枠に収まるまで文字を自動で小さくする。表示を色分けした: 見出し（「あなたの状態」「ご案内」、承認パネルの題）は薄い紫、項目名と補足は灰色、入場できる・許可済みは緑、入場できない・取得できないは赤。ボードの題と、打ち込んでもらう招待 URL は大きく太く出す。色は文字に埋め込むタグで付けるので、既にあるシーンにも、更新するだけで効く。上げたあとに `Wire Notices (existing scene)` をもう一度実行すると、既にあるシーンのボタンにも効く。文字の自動縮小も、このときに付く
- 0.4.1: ボードの「メンバー」の呼び名が、日本語以外でも日本語のまま出ていたのを修正（文言の表に `credits.you.member` が抜けていた）
- 0.4.0: `Convert Existing World` を追加。ゲートの無いワールドに、入口の部屋（何もない四角い部屋）とゲート一式を足す。今のスポーン地点は「入場したあとに出る場所」として引き継ぐ
- 0.3.0: リストの `members`（メンバー）と `links`（案内用のリンク）に対応。Gate に `Use Member List`、Registry に `_IsMember` `_IsLocalMember` `_HasMemberList` `_GetLink` `_GetTierId` を追加。クレジットのボードに、本人の状態と Discord の招待 URL を表示。上げたあとに `Wire Notices (existing scene)` をもう一度実行すると、ボードに「入れないとき」の表示が出るようになる
- 0.2.0: VPM パッケージ化、共通の通知、理由の通知とカウントダウン、多言語

使い方と設定は、リポジトリの `docs/unity-setup.md` を見てください。
