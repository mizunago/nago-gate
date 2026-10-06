// SupporterGateOwnerNamesWindow.cs
// 持ち主の VRChat の表示名を、このパソコンに覚えておく画面（Tools > SupporterGate > Owner Names...）。

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>持ち主の表示名を入れる画面</summary>
public class SupporterGateOwnerNamesWindow : EditorWindow
{
    private string _text = "";

    public static void Open()
    {
        SupporterGateOwnerNamesWindow w = GetWindow<SupporterGateOwnerNamesWindow>(true, "SupporterGate: 持ち主の名前");
        w._text = string.Join("\n", SupporterGateSetup.GetDefaultOwnerNames());
        w.minSize = new Vector2(420, 220);
        w.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "持ち主の VRChat の表示名を、1 行に 1 人ずつ入れてください（完全一致）。\n" +
            "このパソコンに覚えておき、ゲートを作るときに、Gate の Owner Display Names が空なら入れます。\n" +
            "持ち主は、支援者のリストに無くても支援者と同じに入れます（持ち主だけのワールドでは、持ち主だけが入れます）。",
            MessageType.Info);
        _text = EditorGUILayout.TextArea(_text, GUILayout.ExpandHeight(true));
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("保存"))
        {
            SupporterGateSetup.SetDefaultOwnerNames(_text.Split('\n'));
            ShowNotification(new GUIContent("保存しました"));
        }
        if (GUILayout.Button("保存して、シーンのゲートにも入れる"))
        {
            SupporterGateSetup.SetDefaultOwnerNames(_text.Split('\n'));
            string[] names = SupporterGateSetup.GetDefaultOwnerNames();
            int n = 0;
            foreach (SupporterGate gate in Object.FindObjectsOfType<SupporterGate>(true))
            {
                SerializedObject so = new SerializedObject(gate);
                SerializedProperty owners = so.FindProperty("ownerDisplayNames");
                owners.arraySize = names.Length;
                for (int i = 0; i < names.Length; i++) owners.GetArrayElementAtIndex(i).stringValue = names[i];
                so.ApplyModifiedProperties();
                UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(gate);
                n++;
            }
            ShowNotification(new GUIContent(n > 0 ? "シーンのゲート " + n + " 個に入れました" : "シーンにゲートがありません"));
        }
        EditorGUILayout.EndHorizontal();
    }
}
#endif
