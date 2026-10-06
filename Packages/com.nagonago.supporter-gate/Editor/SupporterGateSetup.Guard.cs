// SupporterGateSetup.Guard.cs
// 入れない人の画面で、ワールド本体を見せない・聞かせない仕組み（SupporterContentGuard）を、シーンから自動で組む。
//
//   Tools > SupporterGate > Set Up Content Guard (auto)   何度実行してもよい。ワールド本体を変えたら、もう一度実行する
//
// あやしいけんきゅうじょの GateSetupTool（2026-10-06・07）の選び方を、どのワールドでも使えるように一般にしたもの。
// 選び方:
//   1. 止める物（roots）: シーン直下から下りながら、次のどれにも当たらない部分木の根を選ぶ
//      - 同期する物（同期の設定が None でない Udon・ObjectSync・Pickup・Station）を含む。
//        NoVariableSync の Udon も含める（同期する変数は無くても、ネットワークのイベントを受けられるため）
//      - ほかのギミックが SetActive で切り替える物（Udon の変数・Animator・Timeline・UI のイベントから参照される GameObject）
//      - 部分木の外の Udon から呼ばれる Udon を含む（止めると、呼ぶ側が困る）
//      - ワールド全体に効く物（VRC Scene Descriptor・カメラ・ライト・ポストエフェクト・ライトマップの保存役など）を含む。
//        止めると、入口の部屋の見え方も変わったり、ワールドが壊れたりする（照明をまとめた親も、止めずに中を見る）
//      当たったら、その物は根にせず、子を見ていく。最初から非アクティブの物と EditorOnly は根にしない
//   2. 見た目だけ消す物: 根に入らなかった Renderer と Canvas（最初は非アクティブの物も含める。あとでスイッチが出す物があるため）
//   3. 音を消す物: ワールドの AudioSource
//   どれも、ゲート一式（入口の部屋・パネル。ただし Gate の Content Roots は中身として扱う）・通知の板・EditorOnly・
//   ガードの keep に入れた物の下は、対象にしない。
// 選んだ根はガードに渡し、Gate の Content Roots は空にする（ゲートは毎秒すべての根を調べ直すので、根が多いと引っかかる）。

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NagoNotice;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using VRC.SDK3.Components;
using VRC.Udon;

public static partial class SupporterGateSetup
{
    private const string GuardName = "ContentGuard";

    [MenuItem("Tools/SupporterGate/Set Up Content Guard (auto)", false, 25)]
    public static void SetUpContentGuard()
    {
        EditorUtility.DisplayDialog("SupporterGate", SetUpContentGuardSilent(), "OK");
    }

