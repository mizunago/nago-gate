// 支援者向けコマンド (/vrc) の文言。interaction.locale（ユーザーのクライアント言語）で切り替える。
// 管理者向け (/vrc-admin) は日本語固定。
// 言語を減らしたいときは Lang から外して該当列を消すだけ。

export type Lang = "ja" | "en" | "zh-CN" | "zh-TW" | "ko";
export const SUPPORTED_LANGS: Lang[] = ["ja", "en", "zh-CN", "zh-TW", "ko"];

/** Discord の locale 文字列 (ja, en-US, zh-CN, zh-TW, ko ...) → 対応言語 */
export function langOf(discordLocale: string | null | undefined): Lang {
  const l = (discordLocale ?? "").toLowerCase();
  if (l.startsWith("ja")) return "ja";
  if (l === "zh-cn" || l === "zh-hans") return "zh-CN";
  if (l.startsWith("zh")) return "zh-TW";
  if (l.startsWith("ko")) return "ko";
  return "en";
}

type Params = Record<string, string | number>;
type Table = Record<Lang, string>;

const M = {
  // ---- コマンド説明（Discord UI に表示） ----
  "cmd.vrc": {
    ja: "VRChat 支援者ゲートの登録・確認",
    en: "Register / check your VRChat supporter access",
    "zh-CN": "注册 / 查看 VRChat 支持者权限",
    "zh-TW": "註冊 / 查看 VRChat 支持者權限",
    ko: "VRChat 후원자 게이트 등록 / 확인",
  },
  "cmd.register": {
    ja: "VRChat の表示名を登録・変更する",
    en: "Register or change your VRChat display name",
    "zh-CN": "注册或更改你的 VRChat 显示名称",
    "zh-TW": "註冊或更改你的 VRChat 顯示名稱",
    ko: "VRChat 표시 이름을 등록 / 변경",
  },
  "opt.name": {
    ja: "VRChat に表示されている名前そのまま",
    en: "Your display name exactly as shown in VRChat",
    "zh-CN": "与 VRChat 中显示的名称完全一致",
    "zh-TW": "與 VRChat 中顯示的名稱完全一致",
    ko: "VRChat에 표시되는 이름 그대로",
  },
  "opt.credit": {
    ja: "ワールド内のクレジットに名前を載せる（既定: 載せる）",
    en: "Show your name in the in-world credits (default: yes)",
    "zh-CN": "在世界内的致谢名单中显示名字（默认：显示）",
    "zh-TW": "在世界內的致謝名單中顯示名字（預設：顯示）",
    ko: "월드 내 크레딧에 이름 표시 (기본: 표시)",
  },
  "cmd.status": {
    ja: "自分の登録状態を確認する",
    en: "Check your registration status",
    "zh-CN": "查看你的注册状态",
    "zh-TW": "查看你的註冊狀態",
    ko: "내 등록 상태 확인",
  },
  "cmd.credit": {
    ja: "クレジット表示の ON/OFF",
    en: "Turn credits display on/off",
    "zh-CN": "开启/关闭致谢名单显示",
    "zh-TW": "開啟/關閉致謝名單顯示",
    ko: "크레딧 표시 켜기/끄기",
  },
  "opt.show": {
    ja: "載せるなら true",
    en: "true to show your name",
    "zh-CN": "显示则为 true",
    "zh-TW": "顯示則為 true",
    ko: "표시하려면 true",
  },

  // ---- 返答 ----
  "err.wrongServer": {
    ja: "このサーバーでは使えません",
    en: "This command is not available on this server.",
    "zh-CN": "此命令在本服务器不可用。",
    "zh-TW": "此指令在本伺服器不可用。",
    ko: "이 서버에서는 사용할 수 없습니다.",
  },
  "err.name.empty": {
    ja: "名前が空です",
    en: "The name is empty.",
    "zh-CN": "名称为空。",
    "zh-TW": "名稱為空。",
    ko: "이름이 비어 있습니다.",
  },
  "err.name.tooLong": {
    ja: "名前が長すぎます（最大 {max} 文字）",
    en: "The name is too long (max {max} characters).",
    "zh-CN": "名称过长（最多 {max} 个字符）。",
    "zh-TW": "名稱過長（最多 {max} 個字元）。",
    ko: "이름이 너무 깁니다 (최대 {max}자).",
  },
  "err.name.badChars": {
    ja: "改行やタブは使えません",
    en: "Line breaks and tabs are not allowed.",
    "zh-CN": "不能包含换行或制表符。",
    "zh-TW": "不能包含換行或 Tab。",
    ko: "줄바꿈이나 탭은 사용할 수 없습니다.",
  },
  "err.cannotRegister": {
    ja: "登録できません: {reason}",
    en: "Cannot register: {reason}",
    "zh-CN": "无法注册：{reason}",
    "zh-TW": "無法註冊：{reason}",
    ko: "등록할 수 없습니다: {reason}",
  },
  "err.duplicateName": {
    ja: "その名前は別のアカウントで登録済みです。心当たりがなければ管理者に連絡してください。",
    en: "That name is already registered by another account. Contact an admin if this is unexpected.",
    "zh-CN": "该名称已被其他账号注册。如有疑问请联系管理员。",
    "zh-TW": "該名稱已被其他帳號註冊。如有疑問請聯絡管理員。",
    ko: "이 이름은 다른 계정에서 이미 등록되어 있습니다. 짚이는 바가 없다면 관리자에게 문의하세요.",
  },
  "err.cooldown": {
    ja: "名前の変更は {days} 日に 1 回までです。次回変更可能: {date}",
    en: "You can change your name once every {days} days. Next change: {date}",
    "zh-CN": "每 {days} 天只能更改一次名称。下次可更改：{date}",
    "zh-TW": "每 {days} 天只能更改一次名稱。下次可更改：{date}",
    ko: "이름 변경은 {days}일에 1회만 가능합니다. 다음 변경 가능: {date}",
  },
  "registered": {
    ja: "登録しました。",
    en: "Registered.",
    "zh-CN": "已注册。",
    "zh-TW": "已註冊。",
    ko: "등록했습니다.",
  },
  "registered.active": {
    ja: "数分後にワールド側へ反映されます。",
    en: "It will take effect in the worlds within a few minutes.",
    "zh-CN": "几分钟后将在世界中生效。",
    "zh-TW": "幾分鐘後將在世界中生效。",
    ko: "몇 분 후 월드에 반영됩니다.",
  },
  "registered.inactive": {
    ja: "支援ロールがまだ確認できていません。支援サイトの Discord 連携を完了してからお待ちください。",
    en: "Your supporter role has not been detected yet. Complete the Discord link on your support platform and wait a few minutes.",
    "zh-CN": "尚未检测到你的支持者身份组。请先在支持平台完成 Discord 关联，然后稍等几分钟。",
    "zh-TW": "尚未偵測到你的支持者身分組。請先在支持平台完成 Discord 連結，然後稍等幾分鐘。",
    ko: "후원자 역할이 아직 확인되지 않았습니다. 후원 사이트의 Discord 연동을 완료한 뒤 기다려 주세요.",
  },
  "err.noRecord": {
    ja: "先に /vrc register で名前を登録してください",
    en: "Please register your name first with /vrc register.",
    "zh-CN": "请先使用 /vrc register 注册名称。",
    "zh-TW": "請先使用 /vrc register 註冊名稱。",
    ko: "먼저 /vrc register 로 이름을 등록해 주세요.",
  },
  "credit.set": {
    ja: "クレジット表示を {state} にしました",
    en: "Credits display is now {state}.",
    "zh-CN": "致谢名单显示已设为 {state}。",
    "zh-TW": "致謝名單顯示已設為 {state}。",
    ko: "크레딧 표시를 {state}(으)로 설정했습니다.",
  },
  "on": { ja: "ON", en: "ON", "zh-CN": "开启", "zh-TW": "開啟", ko: "켜짐" },
  "off": { ja: "OFF", en: "OFF", "zh-CN": "关闭", "zh-TW": "關閉", ko: "꺼짐" },
  "err.unknown": {
    ja: "不明なコマンドです",
    en: "Unknown command.",
    "zh-CN": "未知命令。",
    "zh-TW": "未知指令。",
    ko: "알 수 없는 명령입니다.",
  },
  "err.internal": {
    ja: "内部エラーが発生しました",
    en: "An internal error occurred.",
    "zh-CN": "发生内部错误。",
    "zh-TW": "發生內部錯誤。",
    ko: "내부 오류가 발생했습니다.",
  },

  // ---- 状態表示 ----
  "status.none": {
    ja: "登録情報なし（支援ロールが確認できていません）",
    en: "No registration found (supporter role not detected).",
    "zh-CN": "没有注册信息（未检测到支持者身份组）。",
    "zh-TW": "沒有註冊資訊（未偵測到支持者身分組）。",
    ko: "등록 정보 없음 (후원자 역할이 확인되지 않음)",
  },
  "status.name": { ja: "VRChat 名", en: "VRChat name", "zh-CN": "VRChat 名称", "zh-TW": "VRChat 名稱", ko: "VRChat 이름" },
  "status.unregistered": { ja: "未登録", en: "not registered", "zh-CN": "未注册", "zh-TW": "未註冊", ko: "미등록" },
  "status.rank": { ja: "有効ランク", en: "Active tier", "zh-CN": "当前等级", "zh-TW": "目前等級", ko: "유효 등급" },
  "status.rankNone": { ja: "なし", en: "none", "zh-CN": "无", "zh-TW": "無", ko: "없음" },
  "status.link": { ja: "支援サイト連携", en: "Support platform", "zh-CN": "支持平台关联", "zh-TW": "支持平台連結", ko: "후원 사이트 연동" },
  "status.active": { ja: "支援中", en: "active", "zh-CN": "支持中", "zh-TW": "支持中", ko: "후원 중" },
  "status.inactive": { ja: "未確認", en: "not detected", "zh-CN": "未检测到", "zh-TW": "未偵測到", ko: "미확인" },
  "status.credit": { ja: "クレジット表示", en: "Credits", "zh-CN": "致谢名单", "zh-TW": "致謝名單", ko: "크레딧 표시" },
  "status.grace": { ja: "猶予期限", en: "Grace period until", "zh-CN": "宽限期至", "zh-TW": "寬限期至", ko: "유예 기한" },
  "status.manual": { ja: "手動付与", en: "Manual grant", "zh-CN": "手动授予", "zh-TW": "手動授予", ko: "수동 부여" },
  "status.manualUntil": { ja: "ランク {rank}（{date} まで）", en: "tier {rank} (until {date})", "zh-CN": "等级 {rank}（至 {date}）", "zh-TW": "等級 {rank}（至 {date}）", ko: "등급 {rank} ({date}까지)" },
  "status.manualForever": { ja: "ランク {rank}（無期限）", en: "tier {rank} (no expiry)", "zh-CN": "等级 {rank}（无期限）", "zh-TW": "等級 {rank}（無期限）", ko: "등급 {rank} (무기한)" },
  "status.nextChange": { ja: "次回名前変更可能", en: "Next name change", "zh-CN": "下次可更改名称", "zh-TW": "下次可更改名稱", ko: "다음 이름 변경 가능" },
  "status.now": { ja: "今すぐ", en: "now", "zh-CN": "现在", "zh-TW": "現在", ko: "지금" },

  // ---- ボタン / フォーム / テキスト投稿 ----
  "modal.title": {
    ja: "VRChat 表示名の登録",
    en: "Register VRChat display name",
    "zh-CN": "注册 VRChat 显示名称",
    "zh-TW": "註冊 VRChat 顯示名稱",
    ko: "VRChat 표시 이름 등록",
  },
  "modal.name": {
    ja: "VRChat の表示名（プロフィールに出ている名前）",
    en: "VRChat display name (as shown on your profile)",
    "zh-CN": "VRChat 显示名称（个人资料上显示的名字）",
    "zh-TW": "VRChat 顯示名稱（個人資料上顯示的名字）",
    ko: "VRChat 표시 이름 (프로필에 표시되는 이름)",
  },
  "modal.placeholder": {
    ja: "例: なごなご",
    en: "e.g. Nagonago",
    "zh-CN": "例如：Nagonago",
    "zh-TW": "例如：Nagonago",
    ko: "예: Nagonago",
  },
  "hint.plainText": {
    ja: "このチャンネルは登録専用です。上のパネルの **登録** ボタンを押すか、入力欄で `/` を打って `/vrc register` を候補から選んでください（テキストの貼り付けでは動きません）。",
    en: "This channel is for registration only. Press **Register** on the panel above, or type `/` and pick `/vrc register` from the popup (pasting the text does nothing).",
    "zh-CN": "此频道仅用于注册。请点击上方面板的 **注册** 按钮，或输入 `/` 后从弹出列表中选择 `/vrc register`（直接粘贴文本无效）。",
    "zh-TW": "此頻道僅用於註冊。請點擊上方面板的 **註冊** 按鈕，或輸入 `/` 後從彈出清單中選擇 `/vrc register`（直接貼上文字無效）。",
    ko: "이 채널은 등록 전용입니다. 위 패널의 **등록** 버튼을 누르거나, 입력창에 `/`를 입력해 목록에서 `/vrc register`를 선택하세요 (텍스트 붙여넣기는 동작하지 않습니다).",
  },
} satisfies Record<string, Table>;

export type MsgKey = keyof typeof M;

export function t(lang: Lang, key: MsgKey, params: Params = {}): string {
  const table: Table = M[key];
  let text = table[lang] ?? table.en;
  for (const [k, v] of Object.entries(params)) text = text.replaceAll(`{${k}}`, String(v));
  return text;
}

/** Discord の setDescriptionLocalizations 用（英語は基本説明として別途渡す） */
export function localizations(key: MsgKey): Record<string, string> {
  const table: Table = M[key];
  return {
    ja: table.ja,
    "zh-CN": table["zh-CN"],
    "zh-TW": table["zh-TW"],
    ko: table.ko,
  };
}
