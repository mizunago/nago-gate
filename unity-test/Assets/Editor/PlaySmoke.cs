// Play-mode smoke test with ClientSim (test project only).
// Scenario (local player = guest, remote "OwnerDummy" = supporter via ownerDisplayNames, mode = SupporterPresence):
//   t=2   toasts / queue / duplicate suppression
//   t=5   sticky + language override
//   t=8   local player teleports into the content zone (allowed because a supporter is present)
//   t=11  the supporter leaves  -> countdown sticky
//   t=17+ grace (6s) runs out   -> returned to the lobby with a reason
#if UNITY_EDITOR
using System.IO;
using System.Text;
using NagoNotice;
using NagoNotice.EditorTools;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRC.SDKBase;

[InitializeOnLoad]
public static class PlaySmoke
{
    private const string Flag = "NagoPlaySmoke";
    private const string ScenePath = "Assets/Test/Smoke.unity";
    private static string LogPath => Path.Combine(Directory.GetCurrentDirectory(), "smoke-result.txt");

    private static double _start = -1;
    private static bool _spawned, _removed;

    static PlaySmoke()
    {
        if (!SessionState.GetBool(Flag, false)) return;
        Application.logMessageReceived += OnLog;
        EditorApplication.update += OnUpdate;
    }

    public static void Run()
    {
        File.WriteAllText(LogPath, "");
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;   // test project only: local http list
        // ClientSim keeps PlayerData in files between runs. Start clean unless SG_SMOKE_JL_KEEP=1 (to check that a saved setting is restored)
        string saved = Path.Combine(Directory.GetCurrentDirectory(), "ClientSimStorage", "PlayerData");
        if (Env("SG_SMOKE_JL_KEEP") != "1" && Directory.Exists(saved))
        {
            foreach (string f in Directory.GetFiles(saved, "PlayerData_*_Smoke.json")) File.Delete(f);
        }
        BuildScene();
        ClientSimSettings settings = ClientSimSettings.Instance;
        settings.enableClientSim = true;
        settings.spawnPlayer = true;
        settings.hideMenuOnLaunch = true;
        string langEnv = System.Environment.GetEnvironmentVariable("SG_SMOKE_LANG");
        settings.currentLanguage = string.IsNullOrEmpty(langEnv) ? "ja" : langEnv;
        string localName = System.Environment.GetEnvironmentVariable("SG_SMOKE_NAME");
        settings.customLocalPlayerName = string.IsNullOrEmpty(localName) ? "GuestLocal" : localName;
        File.AppendAllText(LogPath, "[EDITOR] local=" + settings.customLocalPlayerName + "\n");
        ClientSimSettings.SaveSettings(settings);
        SessionState.SetBool(Flag, true);
        EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.EnterPlaymode();
    }

    private static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.transform.localScale = new Vector3(20f, 1f, 20f);
        GameObject world = new GameObject("VRCWorld");
        VRCSceneDescriptor desc = world.AddComponent<VRCSceneDescriptor>();
        desc.spawns = new[] { world.transform };
        world.AddComponent<VRC.Core.PipelineManager>();

