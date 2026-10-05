// ボタンのパネルを置くチャンネル。登録・状態・住人・グループの 4 つ。
// 状態・住人・グループのチャンネルは任意で、未設定なら、そのボタンは登録のパネルに並ぶ（前の版と同じ）。
import type { AppConfig } from "./config.js";
import type { Lang } from "./i18n.js";

export type PanelKind = "register" | "status" | "resident" | "group";
export const PANEL_KINDS: PanelKind[] = ["register", "status", "resident", "group"];

/** そのボタンが置かれるチャンネルの ID。専用のチャンネルが無ければ、登録のチャンネル */
export function panelChannelId(config: AppConfig, kind: PanelKind): string | null {
  if (kind === "status") return config.statusChannelId ?? config.registerChannelId;
  if (kind === "resident") return config.residentChannelId ?? config.registerChannelId;
  if (kind === "group") return config.groupChannelId ?? config.registerChannelId;
  return config.registerChannelId;
}

/** そのボタンに専用のチャンネルがあるか（無ければ、登録のパネルに並べる） */
export function hasOwnChannel(config: AppConfig, kind: PanelKind): boolean {
  if (kind === "status") return config.statusChannelId !== null;
  if (kind === "resident") return config.residentChannelId !== null;
  if (kind === "group") return config.groupChannelId !== null;
  return true;
}

const WORDS: Record<PanelKind, Record<Lang, string>> = {
  register: { ja: "登録のチャンネル", en: "the register channel", "zh-CN": "注册频道", "zh-TW": "註冊頻道", ko: "등록 채널" },
  status: { ja: "状態のチャンネル", en: "the status channel", "zh-CN": "状态频道", "zh-TW": "狀態頻道", ko: "상태 채널" },
  resident: { ja: "住人のチャンネル", en: "the resident channel", "zh-CN": "居民频道", "zh-TW": "居民頻道", ko: "주민 채널" },
  group: { ja: "グループのチャンネル", en: "the group channel", "zh-CN": "Group 频道", "zh-TW": "Group 頻道", ko: "Group 채널" },
};

/** 文の中で、ボタンのあるチャンネルを指す書き方（{register} {status} {resident} {group} に入れる）。ID が無ければ言葉で書く */
export function channelRefs(config: AppConfig, lang: Lang): Record<PanelKind, string> {
  const ref = (kind: PanelKind): string => {
    const id = panelChannelId(config, kind);
    if (id) return `<#${id}>`;
    return WORDS[kind][lang] ?? WORDS[kind].en;
  };
  return { register: ref("register"), status: ref("status"), resident: ref("resident"), group: ref("group") };
}
