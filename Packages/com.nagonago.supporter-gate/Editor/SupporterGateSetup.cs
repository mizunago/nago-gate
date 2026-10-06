// SupporterGateSetup.cs
// メニュー Tools/SupporterGate から、シーンに必要な一式（Registry / Gate / ロビー UI / 承認パネル /
// クレジット / 文言の表 / 共通の通知）を生成する。プレハブを手で組む代わりにコードで組み立てる。
// 生成後は Inspector で URL・モード・スポーン位置・コンテンツルートを調整する。
//
//   Tools > SupporterGate > Create Scene Setup              新しく一式を作る
//   Tools > SupporterGate > Wire Notices (existing scene)   既にあるシーンに、通知と文言の表を足して配線する
//   （テスト用のパネルとロビーの板の後始末は SupporterGateSetup.Test.cs）

#if UNITY_EDITOR
using System.Collections.Generic;
using NagoNotice;
using NagoNotice.EditorTools;
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

public static partial class SupporterGateSetup
{
    private const string PackageRoot = "Packages/com.nagonago.supporter-gate";
    private const string TextsPath = PackageRoot + "/Runtime/SupporterGateTexts.json";
    private const string TextsObjectName = "Texts";
    private const string InfoPanelName = "InfoPanel";
    private const int ApprovalRows = 12;

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
        // 「ロビーへ戻る」ボタンは置かない（ロビーの板の上では意味が無い。中からは VRChat の Respawn で戻れる）
        GameObject enterBtn = CreateButton(lobbyCanvas, "EnterButton", "入場する", new Vector2(250f, 40f), new Vector2(400f, 100f), gate, nameof(SupporterGate._EnterContent));
        SetRef(gate, "enterButtonText", enterBtn.GetComponentInChildren<TextMeshProUGUI>());
        SetRef(gate, "statusText", status);
        SetRef(gate, "messageText", message);

        // ---- Test panel（Play モードと Build & Test だけに出る。シーンでは非表示） ----
        CreateTestPanel(root, lobbyCanvas.transform.localPosition + TestPanelOffset, registry, gate);

        // ---- Approval panel ----
        GameObject approvalCanvas = CreateCanvas(root, "ApprovalPanel", new Vector3(1.6f, 1.5f, 3f), new Vector2(900f, 1000f));
        SupporterApprovalPanel panel = approvalCanvas.AddUdonSharpComponent<SupporterApprovalPanel>();
        SetRef(panel, "gate", gate);
        SetRef(panel, "registry", registry);

        GameObject deniedRoot = ChildRect(approvalCanvas, "DeniedRoot");
        TextMeshProUGUI deniedText = CreateText(deniedRoot, "DeniedText", "このパネルは支援者専用です", 36, new Vector2(20f, 450f), new Vector2(860f, 100f), TextAlignmentOptions.Center);
        SetRef(panel, "deniedText", deniedText);

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
        FitText(creditsText, 18f);

        // ---- Info panel（本人の状態と Discord の案内。名前の一覧とは別のパネルにする） ----
        GameObject infoCanvas = CreateCanvas(root, InfoPanelName, new Vector3(-3.2f, 1.5f, 3f), new Vector2(900f, 600f));
        TextMeshProUGUI infoText = CreateText(infoCanvas, "Text", "", 34, new Vector2(20f, 20f), new Vector2(860f, 560f), TextAlignmentOptions.Center);
        FitText(infoText, 20f);
        SetRef(credits, "infoText", infoText);
        // Discord の招待 URL のコピー欄と QR（QR は、リストの URL が決まってから Update Discord QR で作る）
        AddInviteLink(credits, infoText);

