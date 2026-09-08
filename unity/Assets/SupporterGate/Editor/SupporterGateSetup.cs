// SupporterGateSetup.cs
// メニュー Tools/SupporterGate から、シーンに必要な一式（Registry / Gate / ロビー UI / 承認パネル /
// クレジット / ランクタグ）を生成する。プレハブを手で組む代わりにコードで組み立てる。
// 生成後は Inspector で URL・モード・スポーン位置・コンテンツルートを調整する。

#if UNITY_EDITOR
using System.IO;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;

public static class SupporterGateSetup
{
    private const string PackageRoot = "Assets/SupporterGate";
    private const int ApprovalRows = 12;
    private const int RankTagCount = 24;

    [MenuItem("Tools/SupporterGate/Create Scene Setup", false, 1)]
    public static void CreateSceneSetup()
    {
        if (!ProgramAssetsReady())
        {
            EditorUtility.DisplayDialog("SupporterGate",
                "UdonSharp のプログラムアセットがまだ生成されていません。\nコンパイルが終わるのを待ってから再度実行してください。", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();

        GameObject root = new GameObject("SupporterGate System");
        Undo.RegisterCreatedObjectUndo(root, "Create SupporterGate");

        // ---- Registry ----
        GameObject registryGo = Child(root, "Registry");
        SupporterHash hasher = registryGo.AddUdonSharpComponent<SupporterHash>();
        SupporterRegistry registry = registryGo.AddUdonSharpComponent<SupporterRegistry>();
        SetRef(registry, "hasher", hasher);

        // ---- Spawns / content ----
        GameObject lobbySpawn = Child(root, "LobbySpawn");
        lobbySpawn.transform.localPosition = Vector3.zero;
        GameObject contentSpawn = Child(root, "ContentSpawn");
        contentSpawn.transform.localPosition = new Vector3(0f, 0f, 20f);
        GameObject contentRoot = Child(root, "ContentRoot (ここにワールド本体を入れる)");
        contentRoot.transform.localPosition = new Vector3(0f, 0f, 20f);

        GameObject zoneGo = Child(root, "ContentZone");
        zoneGo.transform.localPosition = new Vector3(0f, 5f, 20f);
        BoxCollider zoneCol = zoneGo.AddComponent<BoxCollider>();
        zoneCol.isTrigger = true;
        zoneCol.size = new Vector3(30f, 10f, 30f);

        // ---- Gate ----
        GameObject gateGo = Child(root, "Gate");
        SupporterGate gate = gateGo.AddUdonSharpComponent<SupporterGate>();
        SetRef(gate, "registry", registry);
        SetRef(gate, "lobbySpawn", lobbySpawn.transform);
        SetRef(gate, "contentSpawn", contentSpawn.transform);
        SetArray(gate, "contentRoots", new Object[] { contentRoot });

        SupporterContentZone zone = zoneGo.AddUdonSharpComponent<SupporterContentZone>();
        SetRef(zone, "gate", gate);

        // ---- Lobby panel ----
        GameObject lobbyCanvas = CreateCanvas(root, "LobbyPanel", new Vector3(0f, 1.5f, 3f), new Vector2(900f, 600f));
        TextMeshProUGUI status = CreateText(lobbyCanvas, "Status", "モード: -", 36, new Vector2(20f, 300f), new Vector2(860f, 260f), TextAlignmentOptions.TopLeft);
        TextMeshProUGUI message = CreateText(lobbyCanvas, "Message", "", 32, new Vector2(20f, 170f), new Vector2(860f, 120f), TextAlignmentOptions.TopLeft);
        message.color = new Color(1f, 0.6f, 0.4f);
        CreateButton(lobbyCanvas, "EnterButton", "入場する", new Vector2(20f, 40f), new Vector2(400f, 100f), gate, nameof(SupporterGate._EnterContent));
        CreateButton(lobbyCanvas, "ReturnButton", "ロビーへ戻る", new Vector2(480f, 40f), new Vector2(400f, 100f), gate, nameof(SupporterGate._ReturnToLobby));
        SetRef(gate, "statusText", status);
        SetRef(gate, "messageText", message);

        // ---- Approval panel ----
        GameObject approvalCanvas = CreateCanvas(root, "ApprovalPanel", new Vector3(1.6f, 1.5f, 3f), new Vector2(900f, 1000f));
        SupporterApprovalPanel panel = approvalCanvas.AddUdonSharpComponent<SupporterApprovalPanel>();
        SetRef(panel, "gate", gate);
        SetRef(panel, "registry", registry);

        GameObject deniedRoot = ChildRect(approvalCanvas, "DeniedRoot");
        CreateText(deniedRoot, "DeniedText", "このパネルは支援者専用です", 36, new Vector2(20f, 450f), new Vector2(860f, 100f), TextAlignmentOptions.Center);

        GameObject supporterRoot = ChildRect(approvalCanvas, "SupporterRoot");
        TextMeshProUGUI header = CreateText(supporterRoot, "Header", "入場許可", 40, new Vector2(20f, 920f), new Vector2(860f, 60f), TextAlignmentOptions.Left);
        GameObject activationBtn = CreateButton(supporterRoot, "ActivationButton", "エリアを有効化", new Vector2(20f, 840f), new Vector2(860f, 70f), panel, nameof(SupporterApprovalPanel._OnActivationPressed));
        TextMeshProUGUI activationText = activationBtn.GetComponentInChildren<TextMeshProUGUI>();

        SupporterApprovalRow[] rows = new SupporterApprovalRow[ApprovalRows];
        for (int i = 0; i < ApprovalRows; i++)
        {
            float y = 760f - i * 62f;
            GameObject rowGo = ChildRect(supporterRoot, $"Row{i:00}");
            SupporterApprovalRow row = rowGo.AddUdonSharpComponent<SupporterApprovalRow>();
            GameObject visual = ChildRect(rowGo, "Visual");
            TextMeshProUGUI nameText = CreateText(visual, "Name", "-", 30, new Vector2(20f, y), new Vector2(440f, 56f), TextAlignmentOptions.Left);
            TextMeshProUGUI stateText = CreateText(visual, "State", "", 26, new Vector2(470f, y), new Vector2(160f, 56f), TextAlignmentOptions.Left);
            GameObject btn = CreateButton(visual, "Toggle", "許可する", new Vector2(640f, y), new Vector2(240f, 56f), row, nameof(SupporterApprovalRow._OnClick));
            TextMeshProUGUI btnText = btn.GetComponentInChildren<TextMeshProUGUI>();
            SetRef(row, "panel", panel);
            SetInt(row, "index", i);
            SetRef(row, "nameText", nameText);
            SetRef(row, "stateText", stateText);
            SetRef(row, "buttonText", btnText);
            SetRef(row, "visualRoot", visual);
            visual.SetActive(false);
            rows[i] = row;
        }
        SetRef(panel, "supporterRoot", supporterRoot);
        SetRef(panel, "deniedRoot", deniedRoot);
        SetRef(panel, "headerText", header);
        SetRef(panel, "activationButtonText", activationText);
        SetRef(panel, "activationButtonRoot", activationBtn);
        SetArray(panel, "rows", rows);

        // ---- Credits board ----
        GameObject creditsCanvas = CreateCanvas(root, "CreditsBoard", new Vector3(-1.6f, 1.5f, 3f), new Vector2(900f, 1000f));
        TextMeshProUGUI creditsText = CreateText(creditsCanvas, "Text", "Special Thanks", 34, new Vector2(20f, 20f), new Vector2(860f, 960f), TextAlignmentOptions.Top);
        SupporterCreditsBoard credits = creditsCanvas.AddUdonSharpComponent<SupporterCreditsBoard>();
        SetRef(credits, "registry", registry);
        SetRef(credits, "text", creditsText);

        // ---- Rank tags ----
        GameObject tagsGo = Child(root, "RankTags");
        SupporterRankTag tags = tagsGo.AddUdonSharpComponent<SupporterRankTag>();
        GameObject[] tagPool = new GameObject[RankTagCount];
        for (int i = 0; i < RankTagCount; i++)
        {
            GameObject t = Child(tagsGo, $"Tag{i:00}");
            TextMeshPro tmp = t.AddComponent<TextMeshPro>();
            tmp.text = "Supporter";
            tmp.fontSize = 2.5f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(4f, 0.6f);
            t.SetActive(false);
            tagPool[i] = t;
        }
        SetRef(tags, "registry", registry);
        SetArray(tags, "tagObjects", tagPool);

        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);

        Debug.Log("[SupporterGate] シーンに一式を生成しました。Registry の Data Url、Gate の Mode、ContentRoot の中身を設定してください。");
    }

    [MenuItem("Tools/SupporterGate/Export UnityPackage", false, 20)]
    public static void ExportPackage()
    {
        string path = EditorUtility.SaveFilePanel("Export SupporterGate", "", "SupporterGate.unitypackage", "unitypackage");
        if (string.IsNullOrEmpty(path)) return;
        ExportPackageTo(path);
    }

    public static void ExportPackageTo(string path)
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.ExportPackage(PackageRoot, path, ExportPackageOptions.Recurse);
        Debug.Log($"[SupporterGate] exported: {path}");
    }

