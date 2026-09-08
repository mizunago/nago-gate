# Unity 側セットアップ

対象: VRChat Worlds SDK 3.10.x / Unity 2022.3 / UdonSharp（SDK 同梱）/ TextMeshPro

## パッケージの作り方（最初の 1 回）

1. 任意のワールドプロジェクトに `unity/Assets/SupporterGate` をコピー
2. Unity がコンパイルし、`Editor/SupporterGateProgramAssetGenerator` が各スクリプトの `.asset`（UdonSharpProgramAsset）を自動生成する
3. `Tools > SupporterGate > Export UnityPackage` で `SupporterGate.unitypackage` を書き出す

以降は各ワールドプロジェクトでこの unitypackage をインポートするだけです。

## ワールドへの組み込み

1. `Tools > SupporterGate > Create Scene Setup` を実行すると `SupporterGate System` が生成される

```
SupporterGate System
├─ Registry        SupporterHash + SupporterRegistry   ← Data Url を設定
├─ Gate            SupporterGate                       ← Mode などを設定
├─ LobbySpawn      ロビーの戻り先
├─ ContentSpawn    入場ボタンで飛ぶ先
├─ ContentRoot     ここにワールド本体を入れる（非許可時はローカルで非表示）
├─ ContentZone     本体エリアを覆う Trigger（入り込んだ非許可者をロビーへ戻す）
├─ LobbyPanel      状態表示・入場ボタン
├─ ApprovalPanel   支援者専用の入場許可パネル
├─ CreditsBoard    支援者クレジット
└─ RankTags        頭上のティア表示
```

2. `Registry` の **Data Url** に Bot の公開 URL を入れる
3. `Gate` の設定

| 項目 | 内容 |
|---|---|
| Mode | `Open` 公開 / `SupportersOnly` 支援者のみ / `SupporterApproval` 支援者＋許可した人 / `SupporterPresence` 支援者在室中は誰でも |
| Required Rank | この値以上を支援者扱い（プラチナ限定なら 2 など） |
| Require Supporter Activation | ON にすると、支援者が「エリアを有効化」を押すまで非支援者は入れない |
| No Supporter Grace Seconds | 支援者が全員いなくなってから非支援者を戻すまでの秒数 |
| Fail Open When Registry Unavailable | リスト取得失敗時に非支援者を通すか。限定ワールドでは OFF |
| Content Roots | 非許可時に非表示にするオブジェクト。**同期オブジェクトや ContentZone を含めない** |

4. `ContentRoot` の下にワールド本体を配置し、`ContentZone` の BoxCollider を本体エリアに合わせる
5. ロビー側の VRC_SceneDescriptor スポーンは `LobbySpawn` の位置に置く
6. UI パネルは好きな位置へ移動してよい。見た目は uGUI なので自由に差し替え可

## モード別の使い分け

| ワールド | Mode | 補足 |
|---|---|---|
| 公開ワールド（特典のみ） | `Open` | CreditsBoard と RankTags だけ使う。Gate / Zone は削除してよい |
| アーリーアクセス | `SupportersOnly` | |
| センシティブ寄りの限定ワールド | `SupporterApproval` | 支援者のフレンドが入ってきたら、支援者が ApprovalPanel で個別に許可 |
| 支援者が「開ける」タイプ | `SupporterPresence` + Require Activation | 支援者がいる間だけ全員 OK |

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
- `SupporterGate._IsLocalAllowed()` で入場可否を参照できます

## 動作確認

1. Bot で `/vrc register` した名前と、Unity 側 `Registry` の Debug Mode を ON にして Play（ClientSim）
2. Console に `<表示名> -> <hash> rank=N` が出る。`/vrc-admin hash` の値と一致すれば照合 OK
   （ClientSim の表示名は ClientSim 設定で変えられます）
3. Build & Test で 2 クライアント起動し、承認パネルの同期を確認

## 注意

- `Content Roots` に同期オブジェクト（VRC_ObjectSync、同期 Udon）を入れると、非表示中に同期が壊れます。見た目だけのオブジェクトにしてください
- `SupporterRegistry` はシーンに 1 つ。複数のゲートやボードから共有できます
- JSON の再取得は既定 600 秒。短くしすぎると String Loading のレート制限（5 秒に 1 回）と CDN キャッシュの都合で意味がありません
