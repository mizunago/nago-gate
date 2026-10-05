// SupporterInviteLink.cs
// 案内のパネルの「URL をコピー・QR」ボタンで、Discord の招待 URL を入れた入力欄と QR コードを出す。
// VRChat の中からはリンクを開けないので、PC では欄からコピーしてもらい、スマホでは QR を読んでもらう。
// URL はリスト（links.discord）から読む。リストに URL が無ければ、ボタンごと隠す。
// QR の画像はエディタで作る（Tools > SupporterGate > Add Discord Copy Panel）。作ったときの URL を qrUrl に残し、
// リストの URL と違えば QR を隠す（古い招待を読ませない）。
// 文言は texts（NoticeTable）から今の言語で引く。texts が無ければ日本語の既定の文を使う。

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;
using NagoNotice;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SupporterInviteLink : UdonSharpBehaviour
{
    [SerializeField] private SupporterRegistry registry;
    [Tooltip("リストの links の、どの項目を出すか")]
    [SerializeField] private string linkKey = "discord";
    [Tooltip("案内のパネルに置く「URL をコピー・QR」のボタン。URL が無いときは隠す")]
    [SerializeField] private GameObject openButton;
    [SerializeField] private TextMeshProUGUI openLabel;
    [Tooltip("押したときに出す欄（入力欄・QR・説明・閉じるボタン）。最初は隠しておく")]
    [SerializeField] private GameObject copyPanel;
    [SerializeField] private TMP_InputField urlField;
    [SerializeField] private TextMeshProUGUI helpText;
    [SerializeField] private TextMeshProUGUI closeLabel;
    [Tooltip("QR の画像（RawImage）の GameObject")]
    [SerializeField] private GameObject qrRoot;
    [Tooltip("QR に入れた URL（エディタが書く）。リストの URL と違えば、QR を隠す")]
    [SerializeField] private string qrUrl = "";

    [Header("Texts (optional)")]
    [SerializeField] private NoticeTable texts;
    [SerializeField] private NoticeHub notice;
    [SerializeField] private bool debugLog;

    private string _url = "";

    void Start()
    {
        if (copyPanel != null) copyPanel.SetActive(false);
        if (openButton != null) openButton.SetActive(false);
        if (registry != null) registry._RegisterListener(this);
        if (notice != null) notice._RegisterListener(this);
        Refresh();
    }

    public void _OnRegistryUpdated()
    {
        _url = registry != null ? registry._GetLink(linkKey) : "";
        if (openButton != null) openButton.SetActive(_url.Length > 0);
        if (_url.Length == 0 && copyPanel != null) copyPanel.SetActive(false);
        Refresh();
        if (debugLog) Debug.Log("[SupporterInviteLink] url=" + _url + " qr=" + QrMatches());
    }

    public void _OnNoticeLanguageChanged() { Refresh(); }

    public override void OnLanguageChanged(string language)
    {
        if (notice == null) Refresh();
    }

    /// <summary>ボタン: 欄を出す・隠す</summary>
    public void _Toggle()
    {
        if (copyPanel == null) return;
        bool open = !copyPanel.activeSelf && _url.Length > 0;
        copyPanel.SetActive(open);
        Refresh();
        if (debugLog) Debug.Log("[SupporterInviteLink] open=" + open + " field=" + (urlField != null ? urlField.text : "-"));
    }

    public void _Close()
    {
        if (copyPanel != null) copyPanel.SetActive(false);
    }

    /// <summary>入力欄を書き換えられても、次に見るときは URL に戻す（入力欄の編集の終わりから呼ぶ）</summary>
    public void _ResetField()
    {
        if (urlField != null && urlField.text != _url) urlField.text = _url;
    }

    /// <summary>今の URL（テスト用）</summary>
    public string _GetUrl() { return _url; }

    /// <summary>QR を出しているか（テスト用）</summary>
    public bool _IsQrShown() { return qrRoot != null && qrRoot.activeSelf; }

    /// <summary>欄を開いているか（テスト用）</summary>
    public bool _IsOpen() { return copyPanel != null && copyPanel.activeSelf; }

    private bool QrMatches()
    {
        return qrUrl != null && qrUrl.Length > 0 && Trim(qrUrl) == Trim(_url);
    }

    private void Refresh()
    {
        bool qr = QrMatches();
        if (qrRoot != null) qrRoot.SetActive(qr);
        if (urlField != null && urlField.text != _url) urlField.text = _url;
        if (openLabel != null) openLabel.text = T("info.copy.open", "URL をコピー・QR");
        if (closeLabel != null) closeLabel.text = T("info.copy.close", "閉じる");
        if (helpText != null)
        {
            helpText.text = qr
                ? T("info.copy.help", "<b>PC でコピーする</b>\n上の欄を押して、Ctrl+A のあと Ctrl+C\n\n<b>スマホで開く</b>\n右の QR を読み取る")
                : T("info.copy.helpNoQr", "<b>PC でコピーする</b>\n上の欄を押して、Ctrl+A のあと Ctrl+C");
        }
    }

    private string Trim(string url)
    {
        string s = url.Trim();
        if (s.EndsWith("/")) s = s.Substring(0, s.Length - 1);
        return s;
    }

    private string Lang()
    {
        if (notice != null) return notice._GetLanguage();
        string l = VRCPlayerApi.GetCurrentLanguage();
        if (l == null || l.Length == 0) return "en";
        return l;
    }

    private string T(string key, string fallback)
    {
        if (texts != null)
        {
            string s = texts._Get(key, Lang());
            if (s != null) return s;
        }
        return fallback;
    }
}
