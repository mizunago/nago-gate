# Nago Notice

VRChat ワールド用の共通通知（トースト）です。UdonSharp で書かれていて、完全にローカルで動きます。

- 頭の前に出て、最前面に描くので壁に埋まりません。壁際では壁の手前へ寄せます
- 文の長さから表示秒数を決めます（読み始めの秒数＋1 文字あたりの秒数。下限と上限つき）
- 幅に合わせて自動で折り返し、背景は文に合わせて伸び縮みします
- 同時に 4 件まで縦に積みます。あふれた分は順番待ちになります。同じ文が出ている間は時間だけ延ばします
- 貼り付き通知（カウントダウンなど。消すまで出続け、文を差し替えられる）
- 多言語（VRChat の言語設定に自動で合わせる。ja / en / ko / zh-CN / zh-TW ほか）

対象: VRChat Worlds SDK 3.10 以降 / Unity 2022.3 / TextMeshPro（TMP Essential Resources を取り込み済みであること）

## 入れ方

1. VCC の Settings > Packages > Add Repository に次の URL を入れる
   `https://mizunago.github.io/nago-gate/vpm.json`
2. プロジェクトの Manage Project で **Nago Notice** を足す
3. Unity のメニュー `Tools > Nago Notice > Add Notice Hub To Scene` を実行する（シーンに 1 つ）

## 使い方

```csharp
using NagoNotice;

[SerializeField] private NoticeHub notice;

notice._Show("訳済みの文をそのまま出す");
notice._ShowLevel("満室です", NoticeHub.LevelWarning);

// 表（NoticeTable）のキーで出す。文中の {0} {1} … に差し込む
notice._ShowKeyArgs("item.unlocked", new string[] { itemName, rankName, pointText });

// 秒数・音を細かく指定（seconds が 0 以下なら文の長さから自動。clip が null ならレベルの既定の音）
notice._ShowEx("取り出しました", NoticeHub.LevelInfo, 3f, null, true);

// 貼り付き通知（同じ id で呼ぶと文を差し替える）
notice._SetSticky("call", "着信中 あと 28 秒", NoticeHub.LevelWarning);
notice._ClearSticky("call");
```

| メソッド | 内容 |
|---|---|
| `_Show(text)` / `Show(text)` | 訳済みの文を出す。`Show` は移行用の別名 |
| `_ShowLevel(text, level)` | レベル（色帯と音）を指定。`LevelInfo` `LevelSuccess` `LevelWarning` `LevelError` |
| `_ShowEx(text, level, seconds, clip, playSound)` | 秒数・音・無音を指定 |
| `_ShowKey(key)` / `_ShowKeyLevel(key, level)` | 表のキーで出す |
| `_ShowKeyArgs(key, args)` / `_ShowKeyArgsLevel(key, args, level)` | 表のキー＋差し込み |
| `_SetSticky(id, text, level)` / `_ClearSticky(id)` / `_HasSticky(id)` | 貼り付き通知 |
| `_ClearAll()` | 表示中と順番待ちを全部消す |
| `_Text(key)` / `_Format(key, args)` | 表から今の言語の文を引く（自分の UI に使う） |
| `_Pick(ja, en, ko, zh)` | 4 言語の文から今の言語の物を選ぶ（表を使わない移行用） |
| `_Escape(text)` | プレイヤー名などを文に入れる前に通す（`<` `>` をタグとして読ませない） |
| `_GetLanguage()` / `_SetLanguageOverride(code)` | 今の言語／言語を外から決める（空文字で自動に戻す） |
| `_SetEnabled(bool)` / `_SetSoundEnabled(bool)` | 通知そのもの／音だけの ON・OFF |
| `_RegisterTable(table)` | 文言の表を足す |
| `_RegisterListener(behaviour)` | 言語が変わったときに `_OnNoticeLanguageChanged` を受け取る |

ほかの人の画面に出したいときは、呼び出し側がネットワークイベントで相手に頼み、相手のクライアントで `_Show` を呼びます。

## 文言の表（NoticeTable）

空の GameObject に `NoticeTable` を付け、JSON（TextAsset）を割り当てます。Hub の `Tables` に入れるか、`_RegisterTable` で登録します。

```json
{
  "item.unlocked": {
    "ja": "{0} は {1}（{2}）で解放されます",
    "en": "{0} unlocks at {1} ({2})",
    "ko": "…",
    "zh-CN": "…"
  }
}
```

言語が表に無いときは、次の順に探します。

1. 指定の言語そのもの
2. zh で始まる言語は zh-CN、zh-TW、zh の順
3. ハイフンの前（pt-BR なら pt）
4. en
5. ja

どの表にもキーが無ければ、キーをそのまま表示します。

## 設定（NoticeHub の Inspector）

| 項目 | 既定 | 内容 |
|---|---|---|
| Distance | 1.2 | 頭からの距離（m・目の高さ 1.6m 基準） |
| Pitch Offset Degrees | -12 | 視線の水平から下げる角度 |
| Pitch Follow | 0.5 | 見上げ・見下ろしにどれだけ付いていくか |
| Follow Smooth Seconds | 0.15 | 付いてくる速さ |
| Scale With Avatar | ON | アバターの目の高さで距離と大きさを拡縮 |
| Wall Margin / Min Distance | 0.25 / 0.4 | 壁の手前へ寄せる量と、最も近い距離 |
| Wall Layers | Default, Environment | 壁とみなすレイヤー |
| Read Lead Seconds | 2 | 読み始めるまでの秒数 |
| Seconds Per Unit | 0.2 | 全角 1 文字あたりの秒数（半角は半分） |
| Min / Max Seconds | 5 / 12 | 表示秒数の下限と上限 |
| Level Colors / Level Clips | | レベルごとの色と音（情報・成功・警告・エラー） |
| Debug Language Override | 空 | 確認用の言語の強制（公開時は空にする） |
| Suppress Duplicates | ON | 同じ文を積まない |

フォントを変えるときは、Project で TMP のフォントアセットを選び、`Tools > Nago Notice > Apply Selected Font Asset` を実行します。最前面描画のマテリアルを `Assets/NagoNotice` に作って割り当てます。

## 注意

- NoticeHub は、実行時に非表示にするオブジェクトの下に置かないでください。親が非表示になると通知が出ません
- 通知は UI レイヤー（5）に置いています。写真用のカメラやミラーに写したくない場合は、カメラの Culling Mask とミラーの Reflect Layers から UI を外してください。Reflect Layers が Everything のミラーには写ります
- コライダー・GraphicRaycaster・VRCUiShape は付けていません。手のレーザーやほかの UI を遮りません
- 文字は TMP の既定フォント（LiberationSans SDF）で、日本語・中国語・韓国語は予備フォントで出ます。VRChat の実機では内蔵フォントが補います。エディタでは、予備フォントが無い文字は □ になります
- リッチテキストの `<size=90%>` などはそのまま使えます
- 音は自作の短い音（4 種）です。差し替えは Level Clips で行います
- Quest では動作を確かめていません

## 保守する人へ

- プレハブは `NagoNotice.EditorTools.NoticePrefabBuilder.Build()` で組み立てています。作り直すとプレハブの中の ID が変わるので、公開後はプレハブを直接編集してください
- Inspector で入れる配列のフィールドに初期値（`= new T[n]`）を書かないでください。UdonSharp は、初期値と同じ長さの配列を入れても Udon 側へ保存しません
