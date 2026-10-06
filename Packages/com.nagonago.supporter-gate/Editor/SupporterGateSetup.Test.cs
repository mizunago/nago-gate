// SupporterGateSetup.Test.cs
// テスト用のパネル（Play モードと Build & Test だけに出る）と、ロビーの板の後始末。
//
//   Tools > SupporterGate > Add Test Panel (existing scene)            既にあるシーンに、テスト用のパネルを足す
//   Tools > SupporterGate > Remove Return Button (existing scene)      ロビーの板の「ロビーへ戻る」ボタンを外す
//
// 「ロビーへ戻る」ボタンは、ロビーの板（ロビーの中）にあったので、押しても何も変わらなかった。
// 中から戻るときは VRChat のメニューの Respawn で戻れる（スポーン地点がロビーのため）。
// 入れなくなった人をロビーへ戻す処理（SupporterGate._ReturnToLobby）は、ボタンとは別に動くので残す。

#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static partial class SupporterGateSetup
{
    private const string TestPanelName = "TestPanel";
    private const string LobbyPanelName = "LobbyPanel";
    private static readonly Vector3 TestPanelOffset = new Vector3(3.2f, 0f, 0f);   // ロビーの板からの位置（板の右）

    /// <summary>テスト用のパネルを作る。シーンでは非表示にしておき、Play モードと Build & Test のときだけエディタが出す</summary>
    private static GameObject CreateTestPanel(GameObject parent, Vector3 localPosition, SupporterRegistry registry, SupporterGate gate)
    {
        GameObject canvas = CreateCanvas(parent, TestPanelName, localPosition, new Vector2(900f, 620f));
        Image bg = canvas.transform.Find("Background").GetComponent<Image>();
        bg.color = new Color(0.22f, 0.11f, 0.04f, 0.92f);   // 本物の板と見分けやすい色
        SupporterTestPanel panel = canvas.AddUdonSharpComponent<SupporterTestPanel>();
        GameObject mark = Child(canvas, SupporterTestPanelBuild.PlayModeMarkName);   // Play モードの目印。エディタが Play モードのときだけ表示にする
        mark.SetActive(false);

        TextMeshProUGUI state = CreateText(canvas, "State", "テスト用", 30, new Vector2(20f, 430f), new Vector2(860f, 170f), TextAlignmentOptions.TopLeft);
        CreateText(canvas, "RankLabel", "ランク", 30, new Vector2(20f, 320f), new Vector2(150f, 80f), TextAlignmentOptions.Left);
        GameObject rank0 = CreateButton(canvas, "RankNone", "ランクなし", new Vector2(180f, 320f), new Vector2(220f, 80f), panel, nameof(SupporterTestPanel._RankNone));
        GameObject rank1 = CreateButton(canvas, "RankLow", "ランク 1", new Vector2(420f, 320f), new Vector2(220f, 80f), panel, nameof(SupporterTestPanel._RankLow));
        GameObject rank2 = CreateButton(canvas, "RankHigh", "ランク 2", new Vector2(660f, 320f), new Vector2(220f, 80f), panel, nameof(SupporterTestPanel._RankHigh));
        CreateText(canvas, "ResidentLabel", "住人", 30, new Vector2(20f, 210f), new Vector2(150f, 80f), TextAlignmentOptions.Left);
        CreateButton(canvas, "ResidentOff", "住人ではない", new Vector2(180f, 210f), new Vector2(340f, 80f), panel, nameof(SupporterTestPanel._ResidentOff));
        CreateButton(canvas, "ResidentOn", "住人", new Vector2(540f, 210f), new Vector2(340f, 80f), panel, nameof(SupporterTestPanel._ResidentOn));
        GameObject reset = CreateButton(canvas, "ResetButton", "リストどおりに戻す", new Vector2(20f, 40f), new Vector2(860f, 100f), panel, nameof(SupporterTestPanel._ResetToList));
        reset.GetComponent<Image>().color = new Color(0.45f, 0.45f, 0.5f, 1f);

        SetRef(panel, "registry", registry);
        SetRef(panel, "gate", gate);
        SetRef(panel, "stateText", state);
        SetRef(panel, "playModeMark", mark);
        SetArray(panel, "rankLabels", new Object[]
        {
            rank0.GetComponentInChildren<TextMeshProUGUI>(),
            rank1.GetComponentInChildren<TextMeshProUGUI>(),
            rank2.GetComponentInChildren<TextMeshProUGUI>(),
        });
        canvas.SetActive(false);
        return canvas;
    }

    [MenuItem("Tools/SupporterGate/Add Test Panel (existing scene)", false, 23)]
    public static void AddTestPanels()
    {
        EditorUtility.DisplayDialog("SupporterGate", AddTestPanelsSilent(), "OK");
    }

    /// <summary>ゲートの一式ごとに、テスト用のパネルが無ければ足す。ダイアログを出さずに、やったことの説明を返す</summary>
    public static string AddTestPanelsSilent()
    {
        if (!ProgramAssetsReady()) return "UdonSharp のプログラムアセットがまだ生成されていません。コンパイルが終わるのを待ってから、もう一度実行してください。";
        SupporterRegistry registry = Object.FindObjectOfType<SupporterRegistry>(true);
        int added = 0, skipped = 0;
        List<string> notes = new List<string>();
        foreach (SupporterGate gate in Object.FindObjectsOfType<SupporterGate>(true))
        {
            GameObject root = gate.transform.parent != null ? gate.transform.parent.gameObject : gate.gameObject;
            if (root.GetComponentsInChildren<SupporterTestPanel>(true).Length > 0) { skipped++; continue; }
            Transform lobby = FindDeep(root.transform, LobbyPanelName);
            GameObject panel;
            if (lobby != null)
            {
                // ロビーの板と同じ親に、板の右へ置く（向きと大きさは板に合わせる）
                panel = CreateTestPanel(lobby.parent.gameObject, lobby.localPosition + lobby.localRotation * TestPanelOffset, registry, gate);
                panel.transform.localRotation = lobby.localRotation;
                panel.transform.localScale = lobby.localScale;
            }
            else
            {
                panel = CreateTestPanel(root, TestPanelOffset, registry, gate);
                notes.Add("ロビーの板（" + LobbyPanelName + "）が見つからなかったので、一式の原点の近くに置きました。見やすい場所へ動かしてください。");
            }
            Undo.RegisterCreatedObjectUndo(panel, "Add SupporterGate Test Panel");
            EditorSceneManager.MarkSceneDirty(panel.scene);
            added++;
        }
        if (added == 0 && skipped == 0) return "シーンに SupporterGate がありません。";
        string msg = added > 0
            ? "テスト用のパネルを " + added + " 個足しました。\nシーンでは非表示です。Play モードと Build & Test のときだけ出ます（公開用のビルドでは取り除きます）。"
            : "テスト用のパネルは、もうあります（" + skipped + " 個）。";
        if (notes.Count > 0) msg += "\n" + string.Join("\n", notes);
        Debug.Log("[SupporterGate] " + msg.Replace("\n", " "));
        return msg;
    }

    [MenuItem("Tools/SupporterGate/Remove Return Button (existing scene)", false, 24)]
    public static void RemoveReturnButtons()
    {
        EditorUtility.DisplayDialog("SupporterGate", RemoveReturnButtonsSilent(), "OK");
    }

    /// <summary>ロビーの板の「ロビーへ戻る」ボタンを外し、「入場する」を板の真ん中へ寄せる（作ったときの位置のままなら）</summary>
    public static string RemoveReturnButtonsSilent()
    {
        int removed = 0;
        foreach (SupporterGate gate in Object.FindObjectsOfType<SupporterGate>(true))
        {
            GameObject root = gate.transform.parent != null ? gate.transform.parent.gameObject : gate.gameObject;
            Transform lobby = FindDeep(root.transform, LobbyPanelName);
            if (lobby == null) continue;
            Transform ret = lobby.Find("ReturnButton");
            if (ret == null) continue;
            Undo.DestroyObjectImmediate(ret.gameObject);
            removed++;
            RectTransform enter = lobby.Find("EnterButton") as RectTransform;
            if (enter != null && enter.anchoredPosition == new Vector2(20f, 40f) && enter.sizeDelta == new Vector2(400f, 100f))
            {
                Undo.RecordObject(enter, "Center Enter Button");
                enter.anchoredPosition = new Vector2(250f, 40f);
            }
            EditorSceneManager.MarkSceneDirty(lobby.gameObject.scene);
        }
        string msg = removed > 0
            ? "「ロビーへ戻る」ボタンを " + removed + " 個外しました。\n中から戻るときは、VRChat のメニューの Respawn で戻れます。\n入れなくなった人をロビーへ戻す処理は、そのまま動きます。"
            : "「ロビーへ戻る」ボタンは見つかりませんでした（もう外してあります）。";
        Debug.Log("[SupporterGate] " + msg.Replace("\n", " "));
        return msg;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }
        return null;
    }
}
#endif
