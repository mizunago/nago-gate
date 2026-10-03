// SupporterApprovalPanel.cs
// 在室中の非支援者を一覧し、支援者が個別に許可/取り消しできるパネル。
// 支援者以外には「支援者専用」表示だけを見せる。
// 有効化ボタン（requireSupporterActivation 用）も持つ。
// 文言は texts（NoticeTable）から今の言語で引く。texts が無ければ日本語の既定の文を使う。

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;
using NagoNotice;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SupporterApprovalPanel : UdonSharpBehaviour
{
    [SerializeField] private SupporterGate gate;
    [SerializeField] private SupporterRegistry registry;

    [Header("UI")]
    [Tooltip("支援者にだけ見せる部分")]
    [SerializeField] private GameObject supporterRoot;
    [Tooltip("非支援者に見せる部分")]
    [SerializeField] private GameObject deniedRoot;
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private TextMeshProUGUI deniedText;
    [SerializeField] private TextMeshProUGUI activationButtonText;
    [SerializeField] private GameObject activationButtonRoot;
    [SerializeField] private SupporterApprovalRow[] rows;

    [Header("Texts (optional)")]
    [Tooltip("文言の表（多言語の JSON）。未設定なら日本語の既定の文を使う")]
    [SerializeField] private NoticeTable texts;
    [Tooltip("共通の通知（言語の判定に使う）。未設定なら VRChat の言語設定を直接見る")]
    [SerializeField] private NoticeHub notice;

    private int[] _rowPlayerIds = new int[0];
    private VRCPlayerApi[] _players = new VRCPlayerApi[128];
    private bool _dirty;
    private bool _refreshQueued;

    void Start()
    {
        _rowPlayerIds = new int[rows == null ? 0 : rows.Length];
        if (gate != null) gate._RegisterListener(this);
        if (registry != null) registry._RegisterListener(this);
        if (notice != null) notice._RegisterListener(this);
        QueueRefresh();
    }

    public void _OnGateUpdated() { QueueRefresh(); }
    public void _OnRegistryUpdated() { QueueRefresh(); }
    public void _OnNoticeLanguageChanged() { QueueRefresh(); }
    public override void OnLanguageChanged(string language) { QueueRefresh(); }
    public override void OnPlayerJoined(VRCPlayerApi player) { QueueRefresh(); }
    public override void OnPlayerLeft(VRCPlayerApi player) { QueueRefresh(); }

    /// <summary>行ボタンから呼ばれる</summary>
    public void _OnRowPressed(int index)
    {
        if (gate == null || index < 0 || index >= _rowPlayerIds.Length) return;
        int id = _rowPlayerIds[index];
        if (id <= 0) return;
        gate._SetApproval(id, !gate._IsApproved(id));
        QueueRefresh();
    }

    /// <summary>有効化ボタンから呼ばれる</summary>
    public void _OnActivationPressed()
    {
        if (gate == null) return;
        gate._ToggleActivation();
        QueueRefresh();
    }

    // 1 フレームにまとめて再描画（ゲートは毎秒通知してくるため）
    private void QueueRefresh()
    {
        _dirty = true;
        if (_refreshQueued) return;
        _refreshQueued = true;
        SendCustomEventDelayedFrames(nameof(_DoRefresh), 1);
    }

    public void _DoRefresh()
    {
        _refreshQueued = false;
        if (!_dirty) return;
        _dirty = false;
        Refresh();
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

    private void Refresh()
    {
        if (gate == null || registry == null || rows == null) return;
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return;

        bool isSupporter = gate._IsLocalSupporter();
        if (supporterRoot != null) supporterRoot.SetActive(isSupporter);
        if (deniedRoot != null) deniedRoot.SetActive(!isSupporter);
        if (!isSupporter)
        {
            if (deniedText != null) deniedText.text = T("panel.denied", "このパネルは支援者専用です");
            for (int i = 0; i < rows.Length; i++) if (rows[i] != null) rows[i]._Hide();
            return;
        }

        SupporterGateMode mode = gate._GetMode();
        bool approvalMode = mode == SupporterGateMode.SupporterApproval;

        if (activationButtonRoot != null) activationButtonRoot.SetActive(gate._RequiresActivation());
        if (activationButtonText != null)
            activationButtonText.text = gate._IsActivated() ? T("panel.deactivate", "エリアを無効化") : T("panel.activate", "エリアを有効化");

        string stateOn = "<color=#7CFC9A>" + T("panel.state.approved", "許可済み") + "</color>";
        string stateOff = "<color=#AAAAAA>" + T("panel.state.notApproved", "未許可") + "</color>";
        string buttonOn = T("panel.button.revoke", "取り消す");
        string buttonOff = T("panel.button.approve", "許可する");

        int count = VRCPlayerApi.GetPlayerCount();
        if (_players.Length < count) _players = new VRCPlayerApi[count + 16];
        VRCPlayerApi.GetPlayers(_players);

        int row = 0;
        int guests = 0;
        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = _players[i];
            if (p == null || !p.IsValid() || p.isLocal) continue;
            if (gate._IsPlayerSupporter(p)) continue;
            guests++;
            if (!approvalMode || row >= rows.Length) continue;
            SupporterApprovalRow r = rows[row];
            if (r == null) continue;
            _rowPlayerIds[row] = p.playerId;
            bool approved = gate._IsApproved(p.playerId);
            r._SetRow(p.displayName, approved ? stateOn : stateOff, approved ? buttonOn : buttonOff);
            row++;
        }
        for (int i = row; i < rows.Length; i++)
        {
            _rowPlayerIds[i] = 0;
            if (rows[i] != null) rows[i]._Hide();
        }

        if (headerText != null)
        {
            string n = guests.ToString();
            if (approvalMode) headerText.text = T("panel.header.approval", "入場許可（非支援者 {0} 人）").Replace("{0}", n);
            else if (mode == SupporterGateMode.SupporterPresence) headerText.text = T("panel.header.presence", "支援者在室中は開放（非支援者 {0} 人）").Replace("{0}", n);
            else if (mode == SupporterGateMode.SupportersOnly) headerText.text = T("panel.header.supportersOnly", "支援者限定モード");
            else headerText.text = T("panel.header.open", "公開モード");
        }
    }
}