        // SG_SMOKE_VARIANT=member: members-only world (supporters without membership cannot enter)
        bool memberVariant = System.Environment.GetEnvironmentVariable("SG_SMOKE_VARIANT") == "member";
        // SG_SMOKE_CONVERT=1: start from a world without a gate and run the converter
        bool convert = System.Environment.GetEnvironmentVariable("SG_SMOKE_CONVERT") == "1";
        File.AppendAllText(LogPath, "[EDITOR] variant=" + (memberVariant ? "member" : "presence") + " convert=" + convert + "\n");
        if (convert)
        {
            GameObject oldSpawn = new GameObject("OldSpawn");
            oldSpawn.transform.SetPositionAndRotation(new Vector3(3f, 0f, -4f), Quaternion.Euler(0f, 90f, 0f));
            desc.spawns = new[] { oldSpawn.transform };
            desc.RespawnHeightY = -30f;
            GameObject tower = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tower.name = "Tower";
            tower.transform.position = new Vector3(10f, 5f, 10f);
            tower.transform.localScale = new Vector3(2f, 10f, 2f);
            GameObject hidden = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hidden.name = "HiddenRoom";
            hidden.transform.position = new Vector3(-30f, -3f, 0f);
            hidden.transform.localScale = new Vector3(4f, 2f, 4f);
            hidden.SetActive(false);

            string report = SupporterGateSetup.ConvertExistingWorld(memberVariant ? SupporterGateMode.SupportersOnly : SupporterGateMode.SupporterApproval, memberVariant);
            File.AppendAllText(LogPath, "[EDITOR] convert report: " + report.Replace("\n", " / ") + "\n");
            string again = SupporterGateSetup.ConvertExistingWorld(SupporterGateMode.SupportersOnly, true);
            File.AppendAllText(LogPath, "[EDITOR] convert again: " + again.Replace("\n", " / ") + "\n");

            Transform room = GameObject.Find("SupporterGate System/LobbyRoom").transform;
            Transform cs = GameObject.Find("SupporterGate System/ContentSpawn").transform;
            BoxCollider zone = GameObject.Find("SupporterGate System/ContentZone").GetComponent<BoxCollider>();
            File.AppendAllText(LogPath, "[EDITOR] convert result: spawns=" + desc.spawns.Length + " spawn0=" + desc.spawns[0].name + "@" + desc.spawns[0].position.ToString("0.00")
                + " parent=" + desc.spawns[0].parent.name + " respawnY=" + desc.RespawnHeightY + " room=" + room.position.ToString("0.00")
                + " contentSpawn=" + cs.position.ToString("0.00") + " rotY=" + cs.eulerAngles.y.ToString("0") + " zoneCenter=" + zone.transform.position.ToString("0.00") + " zoneSize=" + zone.size.ToString("0.00")
                + " approvalActive=" + room.Find("ApprovalPanel").gameObject.activeSelf + " roomChildren=" + room.childCount + "\n");
        }
        else
        {
            SupporterGateSetup.CreateSceneSetup();
        }
        SupporterGate gate = Object.FindObjectOfType<SupporterGate>(true);
        SerializedObject so = new SerializedObject(gate);
        if (!convert)
        {
            // SG_SMOKE_MODE: open / supporters / approval / (default) presence
            string modeEnv = Env("SG_SMOKE_MODE");
            SupporterGateMode mode = memberVariant ? SupporterGateMode.SupportersOnly
                : modeEnv == "open" ? SupporterGateMode.Open
                : modeEnv == "supporters" ? SupporterGateMode.SupportersOnly
                : modeEnv == "approval" ? SupporterGateMode.SupporterApproval
                : SupporterGateMode.SupporterPresence;
            so.FindProperty("mode").intValue = (int)mode;
            so.FindProperty("useMemberList").boolValue = memberVariant;
        }
        File.AppendAllText(LogPath, "[EDITOR] gate mode=" + so.FindProperty("mode").intValue + " useMemberList=" + so.FindProperty("useMemberList").boolValue + "\n");
        so.FindProperty("noSupporterGraceSeconds").floatValue = 6f;
        SerializedProperty owners = so.FindProperty("ownerDisplayNames");
        owners.arraySize = 1;
        owners.GetArrayElementAtIndex(0).stringValue = "OwnerDummy";
        so.ApplyModifiedPropertiesWithoutUndo();
        UdonSharpEditorUtility.CopyProxyToUdon(gate);

        SupporterRegistry registry = Object.FindObjectOfType<SupporterRegistry>(true);
        // local test list (python -m http.server) that contains "links"
        // SG_SMOKE_BADURL=1: the list cannot be loaded
        // SG_SMOKE_LIST: another list in TestData (e.g. supporters-130.json)
        string listFile = Env("SG_SMOKE_LIST") == "" ? "supporters.json" : Env("SG_SMOKE_LIST");
        registry.dataUrl = new VRCUrl("http://127.0.0.1:8765/" + (Env("SG_SMOKE_BADURL") == "1" ? "missing.json" : listFile));
        // SG_SMOKE_KEY: the list key (for protected lists). Also turns on the debug log of the registry
        string listKey = Env("SG_SMOKE_KEY");
        if (listKey != "")
        {
            SerializedObject rso = new SerializedObject(registry);
            rso.FindProperty("listKey").stringValue = listKey;
            rso.FindProperty("debugMode").boolValue = true;
            rso.ApplyModifiedPropertiesWithoutUndo();
        }
        UdonSharpEditorUtility.CopyProxyToUdon(registry);

