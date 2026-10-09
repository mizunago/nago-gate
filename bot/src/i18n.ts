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
    ja: "改行やタブなど、表示できない文字は使えません",
    en: "Line breaks, tabs and other control characters are not allowed.",
    "zh-CN": "不能包含换行、制表符等控制字符。",
    "zh-TW": "不能包含換行、Tab 等控制字元。",
    ko: "줄바꿈, 탭 등 제어 문자는 사용할 수 없습니다.",
  },
  "err.banned": {
    ja: "このアカウントでは登録できません。\n心当たりがなければ管理者に連絡してください。",
    en: "This account cannot register. Contact an admin if this is unexpected.",
    "zh-CN": "此账号无法注册。\n如有疑问请联系管理员。",
    "zh-TW": "此帳號無法註冊。\n如有疑問請聯絡管理員。",
    ko: "이 계정으로는 등록할 수 없습니다. 짚이는 바가 없다면 관리자에게 문의하세요.",
  },
  "err.cannotRegister": {
    ja: "登録できません: {reason}",
    en: "Cannot register: {reason}",
    "zh-CN": "无法注册：{reason}",
    "zh-TW": "無法註冊：{reason}",
    ko: "등록할 수 없습니다: {reason}",
  },
  "err.duplicateName": {
    ja: "その名前は別のアカウントで登録済みです。\n心当たりがなければ管理者に連絡してください。",
    en: "That name is already registered by another account. Contact an admin if this is unexpected.",
    "zh-CN": "该名称已被其他账号注册。\n如有疑问请联系管理员。",
    "zh-TW": "該名稱已被其他帳號註冊。\n如有疑問請聯絡管理員。",
    ko: "이 이름은 다른 계정에서 이미 등록되어 있습니다. 짚이는 바가 없다면 관리자에게 문의하세요.",
  },
  "err.cooldown": {
    ja: "名前の変更は {days} 日に 1 回までです。\n次回変更可能: {date}",
    en: "You can change your name once every {days} days. Next change: {date}",
    "zh-CN": "每 {days} 天只能更改一次名称。\n下次可更改：{date}",
    "zh-TW": "每 {days} 天只能更改一次名稱。\n下次可更改：{date}",
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
    ja: "支援者のロールは、まだ確認できていません。\n支援者の方は、支援サイトで Discord をつないでから、数分お待ちください。\n支援していない方は、このままで大丈夫です。",
    en: "No supporter role has been detected yet. If you are a supporter, connect Discord on your support platform and wait a few minutes. If you are not a supporter, there is nothing more to do here.",
    "zh-CN": "尚未检测到支持者身份组。\n如果你是支持者，请在支持平台关联 Discord，然后稍等几分钟。\n如果你不是支持者，这样就可以了。",
    "zh-TW": "尚未偵測到支持者身分組。\n如果你是支持者，請在支持平台連結 Discord，然後稍等幾分鐘。\n如果你不是支持者，這樣就可以了。",
    ko: "후원자 역할이 아직 확인되지 않았습니다. 후원자라면 후원 사이트에서 Discord를 연결한 뒤 몇 분 기다려 주세요. 후원하지 않는 분은 이대로 괜찮습니다.",
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
  "status.tester": { ja: "協力者", en: "Tester", "zh-CN": "协助者", "zh-TW": "協助者", ko: "협력자" },
  "status.testerUntil": {
    ja: "{tier} と同じに入れます（{date} まで、ボードには載りません）",
    en: "same access as {tier} until {date} (not shown on the board)",
    "zh-CN": "与 {tier} 相同的进入权限（至 {date}，不显示在名单板上）",
    "zh-TW": "與 {tier} 相同的進入權限（至 {date}，不顯示在名單板上）",
    ko: "{tier}와 같은 입장 권한 ({date}까지, 보드에는 표시되지 않습니다)",
  },
  "status.nextChange": { ja: "次回名前変更可能", en: "Next name change", "zh-CN": "下次可更改名称", "zh-TW": "下次可更改名稱", ko: "다음 이름 변경 가능" },
  "status.now": { ja: "今すぐ", en: "now", "zh-CN": "现在", "zh-TW": "現在", ko: "지금" },
  "status.fixUntil": {
    ja: "{time} までは、もう一度登録すれば、打ち間違いを何度でも直せます。",
    en: "Until {time}, you can fix a typo as many times as you need by registering again.",
    "zh-CN": "在 {time} 之前，重新注册即可修正输入错误，次数不限。",
    "zh-TW": "在 {time} 之前，重新註冊即可修正輸入錯誤，次數不限。",
    ko: "{time}까지는 다시 등록하면 오타를 몇 번이든 고칠 수 있습니다.",
  },

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
    en: "VRChat display name (as on your profile)",
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
    ja: "このチャンネルは登録専用です。\n上のパネルの **🧾 登録** を押すか、入力欄で `/` を打って `/vrc register` を候補から選んでください（テキストの貼り付けでは動きません）。",
    en: "This channel is for registration only. Press **Register** on the panel above, or type `/` and pick `/vrc register` from the popup (pasting the text does nothing).",
    "zh-CN": "此频道仅用于注册。\n请点击上方面板的 **Register**，或输入 `/` 后从弹出列表中选择 `/vrc register`（直接粘贴文本无效）。",
    "zh-TW": "此頻道僅用於註冊。\n請點擊上方面板的 **Register**，或輸入 `/` 後從彈出清單中選擇 `/vrc register`（直接貼上文字無效）。",
    ko: "이 채널은 등록 전용입니다. 위 패널의 **Register**를 누르거나, 입력창에 `/`를 입력해 목록에서 `/vrc register`를 선택하세요 (텍스트 붙여넣기는 동작하지 않습니다).",
  },

  // ---- 住人（データの中の名前は member。支援とは別の軸） ----
  "member.label": {
    ja: "住人",
    en: "Resident",
    "zh-CN": "居民",
    "zh-TW": "居民",
    ko: "주민",
  },
  "member.state.active": { ja: "有効", en: "active", "zh-CN": "有效", "zh-TW": "有效", ko: "유효" },
  "member.state.pending": {
    ja: "登録済み（{date} から有効）",
    en: "applied (active from {date})",
    "zh-CN": "已登记（{date} 起生效）",
    "zh-TW": "已登記（{date} 起生效）",
    ko: "등록 완료 ({date}부터 유효)",
  },
  "member.state.manual": { ja: "有効（管理者が認定）", en: "active (granted by an admin)", "zh-CN": "有效（由管理员认定）", "zh-TW": "有效（由管理員認定）", ko: "유효 (관리자 인정)" },
  "member.state.manualNeedName": {
    ja: "管理者が認定済み（VRChat の表示名を登録すると有効になります）",
    en: "granted by an admin (becomes active once you register your VRChat display name)",
    "zh-CN": "已由管理员认定（注册 VRChat 显示名称后生效）",
    "zh-TW": "已由管理員認定（註冊 VRChat 顯示名稱後生效）",
    ko: "관리자 인정 완료 (VRChat 표시 이름을 등록하면 유효해집니다)",
  },
  "member.state.none": { ja: "未登録", en: "not applied", "zh-CN": "未登记", "zh-TW": "未登記", ko: "미등록" },
  "member.unavailable": {
    ja: "この機能は今は使えません。",
    en: "This feature is not available.",
    "zh-CN": "此功能目前不可用。",
    "zh-TW": "此功能目前無法使用。",
    ko: "이 기능은 현재 사용할 수 없습니다.",
  },
  "member.needName": {
    ja: "先に {register} で **🧾 登録** を押して、VRChat の表示名を登録してください。\n済んだら、もう一度 **🏠 住人** を押してください。",
    en: "First, press **Register** in {register} and enter your VRChat display name. Then press **🏠 Resident** again.",
    "zh-CN": "请先在 {register} 点击 **Register**，注册 VRChat 显示名称。\n完成后，请再次点击 **🏠 Resident**。",
    "zh-TW": "請先在 {register} 點擊 **Register**，註冊 VRChat 顯示名稱。\n完成後，請再次點擊 **🏠 Resident**。",
    ko: "먼저 {register}에서 **Register**를 눌러 VRChat 표시 이름을 등록해 주세요. 끝나면 **🏠 Resident**를 다시 눌러 주세요.",
  },
  "member.explain": {
    ja: "**住人の登録**\nサーバーに参加してから {days} 日以上たった方は、支援の有無に関係なく、住人限定のワールドの案内を見られます。\n・住人限定のワールドには、大人向けの表現や、刺激の強い演出があります\n・18 歳以上で、そうした内容に抵抗がない方だけ登録してください",
    en: "**Becoming a resident**\nOnce you have been on this server for {days} days, you can see the resident-only worlds, whether or not you are a supporter.\n- Resident-only worlds contain adult themes and intense effects\n- Please apply only if you are 18 or older and comfortable with such content",
    "zh-CN": "**居民登记**\n加入本服务器满 {days} 天后，无论是否支持，都可以查看居民限定世界的说明。\n・居民限定世界包含面向成人的表现和较强烈的演出\n・请仅在年满 18 岁且不介意此类内容时登记",
    "zh-TW": "**居民登記**\n加入本伺服器滿 {days} 天後，無論是否支持，都可以查看居民限定世界的說明。\n・居民限定世界包含面向成人的表現和較強烈的演出\n・請僅在年滿 18 歲且不介意此類內容時登記",
    ko: "**주민 등록**\n이 서버에 참가한 지 {days}일이 지나면, 후원 여부와 관계없이 주민 전용 월드 안내를 볼 수 있습니다.\n・주민 전용 월드에는 성인용 표현과 자극이 강한 연출이 있습니다\n・18세 이상이며 그런 내용에 거부감이 없는 분만 등록해 주세요",
  },
  "member.confirm": {
    ja: "下のボタンを押すと、上の内容すべてに同意して登録します。",
    en: "Pressing the button below means you agree to all of the above.",
    "zh-CN": "点击下方按钮即表示同意以上全部内容并完成登记。",
    "zh-TW": "點擊下方按鈕即表示同意以上全部內容並完成登記。",
    ko: "아래 버튼을 누르면 위 내용 전체에 동의하고 등록합니다.",
  },
  // 共有のお願い。支援者にも住人にも同じ文を出す（登録の返答と、住人の同意の画面）
  "share.notice": {
    ja: "**共有についてのお願い**\n・スクリーンショットや動画を投稿するときは、まず投稿先のルールを守ってください\n・投稿はかまいませんが、**ワールドを特定できる情報（ワールド名・リンク・ID・招待リンク）は一切載せないでください**\n・知り合いに見せるときも、ワールドの情報は画像・動画・説明文・キャプション・メタデータに入れず、別に伝えてください\n・**限定のワールドへのポータルを、パブリックのワールドや知らない人がいる場所で出さないでください**\n・インスタンスは Invite・Friends・承認制の Group で作ってください（Invite+・Friends+・Public と、自動で参加できる Group は使わないでください）",
    en: "**Sharing notice**\n- When you post screenshots or videos, first follow the rules of the platform you post on\n- Posting is fine, but **never include anything that identifies the world (world name, link, ID, or invite link)**\n- Even when you share with people you know, give the world information separately. Do not put it in the image, the video, the description, the caption, or the metadata\n- **Never drop a portal to the private worlds in a public world, or anywhere strangers can see it.** Use Invite, Friends, or approval-required Group instances only (not Invite+, Friends+, Public, or a Group that anyone can join automatically)",
    "zh-CN": "**关于分享**\n・发布截图或视频时，请首先遵守发布平台的规则\n・可以发布，但**请勿包含任何能识别世界的信息（世界名称、链接、ID、邀请链接）**\n・即使只分享给认识的人，也请另行告知世界信息，不要写在图片、视频、说明、标题或元数据中\n・**请勿在公开世界或有陌生人的地方放置通往限定世界的传送门**\n・请仅使用 Invite、Friends 或需要审核才能加入的 Group 实例（请勿使用 Invite+、Friends+、Public，或可自动加入的 Group）",
    "zh-TW": "**關於分享**\n・發布截圖或影片時，請首先遵守發布平台的規則\n・可以發布，但**請勿包含任何能識別世界的資訊（世界名稱、連結、ID、邀請連結）**\n・即使只分享給認識的人，也請另行告知世界資訊，不要寫在圖片、影片、說明、標題或中繼資料中\n・**請勿在公開世界或有陌生人的地方放置通往限定世界的傳送門**\n・請僅使用 Invite、Friends 或需要審核才能加入的 Group 實例（請勿使用 Invite+、Friends+、Public，或可自動加入的 Group）",
    ko: "**공유에 관한 안내**\n・스크린샷이나 영상을 올릴 때는 먼저 올리는 플랫폼의 규칙을 지켜 주세요\n・올리는 것은 괜찮지만, **월드를 특정할 수 있는 정보(월드 이름, 링크, ID, 초대 링크)는 절대 포함하지 마세요**\n・아는 사람에게 보여 줄 때도 월드 정보는 이미지, 영상, 설명, 캡션, 메타데이터에 넣지 말고 따로 전달해 주세요\n・**공개 월드나 모르는 사람이 있는 곳에서 한정 월드로 가는 포털을 열지 마세요**. 인스턴스는 Invite, Friends, 가입 승인이 필요한 Group으로만 만들어 주세요 (Invite+, Friends+, Public, 자동으로 가입되는 Group은 사용하지 마세요)",
  },
  "group.label": {
    ja: "VRChat Group",
    en: "VRChat Group",
    "zh-CN": "VRChat Group",
    "zh-TW": "VRChat Group",
    ko: "VRChat Group",
  },
  "group.requested": {
    ja: "参加を希望（{date}）",
    en: "Requested ({date})",
    "zh-CN": "已申请（{date}）",
    "zh-TW": "已申請（{date}）",
    ko: "가입 희망 ({date})",
  },
  "group.unavailable": {
    ja: "Group への参加は、まだ受け付けていません。",
    en: "Group applications are not open yet.",
    "zh-CN": "尚未开放 Group 申请。",
    "zh-TW": "尚未開放 Group 申請。",
    ko: "Group 가입 신청은 아직 받고 있지 않습니다.",
  },
  "group.needName": {
    ja: "先に {register} で **🧾 登録** を押して、VRChat の表示名を登録してください。",
    en: "First, press **Register** in {register} and enter your VRChat display name.",
    "zh-CN": "请先在 {register} 点击 **Register**，注册 VRChat 显示名称。",
    "zh-TW": "請先在 {register} 點擊 **Register**，註冊 VRChat 顯示名稱。",
    ko: "먼저 {register}에서 **Register**를 눌러 VRChat 표시 이름을 등록해 주세요.",
  },
  "group.notEligible": {
    ja: "VRChat の Group に参加できるのは、支援者か住人の方です。\n住人には、{resident} の **🏠 住人** から登録できます。",
    en: "The VRChat Group is for supporters and residents. You can become a resident with **🏠 Resident** in {resident}.",
    "zh-CN": "只有支持者或居民可以加入 VRChat Group。\n可在 {resident} 点击 **🏠 Resident** 登记为居民。",
    "zh-TW": "只有支持者或居民可以加入 VRChat Group。\n可在 {resident} 點擊 **🏠 Resident** 登記為居民。",
    ko: "VRChat Group에는 후원자 또는 주민만 참가할 수 있습니다. {resident}의 **🏠 Resident**로 주민 등록을 할 수 있습니다.",
  },
  "group.notEligibleApply": {
    ja: "VRChat の Group に参加できるのは、支援者か住人の方です。\n住人には、{resident} の **🏠 住人** から申請できます。\n支援は要りません。",
    en: "The VRChat Group is for supporters and residents. You can apply to become a resident with **🏠 Resident** in {resident}. No support is needed.",
    "zh-CN": "只有支持者或居民可以加入 VRChat Group。\n可在 {resident} 点击 **🏠 Resident** 申请成为居民，无需支持。",
    "zh-TW": "只有支持者或居民可以加入 VRChat Group。\n可在 {resident} 點擊 **🏠 Resident** 申請成為居民，無需支持。",
    ko: "VRChat Group에는 후원자 또는 주민만 참가할 수 있습니다. {resident}의 **🏠 Resident**로 주민 신청을 할 수 있습니다. 후원은 필요 없습니다.",
  },
  "group.howto": {
    ja: "VRChat の Group「{name}」への参加の希望を受け付けました。\n持ち主が確認して、登録した表示名（**{vrcName}**）のアカウントへ、Group の招待を送ります。\n届くまで、少し時間がかかることがあります。\n届いたら、VRChat の通知から承諾してください。\n\nGroup に入ると、フレンドでない方とも、Group のインスタンスで一緒に遊べます。\nGroup のページ: {url}",
    en: "Your request to join the VRChat Group \"{name}\" is recorded.\nThe owner will check it and send a Group invite to the account with your registered display name (**{vrcName}**). This may take a while. When it arrives, accept it from your VRChat notifications.\n\nIn the Group, you can play together in Group instances, even with people who are not your friends.\nGroup page: {url}",
    "zh-CN": "已记录你加入 VRChat Group「{name}」的申请。\n主人确认后，会向已注册显示名称（**{vrcName}**）的账号发送 Group 邀请，可能需要一些时间。\n收到后，请在 VRChat 的通知中接受。\n\n加入 Group 后，即使不是好友，也可以在 Group 实例中一起游玩。\nGroup 页面：{url}",
    "zh-TW": "已記錄你加入 VRChat Group「{name}」的申請。\n主人確認後，會向已註冊顯示名稱（**{vrcName}**）的帳號發送 Group 邀請，可能需要一些時間。\n收到後，請在 VRChat 的通知中接受。\n\n加入 Group 後，即使不是好友，也可以在 Group 實例中一起遊玩。\nGroup 頁面：{url}",
    ko: "VRChat Group「{name}」 가입 희망을 접수했습니다.\n주인이 확인한 뒤, 등록한 표시 이름(**{vrcName}**)의 계정으로 Group 초대를 보냅니다. 시간이 조금 걸릴 수 있습니다. 초대가 오면 VRChat 알림에서 수락해 주세요.\n\nGroup에 들어가면 친구가 아닌 분과도 Group 인스턴스에서 함께 놀 수 있습니다.\nGroup 페이지: {url}",
  },
  "group.invited": {
    ja: "VRChat の Group「{name}」の招待を、表示名 **{vrcName}** のアカウントへ送りました。\nVRChat の通知から承諾してください。\n\nGroup に入ると、フレンドでない方とも、Group のインスタンスで一緒に遊べます。\nGroup のページ: {url}",
    en: "An invite to the VRChat Group \"{name}\" has been sent to the account with the display name **{vrcName}**. Accept it from your VRChat notifications.\n\nIn the Group, you can play together in Group instances, even with people who are not your friends.\nGroup page: {url}",
    "zh-CN": "已向显示名称为 **{vrcName}** 的账号发送 VRChat Group「{name}」的邀请。\n请在 VRChat 的通知中接受。\n\n加入 Group 后，即使不是好友，也可以在 Group 实例中一起游玩。\nGroup 页面：{url}",
    "zh-TW": "已向顯示名稱為 **{vrcName}** 的帳號發送 VRChat Group「{name}」的邀請。\n請在 VRChat 的通知中接受。\n\n加入 Group 後，即使不是好友，也可以在 Group 實例中一起遊玩。\nGroup 頁面：{url}",
    ko: "표시 이름 **{vrcName}** 계정으로 VRChat Group「{name}」 초대를 보냈습니다. VRChat 알림에서 수락해 주세요.\n\nGroup에 들어가면 친구가 아닌 분과도 Group 인스턴스에서 함께 놀 수 있습니다.\nGroup 페이지: {url}",
  },
  "group.accepted": {
    ja: "VRChat の Group「{name}」への参加の申請を承認しました。",
    en: "Your request to join the VRChat Group \"{name}\" has been approved.",
    "zh-CN": "已批准你加入 VRChat Group「{name}」的申请。",
    "zh-TW": "已批准你加入 VRChat Group「{name}」的申請。",
    ko: "VRChat Group「{name}」 가입 신청을 승인했습니다.",
  },
  "group.alreadyMember": {
    ja: "既に、VRChat の Group「{name}」に入っています。",
    en: "You are already in the VRChat Group \"{name}\".",
    "zh-CN": "你已经在 VRChat Group「{name}」中。",
    "zh-TW": "你已經在 VRChat Group「{name}」中。",
    ko: "이미 VRChat Group「{name}」에 들어가 있습니다.",
  },
  "group.alreadyInvited": {
    ja: "VRChat の Group「{name}」の招待は、送信済みです。\nVRChat の通知から承諾してください。",
    en: "An invite to the VRChat Group \"{name}\" has already been sent. Accept it from your VRChat notifications.",
    "zh-CN": "VRChat Group「{name}」的邀请已发送。\n请在 VRChat 的通知中接受。",
    "zh-TW": "VRChat Group「{name}」的邀請已發送。\n請在 VRChat 的通知中接受。",
    ko: "VRChat Group「{name}」 초대는 이미 보냈습니다. VRChat 알림에서 수락해 주세요.",
  },
  "group.userNotFound": {
    ja: "VRChat で、表示名 **{vrcName}** のユーザーが見つかりませんでした。\n今の表示名と合っているかを確かめて、変わっていたら {register} の **🧾 登録** から登録し直してください。",
    en: "No VRChat user with the display name **{vrcName}** was found. Check that it matches your current display name, and register again with **Register** in {register} if it has changed.",
    "zh-CN": "在 VRChat 中找不到显示名称为 **{vrcName}** 的用户。\n请确认是否与当前显示名称一致，如已更改，请在 {register} 点击 **Register** 重新注册。",
    "zh-TW": "在 VRChat 中找不到顯示名稱為 **{vrcName}** 的使用者。\n請確認是否與目前顯示名稱一致，如已更改，請在 {register} 點擊 **Register** 重新註冊。",
    ko: "VRChat에서 표시 이름 **{vrcName}** 사용자를 찾지 못했습니다. 현재 표시 이름과 같은지 확인하고, 바뀌었다면 {register}의 **Register**로 다시 등록해 주세요.",
  },
  "member.agree": {
    ja: "18 歳以上です。同意して住人になる",
    en: "I am 18+ and I agree",
    "zh-CN": "我已满 18 岁并同意",
    "zh-TW": "我已滿 18 歲並同意",
    ko: "18세 이상이며 동의합니다",
  },
  "member.granted": {
    ja: "住人になりました。\n数分後から、住人限定のワールドに入れます。",
    en: "You are now a resident. You can enter the resident-only worlds in a few minutes.",
    "zh-CN": "你已成为居民。\n几分钟后即可进入居民限定世界。",
    "zh-TW": "你已成為居民。\n幾分鐘後即可進入居民限定世界。",
    ko: "주민이 되었습니다. 몇 분 후부터 주민 전용 월드에 들어갈 수 있습니다.",
  },
  "member.pending": {
    ja: "登録を受け付けました。\n{date} になると、自動で住人になります。",
    en: "Your application is recorded. You will become a resident automatically on {date}.",
    "zh-CN": "已收到你的登记。\n到 {date} 将自动成为居民。",
    "zh-TW": "已收到你的登記。\n到 {date} 將自動成為居民。",
    ko: "등록을 접수했습니다. {date}이 되면 자동으로 주민이 됩니다.",
  },
  "member.leave": {
    ja: "住人をやめる",
    en: "Stop being a resident",
    "zh-CN": "取消居民登记",
    "zh-TW": "取消居民登記",
    ko: "주민 등록 취소",
  },
  // ---- 申請制（member.mode が apply のとき） ----
  "member.explainApply": {
    ja: "**住人の申請**\n住人には、申請して、確認が済んだ方だけがなれます。\n支援の有無は関係ありません。\n・18 歳以上の方だけ申請してください\n・確認のために、登録した VRChat の表示名から、公開されているプロフィールを拝見します\n・確認には日数がかかることがあります\n・見送る場合もあります\n・くわしい案内は、確認が済んだ方にお見せします",
    en: "**Resident application**\nOnly people who apply and are approved become residents. It does not matter whether you are a supporter.\n- Please apply only if you are 18 or older\n- To review your application, we look at the public VRChat profile of the display name you registered\n- The review can take some days, and an application may be declined\n- The details are shown to you after your application is approved",
    "zh-CN": "**居民申请**\n只有提出申请并通过确认的人才能成为居民。\n与是否支持无关。\n・请仅在年满 18 岁时申请\n・为了确认，我们会查看你所注册的 VRChat 显示名称的公开个人资料\n・确认可能需要几天时间，也可能不予通过\n・详细说明将在确认通过后向你展示",
    "zh-TW": "**居民申請**\n只有提出申請並通過確認的人才能成為居民。\n與是否支持無關。\n・請僅在年滿 18 歲時申請\n・為了確認，我們會查看你所註冊的 VRChat 顯示名稱的公開個人資料\n・確認可能需要幾天時間，也可能不予通過\n・詳細說明將在確認通過後向你展示",
    ko: "**주민 신청**\n주민은 신청 후 확인이 끝난 분만 될 수 있습니다. 후원 여부와는 관계없습니다.\n・18세 이상인 분만 신청해 주세요\n・확인을 위해, 등록한 VRChat 표시 이름의 공개 프로필을 봅니다\n・확인에는 며칠이 걸릴 수 있으며, 보류될 수도 있습니다\n・자세한 안내는 확인이 끝난 분께 보여 드립니다",
  },
  "member.confirmApply": {
    ja: "下のボタンを押すと、18 歳以上であることを確認して、申請します。",
    en: "Pressing the button below confirms that you are 18 or older, and sends your application.",
    "zh-CN": "点击下方按钮即表示确认你已满 18 岁，并提出申请。",
    "zh-TW": "點擊下方按鈕即表示確認你已滿 18 歲，並提出申請。",
    ko: "아래 버튼을 누르면 18세 이상임을 확인하고 신청합니다.",
  },
  "member.apply": {
    ja: "18 歳以上です。申請する",
    en: "I am 18+. Apply",
    "zh-CN": "我已满 18 岁。申请",
    "zh-TW": "我已滿 18 歲。申請",
    ko: "18세 이상입니다. 신청",
  },
  "verify.dm": {
    ja: "**住人の申請の、本人確認のお願いです。**\n登録された VRChat のアカウントがご本人のものかを確かめるため、そのアカウントのプロフィールの「ステータス」か「自己紹介（Bio）」に、次の文字を一時的に入れてください。\n**{code}**\n入れたら、{resident} で **🏠 住人** を押し、**確かめる** を押してください。\n確認が済んだら、消して構いません。",
    en: "**Identity check for your resident application.**\nTo confirm that the VRChat account you registered is yours, please put the following text in the status or the bio of that account for a short time.\n**{code}**\nThen press **🏠 Resident** in {resident}, and press **Verify**. You can remove the text after the check.",
    "zh-CN": "**关于居民申请的本人确认。**\n为了确认已注册的 VRChat 账号是你本人的，请在该账号个人资料的“状态”或“个人简介（Bio）”中暂时填入以下文字。\n**{code}**\n填好后，请在 {resident} 点击 **🏠 Resident**，再点击 **Verify**。\n确认完成后即可删除。",
    "zh-TW": "**關於居民申請的本人確認。**\n為了確認已註冊的 VRChat 帳號是你本人的，請在該帳號個人資料的「狀態」或「自我介紹（Bio）」中暫時填入以下文字。\n**{code}**\n填好後，請在 {resident} 點擊 **🏠 Resident**，再點擊 **Verify**。\n確認完成後即可刪除。",
    ko: "**주민 신청의 본인 확인 요청입니다.**\n등록한 VRChat 계정이 본인의 것인지 확인하기 위해, 그 계정 프로필의 상태 메시지나 자기소개(Bio)에 아래 문자를 잠시 넣어 주세요.\n**{code}**\n넣은 뒤 {resident}에서 **🏠 Resident**를 누르고 **Verify**를 눌러 주세요. 확인이 끝나면 지워도 됩니다.",
  },
  "verify.prompt": {
    ja: "**本人確認のお願いが届いています。**\nVRChat のプロフィールの「ステータス」か「自己紹介（Bio）」に、次の文字を入れてから、**確かめる** を押してください。\n**{code}**\n確認が済んだら、消して構いません。",
    en: "**You have an identity check request.**\nPut the following text in the status or the bio of your VRChat profile, then press **Verify**.\n**{code}**\nYou can remove it after the check.",
    "zh-CN": "**收到了本人确认的请求。**\n请在 VRChat 个人资料的“状态”或“个人简介（Bio）”中填入以下文字，然后点击 **Verify**。\n**{code}**\n确认完成后即可删除。",
    "zh-TW": "**收到了本人確認的請求。**\n請在 VRChat 個人資料的「狀態」或「自我介紹（Bio）」中填入以下文字，然後點擊 **Verify**。\n**{code}**\n確認完成後即可刪除。",
    ko: "**본인 확인 요청이 도착했습니다.**\nVRChat 프로필의 상태 메시지나 자기소개(Bio)에 아래 문자를 넣은 뒤 **Verify**를 눌러 주세요.\n**{code}**\n확인이 끝나면 지워도 됩니다.",
  },
  "verify.button": { ja: "確かめる / Verify", en: "Verify", "zh-CN": "Verify", "zh-TW": "Verify", ko: "Verify" },
  "verify.ok": {
    ja: "確認できました。\n確認の文字は、もう消して構いません。\n申請の確認が済むまで、お待ちください。",
    en: "Verified. You can remove the text now. Please wait until your application has been reviewed.",
    "zh-CN": "确认完成。\n现在可以删除那段文字了。\n请等待申请审核完成。",
    "zh-TW": "確認完成。\n現在可以刪除那段文字了。\n請等待申請審核完成。",
    ko: "확인되었습니다. 이제 문자를 지워도 됩니다. 신청 확인이 끝날 때까지 기다려 주세요.",
  },
  "verify.notFound": {
    ja: "まだ見つかりません。\nVRChat のプロフィールの「ステータス」か「自己紹介」に「{code}」が入っているかを確かめてください。\n入れてから読み取れるまで、少しかかることがあります。\n1 分ほど待ってから、もう一度押してください。",
    en: "Not found yet. Check that \"{code}\" is in the status or the bio of your VRChat profile. It can take a little while to show up. Please wait about a minute and press it again.",
    "zh-CN": "还没有找到。\n请确认 VRChat 个人资料的“状态”或“个人简介”中是否填入了“{code}”。\n填入后可能需要一点时间才能读取到。\n请等待约 1 分钟后再点击一次。",
    "zh-TW": "還沒有找到。\n請確認 VRChat 個人資料的「狀態」或「自我介紹」中是否填入了「{code}」。\n填入後可能需要一點時間才能讀取到。\n請等待約 1 分鐘後再點擊一次。",
    ko: "아직 찾지 못했습니다. VRChat 프로필의 상태 메시지나 자기소개에 \"{code}\"가 들어 있는지 확인해 주세요. 넣은 뒤 읽힐 때까지 조금 걸릴 수 있습니다. 1분 정도 기다린 뒤 다시 눌러 주세요.",
  },
  "verify.wait": {
    ja: "少し待ってから、もう一度押してください（あと {seconds} 秒）。",
    en: "Please wait a little and press it again (in {seconds} seconds).",
    "zh-CN": "请稍等后再点击一次（还需 {seconds} 秒）。",
    "zh-TW": "請稍等後再點擊一次（還需 {seconds} 秒）。",
    ko: "잠시 기다린 뒤 다시 눌러 주세요 ({seconds}초 후).",
  },
  "verify.noApi": {
    ja: "今は自動で確かめられません。\n管理者が手で確かめますので、そのままお待ちください。",
    en: "We can't check it automatically right now. An admin will check it by hand, so please wait.",
    "zh-CN": "现在无法自动确认。\n管理员会手动确认，请稍候。",
    "zh-TW": "現在無法自動確認。\n管理員會手動確認，請稍候。",
    ko: "지금은 자동으로 확인할 수 없습니다. 관리자가 직접 확인하니 기다려 주세요.",
  },
  "verify.userNotFound": {
    ja: "登録した表示名「{name}」の VRChat アカウントが見つかりませんでした。\n表示名が VRChat のプロフィールと同じかを確かめてください。",
    en: "We could not find a VRChat account named \"{name}\" (your registered display name). Please check that it matches your VRChat profile.",
    "zh-CN": "找不到显示名称为“{name}”（你注册的显示名称）的 VRChat 账号。\n请确认它与 VRChat 个人资料上的名称一致。",
    "zh-TW": "找不到顯示名稱為「{name}」（你註冊的顯示名稱）的 VRChat 帳號。\n請確認它與 VRChat 個人資料上的名稱一致。",
    ko: "등록한 표시 이름 \"{name}\"의 VRChat 계정을 찾지 못했습니다. VRChat 프로필의 이름과 같은지 확인해 주세요.",
  },
  "verify.already": { ja: "本人確認は済んでいます。", en: "Your identity check is already done.", "zh-CN": "本人确认已完成。", "zh-TW": "本人確認已完成。", ko: "본인 확인은 이미 끝났습니다." },
  "rename.dm": {
    ja: "**表示名を、登録し直せるようにしました。**\n{register} で **🧾 登録** を押して、VRChat の表示名を入れ直してください。\nVRChat のプロフィールに出ている名前です。",
    en: "**You can register your display name again now.**\nPress **🧾 Register** in {register} and enter your VRChat display name again (the name shown on your VRChat profile).",
    "zh-CN": "**已可以重新注册显示名称。**\n请在 {register} 点击 **🧾 Register**，重新输入 VRChat 显示名称。\n就是 VRChat 个人资料上显示的名称。",
    "zh-TW": "**已可以重新註冊顯示名稱。**\n請在 {register} 點擊 **🧾 Register**，重新輸入 VRChat 顯示名稱。\n就是 VRChat 個人資料上顯示的名稱。",
    ko: "**표시 이름을 다시 등록할 수 있게 되었습니다.**\n{register}에서 **🧾 Register**를 누르고 VRChat 표시 이름을 다시 입력해 주세요. VRChat 프로필에 표시되는 이름입니다.",
  },
  "member.applied": {
    ja: "申請を受け付けました。\n確認が済んだら、DM でお知らせします。\nそのあと、{resident} でもう一度 **🏠 住人** を押して案内に同意すると、住人になります。\n結果は **状態** のボタンでも見られます。",
    en: "Your application has been received. We will let you know by DM when the review is done. After that, press **🏠 Resident** in {resident} once more and agree to the guide to become a resident. You can also check the result with **Status**.",
    "zh-CN": "已收到你的申请。\n确认完成后会通过私信通知你。\n之后请在 {resident} 再次点击 **🏠 Resident** 并同意说明，即可成为居民。\n结果也可通过 **Status** 查看。",
    "zh-TW": "已收到你的申請。\n確認完成後會透過私訊通知你。\n之後請在 {resident} 再次點擊 **🏠 Resident** 並同意說明，即可成為居民。\n結果也可透過 **Status** 查看。",
    ko: "신청을 접수했습니다. 확인이 끝나면 DM으로 알려 드립니다. 그 후 {resident}에서 **🏠 Resident**를 한 번 더 눌러 안내에 동의하면 주민이 됩니다. 결과는 **Status**로도 확인할 수 있습니다.",
  },
  "member.explainApproved": {
    ja: "**住人の案内**\n申請の確認が済みました。\n住人になる前に、次の内容を読んで、同意してください。\n・住人限定のワールドには、大人向けの表現や、刺激の強い演出があります\n・18 歳以上で、そうした内容に抵抗がない方だけ進んでください",
    en: "**Resident guide**\nYour application has been approved. Before you become a resident, please read the following and agree.\n- Resident-only worlds contain adult themes and intense effects\n- Please continue only if you are 18 or older and comfortable with such content",
    "zh-CN": "**居民说明**\n你的申请已通过确认。\n成为居民之前，请阅读以下内容并同意。\n・居民限定世界包含面向成人的表现和较强烈的演出\n・请仅在年满 18 岁且不介意此类内容时继续",
    "zh-TW": "**居民說明**\n你的申請已通過確認。\n成為居民之前，請閱讀以下內容並同意。\n・居民限定世界包含面向成人的表現和較強烈的演出\n・請僅在年滿 18 歲且不介意此類內容時繼續",
    ko: "**주민 안내**\n신청 확인이 끝났습니다. 주민이 되기 전에 아래 내용을 읽고 동의해 주세요.\n・주민 전용 월드에는 성인용 표현과 자극이 강한 연출이 있습니다\n・18세 이상이며 그런 내용에 거부감이 없는 분만 진행해 주세요",
  },
  "member.state.approved": {
    ja: "確認済み（{resident} で **🏠 住人** を押して、案内に同意すると有効になります）",
    en: "approved (press **🏠 Resident** in {resident} and agree to the guide to activate)",
    "zh-CN": "已通过确认（在 {resident} 点击 **🏠 Resident** 并同意说明后生效）",
    "zh-TW": "已通過確認（在 {resident} 點擊 **🏠 Resident** 並同意說明後生效）",
    ko: "확인 완료 ({resident}에서 **🏠 Resident**를 눌러 안내에 동의하면 유효해집니다)",
  },
  "member.state.applied": { ja: "申請中（確認待ち）", en: "applied (waiting for review)", "zh-CN": "申请中（等待确认）", "zh-TW": "申請中（等待確認）", ko: "신청 중 (확인 대기)" },
  "member.state.declined": {
    ja: "今回は見送り（次に申請できるのは {date} 以降）",
    en: "not approved this time (you can apply again from {date})",
    "zh-CN": "本次未通过（{date} 之后可再次申请）",
    "zh-TW": "本次未通過（{date} 之後可再次申請）",
    ko: "이번에는 보류 ({date} 이후 다시 신청 가능)",
  },
  "member.applyTooEarly": {
    ja: "申請は、サーバーに参加してから {days} 日後にできます（{date} 以降）。",
    en: "You can apply {days} days after joining this server (from {date}).",
    "zh-CN": "加入本服务器 {days} 天后才能申请（{date} 之后）。",
    "zh-TW": "加入本伺服器 {days} 天後才能申請（{date} 之後）。",
    ko: "서버에 참가한 지 {days}일 후부터 신청할 수 있습니다 ({date} 이후).",
  },
  "member.declinedWait": {
    ja: "前回の申請は、見送りとなりました。\n次に申請できるのは {date} 以降です。",
    en: "Your last application was not approved. You can apply again from {date}.",
    "zh-CN": "上次申请未通过。\n{date} 之后可再次申请。",
    "zh-TW": "上次申請未通過。\n{date} 之後可再次申請。",
    ko: "지난 신청은 보류되었습니다. {date} 이후 다시 신청할 수 있습니다.",
  },
  "member.withdraw": { ja: "申請を取り下げる", en: "Withdraw application", "zh-CN": "撤回申请", "zh-TW": "撤回申請", ko: "신청 철회" },
  "member.withdrawn": { ja: "申請を取り下げました。", en: "Your application has been withdrawn.", "zh-CN": "已撤回申请。", "zh-TW": "已撤回申請。", ko: "신청을 철회했습니다." },
  "member.approvedDm": {
    ja: "**住人の申請の確認が済みました。**\n**あと 1 つで完了です。**\n{resident} で **🏠 住人** のボタンを押してください（**🧾 登録** のボタンではありません）。\n案内を読んで同意すると、住人になります。",
    en: "**Your resident application has been approved. One more step.**\nPress **🏠 Resident** in {resident} (not **Register**). Read the guide and agree, and you become a resident.",
    "zh-CN": "**你的居民申请已通过确认。**\n**还差一步。**\n请在 {resident} 点击 **🏠 Resident**（不是 **Register**）。\n阅读说明并同意后，即可成为居民。",
    "zh-TW": "**你的居民申請已通過確認。**\n**還差一步。**\n請在 {resident} 點擊 **🏠 Resident**（不是 **Register**）。\n閱讀說明並同意後，即可成為居民。",
    ko: "**주민 신청 확인이 끝났습니다. 한 단계만 남았습니다.**\n{resident}에서 **🏠 Resident**를 눌러 주세요(**Register**가 아닙니다). 안내를 읽고 동의하면 주민이 됩니다.",
  },
  // 認定のあと、「住人」と間違えて「登録」を押した人に、手続きの続きの前に見せる一言
  "member.registerRedirect": {
    ja: "表示名は登録済みです（**{name}**）。\n住人の手続きの続きは、こちらです。",
    en: "Your display name is already registered (**{name}**). Here is the rest of the resident steps.",
    "zh-CN": "显示名称已注册（**{name}**）。\n以下是居民手续的后续步骤。",
    "zh-TW": "顯示名稱已註冊（**{name}**）。\n以下是居民手續的後續步驟。",
    ko: "표시 이름은 이미 등록되어 있습니다(**{name}**). 주민 절차의 다음 단계입니다.",
  },
  // 表示名を登録した直後（申請制で、まだ申請していない人）に足す一言
  "member.nextApply": {
    ja: "住人の申請をする方は、続けて {resident} で **🏠 住人** を押してください。",
    en: "To apply to become a resident, press **🏠 Resident** in {resident} next.",
    "zh-CN": "如需申请成为居民，请接着在 {resident} 点击 **🏠 Resident**。",
    "zh-TW": "如需申請成為居民，請接著在 {resident} 點擊 **🏠 Resident**。",
    ko: "주민 신청을 하려면 이어서 {resident}에서 **🏠 Resident**를 눌러 주세요.",
  },
  "member.approvedDmGroup": {
    ja: "VRChat の Group に入りたい方は、{group} の **👥 グループ** を押してください。",
    en: "To join our VRChat Group, press **👥 Group** in {group}.",
    "zh-CN": "想加入 VRChat Group 的话，请在 {group} 点击 **👥 Group**。",
    "zh-TW": "想加入 VRChat Group 的話，請在 {group} 點擊 **👥 Group**。",
    ko: "VRChat Group에 들어가려면 {group}에서 **👥 Group**을 눌러 주세요.",
  },
  "member.left": {
    ja: "住人をやめました。",
    en: "You are no longer a resident.",
    "zh-CN": "已取消居民登记。",
    "zh-TW": "已取消居民登記。",
    ko: "주민 등록을 취소했습니다.",
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
