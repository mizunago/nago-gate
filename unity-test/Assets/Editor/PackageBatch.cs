// Batch helper for the nago packages (test project only; not shipped).
#if UNITY_EDITOR
using System;
using System.IO;
using NagoNotice;
using NagoNotice.EditorTools;
using TMPro;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

public static class PackageBatch
{
    private static readonly string[] Packages = { "Packages/com.nagonago.notice", "Packages/com.nagonago.supporter-gate" };
    private static string ResultPath => Path.Combine(Directory.GetCurrentDirectory(), "batch-result.txt");

    private static void Log(string msg)
    {
        Debug.Log("[Batch] " + msg);
        File.AppendAllText(ResultPath, msg + "\n");
    }

    /// <summary>U# assembly definitions + program assets, then UdonSharp compile.</summary>
    public static void Prepare()
    {
        File.WriteAllText(ResultPath, "");
        try
        {
            PrepareInner();
            Log("PREPARE_DONE");
        }
        catch (Exception ex) { Log("EXCEPTION: " + ex); }
    }

    private static void PrepareInner()
    {
        // fresh checkout: TMP Essential Resources are not in the repository
        if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
        {
            Log("Importing TMP Essential Resources...");
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh();
        }
        EnsureUdonSharpAsmdef("Packages/com.nagonago.notice/Runtime/NagoNotice.asmdef");
        EnsureUdonSharpAsmdef("Packages/com.nagonago.supporter-gate/Runtime/NagoSupporterGate.asmdef");
        foreach (string pkg in Packages) EnsureProgramAssets(pkg + "/Runtime");
        if (AssetDatabase.IsValidFolder("Assets/Test")) EnsureProgramAssets("Assets/Test");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Log("Compiling UdonSharp...");
        UdonSharpCompilerV1.CompileSync();
        Log("UdonSharp compile done");
        ReportPrograms();
    }

