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
        BuildScene();
        ClientSimSettings settings = ClientSimSettings.Instance;
        settings.enableClientSim = true;
        settings.spawnPlayer = true;
        settings.hideMenuOnLaunch = true;
        settings.currentLanguage = "ja";
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
            so.FindProperty("mode").intValue = (int)(memberVariant ? SupporterGateMode.SupportersOnly : SupporterGateMode.SupporterPresence);
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
        registry.dataUrl = new VRCUrl("http://127.0.0.1:8765/supporters.json");
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
        driver.creditsText = Object.FindObjectOfType<SupporterCreditsBoard>(true).GetComponentInChildren<TextMeshProUGUI>(true);
        driver.registry = registry;
        UdonSharpEditorUtility.CopyProxyToUdon(driver);

        Directory.CreateDirectory("Assets/Test");
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        bool interesting = condition.Contains("[SMOKE]") || condition.Contains("[NoticeHub]") || condition.Contains("[SupporterRegistry]")
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
            ClientSimMain.SpawnRemotePlayer("OwnerDummy");
            File.AppendAllText(LogPath, "[EDITOR] spawned remote OwnerDummy\n");
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
