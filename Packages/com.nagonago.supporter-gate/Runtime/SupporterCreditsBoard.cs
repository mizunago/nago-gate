// SupporterCreditsBoard.cs
// クレジット表示に同意した支援者の名前をティアごとに並べる。
// 公開ワールドにも置ける（ゲート不要）。
// 題の下に、見ている本人の状態（支援のティア・メンバーかどうか）と、リストに入っている Discord の招待 URL を出す。
// メンバーの名前は一覧に出さない（リストにも名前は載っていない）。
// 文言は texts（NoticeTable）から今の言語で引く。texts が無ければ日本語の既定の文を使う。

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;
using NagoNotice;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SupporterCreditsBoard : UdonSharpBehaviour
{
    [SerializeField] private SupporterRegistry registry;
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private string title = "Special Thanks";
    [Tooltip("1 行に並べる名前の数（0 = 改行区切り）")]
    [SerializeField] private int namesPerLine = 3;
    [SerializeField] private string separator = "　";
    [Tooltip("任意。入れると、本人がこのワールドに入れないときに、その旨を出す")]
    [SerializeField] private SupporterGate gate;

    [Header("Texts (optional)")]
    [Tooltip("文言の表（多言語の JSON）。未設定なら日本語の既定の文を使う")]
    [SerializeField] private NoticeTable texts;
    [Tooltip("共通の通知（言語の判定に使う）。未設定なら VRChat の言語設定を直接見る")]
    [SerializeField] private NoticeHub notice;

    private bool _received;

    void Start()
    {
        if (text != null) text.text = title + "\n<size=70%>" + T("credits.loading", "読み込み中...") + "</size>";
        if (registry != null) registry._RegisterListener(this);
        if (notice != null) notice._RegisterListener(this);
        if (gate != null) gate._RegisterListener(this);
    }

    /// <summary>ゲートの状態（入場の可否）が変わったら、本人向けの行を出し直す</summary>
    public void _OnGateUpdated()
    {
        if (_received) _OnRegistryUpdated();
    }

    public void _OnNoticeLanguageChanged()
    {
        if (_received) _OnRegistryUpdated();
    }

    public override void OnLanguageChanged(string language)
    {
        if (notice == null && _received) _OnRegistryUpdated();
    }

    public void _OnRegistryUpdated()
    {
        if (registry == null || text == null) return;
        _received = true;
        if (!registry._IsLoaded())
        {
            text.text = title + "\n<size=70%>" + T("credits.error", "リストを取得できませんでした") + "</size>";
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append(title);

        // リストに Discord の招待 URL が入っていれば、題の下に出す。
        // VRChat の中からは開けないので、見て打ち込める短い形（https:// を省く）で見せる
        string invite = registry._GetLink("discord");
        if (invite.Length > 0)
        {
            sb.Append("\n<size=70%>").Append(T("credits.discord", "Discord")).Append("  ").Append(EscapeRichText(ShortUrl(invite))).Append("</size>");
        }

        // 見ている本人の状態。支援のティアと、メンバーかどうかを 1 行で出す
        string who = LocalStatus();
        if (who.Length > 0)
        {
            sb.Append("\n<size=70%>").Append(T("credits.you.is", "あなたは{0}です").Replace("{0}", EscapeRichText(who))).Append("</size>");
        }
        if (gate != null && !gate._IsLocalAllowed())
        {
            sb.Append("\n<size=70%>").Append(T("credits.you.noAccess", "あなたはこのワールドにアクセスする権限を持っていません")).Append("</size>");
        }

        int total = registry._GetCreditCount();
        int tierCount = registry._GetTierCount();

        // ランクの高い順にティアごとに出力
        int maxRank = registry._GetMaxRank();
        for (int rank = maxRank; rank >= 1; rank--)
        {
            string label = registry._GetTierLabel(rank);
            if (label.Length == 0 && tierCount > 0) continue;
            int n = 0;
            for (int i = 0; i < total; i++) if (registry._GetCreditRank(i) == rank) n++;
            if (n == 0) continue;

            sb.Append("\n\n<color=").Append(registry._GetTierColorHex(rank)).Append("><b>").Append(label).Append("</b></color>\n");
            int col = 0;
            for (int i = 0; i < total; i++)
            {
                if (registry._GetCreditRank(i) != rank) continue;
                if (col > 0) sb.Append(namesPerLine > 0 && col % namesPerLine == 0 ? "\n" : separator);
                sb.Append(EscapeRichText(registry._GetCreditName(i)));
                col++;
            }
        }
        if (total == 0) sb.Append("\n<size=70%>" + T("credits.empty", "まだ登録がありません") + "</size>");
        text.text = sb.ToString();
    }

    private string Lang()
    {
        if (notice != null) return notice._GetLanguage();
        string l = VRCPlayerApi.GetCurrentLanguage();
        if (l == null || l.Length == 0) return "en";
        return l;
    }

    private string T(string key, string fallback)
    {
        if (texts != null)
        {
            string s = texts._Get(key, Lang());
            if (s != null) return s;
        }
        return fallback;
    }

    /// <summary>本人の状態（例: "プラチナサポーター・メンバー"）。支援者でもメンバーでもなければ空文字</summary>
    private string LocalStatus()
    {
        string who = "";
        int rank = registry._GetLocalRank();
        if (rank >= 1)
        {
            // 呼び名は、文言の表の credits.tier.<ティアの id> を優先し、無ければリストの label を使う
            string label = registry._GetTierLabel(rank);
            if (label == null || label.Length == 0) label = T("credits.you.supporter", "支援者");
            string id = registry._GetTierId(rank);
            who = id.Length > 0 ? T("credits.tier." + id, label) : label;
        }
        if (registry._IsLocalMember())
        {
            string m = T("credits.you.member", "メンバー");
            who = who.Length > 0 ? who + T("credits.you.sep", "・") + m : m;
        }
        return who;
    }

    private string ShortUrl(string url)
    {
        string s = url;
        if (s.StartsWith("https://")) s = s.Substring(8);
        else if (s.StartsWith("http://")) s = s.Substring(7);
        if (s.EndsWith("/")) s = s.Substring(0, s.Length - 1);
        return s;
    }

    // 名前に < > が含まれていても TMP のタグとして解釈されないようにする
    private string EscapeRichText(string s)
    {
        if (s == null) return "";
        return s.Replace("<", "<noparse><</noparse>");
    }
}
