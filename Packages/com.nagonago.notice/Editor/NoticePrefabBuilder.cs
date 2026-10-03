// NoticePrefabBuilder.cs
// 通知のプレハブ（Runtime/Prefabs/NoticeHub.prefab）とマテリアルを、コードから組み立てる。
// パッケージの保守用。利用する側は、出来上がったプレハブを置くだけでよい（NoticeMenu を参照）。
//
// 構成:
//   NoticeHub（常に有効。NoticeHub・AudioSource・VRCSpatialAudioSource）
//    └ NoticeCanvas（World Space の Canvas。通知が出ている間だけ有効。UI レイヤー）
//       └ Area（幅 900px。下端を基準に上へ積む）
//          └ Row00〜（背景・色帯・文。文に合わせて自動で伸び縮み）
// コライダー・GraphicRaycaster・VRCUiShape は付けない（手のレーザーや他の UI を遮らないため）。

#if UNITY_EDITOR
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace NagoNotice.EditorTools
{
    public static class NoticePrefabBuilder
    {
        public const string PackageRoot = "Packages/com.nagonago.notice";
        public const string PrefabPath = PackageRoot + "/Runtime/Prefabs/NoticeHub.prefab";
        public const string TextMaterialPath = PackageRoot + "/Runtime/Materials/NoticeText Overlay.mat";
        public const string PanelMaterialPath = PackageRoot + "/Runtime/Materials/NoticePanel Overlay.mat";

        private const string OverlayTextShader = "TextMeshPro/Distance Field Overlay";
        private const string OverlayPanelShader = "NagoNotice/UI Overlay";
        // TMP Essential Resources の LiberationSans SDF（どのプロジェクトでも同じ GUID）
        private const string DefaultFontGuid = "8f586378b4e144a9851e7b34d9b748ee";

        private const int RowCount = 4;
        private const int UiLayer = 5;
        private const float AreaWidth = 900f;
        private const float FontSize = 46f;

        /// <summary>
        /// プレハブとマテリアルを作り直す（アセットの GUID は保つ）。
        /// 作り直すとプレハブの中のオブジェクトの ID が変わり、利用側のシーンで付けた上書きが外れることがある。
        /// 公開後は、むやみに作り直さず、プレハブを直接編集する。
        /// </summary>
        public static void Build()
        {
            TMP_FontAsset font = LoadDefaultFont();
            if (font == null)
            {
                Debug.LogError("[NagoNotice] TMP Essential Resources が見つかりません（Window > TextMeshPro > Import TMP Essential Resources）");
                return;
            }
            Material textMat = EnsureTextMaterial(font, TextMaterialPath);
            Material panelMat = EnsurePanelMaterial();
            if (textMat == null || panelMat == null) return;

            GameObject root = BuildHierarchy(font, textMat, panelMat);
            string dir = System.IO.Path.GetDirectoryName(PrefabPath);
            if (!AssetDatabase.IsValidFolder(dir)) System.IO.Directory.CreateDirectory(dir);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[NagoNotice] prefab built: " + PrefabPath);
        }

        public static TMP_FontAsset LoadDefaultFont()
        {
            string path = AssetDatabase.GUIDToAssetPath(DefaultFontGuid);
            TMP_FontAsset font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null) font = TMP_Settings.defaultFontAsset;
            return font;
        }

        /// <summary>フォントのマテリアルを元に、最前面描画（Overlay）のマテリアルを作る。既にあれば中身だけ更新する</summary>
        public static Material EnsureTextMaterial(TMP_FontAsset font, string path)
        {
            Shader shader = Shader.Find(OverlayTextShader);
            if (shader == null)
            {
                Debug.LogError("[NagoNotice] シェーダーが見つかりません: " + OverlayTextShader);
                return null;
            }
            Material source = new Material(font.material);
            source.shader = shader;

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                EnsureFolder(System.IO.Path.GetDirectoryName(path));
                source.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(source, path);
                mat = source;
            }
            else
            {
                mat.shader = shader;
                mat.CopyPropertiesFromMaterial(source);
                EditorUtility.SetDirty(mat);
                Object.DestroyImmediate(source);
            }
            return mat;
        }

        private static Material EnsurePanelMaterial()
        {
            Shader shader = Shader.Find(OverlayPanelShader);
            if (shader == null)
            {
                Debug.LogError("[NagoNotice] シェーダーが見つかりません: " + OverlayPanelShader);
                return null;
            }
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(PanelMaterialPath);
            if (mat == null)
            {
                EnsureFolder(System.IO.Path.GetDirectoryName(PanelMaterialPath));
                mat = new Material(shader);
                mat.name = System.IO.Path.GetFileNameWithoutExtension(PanelMaterialPath);
                AssetDatabase.CreateAsset(mat, PanelMaterialPath);
            }
            else
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        private static void EnsureFolder(string dir)
        {
            dir = dir.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }
        }

        private static GameObject BuildHierarchy(TMP_FontAsset font, Material textMat, Material panelMat)
        {
            GameObject root = new GameObject("NoticeHub");

            // ---- 音（2D。VRChat が実行時に足す既定の空間化とゲインを避けるため、VRCSpatialAudioSource を明示する）----
            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;
            audio.dopplerLevel = 0f;
            audio.volume = 1f;
            VRCSpatialAudioSource spatial = root.AddComponent<VRCSpatialAudioSource>();
            spatial.Gain = 0f;
            spatial.EnableSpatialization = false;
            spatial.UseAudioSourceVolumeCurve = true;

            // ---- Canvas ----
            GameObject canvasGo = new GameObject("NoticeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.layer = UiLayer;
            canvasGo.transform.SetParent(root.transform, false);
            canvasGo.transform.localScale = Vector3.one * 0.001f;
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30000;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                                              | AdditionalCanvasShaderChannels.Normal
                                              | AdditionalCanvasShaderChannels.Tangent;
            RectTransform canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.pivot = new Vector2(0.5f, 0f);
            canvasRt.sizeDelta = new Vector2(AreaWidth, 100f);

            // ---- Area（下端を基準に上へ積む）----
            GameObject areaGo = new GameObject("Area", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            areaGo.layer = UiLayer;
            areaGo.transform.SetParent(canvasGo.transform, false);
            RectTransform areaRt = areaGo.GetComponent<RectTransform>();
            areaRt.anchorMin = new Vector2(0.5f, 0f);
            areaRt.anchorMax = new Vector2(0.5f, 0f);
            areaRt.pivot = new Vector2(0.5f, 0f);
            areaRt.anchoredPosition = Vector2.zero;
            areaRt.sizeDelta = new Vector2(AreaWidth, 0f);
            VerticalLayoutGroup v = areaGo.GetComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.LowerCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = false;
            v.childForceExpandHeight = false;
            v.spacing = 12f;
            ContentSizeFitter fitter = areaGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Sprite panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");

            GameObject[] rowObjects = new GameObject[RowCount];
            Transform[] rowTransforms = new Transform[RowCount];
            CanvasGroup[] rowGroups = new CanvasGroup[RowCount];
            TextMeshProUGUI[] rowTexts = new TextMeshProUGUI[RowCount];
            Image[] rowAccents = new Image[RowCount];

            for (int i = 0; i < RowCount; i++)
            {
                GameObject row = new GameObject($"Row{i:00}", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(HorizontalLayoutGroup));
                row.layer = UiLayer;
                row.transform.SetParent(areaGo.transform, false);

                CanvasGroup group = row.GetComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;

                Image bg = row.GetComponent<Image>();
                bg.sprite = panelSprite;
                bg.type = Image.Type.Sliced;
                bg.color = new Color(0.07f, 0.08f, 0.10f, 0.88f);
                bg.material = panelMat;
                bg.raycastTarget = false;

                HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
                h.padding = new RectOffset(20, 30, 16, 18);
                h.spacing = 18f;
                h.childAlignment = TextAnchor.MiddleLeft;
                h.childControlWidth = true;
                h.childControlHeight = true;
                h.childForceExpandWidth = false;
                h.childForceExpandHeight = true;

                GameObject accentGo = new GameObject("Accent", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                accentGo.layer = UiLayer;
                accentGo.transform.SetParent(row.transform, false);
                Image accent = accentGo.GetComponent<Image>();
                accent.color = new Color(0.36f, 0.67f, 1f, 1f);
                accent.material = panelMat;
                accent.raycastTarget = false;
                LayoutElement accentLe = accentGo.GetComponent<LayoutElement>();
                accentLe.minWidth = 8f;
                accentLe.preferredWidth = 8f;
                accentLe.flexibleWidth = 0f;

                GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                textGo.layer = UiLayer;
                textGo.transform.SetParent(row.transform, false);
                TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
                text.font = font;
                text.fontSharedMaterial = textMat;
                text.fontSize = FontSize;
                text.color = Color.white;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.enableWordWrapping = true;
                text.overflowMode = TextOverflowModes.Overflow;
                text.richText = true;
                text.raycastTarget = false;
                text.lineSpacing = 6f;
                text.text = "Notice";

                row.SetActive(false);
                rowObjects[i] = row;
                rowTransforms[i] = row.transform;
                rowGroups[i] = group;
                rowTexts[i] = text;
                rowAccents[i] = accent;
            }

            canvasGo.SetActive(false);

            // ---- Hub ----
            NoticeHub hub = root.AddUdonSharpComponent<NoticeHub>();
            SerializedObject so = new SerializedObject(hub);
            so.FindProperty("canvasRoot").objectReferenceValue = canvasGo;
            so.FindProperty("canvasTransform").objectReferenceValue = canvasGo.transform;
            SetArray(so, "rowObjects", rowObjects);
            SetArray(so, "rowTransforms", rowTransforms);
            SetArray(so, "rowGroups", rowGroups);
            SetArray(so, "rowTexts", rowTexts);
            SetArray(so, "rowAccents", rowAccents);
            so.FindProperty("audioSource").objectReferenceValue = audio;

            string[] clipNames = { "notice-info.wav", "notice-success.wav", "notice-warning.wav", "notice-error.wav" };
            SerializedProperty clips = so.FindProperty("levelClips");
            clips.arraySize = clipNames.Length;
            for (int i = 0; i < clipNames.Length; i++)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(PackageRoot + "/Runtime/Audio/" + clipNames[i]);
                if (clip == null) Debug.LogWarning("[NagoNotice] 音が見つかりません: " + clipNames[i]);
                clips.GetArrayElementAtIndex(i).objectReferenceValue = clip;
            }
            Color[] colors =
            {
                new Color(0.36f, 0.67f, 1.00f, 1f),   // 情報
                new Color(0.45f, 0.85f, 0.50f, 1f),   // 成功
                new Color(1.00f, 0.75f, 0.30f, 1f),   // 警告
                new Color(1.00f, 0.42f, 0.42f, 1f),   // エラー
            };
            SerializedProperty colorsProp = so.FindProperty("levelColors");
            colorsProp.arraySize = colors.Length;
            for (int i = 0; i < colors.Length; i++) colorsProp.GetArrayElementAtIndex(i).colorValue = colors[i];

            so.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(hub);
            return root;
        }

        private static void SetArray(SerializedObject so, string name, Object[] values)
        {
            SerializedProperty p = so.FindProperty(name);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
#endif
