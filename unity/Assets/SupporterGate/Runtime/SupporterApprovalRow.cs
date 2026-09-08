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

    public void _SetRow(string playerName, bool approved)
    {
        if (nameText != null) nameText.text = playerName;
        if (stateText != null) stateText.text = approved ? "<color=#7CFC9A>許可済み</color>" : "<color=#AAAAAA>未許可</color>";
        if (buttonText != null) buttonText.text = approved ? "取り消す" : "許可する";
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
