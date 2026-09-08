# SupporterGate Bot をローカルで起動する（run.bat から呼ばれる。直接実行も可）
#  - 必要なのは Node.js 20 以上だけ。PC 全体には何も追加しない
#  - 依存パッケージは bot
ode_modules の中にだけ入る
#  - 設定は bot\instance\（.env / config.jsonc）
#  - ログは bot\instance\logsot-YYYY-MM-DD.log
#  - 止めるときは Ctrl+C か、このウィンドウを閉じる
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Host.UI.RawUI.WindowTitle = "SupporterGate Bot"
Set-Location -LiteralPath $PSScriptRoot

function Pause-Exit([int]$code) {
  Write-Host ""
  Write-Host "Enter キーで閉じます"
  [void](Read-Host)
  exit $code
}

$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) {
  Write-Host "[ERROR] Node.js が見つかりません。https://nodejs.org/ から LTS を入れてください。" -ForegroundColor Red
  Pause-Exit 1
}
$ver = (& node -v).TrimStart("v")
$major = [int]($ver.Split(".")[0])
if ($major -lt 20) {
  Write-Host "[ERROR] Node.js 20 以上が必要です（現在 v$ver）" -ForegroundColor Red
  Pause-Exit 1
}
Write-Host "Node v$ver を使用します"

if (-not (Test-Path -LiteralPath "instance")) {
  Write-Host "[SETUP] instance フォルダが無いので instance.example からコピーします" -ForegroundColor Yellow
  Copy-Item -LiteralPath "instance.example" -Destination "instance" -Recurse
  Write-Host ""
  Write-Host "  instance\.env にトークン、instance\config.jsonc にサーバー ID を記入してから"
  Write-Host "  もう一度 run.bat を実行してください。"
  Start-Process notepad (Join-Path $PSScriptRoot "instance\.env")
  Pause-Exit 0
}

if (-not (Test-Path -LiteralPath "node_modules")) {
  Write-Host "[SETUP] 依存パッケージをインストールします（初回のみ、1 分ほど）" -ForegroundColor Yellow
  & npm ci --no-audit --no-fund
  if ($LASTEXITCODE -ne 0) { Write-Host "[ERROR] npm ci に失敗しました" -ForegroundColor Red; Pause-Exit 1 }
}

Write-Host "[BUILD] TypeScript をビルドします（数秒）"
& npm run build
if ($LASTEXITCODE -ne 0) { Write-Host "[ERROR] ビルドに失敗しました" -ForegroundColor Red; Pause-Exit 1 }

Write-Host ""
Write-Host "[START] Bot を起動します。止めるには Ctrl+C" -ForegroundColor Green
Write-Host "        ログ: instance\logs\"
Write-Host ""
$env:INSTANCE_DIR = Join-Path $PSScriptRoot "instance"
& node dist\index.js
$code = $LASTEXITCODE
Write-Host ""
Write-Host "[STOP] Bot が終了しました（終了コード $code）" -ForegroundColor Yellow
Pause-Exit $code
