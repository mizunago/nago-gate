// SupporterCreditsBoard.cs
// クレジット表示に同意した支援者の名前をティアごとに並べる。
// 公開ワールドにも置ける（ゲート不要）。

using UdonSharp;
using UnityEngine;
using TMPro;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SupporterCreditsBoard : UdonSharpBehaviour
{
    [SerializeField] private SupporterRegistry registry;
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private string title = "Special Thanks";
    [Tooltip("1 行に並べる名前の数（0 = 改行区切り）")]
    [SerializeField] private int namesPerLine = 3;
    [SerializeField] private string separator = "　";

    void Start()
    {
        if (text != null) text.text = title + "\n<size=70%>読み込み中...</size>";
        if (registry != null) registry._RegisterListener(this);
    }

    public void _OnRegistryUpdated()
    {
        if (registry == null || text == null) return;
        if (!registry._IsLoaded())
        {
            text.text = title + "\n<size=70%>リストを取得できませんでした</size>";
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append(title);
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
        if (total == 0) sb.Append("\n<size=70%>まだ登録がありません</size>");
        text.text = sb.ToString();
    }

    // 名前に < > が含まれていても TMP のタグとして解釈されないようにする
    private string EscapeRichText(string s)
    {
        if (s == null) return "";
        return s.Replace("<", "<noparse><</noparse>");
    }
}
