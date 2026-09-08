// SupporterGate.cs
// ワールド本体への入場を制御する。シーンに 1 つ。
//
// モード:
//   Open              : ゲートなし（クレジット表示・ランクタグだけ使う公開ワールド向け）
//   SupportersOnly    : requiredRank 以上の支援者だけ入場可
//   SupporterApproval : 支援者は入場可。非支援者は在室中の支援者が個別に許可した人だけ入場可
//   SupporterPresence : 支援者が在室している間は誰でも入場可
//
// requireSupporterActivation = true の場合、Approval / Presence モードは支援者が「有効化」するまで誰も（支援者本人以外）入れない。
// 支援者が全員退室すると noSupporterGraceSeconds 後に非支援者はロビーへ戻され、有効化も解除される。
//
// 承認は playerId 単位で保持する。playerId はインスタンス内で再利用されないため、
// 入り直した人は自動的に未許可へ戻る。

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SupporterGate : UdonSharpBehaviour
{
    private const int MaxApprovals = 96;
    private const int MaxListeners = 16;

    [Header("References")]
    [SerializeField] private SupporterRegistry registry;

    [Header("Policy")]
    [SerializeField] private SupporterGateMode mode = SupporterGateMode.SupporterApproval;
    [Tooltip("このランク以上を「支援者」として扱う（将来のプラチナ等の上位ティア用）")]
    [SerializeField] private int requiredRank = 1;
    [Tooltip("支援者が「有効化」するまで非支援者を入れない（Approval / Presence モード）")]
    [SerializeField] private bool requireSupporterActivation = false;
    [Tooltip("支援者が全員退室してから非支援者をロビーへ戻すまでの秒数（支援者のリジョイン猶予）")]
    [SerializeField] private float noSupporterGraceSeconds = 120f;
    [Tooltip("リストが取得できない間、非支援者を通すか（通常は false = 通さない）")]
    [SerializeField] private bool failOpenWhenRegistryUnavailable = false;

    [Header("Scene")]
    [SerializeField] private Transform lobbySpawn;
    [SerializeField] private Transform contentSpawn;
    [Tooltip("非許可時にローカルで非アクティブにするオブジェクト（同期オブジェクトは含めないこと）")]
    [SerializeField] private GameObject[] contentRoots;
    [SerializeField] private bool hideContentWhenLocked = true;

    [Header("UI (optional)")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI messageText;

    // ---- 同期状態 ----
    [UdonSynced] private int[] _approvedIds = new int[MaxApprovals];
    [UdonSynced] private bool _activated;

    // ---- ローカル状態 ----
    private bool _localAllowed;
    private bool _localInside;
    private int _supporterCount;
    private float _lastSupporterSeen = -1f;
    private bool _tickRunning;
    private string _lastMessage = "";

    private UdonSharpBehaviour[] _listeners = new UdonSharpBehaviour[MaxListeners];
    private int _listenerCount;
    private VRCPlayerApi[] _players = new VRCPlayerApi[128];

    void Start()
    {
        if (registry == null) Debug.LogError("[SupporterGate] registry が未設定です");
        else registry._RegisterListener(this);
        if (!_tickRunning)
        {
            _tickRunning = true;
            SendCustomEventDelayedSeconds(nameof(_Tick), 1f);
        }
        Evaluate();
    }

    // ================= 公開 API =================

    public void _RegisterListener(UdonSharpBehaviour listener)
    {
        if (listener == null || _listenerCount >= MaxListeners) return;
        for (int i = 0; i < _listenerCount; i++) if (_listeners[i] == listener) return;
        _listeners[_listenerCount++] = listener;
        listener.SendCustomEvent("_OnGateUpdated");
    }

    public SupporterGateMode _GetMode() { return mode; }
    public int _GetRequiredRank() { return requiredRank; }
    public bool _RequiresActivation() { return requireSupporterActivation; }
    public bool _IsActivated() { return _activated; }
    public bool _IsLocalAllowed() { return _localAllowed; }
    public bool _IsLocalInside() { return _localInside; }
    public int _GetSupporterCount() { return _supporterCount; }
    public bool _IsSupporterPresent() { return _supporterCount > 0; }
    public string _GetLastMessage() { return _lastMessage; }

    /// <summary>ローカルプレイヤーが支援者として扱われるか</summary>
    public bool _IsLocalSupporter()
    {
        if (registry == null) return false;
        return registry._GetLocalRank() >= requiredRank;
    }

    public bool _IsPlayerSupporter(VRCPlayerApi player)
    {
        if (registry == null || player == null || !player.IsValid()) return false;
        return registry._GetRank(player) >= requiredRank;
    }

    public bool _IsApproved(int playerId)
    {
        if (playerId <= 0) return false;
        for (int i = 0; i < MaxApprovals; i++) if (_approvedIds[i] == playerId) return true;
        return false;
    }

    /// <summary>支援者がプレイヤーを許可/不許可にする（UI から呼ぶ）</summary>
    public void _SetApproval(int playerId, bool approved)
    {
        if (!_IsLocalSupporter()) return;
        if (playerId <= 0) return;
        VRCPlayerApi target = VRCPlayerApi.GetPlayerById(playerId);
        if (target == null || !target.IsValid()) return;
        if (target.isLocal) return;

        bool current = _IsApproved(playerId);
        if (current == approved) return;

        TakeOwnership();
        if (approved)
        {
            int free = -1;
            for (int i = 0; i < MaxApprovals; i++)
            {
                if (_approvedIds[i] == 0) { free = i; break; }
            }
            if (free < 0)
            {
                SetMessage("許可枠が上限に達しています");
                return;
            }
            _approvedIds[free] = playerId;
        }
        else
        {
            for (int i = 0; i < MaxApprovals; i++) if (_approvedIds[i] == playerId) _approvedIds[i] = 0;
        }
        RequestSerialization();
        Evaluate();
    }

    /// <summary>支援者が有効化/無効化を切り替える（requireSupporterActivation 用）</summary>
    public void _SetActivated(bool value)
    {
        if (!_IsLocalSupporter()) return;
        if (_activated == value) return;
        TakeOwnership();
        _activated = value;
        RequestSerialization();
        Evaluate();
    }

    public void _ToggleActivation()
    {
        _SetActivated(!_activated);
    }

    /// <summary>ロビーの「入場」ボタンから呼ぶ</summary>
    public void _EnterContent()
    {
        Evaluate();
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return;
        if (!_localAllowed)
        {
            SetMessage(DenyReason());
            return;
        }
        if (contentSpawn != null) local.TeleportTo(contentSpawn.position, contentSpawn.rotation);
    }

    public void _ReturnToLobby()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null || lobbySpawn == null) return;
        local.TeleportTo(lobbySpawn.position, lobbySpawn.rotation);
    }

    /// <summary>SupporterContentZone から呼ばれる</summary>
    public void _SetLocalInside(bool inside)
    {
        _localInside = inside;
        if (inside) Evaluate();
    }

    // ================= イベント =================

    public void _OnRegistryUpdated()
    {
        Evaluate();
    }

    public override void OnDeserialization()
    {
        Evaluate();
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        Evaluate();
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        // 退室者の許可を掃除するのはオーナーの仕事だが、オーナー移譲と前後する可能性があるため Tick 側で行う
        SendCustomEventDelayedSeconds(nameof(_Evaluate), 0.5f);
    }

    public void _Evaluate()
    {
        Evaluate();
    }

    public void _Tick()
    {
        Evaluate();
        SendCustomEventDelayedSeconds(nameof(_Tick), 1f);
    }

    // ================= 判定 =================

    private void Evaluate()
    {
        if (registry == null) return;
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return;

        // 在室支援者数（全クライアントで同じ結果になる: 同じリスト・同じ名前）
        int count = VRCPlayerApi.GetPlayerCount();
        if (_players.Length < count) _players = new VRCPlayerApi[count + 16];
        VRCPlayerApi.GetPlayers(_players);
        int supporters = 0;
        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = _players[i];
            if (p == null || !p.IsValid()) continue;
            if (registry._GetRank(p) >= requiredRank) supporters++;
        }
        _supporterCount = supporters;

        float now = Time.time;
        if (supporters > 0) _lastSupporterSeen = now;
        bool presence = supporters > 0 || (_lastSupporterSeen >= 0f && now - _lastSupporterSeen < noSupporterGraceSeconds);
        bool activationOk = !requireSupporterActivation || _activated;

        // オーナーだけが行う掃除: 退室者の許可を消す / 支援者不在が続いたら有効化を解除
        if (Networking.IsOwner(gameObject)) OwnerMaintenance(presence);

        int localRank = registry._GetLocalRank();
        bool isSupporter = localRank >= requiredRank;
        bool allowed;
        if (mode == SupporterGateMode.Open)
        {
            allowed = true;
        }
        else if (localRank == SupporterRegistry.RankUnknown)
        {
            allowed = failOpenWhenRegistryUnavailable;
        }
        else if (isSupporter)
        {
            allowed = true;
        }
        else if (mode == SupporterGateMode.SupportersOnly)
        {
            allowed = false;
        }
        else if (mode == SupporterGateMode.SupporterApproval)
        {
            allowed = presence && activationOk && _IsApproved(local.playerId);
        }
        else // SupporterPresence
        {
            allowed = presence && activationOk;
        }

        bool changed = allowed != _localAllowed;
        _localAllowed = allowed;

        if (!allowed && _localInside)
        {
            _localInside = false;
            SetMessage(DenyReason());
            _ReturnToLobby();
        }

        if (hideContentWhenLocked && contentRoots != null)
        {
            for (int i = 0; i < contentRoots.Length; i++)
            {
                GameObject go = contentRoots[i];
                if (go != null && go.activeSelf != allowed) go.SetActive(allowed);
            }
        }

        UpdateStatusText(localRank, presence);
        NotifyListeners();
        if (changed && allowed) SetMessage("");
    }

    private void OwnerMaintenance(bool presence)
    {
        bool dirty = false;
        for (int i = 0; i < MaxApprovals; i++)
        {
            int id = _approvedIds[i];
            if (id == 0) continue;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(id);
            if (p == null || !p.IsValid())
            {
                _approvedIds[i] = 0;
                dirty = true;
            }
        }
        if (_activated && !presence)
        {
            _activated = false;
            dirty = true;
        }
        if (dirty) RequestSerialization();
    }

    private string DenyReason()
    {
        if (registry == null) return "設定エラー";
        int localRank = registry._GetLocalRank();
        if (localRank == SupporterRegistry.RankUnknown)
        {
            return registry._HasError() ? "支援者リストを取得できませんでした" : "支援者リストを読み込み中です";
        }
        if (mode == SupporterGateMode.SupportersOnly) return "支援者限定エリアです";
        if (_supporterCount == 0) return "支援者が在室していないため入場できません";
        if (requireSupporterActivation && !_activated) return "支援者がエリアを有効化するまでお待ちください";
        if (mode == SupporterGateMode.SupporterApproval) return "在室中の支援者の許可が必要です";
        return "入場できません";
    }

    private void UpdateStatusText(int localRank, bool presence)
    {
        if (statusText == null) return;
        string modeLabel;
        if (mode == SupporterGateMode.Open) modeLabel = "公開";
        else if (mode == SupporterGateMode.SupportersOnly) modeLabel = "支援者限定";
        else if (mode == SupporterGateMode.SupporterApproval) modeLabel = "支援者＋許可制";
        else modeLabel = "支援者在室中は開放";

        string you;
        if (localRank == SupporterRegistry.RankUnknown) you = "確認中";
        else if (localRank >= requiredRank) you = registry._GetTierLabel(localRank);
        else you = _IsApproved(Networking.LocalPlayer.playerId) ? "許可済み" : "一般";

        string act = requireSupporterActivation ? (_activated ? " / 有効化済み" : " / 未有効化") : "";
        statusText.text = $"モード: {modeLabel}{act}\n在室支援者: {_supporterCount}\nあなた: {you}\n入場: {(_localAllowed ? "可" : "不可")}";
    }

    private void SetMessage(string msg)
    {
        _lastMessage = msg;
        if (messageText != null) messageText.text = msg;
    }

    private void TakeOwnership()
    {
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
    }

    private void NotifyListeners()
    {
        for (int i = 0; i < _listenerCount; i++)
        {
            if (_listeners[i] != null) _listeners[i].SendCustomEvent("_OnGateUpdated");
        }
    }
}
