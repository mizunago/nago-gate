// SupporterGateSetup.Owner.cs
// 持ち主の名前と、持ち主だけのワールド（実験用のワールドなど）。
//
//   Tools > SupporterGate > Owner Names...    持ち主の VRChat の表示名を、このパソコンに覚えておく（どのプロジェクトでも使う）
//   Tools > SupporterGate > Convert Existing World > 持ち主と、許可した人だけが入れるワールドにする
//   Tools > SupporterGate > Convert Existing World > 持ち主だけが入れるワールドにする
//
// 覚えた名前は、ゲートを作るとき（Create Scene Setup・Convert Existing World）に、Gate の Owner Display Names が空なら入れる。
// 持ち主だけのワールドは、支援者のリストを使わない（Gate の Owners Only・Registry の No List）。
// どこのワールドか分からないように、支援者の名前の板と案内のパネル（Discord の招待）は隠す。

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class SupporterGateSetup
{
    private const string OwnerNamesPref = "NagoSupporterGate.OwnerNames";

    /// <summary>このパソコンに覚えた持ち主の表示名</summary>
    public static string[] GetDefaultOwnerNames()
    {
        return EditorPrefs.GetString(OwnerNamesPref, "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToArray();
    }

    /// <summary>持ち主の表示名を、このパソコンに覚える（ほかの道具から呼ぶ用。ダイアログは出さない）</summary>
    public static void SetDefaultOwnerNames(params string[] names)
    {
        EditorPrefs.SetString(OwnerNamesPref, string.Join("\n", (names ?? new string[0]).Select(s => s == null ? "" : s.Trim()).Where(s => s.Length > 0).Distinct()));
    }

    [MenuItem("Tools/SupporterGate/Owner Names...", false, 30)]
    public static void OpenOwnerNames()
    {
        SupporterGateOwnerNamesWindow.Open();
    }

    [MenuItem("Tools/SupporterGate/Convert Existing World/持ち主と、許可した人だけが入れるワールドにする", false, 43)]
    public static void ConvertOwnersApproval() { ConvertWithDialog(SupporterGateMode.SupporterApproval, false, true); }

    [MenuItem("Tools/SupporterGate/Convert Existing World/持ち主だけが入れるワールドにする", false, 44)]
    public static void ConvertOwnersOnly() { ConvertWithDialog(SupporterGateMode.SupportersOnly, false, true); }

    /// <summary>Gate の Owner Display Names が空なら、覚えた名前を入れる。ゲートに入っている名前を返す</summary>
    private static string[] FillOwnerNames(SupporterGate gate)
    {
        SerializedObject so = new SerializedObject(gate);
        SerializedProperty owners = so.FindProperty("ownerDisplayNames");
        List<string> current = new List<string>();
        for (int i = 0; i < owners.arraySize; i++)
        {
            string n = owners.GetArrayElementAtIndex(i).stringValue;
            if (!string.IsNullOrEmpty(n)) current.Add(n);
        }
        if (current.Count > 0) return current.ToArray();
        string[] names = GetDefaultOwnerNames();
        if (names.Length == 0) return names;
        owners.arraySize = names.Length;
        for (int i = 0; i < names.Length; i++) owners.GetArrayElementAtIndex(i).stringValue = names[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(gate);
        return names;
    }

    /// <summary>持ち主だけのワールドにする設定（リストを使わない。どこのワールドか分かる板を隠す）。やったことの説明を返す</summary>
    private static string ApplyOwnersOnly(SupporterGate gate, GameObject root)
    {
        SetBool(gate, "ownersOnly", true);
        SupporterRegistry registry = root.GetComponentInChildren<SupporterRegistry>(true);
        if (registry != null)
        {
            SerializedObject rso = new SerializedObject(registry);
            rso.FindProperty("noList").boolValue = true;
            SerializedProperty url = rso.FindProperty("dataUrl").FindPropertyRelative("url");
            if (url != null) url.stringValue = "";
            rso.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(registry);
        }
        int hidden = 0;
        foreach (string name in new[] { "CreditsBoard", InfoPanelName })
        {
            Transform t = FindDeep(root.transform, name);
            if (t == null || !t.gameObject.activeSelf) continue;
            Undo.RecordObject(t.gameObject, "Hide Board");
            t.gameObject.SetActive(false);
            hidden++;
        }
        return "支援者のリストを使わない設定にし、支援者の名前の板と案内のパネルを隠しました（" + hidden + " 枚）";
    }
}

#endif
