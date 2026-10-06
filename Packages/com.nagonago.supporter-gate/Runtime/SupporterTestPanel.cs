// SupporterTestPanel.cs
// テスト用のパネル。Unity の Play モード（ClientSim）と、SDK の Build & Test のときだけ出る。
// ボタンで、自分（ローカルプレイヤー）のランクと住人の扱いを、リストの結果の代わりに使う（SupporterRegistry の上書き）。
// ゲート・ボード・住人だけに見せる物など、リストを見ている物は、本物と同じ流れで切り替わる。ほかの人の画面には影響しない。
//
// 出し分けはエディタ（SupporterTestPanelBuild）が行う。シーンでは非表示のまま置いておく。
//   Play モード        : 出す。誰でも使える（自分のパソコンの中だけで動くため。目印の playModeMark を表示にする）
//   SDK の Build & Test : 出す。Gate の Owner Display Names の人だけが使える
//   それ以外のビルド    : パネルごと取り除く（Build and Upload を含む）

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SupporterTestPanel : UdonSharpBehaviour
{
    [SerializeField] private SupporterRegistry registry;
    [Tooltip("持ち主の名前を見るのと、入場の可否を出すのに使う。無ければ、Build & Test では誰も使えない")]
    [SerializeField] private SupporterGate gate;
    [Tooltip("ON のあいだは、Gate の Owner Display Names の人だけが使える（Build & Test のとき）。Play モードでは、この設定に関係なく誰でも使える")]
    [SerializeField] private bool ownerOnly = true;
    [Tooltip("Play モードの目印（空のオブジェクト）。エディタが Play モードのときだけ表示にする。表示なら、誰でも使える")]
    [SerializeField] private GameObject playModeMark;
    [SerializeField] private TextMeshProUGUI stateText;
    [Tooltip("ランクのボタンの文字（なし・いちばん下のランク・いちばん上のランク の順）")]
    [SerializeField] private TextMeshProUGUI[] rankLabels;

    void Start()
    {
        if (registry != null) registry._RegisterListener(this);
        if (gate != null) gate._RegisterListener(this);
        Refresh();
    }

    public void _OnRegistryUpdated() { Refresh(); }
    public void _OnGateUpdated() { Refresh(); }

    // ---- ボタン ----
    public void _RankNone() { SetRank(0); }
    public void _RankLow() { SetRank(LowRank()); }
    public void _RankHigh() { SetRank(HighRank()); }
    public void _ResidentOff() { SetResident(false); }
    public void _ResidentOn() { SetResident(true); }

    public void _ResetToList()
    {
        if (!_CanUse()) { Refresh(); return; }
        registry._ClearTestOverride();
        Refresh();
    }

    /// <summary>このパネルを今使えるか（テストの検査からも見る）</summary>
    public bool _CanUse()
    {
        if (registry == null) return false;
        if (playModeMark != null && playModeMark.activeSelf) return true;
        if (!ownerOnly) return true;
        return gate != null && gate._IsLocalOwnerName();
    }

    // 片方の軸だけ変える。もう片方は今の扱い（上書き中なら上書きの値、そうでなければリストの結果）を引き継ぐ
    private void SetRank(int rank)
    {
        if (!_CanUse()) { Refresh(); return; }
        registry._SetTestOverride(rank, registry._IsLocalMember());
        Refresh();
    }

    private void SetResident(bool resident)
    {
        if (!_CanUse()) { Refresh(); return; }
        int rank = registry._GetLocalRank();
        registry._SetTestOverride(rank < 0 ? 0 : rank, resident);
        Refresh();
    }

    private int LowRank()
    {
        int r = registry != null ? registry._GetMinRank() : 0;
        return r > 0 ? r : 1;
    }

    private int HighRank()
    {
        int r = registry != null ? registry._GetMaxRank() : 0;
        return r > 0 ? r : 2;
    }

    private string RankName(int rank)
    {
        if (rank <= 0) return "なし";
        string label = registry != null ? registry._GetTierLabel(rank) : "";
        return label != null && label.Length > 0 ? label : "ランク " + rank;
    }

    private void Refresh()
    {
        if (rankLabels != null && rankLabels.Length >= 3)
        {
            if (rankLabels[0] != null) rankLabels[0].text = "ランクなし";
            if (rankLabels[1] != null) rankLabels[1].text = RankName(LowRank());
            if (rankLabels[2] != null) rankLabels[2].text = RankName(HighRank());
        }
        if (stateText == null) return;

        string s = "<b>テスト用</b> <color=" + SupporterRegistry.ColorDim + ">（Play モードと Build & Test だけに出ます）</color>\n";
        if (registry == null)
        {
            stateText.text = s + "<color=" + SupporterRegistry.ColorWarn + ">Registry が未設定です</color>";
            return;
        }
        if (!_CanUse())
        {
            stateText.text = s + "<color=" + SupporterRegistry.ColorWarn + ">Gate の Owner Display Names に入っている人だけが使えます</color>";
            return;
        }
        bool on = registry._IsTestOverride();
        int rank = registry._GetLocalRank();
        string rankText = rank == SupporterRegistry.RankUnknown ? "（リストを読み込み中）" : RankName(rank);
        s += "今の扱い: " + (on ? "<color=" + SupporterRegistry.ColorWarn + "><b>上書き中</b></color>" : "リストどおり") + "\n";
        s += "ランク: <b>" + rankText + "</b>　住人: <b>" + (registry._IsLocalMember() ? "はい" : "いいえ") + "</b>";
        if (gate != null)
        {
            s += "　入場: " + (gate._IsLocalAllowed()
                ? "<color=" + SupporterRegistry.ColorOk + "><b>可</b></color>"
                : "<color=" + SupporterRegistry.ColorWarn + "><b>不可</b></color>");
        }
        stateText.text = s;
    }
}