        // ---- 文言の表と共通の通知 ----
        WireNotices(root);

        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);

        Debug.Log("[SupporterGate] シーンに一式を生成しました。Registry の Data Url、Gate の Mode、ContentRoot の中身を設定してください。");
    }

    [MenuItem("Tools/SupporterGate/Wire Notices (existing scene)", false, 20)]
    public static void UpgradeScene()
    {
        List<GameObject> roots = new List<GameObject>();
        foreach (SupporterGate gate in Object.FindObjectsOfType<SupporterGate>(true))
        {
            GameObject root = gate.transform.parent != null ? gate.transform.parent.gameObject : gate.gameObject;
            if (!roots.Contains(root)) roots.Add(root);
        }
        int gateRoots = roots.Count;
        // ゲートの無いシーン（公開のワールドに、Registry とボードだけを置いた場合）でも、ボードの配線は行う
        foreach (SupporterCreditsBoard credits in Object.FindObjectsOfType<SupporterCreditsBoard>(true))
        {
            bool covered = false;
            foreach (GameObject root in roots)
            {
                if (credits.transform.IsChildOf(root.transform)) { covered = true; break; }
            }
            if (covered) continue;   // ゲートの一式の中にあるボードは、その一式でまとめて配線される
            roots.Add(credits.transform.parent != null ? credits.transform.parent.gameObject : credits.gameObject);
        }
        if (roots.Count == 0)
        {
            EditorUtility.DisplayDialog("SupporterGate", "シーンに SupporterGate も SupporterCreditsBoard もありません。", "OK");
            return;
        }
        foreach (GameObject root in roots) WireNotices(root);
        Debug.Log("[SupporterGate] 通知と文言の表を配線しました（ゲートの一式 " + gateRoots + " 個、ボードだけの場所 " + (roots.Count - gateRoots) + " 個）");
    }

    // ===================== 既存のワールドへの後付け =====================
    // ゲートの無いワールドに、入口の部屋（ロビー）とゲート一式を足す。
    // 今のスポーン地点は「入場したあとに出る場所」として引き継ぎ、新しいスポーン地点は入口の部屋にする。

    private const string LobbyRoomName = "LobbyRoom";
    private const float LobbyDepth = 40f;       // ワールドの一番下から、入口の部屋の床までの距離
    private const float LobbyWidth = 8f;
    private const float LobbyHeight = 3.2f;
    private const float LobbyWall = 0.2f;

    [MenuItem("Tools/SupporterGate/Convert Existing World/住人だけが入れるワールドにする", false, 40)]
    public static void ConvertMembersOnly() { ConvertWithDialog(SupporterGateMode.SupportersOnly, true); }

    [MenuItem("Tools/SupporterGate/Convert Existing World/支援者だけが入れるワールドにする", false, 41)]
    public static void ConvertSupportersOnly() { ConvertWithDialog(SupporterGateMode.SupportersOnly, false); }

    [MenuItem("Tools/SupporterGate/Convert Existing World/支援者と、許可した人が入れるワールドにする", false, 42)]
    public static void ConvertApproval() { ConvertWithDialog(SupporterGateMode.SupporterApproval, false); }

    private static void ConvertWithDialog(SupporterGateMode mode, bool useMemberList)
    {
        bool go = EditorUtility.DisplayDialog("SupporterGate",
            "今のシーンに、入口の部屋とゲートを足します。\n\n" +
            "・今のスポーン地点は「入場したあとに出る場所」として引き継ぎます\n" +
            "・新しいスポーン地点は、ワールドの下に作る入口の部屋になります\n" +
            "・入れない人は、入口の部屋から先へ進めません\n\n" +
            "ワールド本体のオブジェクトは動かしません。元に戻すときは Undo（Ctrl+Z）を使ってください。",
            "実行する", "やめる");
        if (!go) return;
        EditorUtility.DisplayDialog("SupporterGate", ConvertExistingWorld(mode, useMemberList), "OK");
    }

    /// <summary>
    /// ゲートの無いワールドを、入口の部屋つきのワールドに変える。戻り値は、やったことと次にやることの説明。
    /// ワールド本体のオブジェクトには触らない（スポーン地点の設定と、落下時のリスポーンの高さだけ変える）。
    /// </summary>
    public static string ConvertExistingWorld(SupporterGateMode mode, bool useMemberList)
    {
        if (!ProgramAssetsReady()) return "UdonSharp のプログラムアセットがまだ生成されていません。コンパイルが終わるのを待ってから、もう一度実行してください。";
        VRCSceneDescriptor descriptor = Object.FindObjectOfType<VRCSceneDescriptor>(true);
        if (descriptor == null) return "シーンに VRC Scene Descriptor がありません。先に VRCWorld を置いてください。";
        if (Object.FindObjectOfType<SupporterGate>(true) != null)
        {
            return "このシーンには既に SupporterGate があります。このツールは、ゲートの無いワールド用です。\n" +
                "設定を変えたいときは、Gate の Inspector で Mode と Use Member List を直してください。";
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Convert World To SupporterGate");

        // 1. 今のスポーン地点（入場したあとに出る場所として引き継ぐ）
        Transform oldSpawn = null;
        if (descriptor.spawns != null)
        {
            foreach (Transform t in descriptor.spawns)
            {
                if (t != null) { oldSpawn = t; break; }
            }
        }
        if (oldSpawn == null) oldSpawn = descriptor.transform;
        Vector3 oldPos = oldSpawn.position;
        Quaternion oldRot = oldSpawn.rotation;
        int oldSpawnCount = descriptor.spawns != null ? descriptor.spawns.Length : 0;

        // 2. ワールドの範囲を、一式を作る前に測る
        Bounds bounds = MeasureWorld();

        // 3. 一式を作る
        CreateSceneSetup();
        SupporterGate gate = Object.FindObjectOfType<SupporterGate>(true);
        if (gate == null || gate.transform.parent == null) return "ゲート一式を作れませんでした。Console のエラーを確かめてください。";
        GameObject root = gate.transform.parent.gameObject;

        // 4. 入口の部屋を、ワールドの真下に作る（本体と重ならず、中から本体が見えない）
        float floorY = bounds.min.y - LobbyDepth;
        GameObject room = Child(root, LobbyRoomName);
        room.transform.position = new Vector3(bounds.center.x, floorY, bounds.center.z);
        BuildLobbyRoom(room);

        // 5. スポーン地点とパネルを、部屋の中へ移す（部屋ごと動かせるように、部屋の子にする）
        Transform lobbySpawn = null;
        foreach (string name in new[] { "LobbySpawn", "LobbyPanel", "ApprovalPanel", "CreditsBoard", InfoPanelName, TestPanelName })
        {
            Transform t = root.transform.Find(name);
            if (t == null) continue;
            t.SetParent(room.transform, false);     // 一式の原点からの位置を、そのまま部屋の中の位置にする
            if (name == "LobbySpawn") lobbySpawn = t;
            // 許可のパネルは、許可制のモードでだけ使う
            if (name == "ApprovalPanel" && mode != SupporterGateMode.SupporterApproval) t.gameObject.SetActive(false);
        }
        if (lobbySpawn == null) return "LobbySpawn が見つかりません。";
        lobbySpawn.localPosition = new Vector3(0f, 0.05f, 0f);

        // 6. 入場したあとに出る場所 = 元のスポーン地点
        Transform contentSpawn = root.transform.Find("ContentSpawn");
        if (contentSpawn != null) contentSpawn.SetPositionAndRotation(oldPos, oldRot);

        // 7. 本体の範囲を覆う判定（入れない人が中に入ったら、入口の部屋へ戻す）
        Transform zone = root.transform.Find("ContentZone");
        if (zone != null)
        {
            zone.SetPositionAndRotation(bounds.center, Quaternion.identity);
            BoxCollider col = zone.GetComponent<BoxCollider>();
            if (col != null)
            {
                col.center = Vector3.zero;
                col.size = new Vector3(Mathf.Max(bounds.size.x, 2f) + 4f, Mathf.Max(bounds.size.y, 2f) + 4f, Mathf.Max(bounds.size.z, 2f) + 4f);
            }
        }

        // 8. スポーン地点を入口の部屋に替える。部屋が落下のリスポーンの高さより下にならないようにする
        Undo.RecordObject(descriptor, "Convert World To SupporterGate");
        float oldRespawn = descriptor.RespawnHeightY;
        descriptor.spawns = new[] { lobbySpawn };
        descriptor.RespawnHeightY = Mathf.Min(oldRespawn, floorY - 20f);
        EditorUtility.SetDirty(descriptor);

        // 9. ゲートの設定
        SetInt(gate, "mode", (int)mode);
        SetBool(gate, "useMemberList", useMemberList);

        // 10. 入れない人の画面で、ワールド本体を見せない・聞かせない（同期する物は見た目だけ消す）
        string guardReport = SetUpContentGuardSilent();

        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = room;
        EditorSceneManager.MarkSceneDirty(root.scene);

        string who = useMemberList ? "住人だけ" : (mode == SupporterGateMode.SupporterApproval ? "支援者と、支援者が許可した人" : "支援者だけ");
        string report =
            "入口の部屋とゲートを足しました。入れるのは、" + who + "です。\n\n" +
            "やったこと\n" +
            "・入口の部屋（LobbyRoom）を、ワールドの " + LobbyDepth.ToString("0") + " m 下に作りました\n" +
            "・スポーン地点を入口の部屋に替えました（元は " + oldSpawnCount + " か所。1 つ目を「入場したあとに出る場所」に引き継ぎました）\n" +
            "・落下時のリスポーンの高さ: " + oldRespawn.ToString("0.#") + " → " + descriptor.RespawnHeightY.ToString("0.#") + "\n" +
            "・" + guardReport.Split('\n')[0] + "\n\n" +
            "次にやること\n" +
            "1. SupporterGate System > Registry の Data Url に、支援者リストの URL を入れる\n" +
            "2. 自分が入れるように、Gate の Owner Display Names に自分の VRChat の表示名を入れる\n" +
            "3. 入口の部屋の見た目は自由に変えてよい（LobbyRoom ごと動かせます）\n" +
            "4. ワールド本体を変えたら、Tools > SupporterGate > Set Up Content Guard (auto) をもう一度実行する（入れない人に見せない物を選び直す）";
        Debug.Log("[SupporterGate] " + report);
        return report;
    }

    /// <summary>ワールド本体の範囲（描画されるものと地形）。NoticeHub は数えない</summary>
    private static Bounds MeasureWorld()
    {
        bool any = false;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Renderer r in Object.FindObjectsOfType<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;   // 範囲が決まらない
            if (r.GetComponentInParent<NoticeHub>(true) != null) continue;
            Bounds rb;
            if (r.enabled && r.gameObject.activeInHierarchy)
            {
                rb = r.bounds;
            }
            else
            {
                // 非表示のものは bounds が当てにならないので、メッシュから求める
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                rb = TransformBounds(mf.sharedMesh.bounds, r.transform.localToWorldMatrix);
            }
            if (rb.size.sqrMagnitude <= 0f || rb.size.magnitude > 5000f) continue;   // 空や、空を覆う巨大な球などは除く
            if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
        }
        foreach (Terrain terrain in Object.FindObjectsOfType<Terrain>(true))
        {
            if (terrain.terrainData == null) continue;
            Bounds tb = terrain.terrainData.bounds;
            tb.center += terrain.transform.position;
            if (!any) { b = tb; any = true; } else b.Encapsulate(tb);
        }
        if (!any) b = new Bounds(Vector3.zero, new Vector3(20f, 6f, 20f));
        return b;
    }

    private static Bounds TransformBounds(Bounds local, Matrix4x4 m)
    {
        Vector3 c = local.center, e = local.extents;
        Bounds b = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            b.Encapsulate(m.MultiplyPoint3x4(corner));
        }
        return b;
    }

    /// <summary>床・天井・壁 4 枚と明かりだけの、何もない四角い部屋</summary>
    private static void BuildLobbyRoom(GameObject room)
    {
        GameObject geo = Child(room, "Geometry");
        float w = LobbyWidth, h = LobbyHeight, t = LobbyWall;
        LobbyBox(geo, "Floor", new Vector3(0f, -t * 0.5f, 0f), new Vector3(w + t * 2f, t, w + t * 2f));
        LobbyBox(geo, "Ceiling", new Vector3(0f, h + t * 0.5f, 0f), new Vector3(w + t * 2f, t, w + t * 2f));
        LobbyBox(geo, "Wall +Z", new Vector3(0f, h * 0.5f, w * 0.5f + t * 0.5f), new Vector3(w + t * 2f, h, t));
        LobbyBox(geo, "Wall -Z", new Vector3(0f, h * 0.5f, -w * 0.5f - t * 0.5f), new Vector3(w + t * 2f, h, t));
        LobbyBox(geo, "Wall +X", new Vector3(w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, w));
        LobbyBox(geo, "Wall -X", new Vector3(-w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, w));

        GameObject lightGo = Child(room, "Light");
        lightGo.transform.localPosition = new Vector3(0f, h - 0.5f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 10f;
        light.intensity = 1.2f;
        light.shadows = LightShadows.None;
        light.lightmapBakeType = LightmapBakeType.Realtime;
    }

    private static void LobbyBox(GameObject parent, string name, Vector3 localPosition, Vector3 size)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent.transform, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = size;
    }

    [MenuItem("Tools/SupporterGate/Add Info Panel (existing scene)", false, 21)]
    public static void AddInfoPanels()
    {
        int added = 0;
        foreach (SupporterCreditsBoard credits in Object.FindObjectsOfType<SupporterCreditsBoard>(true))
        {
            if (new SerializedObject(credits).FindProperty("infoText").objectReferenceValue != null) continue;
            Transform board = credits.transform;
            GameObject parent = board.parent != null ? board.parent.gameObject : null;
            GameObject info = CreateCanvas(parent != null ? parent : credits.gameObject, InfoPanelName, Vector3.zero, new Vector2(900f, 600f));
            if (parent == null) info.transform.SetParent(null, true);
            Undo.RegisterCreatedObjectUndo(info, "Add Info Panel");
            // ボードの左隣に置く（あとで自由に動かしてよい）
            info.transform.SetPositionAndRotation(board.position - board.right * 1.6f, board.rotation);
            info.transform.localScale = board.localScale;
            TextMeshProUGUI infoText = CreateText(info, "Text", "", 34, new Vector2(20f, 20f), new Vector2(860f, 560f), TextAlignmentOptions.Center);
            FitText(infoText, 20f);
            SetRef(credits, "infoText", infoText);
            EditorSceneManager.MarkSceneDirty(credits.gameObject.scene);
            added++;
        }
        Debug.Log("[SupporterGate] 案内のパネルを " + added + " 個足しました（ボードの左隣。位置は自由に動かせます）");
    }

    /// <summary>
    /// 一式（systemRoot の下）に、文言の表（NoticeTable）と共通の通知（NoticeHub）を配線する。
    /// 何度呼んでも増えない。NoticeHub はシーンに 1 つ（無ければプレハブから置く）。
    /// </summary>
    public static void WireNotices(GameObject systemRoot)
    {
        if (systemRoot == null) return;
        NoticeHub hub = NoticeMenu.EnsureInScene();
        NoticeTable table = EnsureTexts(systemRoot);
        if (hub != null && table != null) NoticeMenu.AddTable(hub, table);

        foreach (SupporterGate gate in systemRoot.GetComponentsInChildren<SupporterGate>(true))
        {
            SetRef(gate, "notice", hub);
            SetRef(gate, "texts", table);
            // 前の版で作ったシーン: ロビーのボタンの文字を、今の言語で出せるようにつなぐ
            foreach (Button button in systemRoot.GetComponentsInChildren<Button>(true))
            {
                TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label == null) continue;
                if (button.name == "EnterButton") SetRef(gate, "enterButtonText", label);
                else if (button.name == "ReturnButton") SetRef(gate, "returnButtonText", label);
                else continue;
                FitLine(label, 18f);
            }
        }
        foreach (SupporterApprovalPanel panel in systemRoot.GetComponentsInChildren<SupporterApprovalPanel>(true))
        {
            SetRef(panel, "notice", hub);
            SetRef(panel, "texts", table);
            Transform denied = panel.transform.Find("DeniedRoot/DeniedText");
            if (denied != null) SetRef(panel, "deniedText", denied.GetComponent<TextMeshProUGUI>());
            FitApprovalTexts(panel);
        }
        SupporterGate boardGate = systemRoot.GetComponentInChildren<SupporterGate>(true);
        foreach (SupporterCreditsBoard credits in systemRoot.GetComponentsInChildren<SupporterCreditsBoard>(true))
        {
            SetRef(credits, "notice", hub);
            SetRef(credits, "texts", table);
            // 名前が多いときや長いときに、枠からはみ出さないようにする（前の版で作ったシーンにも効かせる）
            TextMeshProUGUI boardText = new SerializedObject(credits).FindProperty("text").objectReferenceValue as TextMeshProUGUI;
            if (boardText != null) FitText(boardText, 18f);
            // 同じ一式のゲートを渡す（本人がこのワールドに入れないときの表示用）
            if (boardGate != null) SetRef(credits, "gate", boardGate);
        }
        foreach (SupporterInviteLink link in systemRoot.GetComponentsInChildren<SupporterInviteLink>(true))
        {
            SetRef(link, "notice", hub);
            SetRef(link, "texts", table);
            if (new SerializedObject(link).FindProperty("registry").objectReferenceValue == null)
            {
                SupporterRegistry registry = Object.FindObjectOfType<SupporterRegistry>(true);
                if (registry != null) SetRef(link, "registry", registry);
            }
        }
        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(systemRoot.scene);
    }

    private static NoticeTable EnsureTexts(GameObject systemRoot)
    {
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(TextsPath);
        if (json == null)
        {
            Debug.LogError("[SupporterGate] 文言の表が見つかりません: " + TextsPath);
            return null;
        }
        Transform existing = systemRoot.transform.Find(TextsObjectName);
        NoticeTable table = existing != null ? existing.GetComponent<NoticeTable>() : null;
        if (table == null)
        {
            GameObject go = existing != null ? existing.gameObject : Child(systemRoot, TextsObjectName);
            if (existing == null) Undo.RegisterCreatedObjectUndo(go, "Create SupporterGate Texts");
            table = go.AddUdonSharpComponent<NoticeTable>();
        }
        SetRef(table, "json", json);
        return table;
    }

    /// <summary>文字を、枠に収まるまで自動で小さくする。それでも入らない分は切る</summary>
    private static void FitText(TextMeshProUGUI tmp, float minSize)
    {
        Undo.RecordObject(tmp, "Fit text");
        // 2 回目からは上限を触らない（自動縮小が効いている間、fontSize は縮んだあとの値を返すことがある）
        if (!tmp.enableAutoSizing) tmp.fontSizeMax = tmp.fontSize;
        tmp.fontSizeMin = Mathf.Min(minSize, tmp.fontSizeMax);
        tmp.enableAutoSizing = true;
        tmp.overflowMode = TextOverflowModes.Truncate;
        EditorUtility.SetDirty(tmp);
    }

    /// <summary>
    /// 1 行で出す文字（見出し・名前・状態・ボタン）。折り返さず、枠の幅に収まるまで小さくする。
    /// それでも入らない分は「…」にする。長い言語や長い名前で 2 行になり、枠からはみ出すのを防ぐ
    /// </summary>
    private static void FitLine(TextMeshProUGUI tmp, float minSize)
    {
        if (tmp == null) return;
        Undo.RecordObject(tmp, "Fit line");
        if (!tmp.enableAutoSizing) tmp.fontSizeMax = tmp.fontSize;
        tmp.fontSizeMin = Mathf.Min(minSize, tmp.fontSizeMax);
        tmp.enableAutoSizing = true;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        EditorUtility.SetDirty(tmp);
    }

    /// <summary>承認パネルの、1 行で出す文字を枠に収める（前の版で作ったシーンにも効かせる）</summary>
    private static void FitApprovalTexts(SupporterApprovalPanel panel)
    {
        SerializedObject so = new SerializedObject(panel);
        FitLine(so.FindProperty("headerText").objectReferenceValue as TextMeshProUGUI, 22f);
        FitLine(so.FindProperty("activationButtonText").objectReferenceValue as TextMeshProUGUI, 18f);
        SerializedProperty rows = so.FindProperty("rows");
        for (int i = 0; rows != null && i < rows.arraySize; i++)
        {
            SupporterApprovalRow row = rows.GetArrayElementAtIndex(i).objectReferenceValue as SupporterApprovalRow;
            if (row == null) continue;
            SerializedObject rso = new SerializedObject(row);
            FitLine(rso.FindProperty("nameText").objectReferenceValue as TextMeshProUGUI, 18f);
            FitLine(rso.FindProperty("stateText").objectReferenceValue as TextMeshProUGUI, 14f);
            FitLine(rso.FindProperty("buttonText").objectReferenceValue as TextMeshProUGUI, 16f);
        }
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
            && UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(SupporterInviteLink)) != null
            && UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(SupporterTestPanel)) != null
            && UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(SupporterContentGuard)) != null
            && UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(NoticeTable)) != null
            && UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(NoticeHub)) != null;
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

    private static void SetBool(UdonSharp.UdonSharpBehaviour proxy, string field, bool value)
    {
        SerializedObject so = new SerializedObject(proxy);
        SerializedProperty prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogError($"[SupporterGate] フィールド {field} が {proxy.GetType().Name} にありません");
            return;
        }
        prop.boolValue = value;
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