        NoticeHub hub = Object.FindObjectOfType<NoticeHub>(true);
        GameObject canvas = hub.transform.Find("NoticeCanvas").gameObject;
        Transform area = canvas.transform.Find("Area");
        GameObject[] rows = new GameObject[area.childCount];
        TextMeshProUGUI[] texts = new TextMeshProUGUI[area.childCount];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = area.GetChild(i).gameObject;
            texts[i] = rows[i].GetComponentInChildren<TextMeshProUGUI>(true);
        }

        GameObject driverGo = new GameObject("SmokeDriver");
        NoticeSmoke driver = driverGo.AddUdonSharpComponent<NoticeSmoke>();
        driver.hub = hub;
        driver.gate = gate;
        driver.canvas = canvas;
        driver.rows = rows;
        driver.texts = texts;
        driver.contentSpawn = GameObject.Find("SupporterGate System/ContentSpawn").transform;
        foreach (TextMeshProUGUI t in Object.FindObjectsOfType<TextMeshProUGUI>(true))
        {
            if (t.transform.parent == null || t.transform.parent.name != "LobbyPanel") continue;
            if (t.name == "Status") driver.gateStatus = t;
            if (t.name == "Message") driver.gateMessage = t;
        }
        SupporterCreditsBoard board = Object.FindObjectOfType<SupporterCreditsBoard>(true);
        // SG_SMOKE_NOMEMBER=1: a world with nothing for members. The board must not mention members
        if (Env("SG_SMOKE_NOMEMBER") == "1")
        {
            SerializedObject bso = new SerializedObject(board);
            bso.FindProperty("showMemberStatus").boolValue = false;
            bso.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(board);
            File.AppendAllText(LogPath, "[EDITOR] board showMemberStatus=False\n");
        }
        driver.creditsText = board.GetComponentInChildren<TextMeshProUGUI>(true);
        foreach (Canvas c in Object.FindObjectsOfType<Canvas>(true))
        {
            if (c.name == "InfoPanel") driver.infoText = c.GetComponentInChildren<TextMeshProUGUI>(true);
        }
        driver.registry = registry;

        // Discord のコピー欄。QR の URL は SG_SMOKE_QRURL（既定は、テスト用のリストの URL から最後の / を除いたもの）
        SupporterInviteLink invite = Object.FindObjectOfType<SupporterInviteLink>(true);
        if (invite != null)
        {
            string qrUrl = Env("SG_SMOKE_QRURL") == "" ? "https://discord.gg/testInvite" : Env("SG_SMOKE_QRURL");
            File.AppendAllText(LogPath, "[EDITOR] qr: " + SupporterGateSetup.UpdateDiscordQr(qrUrl).Replace("\n", " / ") + "\n");
            SerializedObject iso = new SerializedObject(invite);
            iso.FindProperty("debugLog").boolValue = true;
            iso.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(invite);
            driver.invite = invite;
            driver.inviteField = invite.transform.Find("CopyPanel/UrlField").GetComponent<TMP_InputField>();
            driver.inviteHelp = invite.transform.Find("CopyPanel/Help").GetComponent<TextMeshProUGUI>();
            driver.inviteOpenButton = invite.transform.Find("OpenButton").gameObject;
        }

        // SG_SMOKE_JL: join/leave notice. on = default ON / off = default OFF / toggle = default OFF, switched on at t=5
        string jlMode = Env("SG_SMOKE_JL");
        if (jlMode != "")
        {
            NoticeJoinLeave jl = NoticeMenu.EnsureJoinLeaveInScene();
            GameObject labelCanvas = new GameObject("JoinLeaveLabelCanvas", typeof(Canvas));
            GameObject labelGo = new GameObject("JoinLeaveLabel", typeof(RectTransform));
            labelGo.transform.SetParent(labelCanvas.transform, false);
            TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
            SerializedObject jso = new SerializedObject(jl);
            jso.FindProperty("defaultOn").boolValue = jlMode == "on";
            jso.FindProperty("debugLog").boolValue = true;
            SerializedProperty labels = jso.FindProperty("stateLabels");
            labels.arraySize = 1;
            labels.GetArrayElementAtIndex(0).objectReferenceValue = label;
            jso.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(jl);
            driver.joinLeave = jl;
            driver.joinLeaveLabel = label;
            driver.joinLeaveToggle = jlMode == "toggle";
            File.AppendAllText(LogPath, "[EDITOR] joinleave mode=" + jlMode + " keep=" + Env("SG_SMOKE_JL_KEEP") + "\n");
        }

        driver.skipEnter = Env("SG_SMOKE_NOENTER") == "1";         // stay in the lobby (for screenshots)
        driver.noticeGallery = Env("SG_SMOKE_NOTICES") == "1";     // show every kind of gate notice (for screenshots)
        driver.skipLangSwitch = Env("SG_SHOTS") != "";            // keep one language while taking screenshots
        UdonSharpEditorUtility.CopyProxyToUdon(driver);

        Directory.CreateDirectory("Assets/Test");
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static string Env(string name)
    {
        return System.Environment.GetEnvironmentVariable(name) ?? "";
    }

    // ---- screenshots (SG_SHOTS=<folder>): each panel, and the local player's view ----

    private static int _shot;
    private static bool _early;

    private static void Capture(string folder, string tag, bool withView = true)
    {
        try
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots", folder);
            Directory.CreateDirectory(dir);
            foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>())
            {
                if (canvas.renderMode != RenderMode.WorldSpace) continue;
                string n = canvas.name;
                if (n != "LobbyPanel" && n != "ApprovalPanel" && n != "CreditsBoard" && n != "InfoPanel") continue;
                RenderCanvas(canvas, Path.Combine(dir, tag + "-" + n + ".png"));
            }
            if (withView) RenderView(Path.Combine(dir, tag + "-view.png"));
            File.AppendAllText(LogPath, "[EDITOR] screenshots " + folder + "/" + tag + "\n");
        }
        catch (System.Exception ex)
        {
            File.AppendAllText(LogPath, "[EDITOR] screenshot failed: " + ex.Message + "\n");
        }
    }

    private static void RenderCanvas(Canvas canvas, string path)
    {
        RectTransform rt = canvas.GetComponent<RectTransform>();
        float w = rt.sizeDelta.x * rt.lossyScale.x, h = rt.sizeDelta.y * rt.lossyScale.y;
        GameObject go = new GameObject("ShotCamera");
        Camera cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.orthographic = true;
        cam.orthographicSize = h * 0.5f;
        cam.aspect = w / h;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 3f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
        cam.cullingMask = ~(1 << 5);   // without the notice overlay (UI layer)
        go.transform.SetPositionAndRotation(rt.position - rt.forward * 1f, rt.rotation);
        Save(cam, Mathf.RoundToInt(w * 800f), Mathf.RoundToInt(h * 800f), path);
        Object.DestroyImmediate(go);
    }

    private static void RenderView(string path)
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return;
        VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        GameObject go = new GameObject("ShotCamera");
        Camera cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.cullingMask = ~(1 << 19);   // without ClientSim's own menu (InternalUI layer)
        go.transform.SetPositionAndRotation(head.position, head.rotation);
        Save(cam, 1280, 720, path);
        Object.DestroyImmediate(go);
    }

    private static void Save(Camera cam, int width, int height, string path)
    {
        RenderTexture rtex = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rtex;
        cam.Render();
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rtex;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rtex.Release();
        Object.DestroyImmediate(rtex);
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        bool interesting = condition.Contains("[SMOKE]") || condition.Contains("[NoticeHub]") || condition.Contains("[SupporterRegistry]") || condition.Contains("[NoticeJoinLeave]")
            || type == LogType.Exception || type == LogType.Error
            || condition.Contains("exception") || condition.Contains("halted");
        if (!interesting) return;
        string line = (type == LogType.Log ? "" : "<" + type + "> ") + condition.Replace("\r", "").Split('\n')[0];
        if (type == LogType.Exception || condition.Contains("halted")) line += "\n    " + stackTrace.Replace("\n", "\n    ");
        File.AppendAllText(LogPath, line + "\n", Encoding.UTF8);
    }

    private static void OnUpdate()
    {
        if (!EditorApplication.isPlaying) return;
        if (_start < 0) _start = EditorApplication.timeSinceStartup;
        double t = EditorApplication.timeSinceStartup - _start;

        if (!_spawned && t > 0.5 && ClientSimMain.HasInstance())
        {
            _spawned = true;
            // SG_SMOKE_REMOTE: name of the remote player ("none" = nobody). Only "OwnerDummy" leaves at t=11
            string remote = Env("SG_SMOKE_REMOTE");
            if (string.IsNullOrEmpty(remote)) remote = "OwnerDummy";
            if (remote != "none")
            {
                ClientSimMain.SpawnRemotePlayer(remote);
                File.AppendAllText(LogPath, "[EDITOR] spawned remote " + remote + "\n");
            }
        }
        string shots = Env("SG_SHOTS");
        if (!string.IsNullOrEmpty(shots))
        {
            if (!_early && t > 4.0) { _early = true; Capture(shots, "p", false); }   // first page of the board
            if (_shot < 1 && t > 9.7) { _shot = 1; Capture(shots, "a"); }
            if (_shot < 2 && t > 13.3) { _shot = 2; Capture(shots, "b"); }
            if (_shot < 3 && t > 20.5) { _shot = 3; Capture(shots, "c"); }
        }
        if (!_removed && t > 11.0)
        {
            _removed = true;
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            foreach (VRCPlayerApi p in players)
            {
                if (p != null && p.displayName.Contains("OwnerDummy"))
                {
                    ClientSimMain.RemovePlayer(p);
                    File.AppendAllText(LogPath, "[EDITOR] removed remote OwnerDummy\n");
                }
            }
        }
        if (t > 24.0)
        {
            File.AppendAllText(LogPath, "[EDITOR] SMOKE_END\n");
            SessionState.SetBool(Flag, false);
            EditorApplication.Exit(0);
        }
    }
}
#endif