    /// <summary>ガードを作る（あれば作り直さずに中身だけ選び直す）。ダイアログを出さずに、やったことの説明を返す</summary>
    public static string SetUpContentGuardSilent()
    {
        if (!ProgramAssetsReady()) return "UdonSharp のプログラムアセットがまだ生成されていません。コンパイルが終わるのを待ってから、もう一度実行してください。";
        SupporterGate gate = UnityEngine.Object.FindObjectOfType<SupporterGate>(true);
        if (gate == null) return "シーンに SupporterGate がありません。先に Create Scene Setup か Convert Existing World を実行してください。";
        Scene scene = gate.gameObject.scene;
        GameObject systemRoot = gate.transform.parent != null ? gate.transform.parent.gameObject : gate.gameObject;

        // ガード（あれば使う。keep は引き継ぐ）
        SupporterContentGuard guard = systemRoot.GetComponentInChildren<SupporterContentGuard>(true);
        bool created = guard == null;
        if (created)
        {
            GameObject go = Child(systemRoot, GuardName);
            Undo.RegisterCreatedObjectUndo(go, "Add SupporterGate Content Guard");
            guard = go.AddUdonSharpComponent<SupporterContentGuard>();
        }
        SerializedObject gso = new SerializedObject(guard);
        List<GameObject> keep = new List<GameObject>();
        SerializedProperty keepProp = gso.FindProperty("keep");
        for (int i = 0; i < keepProp.arraySize; i++)
        {
            GameObject k = keepProp.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            if (k != null) keep.Add(k);
        }

        // 中身として扱う物: 前に引き継いだ物と、Gate の Content Roots（ゲート一式の下にある ContentRoot など）。
        // ゲートの側は空にするので、ガードの content に覚えておく（選び直しても消えない）
        SerializedObject gateSo = new SerializedObject(gate);
        SerializedProperty gateRoots = gateSo.FindProperty("contentRoots");
        List<GameObject> gateContent = new List<GameObject>();
        SerializedProperty contentProp = gso.FindProperty("content");
        for (int i = 0; i < contentProp.arraySize; i++)
        {
            GameObject g = contentProp.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            if (g != null && !gateContent.Contains(g)) gateContent.Add(g);
        }
        for (int i = 0; i < gateRoots.arraySize; i++)
        {
            GameObject g = gateRoots.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            if (g != null && !gateContent.Contains(g)) gateContent.Add(g);
        }

        GuardScan scan = new GuardScan(scene, systemRoot.transform, gateContent, keep, guard);

        // 1. 止める物
        List<GameObject> roots = new List<GameObject>();
        int skippedSynced = 0, skippedToggled = 0, skippedCalled = 0, skippedPinned = 0;
        foreach (Transform start in scan.Starts())
            CollectRoots(start, scan, roots, ref skippedSynced, ref skippedToggled, ref skippedCalled, ref skippedPinned);
        HashSet<Transform> rootSet = new HashSet<Transform>(roots.Select(r => r.transform));

        // 2・3. 見た目だけ消す物と、音を消す物
        Renderer[] renderers = scan.All<Renderer>().Where(r => !scan.UnderAny(r.transform, rootSet)).ToArray();
        Canvas[] canvases = scan.All<Canvas>().Where(c => !scan.UnderAny(c.transform, rootSet)).ToArray();
        AudioSource[] sources = scan.All<AudioSource>().ToArray();

        SetArray(gso, "roots", roots.ToArray());
        SetArray(gso, "hideRenderers", renderers);
        SetArray(gso, "hideCanvases", canvases);
        SetArray(gso, "sources", sources);
        SetArray(gso, "content", gateContent.ToArray());
        gso.FindProperty("gate").objectReferenceValue = gate;
        gso.ApplyModifiedPropertiesWithoutUndo();
        UdonSharpEditorUtility.CopyProxyToUdon(guard);

        // ゲートには、もう根を持たせない（ガードが切り替える）
        gateRoots.arraySize = 0;
        gateSo.ApplyModifiedPropertiesWithoutUndo();
        UdonSharpEditorUtility.CopyProxyToUdon(gate);
        EditorSceneManager.MarkSceneDirty(scene);

        string msg =
            "入れない人の画面で、ワールド本体を見せない・聞かせない仕組み（" + GuardName + "）を" + (created ? "作りました" : "選び直しました") + "。\n" +
            "・止める物: " + roots.Count + " 個（同期する物を含むので分けた所 " + skippedSynced + "、ギミックが切り替える物 " + skippedToggled +
            "、ほかの Udon から呼ばれる物 " + skippedCalled + "、ワールド全体に効く物 " + skippedPinned + "）\n" +
            "・見た目だけ消す物: Renderer " + renderers.Length + " 個、Canvas " + canvases.Length + " 個\n" +
            "・音を消す物: " + sources.Length + " 個\n" +
            "・隠さない物（keep）: " + keep.Count + " 個\n" +
            "Gate の Content Roots は空にしました（" + GuardName + " が切り替えます）。\n" +
            "ワールド本体を変えたら、もう一度実行してください。";
        Debug.Log("[SupporterGate] " + msg.Replace("\n", " "));
        return msg;
    }

    private static void CollectRoots(Transform t, GuardScan scan, List<GameObject> result, ref int synced, ref int toggled, ref int called, ref int pinned)
    {
        GameObject go = t.gameObject;
        if (scan.Excluded(t) || !go.activeSelf) return;
        string why = scan.WhyNotRoot(t);
        if (why == null)
        {
            result.Add(go);
            return;
        }
        if (why == "synced") synced++;
        else if (why == "toggled") toggled++;
        else if (why == "called") called++;
        else pinned++;
        foreach (Transform c in t) CollectRoots(c, scan, result, ref synced, ref toggled, ref called, ref pinned);
    }