    private static bool ProgramAssetsReady()
    {
        return UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(SupporterRegistry)) != null
            && UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(SupporterGate)) != null
            && UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(SupporterHash)) != null
            && UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(SupporterApprovalPanel)) != null
            && UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(SupporterApprovalRow)) != null
            && UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(SupporterContentZone)) != null
            && UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(SupporterCreditsBoard)) != null
            && UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(SupporterRankTag)) != null;
    }

    // ===================== helpers =====================

    private static GameObject Child(GameObject parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static GameObject ChildRect(GameObject parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return go;
    }

    private static GameObject CreateCanvas(GameObject parent, string name, Vector3 position, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(VRCUiShape));
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = position;
        go.transform.localScale = Vector3.one * 0.0015f;
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(go.transform, false);
        RectTransform brt = bg.GetComponent<RectTransform>();
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;
        Image img = bg.GetComponent<Image>();
        img.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);
        img.raycastTarget = false;
        return go;
    }

    private static TextMeshProUGUI CreateText(GameObject parent, string name, string text, float fontSize, Vector2 pos, Vector2 size, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.richText = true;
        return tmp;
    }

    private static GameObject CreateButton(GameObject parent, string name, string label, Vector2 pos, Vector2 size, UdonSharp.UdonSharpBehaviour target, string eventName)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        img.color = new Color(0.25f, 0.45f, 0.8f, 1f);

        TextMeshProUGUI text = CreateText(go, "Label", label, Mathf.Min(size.y * 0.5f, 34f), Vector2.zero, size, TextAlignmentOptions.Center);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;

        Button button = go.GetComponent<Button>();
        UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
        if (backing == null)
        {
            Debug.LogError($"[SupporterGate] {target.name} の UdonBehaviour が見つかりません");
        }
        else
        {
            UnityEventTools.AddStringPersistentListener(button.onClick, new UnityAction<string>(backing.SendCustomEvent), eventName);
        }
        return go;
    }

    private static void SetRef(UdonSharp.UdonSharpBehaviour proxy, string field, Object value)
    {
        SerializedObject so = new SerializedObject(proxy);
        SerializedProperty prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogError($"[SupporterGate] フィールド {field} が {proxy.GetType().Name} にありません");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        UdonSharpEditorUtility.CopyProxyToUdon(proxy);
    }

    private static void SetInt(UdonSharp.UdonSharpBehaviour proxy, string field, int value)
    {
        SerializedObject so = new SerializedObject(proxy);
        SerializedProperty prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogError($"[SupporterGate] フィールド {field} が {proxy.GetType().Name} にありません");
            return;
        }
        prop.intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        UdonSharpEditorUtility.CopyProxyToUdon(proxy);
    }

    private static void SetArray(UdonSharp.UdonSharpBehaviour proxy, string field, Object[] values)
    {
        SerializedObject so = new SerializedObject(proxy);
        SerializedProperty prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogError($"[SupporterGate] フィールド {field} が {proxy.GetType().Name} にありません");
            return;
        }
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        UdonSharpEditorUtility.CopyProxyToUdon(proxy);
    }
}
#endif
