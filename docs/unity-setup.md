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
└─ CreditsBoard    支援者クレジット
```

2. `Registry` の **Data Url** に Bot の公開 URL を入れる
3. `Gate` の設定

| 項目 | 内容 |
|---|---|
| Mode | `Open` 公開 / `SupportersOnly` 支援者のみ / `SupporterApproval` 支援者＋許可した人 / `SupporterPresence` 支援者在室中は誰でも |
| Required Rank | この値以上を支援者扱い（プラチナ限定なら 2 など） |
| Use Member List | ON にすると、支援のランクではなくメンバーで判定する（下の「メンバーだけが入れるワールド」） |
| Require Supporter Activation | ON にすると、支援者が「エリアを有効化」を押すまで非支援者は入れない |
| No Supporter Grace Seconds | 支援者が全員いなくなってから非支援者を戻すまでの秒数 |
| Fail Open When Registry Unavailable | リスト取得失敗時に非支援者を通すか。限定ワールドでは OFF |
| Owner Display Names | 持ち主の特別枠（下の節） |
| Content Roots | 非許可時に非表示にするオブジェクト。**同期オブジェクト・ContentZone・NoticeHub を含めない** |
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

## メンバーだけが入れるワールド

Bot のメンバー登録（[discord-setup.md](discord-setup.md) の 6b）を使うと、支援とは別に「登録を済ませた人だけ」が入れるワールドを作れます。メンバーは、支援者と同じリストの `members` に載ります（名前は載らず、ハッシュだけ）。

1. `Registry` の **Data Url** は、ほかのワールドと同じ支援者リストの URL のままにする
2. `Gate` の **Use Member List** を ON、Mode を `SupportersOnly` にする

これで、メンバーだけが入れます。支援者でも、メンバーでなければ入れません。持ち主の特別枠は、このモードでも通ります。ゲートの表示は「支援者」が「メンバー」に置き換わります（文言の表の、キーの末尾に `.member` が付いた文）。

## クレジットのボードに出るもの

| 表示 | 出る条件 |
|---|---|
| 支援者の名前の一覧（ティアごと） | クレジット表示に同意した支援者。メンバーの名前は出ない |
| 「あなたは ○○ です」 | 見ている本人が支援者かメンバーのとき（例: サポーター、プラチナサポーター、メンバー） |
| 「あなたはこのワールドにアクセスする権限を持っていません」 | ボードの `Gate` にゲートが入っていて、本人が入れないとき |
| Discord の招待 URL | Bot の設定の `links.discord` に URL があるとき。変えれば全ワールドの表示が変わる |

ティアの呼び名は、文言の表の `credits.tier.<ティアの id>` から引きます。無ければリストの label を使います。

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
- `_IsMember(VRCPlayerApi)` / `_IsLocalMember()` でメンバーかどうか、`_GetLink("discord")` で招待 URL が取れます
- `SupporterGate._IsLocalAllowed()` で入場可否を参照できます
- 通知を自分のギミックから出す方法は Nago Notice の README を参照

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
