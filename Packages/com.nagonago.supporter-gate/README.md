# Nago Supporter Gate

支援者だけが入れる VRChat ワールド用のゲートです。プレイヤーの表示名のハッシュを、公開した支援者リスト（JSON）と照合します。

- モード: 公開 / 支援者のみ / 支援者＋支援者が許可した人 / 支援者が在室している間は誰でも
- 持ち主だけのワールド（実験用など）: 支援者のリストを使わず、持ち主と、持ち主がインスタンスの中で許可した人だけが入れる
- 承認パネル: 在室中の支援者が、支援者でない人を個別に許可・取り消し
- 通知（Nago Notice を使用）: ロビーへ戻した理由、支援者の退出後のカウントダウン、許可・取り消し、支援者への入室の知らせ
- 住人（支援とは別の軸。設定やリストの中の名前は member）で判定するゲートも作れる。支援者でも、住人でなければ入れない
- クレジットのボード: 支援者の名前の一覧、見ている本人の状態（「あなたはサポーターです」など）、Discord の招待 URL
- Discord の招待 URL のコピー欄と QR コード（VRChat の中ではリンクを開けないため。PC はコピー、スマホは QR）
- テスト用のパネル: Unity の Play モードと SDK の Build & Test のときだけ出て、自分の扱い（ランク・住人）を切り替えて確かめられる
- 入れない人に見せない仕組み（ContentGuard）: 入れない人の画面では、ワールド本体を隠し、音を消す。同期する物や動く物は、止めずに見た目だけ消す。隠す物はメニューで自動で選ぶ
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

- 0.9.1: 入れない人に見せない仕組み（ContentGuard）の直し。入れる人の画面で、起動時に止める物（roots）をいったん止めてから戻していたのをやめた。これで、持ち主の画面で NPC の声が入口でも大きく聞こえる（距離の減衰が効かない）ことがあった（adult_room で発覚）。入れるかがまだ決まっていない間（リストを読み込み中）は、止めずに見た目（Renderer・Canvas の enabled）と音（mute）だけを消し、入れないと決まってから止める。入れると決まったら、見た目と音を戻すだけで、止める物には触らない。ガードはゲートの Start のあとに動く（持ち主だけのワールドでは、持ち主には最初から何もしない）。音源（AudioSource）を含む部分は止める物にしない（止めずに mute する）。隠している間は、毎フレーム mute をかけ直す（動画プレイヤーなどが戻すため。今まではゲートの評価のたび）。前の版でガードを作ったシーンは、`Tools > SupporterGate > Set Up Content Guard (auto)` をもう一度実行する（止める物の下の見た目の一覧を作るため）
- 0.9.0: 持ち主だけのワールドを作れるようにした（実験用のワールドを、オンラインの状態の変え忘れなどで見つけられても、入れないように）。Gate の Owners Only を ON にすると、支援者のリストを使わず、Owner Display Names の人だけを「支援者」として扱う。Mode が SupporterApproval なら、在室の持ち主が承認パネルで許可した人も入れる。リストの読み込みを待たないので、持ち主はすぐ入れる。表示の「支援者」は「持ち主」になる（文言の表のキー + ".owner"）。Registry の No List を ON にすると、リストを読まない。`Tools > SupporterGate > Convert Existing World` に「持ち主と、許可した人だけが入れるワールドにする」「持ち主だけが入れるワールドにする」を足した。どちらも、どこのワールドか分かる板（支援者の名前の一覧と、Discord の招待のある案内のパネル）を隠し、入れない人に見せない仕組み（ContentGuard）も作る。`Tools > SupporterGate > Owner Names...` で持ち主の表示名をこのパソコンに覚えさせると、ゲートを作るときに Owner Display Names が空なら入る（ほかの道具からは `SupporterGateSetup.SetDefaultOwnerNames()`）
- 0.8.0: 入れない人に見せない仕組み（ContentGuard）を足した。あやしいけんきゅうじょのワールド側の仕組み（NonResidentGuard）を取り込んだもの。入れない人の画面では、ワールド本体を止め（SetActive）、同期する物（同期の設定が None でない Udon・ObjectSync・Pickup・Station）と、ほかのギミックが切り替える物は、止めずに Renderer と Canvas だけを消す（入れない間は毎フレーム消し直す）。音源は mute にする。入れるようになったら元に戻す。ゲートの Content Roots と違い、入れる・入れないが変わったときだけ切り替えるので、隠す物が多くても毎秒の引っかかりにならない。`Tools > SupporterGate > Set Up Content Guard (auto)` で、隠す物を自動で選ぶ（ワールド全体に効く物・入口の部屋・通知の板は対象にしない。ほかに外したい物はガードの Keep に入れる。何度実行してもよい）。`Convert Existing World` は、最後にこれも行う。ワールド本体を変えたら、もう一度実行する
- 0.7.0: テスト用のパネルを足した。Unity の Play モード（ClientSim）と SDK の Build & Test のときだけ出て、自分のランクと住人の扱いを、リストの結果の代わりに切り替えられる。ゲート・ボード・住人だけに見せる物が、本物と同じ流れで切り替わる。持ち主の名前で入っていても、上書き中は持ち主の特別枠を使わないので、入れない人の見え方も確かめられる。Build & Test では Gate の Owner Display Names の人だけが使え、公開用のビルド（Build and Upload）ではパネルごと取り除く。新しく作る一式には付く。既にあるシーンには `Tools > SupporterGate > Add Test Panel (existing scene)` で足せる。ロビーの板の「ロビーへ戻る」ボタンをやめた（ロビーの中にあり、押しても何も変わらなかった。中からは VRChat のメニューの Respawn で戻れる）。既にあるシーンのボタンは `Tools > SupporterGate > Remove Return Button (existing scene)` で外せる。入れなくなった人をロビーへ戻す処理は、今までどおり動く
- 0.6.2: Discord のコピー欄を、板（案内のパネル）の大きさに対する比率で置くようにした。0.6.1 までは 900×600 の板を前提に決まった位置に置いていたので、小さな板では欄がはみ出していた。QR は、板の形に合わせて正方形を保つ。ボタンの場所を空けるとき、案内の文の欄の下の辺だけを上げ、上の辺は動かさないようにした（0.6.1 は欄ごと上へずらしていたので、額縁に合わせた文が上に寄った）。既に欄を足したシーンでも、`Tools > SupporterGate > Add Discord Copy Panel (existing scene)` をもう一度実行すると、板の大きさに合わせて置き直す。0.6.1 で上へずれた文の欄は、元の位置に戻してから実行する
- 0.6.1: QR の画像のファイルが無いとき（消した、Git で追跡していないなど）は、QR を出さないようにした（白い四角だけが出ていた）。そのときは入力欄と説明だけが出る。`Tools > SupporterGate > Update Discord QR` で作り直せば戻る。エディタを外から動かす道具向けに、ダイアログを出さずに結果の文を返す `SupporterGateSetup.AddDiscordCopyPanelsSilent()` を足した（`UpdateDiscordQr(null)` も、ダイアログを出さずに文を返す）
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
