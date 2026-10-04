# Nago Supporter Gate

支援者だけが入れる VRChat ワールド用のゲートです。プレイヤーの表示名のハッシュを、公開した支援者リスト（JSON）と照合します。

- モード: 公開 / 支援者のみ / 支援者＋支援者が許可した人 / 支援者が在室している間は誰でも
- 承認パネル: 在室中の支援者が、支援者でない人を個別に許可・取り消し
- 通知（Nago Notice を使用）: ロビーへ戻した理由、支援者の退出後のカウントダウン、許可・取り消し、支援者への入室の知らせ
- メンバー（支援とは別の軸）で判定するゲートも作れる。支援者でも、メンバーでなければ入れない
- クレジットのボード: 支援者の名前の一覧、見ている本人の状態（「あなたはサポーターです」など）、Discord の招待 URL
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

- 0.4.0: `Convert Existing World` を追加。ゲートの無いワールドに、入口の部屋（何もない四角い部屋）とゲート一式を足す。今のスポーン地点は「入場したあとに出る場所」として引き継ぐ
- 0.3.0: リストの `members`（メンバー）と `links`（案内用のリンク）に対応。Gate に `Use Member List`、Registry に `_IsMember` `_IsLocalMember` `_HasMemberList` `_GetLink` `_GetTierId` を追加。クレジットのボードに、本人の状態と Discord の招待 URL を表示。上げたあとに `Wire Notices (existing scene)` をもう一度実行すると、ボードに「入れないとき」の表示が出るようになる
- 0.2.0: VPM パッケージ化、共通の通知、理由の通知とカウントダウン、多言語

使い方と設定は、リポジトリの `docs/unity-setup.md` を見てください。
