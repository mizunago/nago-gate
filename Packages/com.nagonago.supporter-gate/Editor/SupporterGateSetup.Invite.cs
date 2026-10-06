// SupporterGateSetup.Invite.cs
// 案内のパネルに、Discord の招待 URL のコピー欄と QR コードを足す（SupporterInviteLink）。
// VRChat の中からはリンクを開けず、URL を見て打ち込むのも大変なので、PC ではコピー、スマホでは QR で開けるようにする。
//
//   Tools > SupporterGate > Add Discord Copy Panel (existing scene)   今のシーンの案内のパネルに足し、QR も作る
//   Tools > SupporterGate > Update Discord QR                         招待 URL を変えたとき、QR を作り直す
//
// QR に入れる URL は、Registry の Data Url のリストを読み、links.discord から取る（リストの links は、鍵つきでも読める）。
// 読めないときは、SupporterInviteLink の Qr Url に入っている URL を使う。

#if UNITY_EDITOR
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using NagoSupporterGate.EditorTools;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VRC.Udon;

public static partial class SupporterGateSetup
{
    private const string InviteRootName = "DiscordInvite";
    private const string QrFolder = "Assets/NagoSupporterGate";
    private const string QrPath = QrFolder + "/DiscordInviteQR.png";
    private const int QrPixelsPerModule = 10;
    private const int QrQuietModules = 4;

    [MenuItem("Tools/SupporterGate/Add Discord Copy Panel (existing scene)", false, 22)]
    public static void AddDiscordCopyPanels()
    {
        string msg = AddDiscordCopyPanelsSilent();
        if (!Application.isBatchMode) EditorUtility.DisplayDialog("SupporterGate", msg, "OK");
    }

    /// <summary>
    /// Add Discord Copy Panel の本体。ダイアログを出さずに、やったことの説明を返す（エディタを外から動かす道具や、検査から呼ぶ用）
    /// </summary>
    public static string AddDiscordCopyPanelsSilent()
    {
        int added = 0, relaid = 0;
        foreach (SupporterCreditsBoard credits in Object.FindObjectsOfType<SupporterCreditsBoard>(true))
        {
            TextMeshProUGUI infoText = new SerializedObject(credits).FindProperty("infoText").objectReferenceValue as TextMeshProUGUI;
            if (infoText == null) continue;
            Transform existing = infoText.transform.parent != null ? infoText.transform.parent.Find(InviteRootName) : null;
            if (existing != null)
            {
                // 前の版で足した欄: 板の大きさに合わせて置き直す（2 回目以降は何も変わらない）
                MakeRoomForButton(infoText, (RectTransform)existing.parent);
                LayoutInvite(existing);
                relaid++;
                continue;
            }
            AddInviteLink(credits, infoText);
            WireNotices(credits.transform.parent != null ? credits.transform.parent.gameObject : credits.gameObject);
            added++;
        }
        string qr = UpdateDiscordQr(null);
        string msg = (added > 0 ? "案内のパネルに、Discord のコピー欄を " + added + " 個足しました。" : "足す場所はありませんでした（案内のパネルが無いか、もう足してあります）。")
            + (relaid > 0 ? "\n既にあった欄 " + relaid + " 個を、板の大きさに合わせて置き直しました。" : "") + "\n\n" + qr;
        Debug.Log("[SupporterGate] " + msg.Replace("\n", " "));
        return msg;
    }

    [MenuItem("Tools/SupporterGate/Update Discord QR", false, 23)]
    public static void UpdateDiscordQrMenu()
    {
        string msg = UpdateDiscordQr(null);
        Debug.Log("[SupporterGate] " + msg.Replace("\n", " "));
        if (!Application.isBatchMode) EditorUtility.DisplayDialog("SupporterGate", msg, "OK");
    }

