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
