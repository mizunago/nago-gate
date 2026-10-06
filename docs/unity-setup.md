# Unity 側セットアップ

対象: VRChat Worlds SDK 3.10.x / Unity 2022.3 / UdonSharp（SDK 同梱）/ TextMeshPro

## パッケージの入れ方（VPM）

配布は VPM パッケージです。unitypackage は廃止しました。

1. VCC（VRChat Creator Companion）の Settings > Packages > Add Repository に次の URL を入れる
   `https://mizunago.github.io/nago-gate/vpm.json`
2. プロジェクトの Manage Project で **Nago Supporter Gate** を足す。依存する **Nago Notice**（共通の通知）も一緒に入る
3. 更新するときは、VCC の同じ画面で新しい版を選ぶ

| パッケージ | 中身 |
|---|---|
| `com.nagonago.supporter-gate` | 支援者ゲート（Registry / Gate / 承認パネル / クレジット） |
| `com.nagonago.notice` | 共通の通知。ゲート以外のギミックからも使える（[README](../Packages/com.nagonago.notice/README.md)） |

以前に `Assets/SupporterGate` をコピーして使っていたプロジェクトは、[古い置き方からの入れ替え](#古い置き方からの入れ替え) を見てください。

## ワールドへの組み込み

1. `Tools > SupporterGate > Create Scene Setup` を実行すると、次が生成される

```
NoticeHub             共通の通知（シーンに 1 つ。ContentRoot の外に置く）
SupporterGate System
├─ Registry        SupporterHash + SupporterRegistry   ← Data Url を設定
├─ Gate            SupporterGate                       ← Mode などを設定
├─ Texts           文言の表（多言語の JSON）
├─ LobbySpawn      ロビーの戻り先
├─ ContentSpawn    入場ボタンで飛ぶ先
├─ ContentRoot     ここにワールド本体を入れる（非許可時はローカルで非表示）
├─ ContentZone     本体エリアを覆う Trigger（入り込んだ非許可者をロビーへ戻す）
├─ LobbyPanel      状態表示・入場ボタン
├─ ApprovalPanel   支援者専用の入場許可パネル
├─ CreditsBoard    支援者クレジット
└─ TestPanel       テスト用のパネル（シーンでは非表示。Play モードと Build & Test だけに出る）
```

ロビーの板には「ロビーへ戻る」ボタンを置かない（0.7.0 から）。ロビーの中にあって、押しても何も変わらなかったため。中からは VRChat のメニューの Respawn で戻れる（スポーン地点がロビーのため）。前の版で作ったシーンのボタンは、`Tools > SupporterGate > Remove Return Button (existing scene)` で外せる。入れなくなった人をロビーへ戻す処理は、ボタンとは別に動く。

2. `Registry` の **Data Url** に Bot の公開 URL を入れる
3. `Gate` の設定

| 項目 | 内容 |
|---|---|
| Mode | `Open` 公開 / `SupportersOnly` 支援者のみ / `SupporterApproval` 支援者＋許可した人 / `SupporterPresence` 支援者在室中は誰でも |
| Required Rank | この値以上を支援者扱い（プラチナ限定なら 2 など） |
| Use Member List | ON にすると、支援のランクではなく住人で判定する（下の「住人だけが入れるワールド」） |
| Owners Only | ON にすると、支援者のリストを使わず、Owner Display Names の人だけを支援者として扱う（下の「持ち主だけのワールド」） |
| Require Supporter Activation | ON にすると、支援者が「エリアを有効化」を押すまで非支援者は入れない |
| No Supporter Grace Seconds | 支援者が全員いなくなってから非支援者を戻すまでの秒数 |
| Fail Open When Registry Unavailable | リスト取得失敗時に非支援者を通すか。限定ワールドでは OFF |
| Owner Display Names | 持ち主の特別枠（下の節） |
| Content Roots | 非許可時に非表示にするオブジェクト。**同期オブジェクト・ContentZone・NoticeHub を含めない**。隠す物が多いワールドや、同期する物があるワールドでは、代わりに下の「入れない人に見せない仕組み」を使う（Content Roots は空になる） |
| Notice / Texts | 共通の通知と文言の表。セットアップが配線する |
| Notify … | 通知の種類ごとの ON/OFF（下の節） |

4. `ContentRoot` の下にワールド本体を配置し、`ContentZone` の BoxCollider を本体エリアに合わせる
5. ロビー側の VRC_SceneDescriptor スポーンは `LobbySpawn` の位置に置く
6. UI パネルは好きな位置へ移動してよい。見た目は uGUI なので自由に差し替え可

## モード別の使い分け

| ワールド | Mode | 補足 |
|---|---|---|
| 公開ワールド（特典のみ） | `Open` | CreditsBoard だけ使う。Gate / Zone は削除してよい |
| アーリーアクセス | `SupportersOnly` | |
| 限定ワールド | `SupporterApproval` | 支援者のフレンドが入ってきたら、支援者が ApprovalPanel で個別に許可 |
| 支援者が「開ける」タイプ | `SupporterPresence` + Require Activation | 支援者がいる間だけ全員 OK |

## ゲートの無い既存のワールドに、後から足す

入口の部屋が無く、スポーンしたらすぐ本体、というワールド向けです。`Tools > SupporterGate > Convert Existing World` から、入れる人に合わせて選びます。

| メニュー | 入れる人 | ゲートの設定 |
|---|---|---|
| 住人だけが入れるワールドにする | 住人 | Mode: SupportersOnly、Use Member List: ON |
| 支援者だけが入れるワールドにする | 支援者 | Mode: SupportersOnly |
| 支援者と、許可した人が入れるワールドにする | 支援者と、在室の支援者が許可した人 | Mode: SupporterApproval |

実行すると、次のことが起きます。ワールド本体のオブジェクトは動かしません。

1. ゲート一式（Registry・Gate・パネル・ボード・文言の表・通知）を作る
2. 入口の部屋 `LobbyRoom`（床・天井・壁 4 枚と明かりだけの四角い部屋）を、ワールドの 40 m 下に作る
3. スポーン地点を入口の部屋に替える。元のスポーン地点（複数あれば 1 つ目）は、**入場したあとに出る場所**（ContentSpawn）として引き継ぐ
4. 落下時のリスポーンの高さを、入口の部屋より下に下げる
5. ワールド本体の範囲を覆う判定（ContentZone）を置く。入れない人が中に入っても、入口の部屋へ戻る

入れる人は、入口の部屋の「入場する」ボタンで元のスポーン地点へ出ます。入れない人は、入口の部屋から先へ進めません。

実行したあとにやること:

1. `Registry` の **Data Url** に、支援者リストの URL を入れる
2. 自分が入れるように、`Gate` の **Owner Display Names** に自分の VRChat の表示名を入れる
3. 入口の部屋の見た目を整える（`LobbyRoom` ごと動かせる。スポーン地点とパネルは部屋の子になっている）

元に戻すときは Undo（Ctrl+Z）を使います。既にゲートがあるシーンでは実行できません。

## 住人だけが入れるワールド

表の呼び名は「住人 / Resident」です。設定とリストの中の名前は member のままです（0.6.0 より前の版では「住人」と出していました）。

Bot の住人の機能（[discord-setup.md](discord-setup.md) の 6b）を使うと、支援とは別に「申請して認定された人だけ」が入れるワールドを作れます。住人は、支援者と同じリストの `members` に載ります（名前は載らず、ハッシュだけ）。

1. `Registry` の **Data Url** は、ほかのワールドと同じ支援者リストの URL のままにする
2. `Gate` の **Use Member List** を ON、Mode を `SupportersOnly` にする

これで、住人だけが入れます。支援者でも、住人でなければ入れません。持ち主の特別枠は、このモードでも通ります。ゲートの表示は「支援者」が「住人」に置き換わります（文言の表の、キーの末尾に `.member` が付いた文）。

## クレジットのボードと、案内のパネル

名前の一覧（Special Thanks）と、見ている本人向けの表示は、別のパネルに分かれています。どちらも `SupporterCreditsBoard` が書きます。

| パネル | 表示 | 出る条件 |
|---|---|---|
| `CreditsBoard` | 支援者の名前の一覧（ティアごと） | クレジット表示に同意した支援者。住人の名前は出ない |
| `InfoPanel` | 「あなたは ○○ です」 | 見ている本人が支援者か住人のとき。役割の名前に色が付く（支援者はティアの色、住人は緑） |
| `InfoPanel` | 「支援者・住人の登録は見つかりません」 | どちらでもないとき |
| `InfoPanel` | 「あなたはこのワールドにアクセスする権限を持っていません」 | ボードの `Gate` にゲートが入っていて、本人が入れないとき |
| `InfoPanel` | 「支援と住人の申請の方法は、Discord で案内しています」と招待 URL | Bot の設定の `links.discord` に URL があるとき。変えれば全ワールドの表示が変わる |
| `InfoPanel` の下 | 「URL をコピー・QR」のボタン | 同上。押すと、招待 URL の入力欄・QR コード・Discord での開き方が、案内のパネルの上に重なって出る（下の「Discord のコピー欄と QR」） |

- ティアの呼び名は、文言の表の `credits.tier.<ティアの id>` から引きます。無ければリストの label を使います
- `InfoPanel` は、ボードの `Info Text` につながっています。前の版で作ったシーンには、`Tools > SupporterGate > Add Info Panel (existing scene)` で足せます（ボードの左隣に出るので、好きな場所へ動かす）
- `InfoPanel` が無いシーンでは、本人の状態は名前の一覧の題の下に、案内は一番下に出ます
- 住人向けの物が無いワールド（公開ワールドで、支援者の特典だけがある場合など）では、ボードの `Show Member Status` を OFF にします。本人が住人でも「住人」とは出さず、登録が無い人には「支援者の登録は見つかりません」、案内は「支援の方法は、Discord で案内しています」になります。既定は ON です

### Discord のコピー欄と QR

VRChat の中ではリンクを開けないので、招待 URL を見て打ち込むのは大変です。案内のパネルの「URL をコピー・QR」を押すと、次が出ます。

| 出るもの | 使い方 |
|---|---|
| 招待 URL の入力欄 | PC では、欄を押して Ctrl+A、Ctrl+C でコピーできる。書き換えても、次に見るときは URL に戻る |
| QR コード | スマホのカメラで読む |
| Discord での開き方 | ブラウザのアドレス欄に貼る。アプリなら、左の「＋」を押し、「もう招待されていますか？」の下の「サーバーに参加」を押して貼る |

- 新しく作る一式には付きます。前の版で作ったシーンには、`Tools > SupporterGate > Add Discord Copy Panel (existing scene)` で足せます。案内の文は、ボタンの分だけ上に詰めます。2 回実行しても増えません
- QR の画像は、エディタが `Registry` の Data Url のリストを読み、`links.discord` から作ります（`Assets/NagoSupporterGate/DiscordInviteQR.png`）。リストを読めないときは、`DiscordInvite` の `SupporterInviteLink` の **Qr Url** に入れた URL で作ります
- 招待 URL を変えたら、`Tools > SupporterGate > Update Discord QR` で QR を作り直して、ワールドを上げ直します。リストの URL と QR の URL が違う間は、古い招待を読ませないよう QR を出しません（入力欄と説明は出ます）
- リストに招待 URL が無いときは、ボタンも出ません

### 表示の色

どのパネルも、同じ意味には同じ色を使います。色は `SupporterRegistry` の定数（`ColorHeading` など）で、自作のギミックからも使えます。

| 色 | 使う場所 |
|---|---|
| 薄い紫 `#C9B8FF` | 見出し（「あなたの状態」「ご案内」、承認パネルの題） |
| 灰色 `#AEB4BE` | 項目名（「モード:」など）と補足（読み込み中、登録が見つからない、未許可、ページの番号） |
| 緑 `#7CFC9A` | 入場できる、許可済み |
| 赤 `#FF8A80` | 入場できない、リストを取得できない、このワールドに入る権限が無い |
| ティアの色（リストの `tiers[].color`） | 支援者の呼び名（「サポーター」「プラチナサポーター」）と、名前の一覧のティアの見出し |
| 薄い緑 `#9BE7A8` | 住人の呼び名 |

打ち込んでもらう招待 URL は、説明の文より大きく太く出します。色は、文字に埋め込むタグで付けているので、前の版で作ったシーンでも、パッケージを更新するだけで変わります。

## 鍵つきのリスト（任意）

Bot に `LIST_KEY` を設定すると、公開するリストが鍵つきになります（何のためか、切り替えと鍵の入れ替えの手順は [discord-setup.md](discord-setup.md) の「鍵つきのリスト」）。ワールド側ですることは 1 つだけです。

- `SupporterRegistry` の `List Key` に、Bot と同じ鍵を入れて、アップロードし直す（supporter-gate 0.5.0 以上）

動き:

- リストには、鍵ごとの区画が入っています。ワールドは、自分の鍵の区画を読みます。鍵を入れ替えている間は、Bot が新旧 2 本の区画を載せるので、上げ直す前のワールドも、上げ直したあとのワールドも動きます
- `List Key` を入れたワールドは、鍵なしのリストもそのまま読めます。Bot を切り替える前に、先にワールドを上げておけます
- クレジットの名前は、読み込んだあとに元に戻します。1 フレームに少しずつ進め（1 フレームあたり 3 ミリ秒ほど）、130 人で 1〜2 秒かかります。その間、ボードは「読み込み中...」と出ます。入場の判定は待ちません。リストの中身が変わらなければ、再取得のたびにやり直すことはありません
- 自分の鍵の区画が無く、鍵なしの部分も空のときは、リストを取得できなかったときと同じ扱いになります（限定のワールドでは誰も入れない。Console に理由が出る）
- 鍵はシーンに保存されます。ワールドのプロジェクトを公開のリポジトリに置く場合は、鍵が入ったシーンを公開しないでください

## 通知

Gate の `Notice` に NoticeHub が入っていると、本人の画面に次の通知が出ます。種類ごとに Gate の Inspector で切れます。

| 通知 | 出る相手 | 設定 |
|---|---|---|
| ロビーへ戻した理由（許可の取り消し、支援者の退出から時間切れ など）と、入れなかった理由 | 戻された人 | Notify Return Reason |
| 支援者が全員退出したあとの予告「あと m:ss でロビーに戻ります」。支援者が戻ると消える | 中にいる非支援者 | Notify Countdown |
| 入場が許可された／取り消された | 許可された人 | Notify Approval Change |
| 支援者でない人が入室した（承認パネルで許可できます） | 支援者 | Notify Supporter Of Guests |

文言は `Texts`（`SupporterGateTexts.json`）にあり、VRChat の言語設定に合わせて日本語・英語・韓国語・中国語（簡体・繁体）で出ます。状態表示と承認パネルの文も同じ表から出ます。

既にゲートを置いてあるシーンには、`Tools > SupporterGate > Wire Notices (existing scene)` で通知と文言の表を足せます。何度実行しても増えません。

## 持ち主の特別枠（ownerDisplayNames）

Gate の `Owner Display Names` に VRChat の表示名（完全一致）を入れると、その人は支援者のリストに無くても支援者（Required Rank）と同じに入場・許可ができる。クレジットには出ない。ワールドの持ち主・運営用。

持ち主をクレジットに出したくない場合は、Bot 側の支援者リストにも載せないこと（テスト用のロールを付けたままにしない）。

## 他のギミックからランクを使う

```csharp
[SerializeField] private SupporterRegistry registry;

void Start() { registry._RegisterListener(this); }

public void _OnRegistryUpdated()
{
    int rank = registry._GetLocalRank();     // -1 未判定 / 0 一般 / 1 以上 ランク
    Color c = registry._GetTierColor(rank);  // 階級プレートの色などに
}
```

- `_GetRank(VRCPlayerApi)` で他プレイヤーのランクも取れます
- `_IsMember(VRCPlayerApi)` / `_IsLocalMember()` で住人かどうか、`_GetLink("discord")` で招待 URL が取れます
- クレジットの名前を自分で並べるときは、`_AreCreditsReady()` が true になってから `_GetCreditCount()` / `_GetCreditName(i)` / `_GetCreditRank(i)` を読みます（鍵つきのリストでは、読み込みの少しあとに揃う。揃ったときにも `_OnRegistryUpdated` が呼ばれる）
- `SupporterGate._IsLocalAllowed()` で入場可否を参照できます
- 通知を自分のギミックから出す方法は Nago Notice の README を参照

## 持ち主だけのワールド（実験用など）

実験用に上げたワールドを、オンラインの状態の変え忘れなどで見つけられても、入れないようにする設定です。支援者のリストは使いません。

| メニュー（Convert Existing World の下） | 入れる人 |
|---|---|
| 持ち主と、許可した人だけが入れるワールドにする | 持ち主と、在室の持ち主が承認パネルで許可した人（持ち主が抜けると、猶予のあとロビーへ戻る） |
| 持ち主だけが入れるワールドにする | 持ち主だけ |

- 持ち主は Gate の **Owner Display Names**（VRChat の表示名の完全一致）。`Tools > SupporterGate > Owner Names...` で、このパソコンに名前を覚えさせると、ゲートを作るときに自動で入る（空のままだと誰も入れない）
- Gate の **Owners Only** と、Registry の **No List** が ON になる。リストの読み込みを待たないので、持ち主はすぐ入れる
- 表示の「支援者」は「持ち主」になる（ロビーの板・承認パネル・通知）
- どこのワールドか分からないように、支援者の名前の一覧と、案内のパネル（Discord の招待）は隠す
- 入れない人に見せない仕組み（下の ContentGuard）も作る。入れない人には、ワールドの中身が見えず、音も聞こえない
- テスト用のパネル（Build & Test）で、「ランクなし」を押すと、持ち主のままで入れない人の見え方を確かめられる

## 入れない人に見せない仕組み（ContentGuard）

入れない人（支援者・住人でない人）の画面で、ワールド本体を見せない・聞かせない仕組みです。ドローンのカメラなどで中を覗かれても、何も見えません。ワールド本体を置いたあとに `Tools > SupporterGate > Set Up Content Guard (auto)` を実行すると、ゲート一式の下に `ContentGuard` を作り、隠す物を自動で選びます。`Convert Existing World` は、最後にこれも行います。

| 隠し方 | 対象 | 入れない間 |
|---|---|---|
| 止める（SetActive） | 下の物を含まない部分木の根 | GameObject ごと止める |
| 見た目だけ消す | 止められない物の Renderer と Canvas（最初は非アクティブの物も含む） | enabled を false にし、毎フレーム消し直す（Animator やスクリプトが戻すため） |
| 音を消す | ワールドの AudioSource | mute にし、ゲートの評価のたびにかけ直す（動画プレイヤーなどが戻すため） |

止められない物（止めると壊れるので、見た目だけ消す）:

- 同期する物: 同期の設定が None でない Udon（NoVariableSync を含む。ネットワークのイベントを受けられるため）・VRC Object Sync・VRC Pickup・VRC Station
- ほかのギミックが SetActive で切り替える物: Udon の変数・Animator のクリップ・Timeline・UI のイベントから参照される GameObject
- 外の Udon から呼ばれる Udon を含む部分（止めると、呼ぶ側が困る）
- ワールド全体に効く物を含む部分: VRC Scene Descriptor・カメラ・ライト・ポストエフェクト・Light Volumes・Bakery のライトマップの保存役など（照明をまとめた親も止めず、中を見ていく）

対象にしない物: ゲート一式（入口の部屋・パネル。ただし Gate の Content Roots は中身として扱う）・通知の板・EditorOnly・ガードの **Keep** に入れた物。入口の部屋をゲート一式の外に作った場合や、止めると困る管理役があれば、Keep に入れてからもう一度実行する（Keep は選び直しても残る）。

- 入れる・入れないが変わったときだけ切り替える。ゲートの Content Roots のように毎秒すべてを調べ直さないので、隠す物が多くても引っかからない
- 入れるようになったら、入れないに変わった時点の値（表示と mute）に戻す
- リストの読み込みが終わるまでの数秒は、入れる人にも隠れる（入口の部屋にいる間）
- ワールド本体を変えたら、もう一度実行する。前の版の Gate の Content Roots は、ガードが引き継いで空にする

## テスト用のパネル（Play モードと Build & Test だけ）

ロビーの板の右に、テスト用のパネルが付く（シーンでは非表示）。ボタンで、自分のランク（なし・いちばん下・いちばん上）と、住人かどうかを、リストの結果の代わりに使う。ゲート・ボード・住人だけに見せる物（`_IsLocalAllowed()` や `_IsLocalMember()` を見ている物）が、本物と同じ流れで切り替わる。「リストどおりに戻す」で元に戻る。

| 場面 | パネル | 使える人 |
|---|---|---|
| Unity の Play モード（ClientSim） | 出る | 誰でも |
| SDK の Build & Test | 出る | Gate の Owner Display Names の人だけ（パネルの Owner Only を OFF にすると誰でも） |
| Build and Upload（公開用のビルド） | パネルごと取り除く | - |

- 持ち主の名前で入っていても、上書き中は持ち主の特別枠を使わない。「ランクなし」と「住人ではない」を押すと、入れない人の見え方を確かめられる
- 上書きは自分の画面だけに効く。Build & Test で 2 つのクライアントを開いても、相手の画面ではリストどおりに見える
- 出し分けは、SDK がビルドの前に立てる印（Build & Test かどうか）で行う。印を読めないときは、取り除く側に倒す
- 前の版で作ったシーンには、`Tools > SupporterGate > Add Test Panel (existing scene)` で足す。2 回実行しても増えない

## 動作確認

1. Bot で `/vrc register` した名前と、Unity 側 `Registry` の Debug Mode を ON にして Play（ClientSim）
2. Console に `<表示名> -> <hash> rank=N` が出る。`/vrc-admin hash` の値と一致すれば照合 OK
   （ClientSim の表示名は ClientSim 設定で変えられます）
3. 通知の言語は、NoticeHub の `Debug Language Override`（ja / en / ko / zh-CN / zh-TW）で確かめられます。公開時は空に戻す
4. 2 アカウントで承認パネルの同期を確認（Build & Test の 2 クライアントは同じ表示名になるので、持ち主の特別枠を使う場合は別アカウントが必要）

## 古い置き方からの入れ替え

`Assets/SupporterGate` をコピーして使っていたプロジェクト向け。スクリプトとプログラムアセットの GUID は同じなので、**古いフォルダを先に消してから**パッケージを入れれば、シーンの参照は切れません。

1. Unity を閉じる
2. `Assets/SupporterGate` フォルダと `Assets/SupporterGate.meta` を消す（中身はパッケージに入っている）
3. VCC で Nago Supporter Gate を足す
4. Unity を開き、UdonSharp のコンパイルが終わるのを待つ
5. `Tools > SupporterGate > Wire Notices (existing scene)` を実行する

順番を逆にしない（古いフォルダが残ったままパッケージを入れない）。同じ GUID のファイルが 2 か所にあると、Unity はパッケージ側の `.meta` を新しい GUID に書き換えます。型の重複（CS0433）のエラーが出て、古いフォルダを消したあとにシーンの参照が切れます。

そうなったときの戻し方:

1. Unity を閉じる
2. `Packages/com.nagonago.supporter-gate` と `Packages/com.nagonago.notice` を消す
3. `Assets/SupporterGate` も消す
4. VCC でパッケージを入れ直す（zip から展開されるので、元の GUID に戻る）
5. `Packages/com.nagonago.supporter-gate/Runtime/SupporterGate.cs.meta` の guid が `4cfc2f515993e9a41b214921b0f1ef28` であることを確かめてから Unity を開く

`SupporterGateProgramAssetGenerator`（新しい U# スクリプトにプログラムアセットを自動で作る補助）はパッケージに入っていません。使っていた場合は、自分のプロジェクトの Editor フォルダに置いてください。

## 注意

- `Content Roots` に同期オブジェクト（VRC_ObjectSync、同期 Udon）を入れると、非表示中に同期が壊れます。見た目だけのオブジェクトにしてください
- `SupporterRegistry` はシーンに 1 つ。複数のゲートやボードから共有できます
- JSON の再取得は既定 600 秒。短くしすぎると String Loading のレート制限（5 秒に 1 回）と CDN キャッシュの都合で意味がありません
