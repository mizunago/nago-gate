# Nago Supporter Gate

支援者だけが入れる VRChat ワールド用のゲートです。プレイヤーの表示名のハッシュを、公開した支援者リスト（JSON）と照合します。

- モード: 公開 / 支援者のみ / 支援者＋支援者が許可した人 / 支援者が在室している間は誰でも
- 承認パネル: 在室中の支援者が、支援者でない人を個別に許可・取り消し
- 通知（Nago Notice を使用）: ロビーへ戻した理由、支援者の退出後のカウントダウン、許可・取り消し、支援者への入室の知らせ
- 文言は日本語・英語・韓国語・中国語（簡体・繁体）

対象: VRChat Worlds SDK 3.10 以降 / Unity 2022.3。Nago Notice（`com.nagonago.notice`）に依存します。

## 入れ方

1. VCC の Settings > Packages > Add Repository に次の URL を入れる
   `https://mizunago.github.io/nago-gate/vpm.json`
2. プロジェクトの Manage Project で **Nago Supporter Gate** を足す（Nago Notice も一緒に入る）
3. Unity のメニュー `Tools > SupporterGate > Create Scene Setup` を実行する

既にゲートを置いてあるシーンは、`Tools > SupporterGate > Wire Notices (existing scene)` で通知と文言の表を足せます。

使い方と設定は、リポジトリの `docs/unity-setup.md` を見てください。
