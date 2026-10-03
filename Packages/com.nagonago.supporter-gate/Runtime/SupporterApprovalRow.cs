// SupporterApprovalRow.cs
// 承認パネルの 1 行。ボタンの OnClick から _OnClick を SendCustomEvent で呼ぶ。

using UdonSharp;
using UnityEngine;
using TMPro;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SupporterApprovalRow : UdonSharpBehaviour
{
    [SerializeField] private SupporterApprovalPanel panel;
    [SerializeField] private int index;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI stateText;
    [SerializeField] private TextMeshProUGUI buttonText;
    [Tooltip("表示/非表示を切り替える子オブジェクト。行自身の GameObject は常にアクティブにしておく（Udon がイベントを受け取れるように）")]
    [SerializeField] private GameObject visualRoot;

    public void _Setup(SupporterApprovalPanel owner, int rowIndex)
    {
        panel = owner;
        index = rowIndex;
    }

    /// <summary>名前と、パネルが今の言語で用意した状態・ボタンの文を出す。名前はリッチテキストとして解釈させない</summary>
    public void _SetRow(string playerName, string stateLabel, string buttonLabel)
    {
        if (nameText != null)
        {
            string n = playerName == null ? "" : playerName;
            nameText.text = "<noparse>" + n.Replace("</noparse>", "") + "</noparse>";
        }
        if (stateText != null) stateText.text = stateLabel;
        if (buttonText != null) buttonText.text = buttonLabel;
        if (visualRoot != null) visualRoot.SetActive(true);
    }

    public void _Hide()
    {
        if (visualRoot != null) visualRoot.SetActive(false);
    }

    public void _OnClick()
    {
        if (panel != null) panel._OnRowPressed(index);
    }
}
