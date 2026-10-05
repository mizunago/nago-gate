// SupporterCreditsBoard.cs
// クレジット表示に同意した支援者の名前をティアごとに並べる。
// 公開ワールドにも置ける（ゲート不要）。
// 見ている本人の状態（支援のティア・住人かどうか）と、Discord の案内は、別のパネル（infoText）に出す。
// 住人（表の呼び名。リストやコードの中の名前は member）向けの物が無いワールドでは、showMemberStatus を OFF にすると、住人に触れる表示をやめる。
// 別のパネルが無いシーンでは、本人の状態を題の下に、案内を一番下に出す。
// 住人の名前は一覧に出さない（リストにも名前は載っていない）。
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
    [Tooltip("1 ページに出す名前の数。これより多いと、ページを自動で切り替える")]
    [SerializeField] private int namesPerPage = 30;
    [Tooltip("ページを切り替える間隔（秒）")]
    [SerializeField] private float pageSeconds = 8f;
    [Tooltip("任意。入れると、本人がこのワールドに入れないときに、その旨を出す")]
    [SerializeField] private SupporterGate gate;
    [Tooltip("任意。入れると、本人の状態と案内（Discord の招待 URL）を、名前の一覧とは別のパネルに出す")]
    [SerializeField] private TextMeshProUGUI infoText;
    [Tooltip("本人の状態と案内で、住人に触れるか。住人向けの物が無いワールドでは OFF にする（支援者のことだけを出す）")]
    [SerializeField] private bool showMemberStatus = true;

    [Header("Texts (optional)")]
    [Tooltip("文言の表（多言語の JSON）。未設定なら日本語の既定の文を使う")]
    [SerializeField] private NoticeTable texts;
    [Tooltip("共通の通知（言語の判定に使う）。未設定なら VRChat の言語設定を直接見る")]
    [SerializeField] private NoticeHub notice;

    private bool _received;
    private int _page;
    private int _pageCount = 1;
    private bool _pageTimer;

    void Start()
    {
        if (text != null) text.text = TitleLine() + "\n" + Note(SupporterRegistry.ColorDim, T("credits.loading", "読み込み中..."));
        if (infoText != null) infoText.text = Heading(T("info.title", "あなたの状態")) + Note(SupporterRegistry.ColorDim, T("credits.loading", "読み込み中..."));
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
            text.text = TitleLine() + "\n" + Note(SupporterRegistry.ColorWarn, T("credits.error", "リストを取得できませんでした"));
            if (infoText != null) infoText.text = Heading(T("info.title", "あなたの状態")) + Note(SupporterRegistry.ColorWarn, T("credits.error", "リストを取得できませんでした"));
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append(TitleLine());

        // 本人の状態と案内は、別のパネルがあればそちらに出す（名前の一覧と混ざると、誤解を招くため）
        string guide = GuideLines();
        if (infoText != null)
        {
            string info = Heading(T("info.title", "あなたの状態")) + "<size=110%>" + StatusLines(true) + "</size>";
            if (guide.Length > 0) info += "\n\n" + Heading(T("info.guide.title", "ご案内")) + guide;
            infoText.text = info;
        }
        else
        {
            string status = StatusLines(false);
            if (status.Length > 0) sb.Append("\n<size=70%>").Append(status).Append("</size>");
        }

        // 鍵つきのリストは、名前を元に戻し終わるまで少しかかる
        if (!registry._AreCreditsReady())
        {
            sb.Append("\n").Append(Note(SupporterRegistry.ColorDim, T("credits.loading", "読み込み中...")));
            text.text = sb.ToString();
            return;
        }

        int total = registry._GetCreditCount();
        int tierCount = registry._GetTierCount();

        // 人数が多いときは、ページに分けて自動で切り替える
        int per = namesPerPage > 0 ? namesPerPage : 30;
        _pageCount = total > per ? (total + per - 1) / per : 1;
        if (_page >= _pageCount) _page = 0;
        int first = _page * per, last = first + per;   // このページに出す範囲（ランクの高い順に数えた番号）
        int index = 0;

        // ランクの高い順にティアごとに出力
        int maxRank = registry._GetMaxRank();
        for (int rank = maxRank; rank >= 1; rank--)
        {
            string label = registry._GetTierLabel(rank);
            if (label.Length == 0 && tierCount > 0) continue;
            int col = 0;
            for (int i = 0; i < total; i++)
            {
                if (registry._GetCreditRank(i) != rank) continue;
                int at = index++;
                if (at < first || at >= last) continue;
                // ティアの見出しは、そのページで最初の名前の前に出す
                if (col == 0) sb.Append("\n\n<color=").Append(registry._GetTierColorHex(rank)).Append("><b>").Append(label).Append("</b></color>\n");
                else sb.Append(namesPerLine > 0 && col % namesPerLine == 0 ? "\n" : separator);
                // 名前の途中では折り返さない（長い行は、名前と名前の間で折り返す）
                sb.Append("<nobr>").Append(EscapeRichText(registry._GetCreditName(i))).Append("</nobr>");
                col++;
            }
        }
        if (total == 0) sb.Append("\n").Append(Note(SupporterRegistry.ColorDim, T("credits.empty", "まだ登録がありません")));
        // 別のパネルが無いときは、案内を一番下に出す（題のすぐ下には置かない）
        if (infoText == null && guide.Length > 0) sb.Append("\n\n<size=75%>").Append(guide).Append("</size>");
        if (_pageCount > 1) sb.Append("\n\n<size=60%><color=").Append(SupporterRegistry.ColorDim).Append(">").Append((_page + 1).ToString()).Append(" / ").Append(_pageCount.ToString()).Append("</color></size>");
        text.text = sb.ToString();

        if (_pageCount > 1 && !_pageTimer)
        {
            _pageTimer = true;
            SendCustomEventDelayedSeconds(nameof(_NextPage), pageSeconds > 1f ? pageSeconds : 8f);
        }
    }

    /// <summary>次のページへ（人数が 1 ページに収まるときは何もしない）</summary>
    public void _NextPage()
    {
        _pageTimer = false;
        if (_pageCount <= 1) return;
        _page = (_page + 1) % _pageCount;
        _OnRegistryUpdated();
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

    /// <summary>本人の状態（例: "プラチナサポーター・住人"）。色のタグつき。支援者でもメンバーでもなければ空文字</summary>
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
            string name = id.Length > 0 ? T("credits.tier." + id, label) : label;
            // 名前の一覧の見出しと同じ、ティアの色を付ける
            who = "<color=" + registry._GetTierColorHex(rank) + "><b>" + EscapeRichText(name) + "</b></color>";
        }
        if (showMemberStatus && registry._IsLocalMember())
        {
            string m = "<color=" + SupporterRegistry.ColorMember + "><b>" + EscapeRichText(T("credits.you.member", "住人")) + "</b></color>";
            who = who.Length > 0 ? who + T("credits.you.sep", "・") + m : m;
        }
        return who;
    }

    /// <summary>ボードの題。名前より大きく、太く出す</summary>
    private string TitleLine()
    {
        return "<size=125%><b>" + title + "</b></size>";
    }

    /// <summary>見出し。本文と見分けがつくように、色を変えて少し小さく出す（改行つき）</summary>
    private string Heading(string s)
    {
        return "<size=80%><color=" + SupporterRegistry.ColorHeading + "><b>" + s + "</b></color></size>\n";
    }

    /// <summary>補足の 1 行（読み込み中・取得できない・登録なし）</summary>
    private string Note(string colorHex, string s)
    {
        return "<size=80%><color=" + colorHex + ">" + s + "</color></size>";
    }

    /// <summary>本人の状態の行。showNone が true なら、支援者でも住人でもないときに、その旨を出す</summary>
    private string StatusLines(bool showNone)
    {
        string s = "";
        string who = LocalStatus();
        if (who.Length > 0) s = T("credits.you.is", "あなたは{0}です").Replace("{0}", who);
        else if (showNone) s = "<size=85%><color=" + SupporterRegistry.ColorDim + ">" + (showMemberStatus && registry._HasMemberList() ? T("info.none", "支援者・住人の登録は見つかりません") : T("info.noneSupporter", "支援者の登録は見つかりません")) + "</color></size>";
        if (gate != null && !gate._IsLocalAllowed())
        {
            if (s.Length > 0) s += "\n";
            // 長い文なので少し小さくして 1 行に収める。入れないことは色でも分かるようにする
            s += "<size=75%><color=" + SupporterRegistry.ColorWarn + ">" + T("credits.you.noAccess", "あなたはこのワールドにアクセスする権限を持っていません") + "</color></size>";
        }
        return s;
    }

    /// <summary>
    /// 案内の行。リストに Discord の招待 URL があるときだけ出す。
    /// VRChat の中からは開けないので、見て打ち込める短い形（https:// を省く）で見せる
    /// </summary>
    private string GuideLines()
    {
        string invite = registry._GetLink("discord");
        if (invite.Length == 0) return "";
        // 打ち込む URL は、説明の文より大きく、太く出す
        string how = showMemberStatus ? T("info.guide", "支援と住人の申請の方法は、\nDiscord で案内しています") : T("info.guide.supporter", "支援の方法は、\nDiscord で案内しています");
        return "<size=80%>" + how + "</size>\n<size=115%><b>" + EscapeRichText(ShortUrl(invite)) + "</b></size>";
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