    private static void SetArray(SerializedObject so, string field, UnityEngine.Object[] values)
    {
        SerializedProperty prop = so.FindProperty(field);
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    /// <summary>シーンを一度だけ調べて、選び方の材料をまとめる</summary>
    private class GuardScan
    {
        private readonly Scene _scene;
        private readonly Transform _systemRoot;
        private readonly List<Transform> _gateContent;
        private readonly List<Transform> _keep;
        private readonly HashSet<GameObject> _toggled = new HashSet<GameObject>();
        // ほかの Udon から参照される Udon（参照される側の Transform → 参照する側の Transform の一覧）
        private readonly Dictionary<Transform, List<Transform>> _calledBy = new Dictionary<Transform, List<Transform>>();
        // ワールド全体に効く物が付いている Transform
        private readonly List<Transform> _pinned = new List<Transform>();

        public GuardScan(Scene scene, Transform systemRoot, List<GameObject> gateContent, List<GameObject> keep, SupporterContentGuard guard)
        {
            _scene = scene;
            _systemRoot = systemRoot;
            _gateContent = gateContent.Select(g => g.transform).ToList();
            _keep = keep.Select(g => g.transform).ToList();
            UdonBehaviour guardUb = UdonSharpEditorUtility.GetBackingUdonBehaviour(guard);
            CollectReferences(guardUb);
            foreach (Transform t in SceneRoots().SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
                if (Pinned(t)) _pinned.Add(t);
            CollectAnimators();
            CollectTimelines();
            CollectUnityEvents();
        }

        /// <summary>調べ始める所: シーン直下（ゲート一式と通知の板を除く）と、ゲート一式の下にある Gate の Content Roots</summary>
        public IEnumerable<Transform> Starts()
        {
            foreach (GameObject r in _scene.GetRootGameObjects())
            {
                if (r.transform == _systemRoot) continue;
                yield return r.transform;
            }
            foreach (Transform c in _gateContent)
                if (c.IsChildOf(_systemRoot)) yield return c;
        }

        /// <summary>対象にしない物（ゲート一式・通知の板・EditorOnly・keep）。Gate の Content Roots の下は、ゲート一式の下でも対象にする</summary>
        public bool Excluded(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p.CompareTag("EditorOnly")) return true;
                if (_keep.Contains(p)) return true;
                if (p.GetComponent<NoticeHub>() != null) return true;
            }
            if (t.IsChildOf(_systemRoot)) return !_gateContent.Any(c => t.IsChildOf(c));
            return false;
        }

        public IEnumerable<T> All<T>() where T : Component
        {
            foreach (Transform s in Starts())
                foreach (T c in s.GetComponentsInChildren<T>(true))
                    if (!Excluded(c.transform)) yield return c;
        }

        public bool UnderAny(Transform t, HashSet<Transform> set)
        {
            for (Transform p = t; p != null; p = p.parent) if (set.Contains(p)) return true;
            return false;
        }

        /// <summary>根にできない理由（できるなら null）</summary>
        public string WhyNotRoot(Transform t)
        {
            if (_pinned.Any(p => p.IsChildOf(t))) return "pinned";
            if (ContainsSynced(t)) return "synced";
            if (_toggled.Contains(t.gameObject)) return "toggled";
            if (CalledFromOutside(t)) return "called";
            return null;
        }

        // ワールド全体に効く物（照明・カメラ・ポストエフェクト・Light Volumes・Bakery のライトマップの保存役など）
        private static bool Pinned(Transform t)
        {
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c is VRCSceneDescriptor || c is Camera || c is Light) return true;
                string n = c.GetType().Name;
                if (n == "PipelineManager" || n.Contains("PostProcess") || n.Contains("Volume") || n.Contains("Lightmap") || n.StartsWith("ftLightmaps")) return true;
            }
            return false;
        }

        private static bool ContainsSynced(Transform t)
        {
            if (t.GetComponentInChildren<VRCObjectSync>(true) != null) return true;
            if (t.GetComponentInChildren<VRCPickup>(true) != null) return true;
            if (t.GetComponentInChildren<VRCStation>(true) != null) return true;
            foreach (UdonBehaviour ub in t.GetComponentsInChildren<UdonBehaviour>(true))
                if (IsNetworked(ub)) return true;
            return false;
        }

        // U# のスクリプトは、スクリプトに書いた同期の設定を読む（シーンの SyncMethod は、足した直後などに古いことがある）。
        // None だけが、ネットワークに関わらない。NoVariableSync は同期する変数が無くても、ネットワークのイベントを受けられる
        private static bool IsNetworked(UdonBehaviour ub)
        {
            if (ub.programSource is UdonSharp.UdonSharpProgramAsset usp && usp.sourceCsScript != null)
            {
                Type cls = usp.sourceCsScript.GetClass();
                UdonSharp.UdonBehaviourSyncModeAttribute attr = cls != null
                    ? (UdonSharp.UdonBehaviourSyncModeAttribute)Attribute.GetCustomAttribute(cls, typeof(UdonSharp.UdonBehaviourSyncModeAttribute))
                    : null;
                if (attr != null && attr.behaviourSyncMode != UdonSharp.BehaviourSyncMode.Any) return attr.behaviourSyncMode != UdonSharp.BehaviourSyncMode.None;
            }
            return ub.SyncMethod != VRC.SDKBase.Networking.SyncType.None;
        }

        private bool CalledFromOutside(Transform t)
        {
            foreach (KeyValuePair<Transform, List<Transform>> kv in _calledBy)
            {
                if (!kv.Key.IsChildOf(t)) continue;
                foreach (Transform caller in kv.Value) if (!caller.IsChildOf(t)) return true;
            }
            return false;
        }

        private IEnumerable<GameObject> SceneRoots() { return _scene.GetRootGameObjects(); }

        // Udon の変数から参照される GameObject（切り替えられうる）と、Udon（呼ばれうる）
        private void CollectReferences(UdonBehaviour guardUb)
        {
            foreach (UdonBehaviour ub in SceneRoots().SelectMany(r => r.GetComponentsInChildren<UdonBehaviour>(true)))
            {
                if (ub == null || ub == guardUb || ub.publicVariables == null) continue;
                // ゲート自身の参照（Content Roots）は数えない（選び直すたびに、前に選んだ根を「切り替えられる物」と誤る）
                if (ub.programSource != null && ub.programSource.name == "SupporterGate") continue;
                foreach (string sym in ub.publicVariables.VariableSymbols)
                {
                    if (!ub.publicVariables.TryGetVariableValue(sym, out object v) || v == null) continue;
                    foreach (UnityEngine.Object o in Flatten(v))
                    {
                        if (o is GameObject g) _toggled.Add(g);
                        else if (o is UdonBehaviour target && target != ub)
                        {
                            if (!_calledBy.TryGetValue(target.transform, out List<Transform> list)) _calledBy[target.transform] = list = new List<Transform>();
                            list.Add(ub.transform);
                        }
                    }
                }
            }
        }

        private static IEnumerable<UnityEngine.Object> Flatten(object v)
        {
            if (v is UnityEngine.Object o) { yield return o; yield break; }
            if (v is Array arr)
                foreach (object e in arr)
                    if (e is UnityEngine.Object eo && eo != null) yield return eo;
        }

        // Animator のクリップで m_IsActive を動かされる物
        private void CollectAnimators()
        {
            foreach (Animator an in SceneRoots().SelectMany(r => r.GetComponentsInChildren<Animator>(true)))
            {
                if (an.runtimeAnimatorController == null) continue;
                foreach (AnimationClip clip in an.runtimeAnimatorController.animationClips.Distinct())
                    AddActiveCurveTargets(clip, an.transform);
            }
        }

        // Timeline の Activation トラックと、アニメーションのトラックの m_IsActive
        private void CollectTimelines()
        {
            foreach (PlayableDirector pd in SceneRoots().SelectMany(r => r.GetComponentsInChildren<PlayableDirector>(true)))
            {
                TimelineAsset tl = pd.playableAsset as TimelineAsset;
                if (tl == null) continue;
                foreach (TrackAsset track in tl.GetOutputTracks())
                {
                    UnityEngine.Object binding = pd.GetGenericBinding(track);
                    if (track is ActivationTrack)
                    {
                        if (binding is GameObject g) _toggled.Add(g);
                    }
                    else if (track is AnimationTrack at && binding is Animator an)
                    {
                        foreach (TimelineClip c in track.GetClips())
                            if (c.animationClip != null) AddActiveCurveTargets(c.animationClip, an.transform);
                        if (at.infiniteClip != null) AddActiveCurveTargets(at.infiniteClip, an.transform);
                    }
                }
            }
        }

        // UI などの UnityEvent の SetActive
        private void CollectUnityEvents()
        {
            foreach (MonoBehaviour mb in SceneRoots().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)))
            {
                if (mb == null || mb is UdonBehaviour || mb is UdonSharp.UdonSharpBehaviour) continue;
                SerializedObject so = new SerializedObject(mb);
                SerializedProperty it = so.GetIterator();
                while (it.Next(true))
                {
                    if (!it.propertyPath.EndsWith("m_MethodName") || it.propertyType != SerializedPropertyType.String || it.stringValue != "SetActive") continue;
                    SerializedProperty target = so.FindProperty(it.propertyPath.Replace("m_MethodName", "m_Target"));
                    if (target != null && target.objectReferenceValue is GameObject g) _toggled.Add(g);
                }
            }
        }

        private void AddActiveCurveTargets(AnimationClip clip, Transform animatorRoot)
        {
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.propertyName != "m_IsActive") continue;
                Transform t = string.IsNullOrEmpty(b.path) ? animatorRoot : animatorRoot.Find(b.path);
                if (t != null) _toggled.Add(t.gameObject);
            }
        }
    }
}
#endif