    /// <summary>
    /// QR の画像を作り、シーンの SupporterInviteLink に入れる。url が null なら、リストから読む。
    /// 戻り値は、やったことの説明
    /// </summary>
    public static string UpdateDiscordQr(string url)
    {
        SupporterInviteLink[] links = Object.FindObjectsOfType<SupporterInviteLink>(true);
        if (links.Length == 0) return "シーンに Discord のコピー欄がありません。";
        string source = "指定";
        if (string.IsNullOrEmpty(url))
        {
            url = FetchInviteFromList(out source);
        }
        if (string.IsNullOrEmpty(url))
        {
            // リストが読めないときは、前に入れた URL で作り直す
            url = new SerializedObject(links[0]).FindProperty("qrUrl").stringValue;
            source = "Qr Url の欄";
        }
        if (string.IsNullOrEmpty(url))
        {
            return "QR は作っていません。リストに Discord の招待 URL がありません（Bot の設定の links.discord）。\n" +
                "リストを使わないときは、DiscordInvite の SupporterInviteLink の Qr Url に URL を入れてから、Tools > SupporterGate > Update Discord QR を実行してください。";
        }

        int version;
        bool[,] modules = QrEncoder.Encode(url, out version);
        Texture2D texture = WriteQrTexture(modules);
        foreach (SupporterInviteLink link in links)
        {
            SetString(link, "qrUrl", url);
            Transform qr = link.transform.Find("CopyPanel/QR");
            RawImage image = qr != null ? qr.GetComponent<RawImage>() : null;
            if (image != null)
            {
                Undo.RecordObject(image, "Set Discord QR");
                image.texture = texture;
                EditorUtility.SetDirty(image);
            }
            EditorSceneManager.MarkSceneDirty(link.gameObject.scene);
        }
        return "QR を作りました（" + source + " の URL: " + url + "、型番 " + version + "）。画像: " + QrPath +
            "\nワールドの URL（リストの links.discord）がこれと違うと、QR は出ません。招待 URL を変えたら、Tools > SupporterGate > Update Discord QR で作り直して、ワールドを上げ直してください。";
    }