    /// <summary>Prepare + build the notice prefab + render previews.</summary>
    public static void BuildAll()
    {
        File.WriteAllText(ResultPath, "");
        try
        {
            PrepareInner();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(NoticePrefabBuilder.PrefabPath) == null
                || Environment.GetEnvironmentVariable("NAGO_REBUILD_PREFAB") == "1") NoticePrefabBuilder.Build();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NoticePrefabBuilder.PrefabPath);
            Log("prefab: " + (prefab != null ? "OK" : "MISSING"));
            if (prefab != null)
            {
                NoticeHub hub = prefab.GetComponent<NoticeHub>();
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(hub);
                Log("prefab hub program: " + (backing != null && backing.programSource != null ? backing.programSource.name : "NULL"));
                Log("prefab hub.levelClips = " + Var(hub, "levelClips") + " | levelColors = " + Var(hub, "levelColors") + " | rowTexts = " + Var(hub, "rowTexts"));
                Log("prefab colliders: " + prefab.GetComponentsInChildren<Collider>(true).Length
                    + ", raycasters: " + prefab.GetComponentsInChildren<GraphicRaycaster>(true).Length);
                Preview();
                VerifyGate();
            }
            Log("BUILD_DONE");
        }
        catch (Exception ex) { Log("EXCEPTION: " + ex); }
    }

    public static void ReportPrograms()
    {
        int bad = 0;
        foreach (string pkg in Packages)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { pkg + "/Runtime" }))
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                Type cls = script != null ? script.GetClass() : null;
                if (cls == null || cls.IsAbstract || !typeof(UdonSharpBehaviour).IsAssignableFrom(cls)) continue;
                UdonSharpProgramAsset asset = UdonSharpEditorUtility.GetUdonSharpProgramAsset(cls);
                bool ok = asset != null && asset.SerializedProgramAsset != null && asset.SerializedProgramAsset.RetrieveProgram() != null;
                if (!ok) bad++;
                Log("program " + cls.FullName + ": " + (asset == null ? "MISSING ASSET" : ok ? "OK" : "NO PROGRAM"));
            }
        }
        Log(bad == 0 ? "PROGRAMS_OK" : "PROGRAMS_BAD " + bad);
    }

    private static void EnsureUdonSharpAsmdef(string asmdefPath)
    {
        AssemblyDefinitionAsset asmdef = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(asmdefPath);
        if (asmdef == null) { Log("asmdef not found: " + asmdefPath); return; }
        string assetPath = Path.ChangeExtension(asmdefPath, ".asset").Replace("\\", "/");
        UdonSharpAssemblyDefinition existing = AssetDatabase.LoadAssetAtPath<UdonSharpAssemblyDefinition>(assetPath);
        if (existing != null)
        {
            if (existing.sourceAssembly != asmdef) { existing.sourceAssembly = asmdef; EditorUtility.SetDirty(existing); }
            return;
        }
        UdonSharpAssemblyDefinition def = ScriptableObject.CreateInstance<UdonSharpAssemblyDefinition>();
        def.sourceAssembly = asmdef;
        AssetDatabase.CreateAsset(def, assetPath);
        Log("created U# asmdef: " + assetPath);
    }

    private static void EnsureProgramAssets(string folder)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Type cls = script != null ? script.GetClass() : null;
            if (cls == null || cls.IsAbstract || !typeof(UdonSharpBehaviour).IsAssignableFrom(cls)) continue;
            string assetPath = Path.ChangeExtension(path, ".asset").Replace("\\", "/");
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null) continue;
            UdonSharpProgramAsset programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = script;
            AssetDatabase.CreateAsset(programAsset, assetPath);
            Log("created program asset: " + assetPath);
        }
    }

    private static string Var(UdonSharpBehaviour proxy, string symbol)
    {
        var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
        object value;
        if (backing == null || backing.publicVariables == null || !backing.publicVariables.TryGetVariableValue(symbol, out value)) return "(unset)";
        if (value == null) return "null";
        Array arr = value as Array;
        if (arr != null) return "array[" + arr.Length + "]";
        return value.ToString();
    }

    /// <summary>Create the gate setup in a fresh scene and check the notice wiring; then run the upgrade path twice.</summary>
    public static void VerifyGate()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        SupporterGateSetup.CreateSceneSetup();
        SupporterGate gate = UnityEngine.Object.FindObjectOfType<SupporterGate>(true);
        if (gate == null) { Log("gate: MISSING"); return; }
        Log("gate.notice = " + Var(gate, "notice"));
        Log("gate.texts = " + Var(gate, "texts"));
        Log("gate.registry = " + Var(gate, "registry"));
        SupporterApprovalPanel panel = UnityEngine.Object.FindObjectOfType<SupporterApprovalPanel>(true);
        Log("panel.texts = " + Var(panel, "texts") + " | panel.deniedText = " + Var(panel, "deniedText") + " | rows = " + Var(panel, "rows"));
        SupporterCreditsBoard credits = UnityEngine.Object.FindObjectOfType<SupporterCreditsBoard>(true);
        Log("credits.texts = " + Var(credits, "texts"));
        NoticeHub hub = UnityEngine.Object.FindObjectOfType<NoticeHub>(true);
        Log("hub.tables = " + Var(hub, "tables") + " | hub.rowTexts = " + Var(hub, "rowTexts") + " | hub.levelClips = " + Var(hub, "levelClips")
            + " | hub parent = " + (hub.transform.parent == null ? "(scene root)" : hub.transform.parent.name));
        NoticeTable table = UnityEngine.Object.FindObjectOfType<NoticeTable>(true);
        Log("table.json = " + Var(table, "json"));

        SupporterGateSetup.UpgradeScene();
        SupporterGateSetup.UpgradeScene();
        Log("after upgrade x2: hubs=" + UnityEngine.Object.FindObjectsOfType<NoticeHub>(true).Length
            + " tables=" + UnityEngine.Object.FindObjectsOfType<NoticeTable>(true).Length
            + " hub.tables=" + Var(hub, "tables"));
        int wired = 0, total = 0;
        foreach (Button b in UnityEngine.Object.FindObjectsOfType<Button>(true)) { total++; if (b.onClick.GetPersistentEventCount() > 0) wired++; }
        Log("buttons: " + total + ", wired: " + wired);
        Log("VERIFY_GATE_DONE");
        VerifyInvite(credits);
        VerifyBoardOnly();
    }

    /// <summary>Discord のコピー欄: 一式の生成で付き、配線され、QR が作れて、案内の文がボタンの分だけ上に詰まっている</summary>
    private static void VerifyInvite(SupporterCreditsBoard credits)
    {
        SupporterInviteLink invite = UnityEngine.Object.FindObjectOfType<SupporterInviteLink>(true);
        if (invite == null) { Log("VERIFY_INVITE_FAIL missing"); return; }
        const string url = "https://discord.gg/ENg5sh23J5";
        string qrMsg = SupporterGateSetup.UpdateDiscordQr(url);
        Log("qr: " + qrMsg.Replace("\n", " / "));
        UdonSharpEditorUtility.CopyUdonToProxy(invite);
        string[] refs = { "registry", "openButton", "openLabel", "copyPanel", "urlField", "helpText", "closeLabel", "qrRoot", "texts", "notice" };
        bool ok = true;
        foreach (string r in refs)
        {
            string v = Var(invite, r);
            Log("invite." + r + " = " + v);
            if (v == "null" || v == "(unset)") ok = false;
        }
        string qrUrl = Var(invite, "qrUrl");
        RawImage image = invite.transform.Find("CopyPanel/QR").GetComponent<RawImage>();
        string texPath = image.texture != null ? AssetDatabase.GetAssetPath(image.texture) : "";
        TextMeshProUGUI infoText = new SerializedObject(credits).FindProperty("infoText").objectReferenceValue as TextMeshProUGUI;
        float infoY = infoText != null ? infoText.rectTransform.anchoredPosition.y : -1f;
        Button open = invite.transform.Find("OpenButton").GetComponent<Button>();
        TMP_InputField field = invite.transform.Find("CopyPanel/UrlField").GetComponent<TMP_InputField>();
        Log("invite qrUrl=" + qrUrl + " texture=" + texPath + " (" + (image.texture != null ? image.texture.width + "px" : "-") + ") infoTextY=" + infoY
            + " openWired=" + open.onClick.GetPersistentEventCount() + " fieldEndEdit=" + field.onEndEdit.GetPersistentEventCount()
            + " copyPanelActive=" + invite.transform.Find("CopyPanel").gameObject.activeSelf + " openActive=" + open.gameObject.activeSelf);
        if (texPath != "") File.Copy(Path.GetFullPath(texPath), Path.Combine(Directory.GetCurrentDirectory(), "qr-check.png"), true);
        ok = ok && qrUrl == url && texPath != "" && Mathf.Approximately(infoY, 96f) && open.onClick.GetPersistentEventCount() == 1
            && field.onEndEdit.GetPersistentEventCount() == 1 && !invite.transform.Find("CopyPanel").gameObject.activeSelf;
        Log(ok ? "VERIFY_INVITE_OK" : "VERIFY_INVITE_FAIL");
        VerifyAddInvite();
    }

    /// <summary>前の版で作ったシーン（コピー欄の無い案内のパネル）に、メニューから足す。2 回流しても増えない</summary>
    private static void VerifyAddInvite()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        SupporterGateSetup.CreateSceneSetup();
        foreach (SupporterInviteLink l in UnityEngine.Object.FindObjectsOfType<SupporterInviteLink>(true)) UnityEngine.Object.DestroyImmediate(l.gameObject);
        SupporterCreditsBoard credits = UnityEngine.Object.FindObjectOfType<SupporterCreditsBoard>(true);
        TextMeshProUGUI infoText = new SerializedObject(credits).FindProperty("infoText").objectReferenceValue as TextMeshProUGUI;
        infoText.rectTransform.anchoredPosition = new Vector2(20f, 20f);   // 前の版の大きさに戻す
        infoText.rectTransform.sizeDelta = new Vector2(860f, 560f);
        string first = SupporterGateSetup.AddDiscordCopyPanelsSilent();
        string second = SupporterGateSetup.AddDiscordCopyPanelsSilent();
        Log("add invite: " + first.Replace("\n", " / ") + " || again: " + second.Replace("\n", " / "));
        SupporterInviteLink[] links = UnityEngine.Object.FindObjectsOfType<SupporterInviteLink>(true);
        string texts = links.Length > 0 ? Var(links[0], "texts") : "-";
        Log("add invite x2: links=" + links.Length + " texts=" + texts + " infoText=" + infoText.rectTransform.anchoredPosition + " " + infoText.rectTransform.sizeDelta);
        bool ok = links.Length == 1 && texts.Contains("UdonBehaviour") && Mathf.Approximately(infoText.rectTransform.anchoredPosition.y, 96f) && Mathf.Approximately(infoText.rectTransform.sizeDelta.y, 484f);
        Log(ok ? "VERIFY_ADD_INVITE_OK" : "VERIFY_ADD_INVITE_FAIL");
        VerifySmallBoard();
    }

    /// <summary>
    /// 板が小さい（820×520）。案内の文は額縁に合わせて引き伸ばしてある。
    /// コピー欄の部品が板からはみ出さず、QR は正方形で、文の上の辺は動かない。0.6.1 の置き方からの置き直しも確かめる
    /// </summary>
    private static void VerifySmallBoard()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        SupporterGateSetup.CreateSceneSetup();
        SupporterInviteLink first = UnityEngine.Object.FindObjectOfType<SupporterInviteLink>(true);
        RectTransform board = (RectTransform)first.transform.parent;
        board.sizeDelta = new Vector2(820f, 520f);
        // 0.6.1 の置き方（決まった位置）に戻して、置き直しを試す
        foreach (string n in new[] { "OpenButton", "CopyPanel/UrlField", "CopyPanel/Help", "CopyPanel/QR", "CopyPanel/CloseButton" })
        {
            RectTransform r = (RectTransform)first.transform.Find(n);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.zero; r.pivot = Vector2.zero;
        }
        ((RectTransform)first.transform.Find("CopyPanel/UrlField")).anchoredPosition = new Vector2(20f, 490f);
        ((RectTransform)first.transform.Find("CopyPanel/UrlField")).sizeDelta = new Vector2(860f, 90f);
        // 案内の文は、額縁に合わせて引き伸ばした形にする
        SupporterCreditsBoard credits = UnityEngine.Object.FindObjectOfType<SupporterCreditsBoard>(true);
        RectTransform text = (new SerializedObject(credits).FindProperty("infoText").objectReferenceValue as TextMeshProUGUI).rectTransform;
        text.anchorMin = Vector2.zero; text.anchorMax = Vector2.one; text.pivot = new Vector2(0.5f, 0.5f);
        text.offsetMin = new Vector2(40f, 40f); text.offsetMax = new Vector2(-40f, -60f);
        Vector3[] tc = new Vector3[4];
        text.GetWorldCorners(tc);
        float topBefore = board.InverseTransformPoint(tc[1]).y;
        string msg = SupporterGateSetup.AddDiscordCopyPanelsSilent();
        SupporterGateSetup.AddDiscordCopyPanelsSilent();
        text.GetWorldCorners(tc);
        float topAfter = board.InverseTransformPoint(tc[1]).y, bottomAfter = board.InverseTransformPoint(tc[0]).y;
        Vector3[] bc = new Vector3[4];
        board.GetWorldCorners(bc);
        Rect boardRect = board.rect;
        bool inside = true;
        string worst = "";
        foreach (string n in new[] { "OpenButton", "CopyPanel", "CopyPanel/UrlField", "CopyPanel/Help", "CopyPanel/QR", "CopyPanel/CloseButton" })
        {
            RectTransform r = (RectTransform)first.transform.Find(n);
            Vector3[] c = new Vector3[4];
            r.GetWorldCorners(c);
            foreach (Vector3 w in c)
            {
                Vector3 l = board.InverseTransformPoint(w);
                if (l.x < boardRect.xMin - 0.5f || l.x > boardRect.xMax + 0.5f || l.y < boardRect.yMin - 0.5f || l.y > boardRect.yMax + 0.5f) { inside = false; worst = n + " " + l; }
            }
        }
        RectTransform qr = (RectTransform)first.transform.Find("CopyPanel/QR");
        Vector3[] oc = new Vector3[4];
        ((RectTransform)first.transform.Find("OpenButton")).GetWorldCorners(oc);
        float openTop = board.InverseTransformPoint(oc[1]).y;
        int links = UnityEngine.Object.FindObjectsOfType<SupporterInviteLink>(true).Length;
        Log("small board: inside=" + inside + (worst != "" ? " (" + worst + ")" : "") + " qr=" + qr.rect.width.ToString("0.0") + "x" + qr.rect.height.ToString("0.0")
            + " textTop " + topBefore.ToString("0.0") + "->" + topAfter.ToString("0.0") + " textBottom=" + bottomAfter.ToString("0.0") + " openTop=" + openTop.ToString("0.0") + " links=" + links
            + " msg=" + msg.Split('\n')[0] + " / " + (msg.Contains("置き直しました") ? "置き直した" : "置き直していない"));
        bool ok = inside && Mathf.Abs(qr.rect.width - qr.rect.height) < 0.5f && Mathf.Abs(topBefore - topAfter) < 0.5f && bottomAfter >= openTop - 0.5f && links == 1 && msg.Contains("置き直しました");
        Log(ok ? "VERIFY_SMALL_BOARD_OK" : "VERIFY_SMALL_BOARD_FAIL");
    }

    /// <summary>A scene without a gate (a public world with only the registry and the board): the upgrade path must still wire the board.</summary>
    private static void VerifyBoardOnly()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        SupporterGateSetup.CreateSceneSetup();
        // remove everything that belongs to the gate, and the notice wiring, so that only the registry and the board remain
        foreach (SupporterContentZone z in UnityEngine.Object.FindObjectsOfType<SupporterContentZone>(true)) UnityEngine.Object.DestroyImmediate(z.gameObject);
        foreach (SupporterApprovalPanel p in UnityEngine.Object.FindObjectsOfType<SupporterApprovalPanel>(true)) UnityEngine.Object.DestroyImmediate(p.gameObject);
        foreach (SupporterGate g in UnityEngine.Object.FindObjectsOfType<SupporterGate>(true)) UnityEngine.Object.DestroyImmediate(g.gameObject);
        foreach (NoticeTable t in UnityEngine.Object.FindObjectsOfType<NoticeTable>(true)) UnityEngine.Object.DestroyImmediate(t.gameObject);
        foreach (NoticeHub h in UnityEngine.Object.FindObjectsOfType<NoticeHub>(true)) UnityEngine.Object.DestroyImmediate(h.gameObject);
        SupporterCreditsBoard credits = UnityEngine.Object.FindObjectOfType<SupporterCreditsBoard>(true);
        if (credits == null) { Log("VERIFY_BOARD_ONLY_FAIL board missing"); return; }
        UdonSharpEditorUtility.CopyUdonToProxy(credits);
        Log("board only, before: gates=" + UnityEngine.Object.FindObjectsOfType<SupporterGate>(true).Length + " credits.texts = " + Var(credits, "texts") + " | credits.notice = " + Var(credits, "notice"));

        SupporterGateSetup.UpgradeScene();
        SupporterGateSetup.UpgradeScene();
        string texts = Var(credits, "texts"), notice = Var(credits, "notice");
        int hubs = UnityEngine.Object.FindObjectsOfType<NoticeHub>(true).Length, tables = UnityEngine.Object.FindObjectsOfType<NoticeTable>(true).Length;
        Log("board only, after upgrade x2: credits.texts = " + texts + " | credits.notice = " + notice + " | hubs=" + hubs + " tables=" + tables);
        bool ok = texts.Contains("UdonBehaviour") && notice.Contains("UdonBehaviour") && hubs == 1 && tables == 1;
        Log(ok ? "VERIFY_BOARD_ONLY_OK" : "VERIFY_BOARD_ONLY_FAIL");
        VerifyJoinLeave();
    }

    /// <summary>The join/leave notice menu in an empty scene: it must place the hub too, wire the clips, and not duplicate on a second run.</summary>
    private static void VerifyJoinLeave()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        NoticeJoinLeave first = NoticeMenu.EnsureJoinLeaveInScene();
        NoticeJoinLeave second = NoticeMenu.EnsureJoinLeaveInScene();
        if (first == null) { Log("VERIFY_JOINLEAVE_FAIL missing"); return; }
        string notice = Var(first, "notice"), join = Var(first, "joinClip"), leave = Var(first, "leaveClip"), key = Var(first, "saveKey");
        int count = UnityEngine.Object.FindObjectsOfType<NoticeJoinLeave>(true).Length, hubs = UnityEngine.Object.FindObjectsOfType<NoticeHub>(true).Length;
        Log("joinleave: notice = " + notice + " | joinClip = " + join + " | leaveClip = " + leave + " | saveKey = " + key + " | defaultOn = " + Var(first, "defaultOn")
            + " | count=" + count + " hubs=" + hubs + " same=" + (first == second));
        bool ok = notice.Contains("UdonBehaviour") && join.Contains("notice-join") && leave.Contains("notice-leave") && key == "nago.notice.joinleave"
            && count == 1 && hubs == 1 && first == second;
        Log(ok ? "VERIFY_JOINLEAVE_OK" : "VERIFY_JOINLEAVE_FAIL");
    }

    // ------------------------------------------------------------------
    // Preview: render the notice in front of a camera, with a wall between
    // ------------------------------------------------------------------

    private static readonly Color[] LevelColors =
    {
        new Color(0.36f, 0.67f, 1.00f), new Color(0.45f, 0.85f, 0.50f), new Color(1.00f, 0.75f, 0.30f), new Color(1.00f, 0.42f, 0.42f),
    };

    public static void Preview()
    {
        string outDir = Path.Combine(Directory.GetCurrentDirectory(), "preview");
        Directory.CreateDirectory(outDir);

        RenderCase(outDir, "01-ja", new[]
        {
            "支援者が退出しました。あと 1:42 でロビーに戻ります",
            "入場が許可されました",
            "アイテム「撮影用ライト」は 主任（1200pt）で解放されます。\n現在: 850pt",
        }, new[] { 2, 1, 0 });

        RenderCase(outDir, "02-en", new[]
        {
            "The supporter has left. You will return to the lobby in 1:42",
            "Approved.",
            "The supporter revoked your access, so you were returned to the lobby. Ask a supporter in the instance to approve you again from the approval panel.",
        }, new[] { 2, 1, 2 });

        RenderCase(outDir, "03-long-ja", new[]
        {
            "支援者が入場の許可を取り消したため、ロビーに戻りました。もう一度入るには、在室中の支援者に承認パネルから許可してもらってください。",
            "満室です",
        }, new[] { 2, 3 });

        RenderCase(outDir, "04-zh-ko", new[]
        {
            "支持者已离开。1:42 后将返回大厅",
            "후원자가 퇴장했습니다. 1:42 후 로비로 돌아갑니다",
            "<noparse><b>Name</b></noparse> さんが来ました。承認パネルで入場を許可できます",
        }, new[] { 2, 2, 0 });
    }

    /// <summary>
    /// Discord のコピー欄の見た目を確かめる: 案内のパネルを、閉じた状態と開いた状態で撮って preview/ に書き出す。
    /// 編集中は Udon が動かないので、ワールドで出る文を、文言の表から読んで入れておく
    /// </summary>
    public static void PreviewInvite()
    {
        File.WriteAllText(ResultPath, "");
        try
        {
            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "preview");
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SupporterGateSetup.CreateSceneSetup();
            Log("qr: " + SupporterGateSetup.UpdateDiscordQr("https://discord.gg/ENg5sh23J5").Replace("\n", " / "));
            SupporterInviteLink invite = UnityEngine.Object.FindObjectOfType<SupporterInviteLink>(true);
            Transform info = invite.transform.parent;
            string json = AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/com.nagonago.supporter-gate/Runtime/SupporterGateTexts.json").text;
            foreach (string lang in new[] { "ja", "en" })
            {
                TextMeshProUGUI infoText = info.Find("Text").GetComponent<TextMeshProUGUI>();
                infoText.text = "<size=80%><color=#C9B8FF><b>" + (lang == "ja" ? "あなたの状態" : "Your status") + "</b></color></size>\n<size=110%>"
                    + (lang == "ja" ? "あなたは<color=#9BE7A8><b>住人</b></color>です" : "You are: <color=#9BE7A8><b>Resident</b></color>") + "</size>\n\n<size=80%><color=#C9B8FF><b>"
                    + (lang == "ja" ? "ご案内" : "Information") + "</b></color></size>\n<size=80%>" + JsonText(json, "info.guide", lang) + "</size>\n<size=115%><b>discord.gg/ENg5sh23J5</b></size>";
                invite.transform.Find("OpenButton").gameObject.SetActive(true);
                invite.transform.Find("OpenButton").GetComponentInChildren<TextMeshProUGUI>().text = JsonText(json, "info.copy.open", lang);
                Transform panel = invite.transform.Find("CopyPanel");
                panel.gameObject.SetActive(false);
                RenderPanel(outDir, "invite-closed-" + lang, info);
                panel.gameObject.SetActive(true);
                panel.Find("UrlField").GetComponent<TMP_InputField>().text = "https://discord.gg/ENg5sh23J5";
                panel.Find("Help").GetComponent<TextMeshProUGUI>().text = JsonText(json, "info.copy.help", lang);
                panel.Find("CloseButton").GetComponentInChildren<TextMeshProUGUI>().text = JsonText(json, "info.copy.close", lang);
                RenderPanel(outDir, "invite-open-" + lang, info);
            }
            // 板が小さいとき（820×520）
            ((RectTransform)info).sizeDelta = new Vector2(820f, 520f);
            SupporterGateSetup.AddDiscordCopyPanelsSilent();
            RenderPanel(outDir, "invite-open-small", info);
            Log("PREVIEW_INVITE_DONE");
        }
        catch (Exception ex) { Log("EXCEPTION: " + ex); }
    }

    /// <summary>文言の表（JSON）から 1 つの文を取り出す（\n と \" だけ戻す）</summary>
    private static string JsonText(string json, string key, string lang)
    {
        int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        int l = json.IndexOf("\"" + lang + "\"", k, StringComparison.Ordinal);
        int start = json.IndexOf('"', json.IndexOf(':', l) + 1) + 1;
        var sb = new System.Text.StringBuilder();
        for (int i = start; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '\\') { char n = json[++i]; sb.Append(n == 'n' ? '\n' : n); continue; }
            if (c == '"') break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static void RenderPanel(string outDir, string name, Transform canvas)
    {
        Camera cam = UnityEngine.Object.FindObjectOfType<Camera>();
        if (cam == null)
        {
            cam = new GameObject("PreviewCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.62f, 0.70f);
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 50f;
        }
        cam.transform.position = canvas.position - canvas.forward * 1.15f;
        cam.transform.rotation = Quaternion.LookRotation(canvas.forward, Vector3.up);
        Canvas.ForceUpdateCanvases();
        foreach (TextMeshProUGUI t in canvas.GetComponentsInChildren<TextMeshProUGUI>(true)) t.ForceMeshUpdate();
        Canvas.ForceUpdateCanvases();
        const int W = 1200, H = 820;
        RenderTexture rtx = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        rtx.antiAliasing = 4;
        cam.targetTexture = rtx;
        cam.Render();
        RenderTexture.active = rtx;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Log("rendered " + name);
    }

    private static void RenderCase(string outDir, string name, string[] texts, int[] levels)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject camGo = new GameObject("Cam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.transform.position = new Vector3(0f, 1.6f, 0f);
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.62f, 0.70f);

        // A wall closer than the notice, covering the left half: the notice must draw over it.
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(-0.75f, 1.3f, 0.8f);
        wall.transform.localScale = new Vector3(1.5f, 2f, 0.05f);
        GameObject light = new GameObject("Light");
        light.AddComponent<Light>().type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(40f, 20f, 0f);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NoticePrefabBuilder.PrefabPath);
        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Transform canvas = inst.transform.Find("NoticeCanvas");
        canvas.gameObject.SetActive(true);
        Vector3 dir = Quaternion.Euler(12f, 0f, 0f) * Vector3.forward;
        canvas.position = cam.transform.position + dir * 1.2f;
        canvas.rotation = Quaternion.LookRotation(dir, Vector3.up);
        canvas.localScale = Vector3.one * 0.001f;

        Transform area = canvas.Find("Area");
        for (int i = 0; i < area.childCount; i++)
        {
            Transform row = area.GetChild(i);
            bool on = i < texts.Length;
            row.gameObject.SetActive(on);
            if (!on) continue;
            row.GetComponent<CanvasGroup>().alpha = 1f;
            TextMeshProUGUI t = row.GetComponentInChildren<TextMeshProUGUI>(true);
            t.text = texts[i];
            t.ForceMeshUpdate();
            row.Find("Accent").GetComponent<Image>().color = LevelColors[levels[i]];
        }
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)area);
        Canvas.ForceUpdateCanvases();

        for (int i = 0; i < texts.Length; i++)
        {
            RectTransform rt = (RectTransform)area.GetChild(i);
            TextMeshProUGUI t = rt.GetComponentInChildren<TextMeshProUGUI>(true);
            Log(name + " row" + i + ": " + rt.rect.width.ToString("0") + "x" + rt.rect.height.ToString("0")
                + " lines=" + t.textInfo.lineCount + " chars=" + t.textInfo.characterCount);
        }
        Log(name + " area: " + ((RectTransform)area).rect.width.ToString("0") + "x" + ((RectTransform)area).rect.height.ToString("0"));

        const int W = 1280, H = 720;
        RenderTexture rtx = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        rtx.antiAliasing = 4;
        cam.targetTexture = rtx;
        cam.Render();
        RenderTexture.active = rtx;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Log("rendered " + name);
    }
}
#endif
