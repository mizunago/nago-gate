// NoticeMenu.cs
// 利用する側のメニューと、他のエディタツールから呼ぶ入口。
//   Tools > Nago Notice > Add Notice Hub To Scene          シーンに通知（NoticeHub）を 1 つ置く
//   Tools > Nago Notice > Add Join-Leave Notice To Scene   入退室の通知（NoticeJoinLeave）を 1 つ置く
//   Tools > Nago Notice > Apply Selected Font Asset        Project で選んだ TMP フォントを通知の文字に使う

#if UNITY_EDITOR
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NagoNotice.EditorTools
{
    public static class NoticeMenu
    {
        private const string GeneratedFolder = "Assets/NagoNotice";

        [MenuItem("Tools/Nago Notice/Add Notice Hub To Scene", false, 1)]
        public static void AddToScene()
        {
            NoticeHub hub = EnsureInScene();
            if (hub != null) Selection.activeGameObject = hub.gameObject;
        }

        /// <summary>シーンに NoticeHub があればそれを返し、無ければプレハブから置いて返す</summary>
        public static NoticeHub EnsureInScene()
        {
            NoticeHub existing = Object.FindObjectOfType<NoticeHub>(true);
            if (existing != null) return existing;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NoticePrefabBuilder.PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[NagoNotice] プレハブが見つかりません: " + NoticePrefabBuilder.PrefabPath);
                return null;
            }
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(go, "Add Notice Hub");
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[NagoNotice] NoticeHub をシーンに置きました");
            return go.GetComponent<NoticeHub>();
        }

        [MenuItem("Tools/Nago Notice/Add Join-Leave Notice To Scene", false, 2)]
        public static void AddJoinLeaveToScene()
        {
            NoticeJoinLeave joinLeave = EnsureJoinLeaveInScene();
            if (joinLeave != null) Selection.activeGameObject = joinLeave.gameObject;
        }

        /// <summary>
        /// シーンに NoticeJoinLeave があればそれを返し、無ければ置いて返す（NoticeHub も無ければ一緒に置く）。
        /// 音は、パッケージの入室・退室の音を割り当てる。
        /// </summary>
        public static NoticeJoinLeave EnsureJoinLeaveInScene()
        {
            NoticeJoinLeave existing = Object.FindObjectOfType<NoticeJoinLeave>(true);
            if (existing != null) return existing;

            NoticeHub hub = EnsureInScene();
            if (hub == null) return null;

            GameObject go = new GameObject("NoticeJoinLeave");
            Undo.RegisterCreatedObjectUndo(go, "Add Join-Leave Notice");
            NoticeJoinLeave joinLeave = go.AddUdonSharpComponent<NoticeJoinLeave>();
            SerializedObject so = new SerializedObject(joinLeave);
            so.FindProperty("notice").objectReferenceValue = hub;
            so.FindProperty("joinClip").objectReferenceValue = LoadClip("notice-join.wav");
            so.FindProperty("leaveClip").objectReferenceValue = LoadClip("notice-leave.wav");
            so.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(joinLeave);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[NagoNotice] NoticeJoinLeave をシーンに置きました（既定は OFF。切り替えは _Toggle を呼ぶ）");
            return joinLeave;
        }

        private static AudioClip LoadClip(string fileName)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(NoticePrefabBuilder.PackageRoot + "/Runtime/Audio/" + fileName);
            if (clip == null) Debug.LogWarning("[NagoNotice] 音が見つかりません: " + fileName);
            return clip;
        }

        /// <summary>文言の表（NoticeTable）を Hub の tables に足す。既に入っていれば何もしない</summary>
        public static void AddTable(NoticeHub hub, NoticeTable table)
        {
            if (hub == null || table == null) return;
            SerializedObject so = new SerializedObject(hub);
            SerializedProperty p = so.FindProperty("tables");
            for (int i = 0; i < p.arraySize; i++)
            {
                if (p.GetArrayElementAtIndex(i).objectReferenceValue == table) return;
            }
            p.arraySize++;
            p.GetArrayElementAtIndex(p.arraySize - 1).objectReferenceValue = table;
            so.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(hub);
            EditorUtility.SetDirty(hub);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        }

        [MenuItem("Tools/Nago Notice/Apply Selected Font Asset", false, 20)]
        public static void ApplySelectedFont()
        {
            TMP_FontAsset font = Selection.activeObject as TMP_FontAsset;
            if (font == null)
            {
                EditorUtility.DisplayDialog("Nago Notice", "Project ウィンドウで TMP のフォントアセットを選んでから実行してください。", "OK");
                return;
            }
            NoticeHub hub = Object.FindObjectOfType<NoticeHub>(true);
            if (hub == null)
            {
                EditorUtility.DisplayDialog("Nago Notice", "シーンに NoticeHub がありません。先に Add Notice Hub To Scene を実行してください。", "OK");
                return;
            }
            ApplyFont(hub, font);
        }

        [MenuItem("Tools/Nago Notice/Apply Selected Font Asset", true)]
        private static bool ApplySelectedFontValidate()
        {
            return Selection.activeObject is TMP_FontAsset;
        }

        /// <summary>
        /// 通知の文字のフォントを差し替える。最前面描画のマテリアルを Assets/NagoNotice に作って割り当てる。
        /// （パッケージの中のマテリアルは書き換えない）
        /// </summary>
        public static void ApplyFont(NoticeHub hub, TMP_FontAsset font)
        {
            if (!AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.CreateFolder("Assets", "NagoNotice");
            string path = GeneratedFolder + "/" + font.name + " - NoticeOverlay.mat";
            Material mat = NoticePrefabBuilder.EnsureTextMaterial(font, path);
            if (mat == null) return;

            TextMeshProUGUI[] texts = hub.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI t in texts)
            {
                Undo.RecordObject(t, "Apply Notice Font");
                t.font = font;
                t.fontSharedMaterial = mat;
                EditorUtility.SetDirty(t);
            }
            AssetDatabase.SaveAssets();
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
            Debug.Log($"[NagoNotice] フォントを {font.name} にしました（{texts.Length} 行）。マテリアル: {path}");
        }
    }
}
#endif
