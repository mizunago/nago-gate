# Bot のホスティング

## 必要なもの

- Node.js 20 以上が動く常時稼働の環境 1 台。CPU・メモリはほぼ使わない（数十 MB）
- 外向きの HTTPS 通信（Discord Gateway と GitHub API）。**受信ポートは不要**（Webhook を使わない）
- 永続ストレージ: `bot/instance/`（設定・トークン・`data/db.json`）。db.json が消えると猶予情報と名前登録が消える

止まっても致命的ではありません。復帰時の同期で追いつきます。止まっている間は `/vrc register` が使えないだけです。

## 選択肢

| 方式 | 目安費用 | 向き不向き |
|---|---|---|
| 国内 VPS 最小プラン（さくら / ConoHa / Xserver VPS など） | 月 500〜1,000 円 | 推奨。systemd で常駐。Linux の基本操作が必要 |
| Railway / Fly.io などの PaaS | 月 $5 前後 | サーバー管理不要。永続ボリュームを付けて db.json を置くこと |
| Oracle Cloud Always Free | 無料 | アカウント審査・突然の停止リスクあり。試すなら可 |
| 自宅 PC / Raspberry Pi | 電気代のみ | PC を落とすと Bot も止まる。Raspberry Pi なら実用可 |
| Render 無料枠 | 無料 | **不向き**。無料枠は待機でスリープし、Gateway 接続が切れる |

## VPS（Ubuntu）での手順

```bash
sudo apt update && sudo apt install -y nodejs npm git   # Node 20 未満なら NodeSource から入れる
sudo useradd -r -m -d /opt/supporter-gate-bot bot
sudo -u bot git clone <このリポジトリ> /opt/supporter-gate-bot/src
cd /opt/supporter-gate-bot/src/bot
sudo -u bot npm ci && sudo -u bot npm run build
sudo -u bot cp -r instance.example instance
# instance/.env と instance/config.jsonc を編集
sudo cp supporter-gate-bot.service /etc/systemd/system/
# WorkingDirectory と INSTANCE_DIR を実際のパス（/opt/supporter-gate-bot/src/bot...）に直す
sudo systemctl daemon-reload && sudo systemctl enable --now supporter-gate-bot
journalctl -u supporter-gate-bot -f
```

## Docker（既存の AWS EC2 などに同居させる場合）

ホストを汚さず動かせます。必要なのは Docker Engine と compose plugin だけです。

```bash
# Amazon Linux 2023 の例（Ubuntu なら apt で docker.io docker-compose-v2）
sudo dnf install -y docker git
sudo systemctl enable --now docker
sudo usermod -aG docker $USER      # 再ログイン後 docker が sudo なしで使える
# compose plugin が無い場合
sudo mkdir -p /usr/local/lib/docker/cli-plugins
sudo curl -SL https://github.com/docker/compose/releases/latest/download/docker-compose-linux-x86_64   -o /usr/local/lib/docker/cli-plugins/docker-compose
sudo chmod +x /usr/local/lib/docker/cli-plugins/docker-compose
```

```bash
git clone <このリポジトリ> ~/supporter-gate && cd ~/supporter-gate/bot
cp -r instance.example instance
# instance/.env にトークン、instance/config.jsonc にロール ID・公開先を記入
docker compose up -d --build
docker compose logs -f                      # 「ログイン: <Bot名>」「スラッシュコマンドを登録しました」が出れば OK
```

- 更新時: `git pull && docker compose up -d --build`
- 停止: `docker compose down`（`instance/` はホスト側に残る）
- `restart: unless-stopped` なので EC2 再起動後も自動で上がります（Docker 自体を `systemctl enable` していること）
- ログは json-file で 10MB × 3 世代にローテーションします
- セキュリティグループの受信許可は不要です（外向き HTTPS のみ）
- バックアップは `~/supporter-gate/bot/instance/` をコピーするだけです（トークンも含むので保管場所に注意）

## バックアップ

`bot/instance/` を日次でコピーしておけば十分です。GitHub 側の `supporters.json` は再生成できるので不要です。
`instance/` は `.gitignore` 済みですが、トークンを含むので絶対にリポジトリへ入れないこと。
