// SupporterApprovalPanel.cs
// 在室中の非支援者を一覧し、支援者が個別に許可/取り消しできるパネル。
// 支援者以外には「支援者専用」表示だけを見せる。
// 有効化ボタン（requireSupporterActivation 用）も持つ。

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;

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
    [SerializeField] private TextMeshProUGUI activationButtonText;
    [SerializeField] private GameObject activationButtonRoot;
    [SerializeField] private SupporterApprovalRow[] rows;

    private int[] _rowPlayerIds = new int[0];
    private VRCPlayerApi[] _players = new VRCPlayerApi[128];
    private bool _dirty;
    private bool _refreshQueued;

    void Start()
    {
        _rowPlayerIds = new int[rows == null ? 0 : rows.Length];
        if (gate != null) gate._RegisterListener(this);
        if (registry != null) registry._RegisterListener(this);
        QueueRefresh();
    }

    public void _OnGateUpdated() { QueueRefresh(); }
    public void _OnRegistryUpdated() { QueueRefresh(); }
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
            for (int i = 0; i < rows.Length; i++) if (rows[i] != null) rows[i]._Hide();
            return;
        }

        SupporterGateMode mode = gate._GetMode();
        bool approvalMode = mode == SupporterGateMode.SupporterApproval;

        if (activationButtonRoot != null) activationButtonRoot.SetActive(gate._RequiresActivation());
        if (activationButtonText != null) activationButtonText.text = gate._IsActivated() ? "エリアを無効化" : "エリアを有効化";

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
            r._SetRow(p.displayName, gate._IsApproved(p.playerId));
            row++;
        }
        for (int i = row; i < rows.Length; i++)
        {
            _rowPlayerIds[i] = 0;
            if (rows[i] != null) rows[i]._Hide();
        }

        if (headerText != null)
        {
            if (approvalMode) headerText.text = $"入場許可（非支援者 {guests} 人）";
            else if (mode == SupporterGateMode.SupporterPresence) headerText.text = $"支援者在室中は開放（非支援者 {guests} 人）";
            else if (mode == SupporterGateMode.SupportersOnly) headerText.text = "支援者限定モード";
            else headerText.text = "公開モード";
        }
    }
}