    /// <summary>Registry の Data Url のリストを読み、links.discord を返す。読めなければ null</summary>
    private static string FetchInviteFromList(out string source)
    {
        source = "リスト";
        SupporterRegistry registry = Object.FindObjectOfType<SupporterRegistry>(true);
        if (registry == null) return null;
        string dataUrl = registry.dataUrl != null ? registry.dataUrl.Get() : "";
        if (string.IsNullOrEmpty(dataUrl)) return null;
        try
        {
            using (WebClient client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                string json = client.DownloadString(dataUrl);
                Match m = Regex.Match(json, "\"links\"\\s*:\\s*\\{[^}]*\"discord\"\\s*:\\s*\"([^\"]+)\"");
                if (!m.Success) return null;
                source = "リスト（" + dataUrl + "）";
                return m.Groups[1].Value.Replace("\\/", "/");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[SupporterGate] リストを読めませんでした（" + dataUrl + "）: " + ex.Message);
            return null;
        }
    }

    /// <summary>QR の模様を PNG にして保存し、読み込んだテクスチャを返す（くっきり見えるよう、補間なし・圧縮なし）</summary>
    private static Texture2D WriteQrTexture(bool[,] modules)
    {
        int n = modules.GetLength(0);
        int px = (n + QrQuietModules * 2) * QrPixelsPerModule;
        Texture2D tex = new Texture2D(px, px, TextureFormat.RGB24, false);
        Color32[] pixels = new Color32[px * px];
        Color32 white = new Color32(255, 255, 255, 255);
        Color32 black = new Color32(0, 0, 0, 255);
        for (int py = 0; py < px; py++)
        {
            int my = (px - 1 - py) / QrPixelsPerModule - QrQuietModules;   // テクスチャは下から上へ並ぶ
            for (int pxx = 0; pxx < px; pxx++)
            {
                int mx = pxx / QrPixelsPerModule - QrQuietModules;
                bool dark = mx >= 0 && my >= 0 && mx < n && my < n && modules[my, mx];
                pixels[py * px + pxx] = dark ? black : white;
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        if (!AssetDatabase.IsValidFolder(QrFolder)) AssetDatabase.CreateFolder("Assets", "NagoSupporterGate");
        File.WriteAllBytes(QrPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(QrPath, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(QrPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(QrPath);
    }

    /// <summary>
    /// 案内のパネル（infoText のキャンバス）に、「URL をコピー・QR」のボタンと、押したときに出る欄を足す。
    /// 案内の文は、ボタンの分だけ上に詰める
    /// </summary>
    public static SupporterInviteLink AddInviteLink(SupporterCreditsBoard credits, TextMeshProUGUI infoText)
    {
        GameObject canvas = infoText.transform.parent.gameObject;
        MakeRoomForButton(infoText, (RectTransform)canvas.transform);

        GameObject root = ChildRect(canvas, InviteRootName);
        Undo.RegisterCreatedObjectUndo(root, "Add Discord Copy Panel");
        SupporterInviteLink link = root.AddUdonSharpComponent<SupporterInviteLink>();
        SetRef(link, "registry", new SerializedObject(credits).FindProperty("registry").objectReferenceValue);

        // 開くボタン（案内の文の下）
        GameObject open = CreateButton(root, "OpenButton", "URL をコピー・QR", new Vector2(250f, 16f), new Vector2(400f, 66f), link, nameof(SupporterInviteLink._Toggle));
        TextMeshProUGUI openLabel = open.GetComponentInChildren<TextMeshProUGUI>();
        FitLine(openLabel, 16f);

        // 開いたときの欄（案内のパネルの上に重ねる）
        GameObject panel = ChildRect(root, "CopyPanel");
        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.06f, 0.08f, 1f);   // 下の案内の文が透けないように、不透明にする

        TMP_InputField field = CreateUrlField(panel, link, new Vector2(20f, 490f), new Vector2(860f, 90f));
        TextMeshProUGUI help = CreateText(panel, "Help", "", 28, new Vector2(20f, 100f), new Vector2(530f, 370f), TextAlignmentOptions.TopLeft);
        FitText(help, 14f);
        GameObject qr = new GameObject("QR", typeof(RectTransform), typeof(RawImage));
        qr.transform.SetParent(panel.transform, false);
        RectTransform qrt = qr.GetComponent<RectTransform>();
        qrt.anchorMin = Vector2.zero;
        qrt.anchorMax = Vector2.zero;
        qrt.pivot = Vector2.zero;
        qrt.anchoredPosition = new Vector2(570f, 110f);
        qrt.sizeDelta = new Vector2(310f, 310f);
        qr.GetComponent<RawImage>().raycastTarget = false;
        GameObject close = CreateButton(panel, "CloseButton", "閉じる", new Vector2(570f, 18f), new Vector2(310f, 70f), link, nameof(SupporterInviteLink._Close));
        TextMeshProUGUI closeLabel = close.GetComponentInChildren<TextMeshProUGUI>();
        FitLine(closeLabel, 16f);

        SetRef(link, "openButton", open);
        SetRef(link, "openLabel", openLabel);
        SetRef(link, "copyPanel", panel);
        SetRef(link, "urlField", field);
        SetRef(link, "helpText", help);
        SetRef(link, "closeLabel", closeLabel);
        SetRef(link, "qrRoot", qr);
        LayoutInvite(root.transform);
        panel.SetActive(false);
        open.SetActive(false);   // リストに URL が入ってから出す
        EditorSceneManager.MarkSceneDirty(canvas.scene);
        return link;
    }

    // 部品の置き場所は、900×600 の板を基準にした比率で決める（板の大きさが違っても、はみ出さないように）
    private const float BaseW = 900f, BaseH = 600f;
    // ボタンの上の端（板の下からの割合）。案内の文は、これより下に掛からないようにする
    private const float ButtonTop = 96f / BaseH;

    private static void PlaceRatio(Transform t, float x, float y, float w, float h)
    {
        RectTransform rt = t as RectTransform;
        if (rt == null) return;
        Undo.RecordObject(rt, "Layout Discord Copy Panel");
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchorMin = new Vector2(x / BaseW, y / BaseH);
        rt.anchorMax = new Vector2((x + w) / BaseW, (y + h) / BaseH);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// コピー欄の部品を、板（DiscordInvite の親）の大きさに対する比率で置き直す。QR は、今の板の形で正方形になる大きさにする。
    /// 何度呼んでもよい。板の大きさを変えたら、Add Discord Copy Panel をもう一度実行すれば合わせ直せる
    /// </summary>
    public static void LayoutInvite(Transform root)
    {
        Transform open = root.Find("OpenButton");
        Transform panel = root.Find("CopyPanel");
        if (open != null) PlaceRatio(open, 250f, 16f, 400f, 66f);
        if (panel == null) return;
        PlaceRatio(panel.Find("UrlField"), 20f, 490f, 860f, 90f);
        PlaceRatio(panel.Find("Help"), 20f, 100f, 530f, 370f);
        PlaceRatio(panel.Find("CloseButton"), 570f, 18f, 310f, 70f);
        RectTransform qr = panel.Find("QR") as RectTransform;
        RectTransform area = root as RectTransform;
        if (qr != null && area != null)
        {
            // QR の枠（基準で 310×310）の中に、正方形で収める
            Vector2 size = area.rect.size;
            if (size.x <= 0f || size.y <= 0f) size = new Vector2(BaseW, BaseH);
            float side = Mathf.Min(310f / BaseW * size.x, 310f / BaseH * size.y);
            Undo.RecordObject(qr, "Layout Discord QR");
            Vector2 center = new Vector2((570f + 155f) / BaseW, (110f + 155f) / BaseH);
            qr.anchorMin = center;
            qr.anchorMax = center;
            qr.pivot = new Vector2(0.5f, 0.5f);
            qr.anchoredPosition = Vector2.zero;
            qr.sizeDelta = new Vector2(side, side);
        }
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
    }

    /// <summary>
    /// 案内の文が、ボタンに掛からないようにする。文の欄の下の辺だけを上げ、上の辺は動かさない（額縁などに合わせた位置を崩さない）
    /// </summary>
    private static void MakeRoomForButton(TextMeshProUGUI infoText, RectTransform board)
    {
        RectTransform t = infoText.rectTransform;
        RectTransform parent = t.parent as RectTransform;
        if (parent == null || board == null) return;
        Vector3[] bc = new Vector3[4];
        board.GetWorldCorners(bc);   // 0: 左下、1: 左上
        float limit = parent.InverseTransformPoint(Vector3.Lerp(bc[0], bc[1], ButtonTop)).y;
        Vector3[] tc = new Vector3[4];
        t.GetWorldCorners(tc);
        float bottom = parent.InverseTransformPoint(tc[0]).y;
        float delta = limit - bottom;
        if (delta <= 0.01f) return;
        Undo.RecordObject(t, "Make room for the Discord button");
        t.offsetMin = new Vector2(t.offsetMin.x, t.offsetMin.y + delta);
    }

    /// <summary>URL を入れておく入力欄（TextMeshPro）。編集が終わったら、URL に戻す</summary>
    private static TMP_InputField CreateUrlField(GameObject parent, SupporterInviteLink link, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject("UrlField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.95f, 0.95f, 0.97f, 1f);

        GameObject area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);
        RectTransform art = area.GetComponent<RectTransform>();
        art.anchorMin = Vector2.zero;
        art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(18f, 8f);
        art.offsetMax = new Vector2(-18f, -8f);

        TextMeshProUGUI text = NewFieldText(area, "Text", new Color(0.08f, 0.08f, 0.1f, 1f));
        TextMeshProUGUI placeholder = NewFieldText(area, "Placeholder", new Color(0.5f, 0.5f, 0.55f, 1f));
        placeholder.text = "discord.gg/...";

        TMP_InputField field = go.GetComponent<TMP_InputField>();
        field.textViewport = art;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.fontAsset = text.font;
        field.pointSize = 40f;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.richText = false;
        field.characterLimit = 0;
        UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(link);
        if (backing != null)
        {
            UnityEventTools.AddStringPersistentListener(field.onEndEdit, new UnityAction<string>(backing.SendCustomEvent), nameof(SupporterInviteLink._ResetField));
        }
        return field;
    }

    private static TextMeshProUGUI NewFieldText(GameObject parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 40f;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.enableWordWrapping = false;
        tmp.richText = false;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void SetString(UdonSharp.UdonSharpBehaviour proxy, string field, string value)
    {
        SerializedObject so = new SerializedObject(proxy);
        SerializedProperty prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogError("[SupporterGate] フィールド " + field + " が " + proxy.GetType().Name + " にありません");
            return;
        }
        prop.stringValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        UdonSharpEditorUtility.CopyProxyToUdon(proxy);
    }
}
#endif
