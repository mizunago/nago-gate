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
//
// 通知（任意）: notice に共通の通知（NagoNotice の NoticeHub）を入れると、次を本人の画面に出す。
//   - ロビーへ戻された理由（許可の取り消し・支援者の退出から時間切れ など）
//   - 支援者が全員退出したあとの予告（あと m:ss でロビーに戻ります、のカウントダウン）
//   - 入場が許可された／取り消された
//   - 支援者向け: 支援者でない人が入室した（承認パネルで許可できます）
// 文言は texts（NoticeTable の JSON・多言語）から引く。texts が無ければ日本語の既定の文を使う。

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;
using NagoNotice;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SupporterGate : UdonSharpBehaviour
{
    private const int MaxApprovals = 96;
    private const int MaxListeners = 16;

    // 入れない理由
    private const int ReasonLoading = 1;         // リストを読み込み中
    private const int ReasonListError = 2;       // リストを取得できない
    private const int ReasonSupportersOnly = 3;  // 支援者限定
    private const int ReasonNoSupporter = 4;     // 支援者が在室していない
    private const int ReasonNotActivated = 5;    // 支援者がまだ有効化していない
    private const int ReasonNotApproved = 6;     // 支援者の許可が無い
    private const int ReasonOther = 7;

    private const string CountdownStickyId = "supporter-gate.countdown";

    [Header("References")]
    [SerializeField] private SupporterRegistry registry;

    [Header("Policy")]
    [SerializeField] private SupporterGateMode mode = SupporterGateMode.SupporterApproval;
    [Tooltip("このランク以上を「支援者」として扱う（将来のプラチナ等の上位ティア用）")]
    [SerializeField] private int requiredRank = 1;
    [Tooltip("ON にすると、支援のランクではなく「メンバー」（リストの members）で判定する。支援者でも、メンバーでなければ入れない")]
    [SerializeField] private bool useMemberList = false;
    [Tooltip("持ち主だけ: 支援者のリストを使わず、Owner Display Names の人だけを「支援者」として扱う（実験用のワールドなど）。Mode が SupporterApproval なら、在室の持ち主が許可した人も入れる。リストの読み込みを待たない。表示の「支援者」は「持ち主」になる")]
    [SerializeField] private bool ownersOnly = false;
    [Tooltip("支援者が「有効化」するまで非支援者を入れない（Approval / Presence モード）")]
    [SerializeField] private bool requireSupporterActivation = false;
    [Tooltip("支援者が全員退室してから非支援者をロビーへ戻すまでの秒数（支援者のリジョイン猶予）")]
    [SerializeField] private float noSupporterGraceSeconds = 120f;
    [Tooltip("リストが取得できない間、非支援者を通すか（通常は false = 通さない）")]
    [SerializeField] private bool failOpenWhenRegistryUnavailable = false;
    [Tooltip("ワールドの持ち主など、支援者のリストに載せずに支援者と同じに扱う VRChat の表示名（完全一致）。クレジットには出ない（特別枠）")]
    [SerializeField] private string[] ownerDisplayNames = new string[0];

    [Header("Scene")]
    [SerializeField] private Transform lobbySpawn;
    [SerializeField] private Transform contentSpawn;
    [Tooltip("非許可時にローカルで非アクティブにするオブジェクト（同期オブジェクトは含めないこと）")]
    [SerializeField] private GameObject[] contentRoots;
    [SerializeField] private bool hideContentWhenLocked = true;

    [Header("UI (optional)")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI messageText;
    [Tooltip("任意。入れると、ボタンの文字を今の言語で出す")]
    [SerializeField] private TextMeshProUGUI enterButtonText;
    [SerializeField] private TextMeshProUGUI returnButtonText;

    [Header("Notice (optional)")]
    [Tooltip("共通の通知（NagoNotice の NoticeHub）。未設定なら通知は出さず、ロビーパネルの文だけ更新する")]
    [SerializeField] private NoticeHub notice;
    [Tooltip("文言の表（多言語の JSON）。未設定なら日本語の既定の文を使う")]
    [SerializeField] private NoticeTable texts;
    [Tooltip("ロビーへ戻したとき・入れなかったときに、理由を通知する")]
    [SerializeField] private bool notifyReturnReason = true;
    [Tooltip("支援者が全員退出したあと、ロビーへ戻るまでの残り時間を出し続ける")]
    [SerializeField] private bool notifyCountdown = true;
    [Tooltip("入場が許可された・取り消されたことを本人に通知する")]
    [SerializeField] private bool notifyApprovalChange = true;
    [Tooltip("支援者に、支援者でない人が入室したことを通知する（承認パネルで許可できるように）")]
    [SerializeField] private bool notifySupporterOfGuests = true;

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
    private bool _prevApproved;
    private int _countdownShown = -1;   // 表示中の残り秒（-1 = 出していない）
    private float _startTime;

    private UdonSharpBehaviour[] _listeners = new UdonSharpBehaviour[MaxListeners];
    private int _listenerCount;
    private VRCPlayerApi[] _players = new VRCPlayerApi[128];

    void Start()
    {
        _startTime = Time.time;
        if (registry == null) { if (!ownersOnly) Debug.LogError("[SupporterGate] registry が未設定です"); }
        else registry._RegisterListener(this);
        if (notice != null)
        {
            notice._RegisterListener(this);
            if (texts != null) notice._RegisterTable(texts);
        }
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
    /// <summary>メンバーで判定するゲートか（表示の言葉を「メンバー」に替えるために、パネルが見る）</summary>
    public bool _UsesMemberList() { return useMemberList; }
    /// <summary>持ち主だけのゲートか（表示の言葉を「持ち主」に替えるために、パネルが見る）</summary>
    public bool _IsOwnersOnly() { return ownersOnly; }
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
        if (registry == null && !ownersOnly) return false;
        return LocalRank() >= requiredRank;
    }

    public bool _IsPlayerSupporter(VRCPlayerApi player)
    {
        if ((registry == null && !ownersOnly) || player == null || !player.IsValid()) return false;
        return RankOf(player) >= requiredRank;
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
                Announce(T("gate.msg.approvalFull", "許可枠が上限に達しています"), NoticeHub.LevelWarning, notifyReturnReason);
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
            Announce(DenyReason(), NoticeHub.LevelWarning, notifyReturnReason);
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
        NotifySupporterOfGuest(player);
    }

    /// <summary>NoticeHub から呼ばれる（言語が変わった）</summary>
    public void _OnNoticeLanguageChanged()
    {
        Evaluate();
    }

    public override void OnLanguageChanged(string language)
    {
        if (notice == null) Evaluate();
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
        if (registry == null && !ownersOnly) return;
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
            if (RankOf(p) >= requiredRank) supporters++;
        }
        _supporterCount = supporters;

        float now = Time.time;
        if (supporters > 0) _lastSupporterSeen = now;
        bool presence = supporters > 0 || (_lastSupporterSeen >= 0f && now - _lastSupporterSeen < noSupporterGraceSeconds);
        bool activationOk = !requireSupporterActivation || _activated;

        // オーナーだけが行う掃除: 退室者の許可を消す / 支援者不在が続いたら有効化を解除
        if (Networking.IsOwner(gameObject)) OwnerMaintenance(presence);

        int localRank = LocalRank();
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

        bool wasAllowed = _localAllowed;
        bool changed = allowed != _localAllowed;
        _localAllowed = allowed;

        bool approvedNow = _IsApproved(local.playerId);
        bool returned = false;
        if (!allowed && _localInside)
        {
            // 中に居るのに入れない状態になった: 理由を伝えてロビーへ戻す。
            // 今まで入れていた人には「戻した理由」、元から入れない人には「入れない理由」を出す
            _localInside = false;
            int code = DenyCode(localRank);
            string msg = wasAllowed ? ReturnReasonText(code, _prevApproved && !approvedNow) : DenyReasonText(code);
            Announce(msg, NoticeHub.LevelWarning, notifyReturnReason);
            _ReturnToLobby();
            returned = true;
        }

        if (hideContentWhenLocked && contentRoots != null)
        {
            for (int i = 0; i < contentRoots.Length; i++)
            {
                GameObject go = contentRoots[i];
                if (go != null && go.activeSelf != allowed) go.SetActive(allowed);
            }
        }

        // 支援者の在室に頼って入っている人（支援者でない人）向けの通知
        bool dependsOnSupporter = !isSupporter && localRank != SupporterRegistry.RankUnknown
            && (mode == SupporterGateMode.SupporterApproval || mode == SupporterGateMode.SupporterPresence);
        UpdateCountdown(dependsOnSupporter && allowed && _localInside && supporters == 0 && _lastSupporterSeen >= 0f,
                        dependsOnSupporter && allowed && supporters > 0, now);

        if (notifyApprovalChange && notice != null && dependsOnSupporter && mode == SupporterGateMode.SupporterApproval)
        {
            if (approvedNow && !_prevApproved)
                notice._ShowLevel(T("gate.approved", "入場が許可されました"), NoticeHub.LevelSuccess);
            else if (!approvedNow && _prevApproved && !returned)
                notice._ShowLevel(T("gate.revoked", "入場の許可が取り消されました"), NoticeHub.LevelWarning);
        }
        _prevApproved = approvedNow;

        UpdateStatusText(localRank, presence);
        NotifyListeners();
        if (changed && allowed) SetMessage("");
    }

    // ================= 通知 =================

    /// <summary>
    /// 支援者が全員退出したあとの予告。残り秒が変わったときだけ文を差し替える。
    /// 支援者が戻ったら消して「戻りました」を出す。
    /// </summary>
    private void UpdateCountdown(bool show, bool supporterBack, float now)
    {
        if (notice == null || !notifyCountdown)
        {
            _countdownShown = -1;
            return;
        }
        if (show)
        {
            int remain = Mathf.CeilToInt(noSupporterGraceSeconds - (now - _lastSupporterSeen));
            if (remain < 0) remain = 0;
            if (remain != _countdownShown)
            {
                _countdownShown = remain;
                int m = remain / 60;
                int sec = remain % 60;
                string clock = m.ToString() + ":" + (sec < 10 ? "0" : "") + sec.ToString();
                notice._SetSticky(CountdownStickyId,
                    Fmt(T("gate.countdown", "支援者が退出しました。あと {0} でロビーに戻ります"), clock),
                    NoticeHub.LevelWarning);
            }
        }
        else if (_countdownShown >= 0)
        {
            _countdownShown = -1;
            notice._ClearSticky(CountdownStickyId);
            if (supporterBack)
                notice._ShowLevel(T("gate.supporterBack", "支援者が戻りました。このまま利用できます"), NoticeHub.LevelSuccess);
        }
    }

    /// <summary>支援者に、支援者でない人の入室を知らせる（自分の入室直後に並ぶ既存の人の分は出さない）</summary>
    private void NotifySupporterOfGuest(VRCPlayerApi player)
    {
        if (!notifySupporterOfGuests || notice == null || (registry == null && !ownersOnly)) return;
        if (mode != SupporterGateMode.SupporterApproval) return;
        if (player == null || !player.IsValid() || player.isLocal) return;
        if (Time.time - _startTime < 8f) return;
        if (!_IsLocalSupporter() || _IsPlayerSupporter(player)) return;
        notice._ShowLevel(
            Fmt(T("gate.guestJoined", "{0} さんが来ました。承認パネルで入場を許可できます"), notice._Escape(player.displayName)),
            NoticeHub.LevelInfo);
    }

    /// <summary>ロビーパネルの文を更新し、必要なら通知も出す</summary>
    private void Announce(string msg, int level, bool alsoNotify)
    {
        SetMessage(msg);
        if (alsoNotify && notice != null && msg != null && msg.Length > 0) notice._ShowLevel(msg, level);
    }

    // ================= 文言 =================

    private string Lang()
    {
        if (notice != null) return notice._GetLanguage();
        string l = VRCPlayerApi.GetCurrentLanguage();
        if (l == null || l.Length == 0) return "en";
        return l;
    }

    /// <summary>表から今の言語の文を引く。表やキーが無ければ既定の文（日本語）</summary>
    private string T(string key, string fallback)
    {
        if (texts != null)
        {
            string lang = Lang();
            // 持ち主だけのゲートでは、「支援者」を「持ち主」に言い換えた文（キー + ".owner"）があればそれを使う
            if (ownersOnly)
            {
                string o = texts._Get(key + ".owner", lang);
                if (o != null) return o;
            }
            // メンバーで判定するゲートでは、「支援者」を「メンバー」に言い換えた文（キー + ".member"）があればそれを使う
            if (useMemberList)
            {
                string m = texts._Get(key + ".member", lang);
                if (m != null) return m;
            }
            string s = texts._Get(key, lang);
            if (s != null) return s;
        }
        return fallback;
    }

    private string Fmt(string format, string arg0)
    {
        if (format == null) return "";
        return format.Replace("{0}", arg0 == null ? "" : arg0);
    }

    // ================= 持ち主の特別枠 =================

    private bool IsOwnerName(VRCPlayerApi p)
    {
        if (p == null || !p.IsValid() || ownerDisplayNames == null) return false;
        string n = p.displayName;
        for (int i = 0; i < ownerDisplayNames.Length; i++) if (ownerDisplayNames[i] == n) return true;
        return false;
    }

    /// <summary>
    /// 判定に使うランク。useMemberList が ON のときは、メンバーなら requiredRank、そうでなければ 0（支援のランクは見ない）。
    /// リストの取得前（未判定）はそのまま返す
    /// </summary>
    private int ListRank(VRCPlayerApi p)
    {
        if (ownersOnly)
        {
            // 持ち主だけ: リストは見ない（読み込みを待たない）。テスト用の上書き中の自分だけは、その値を使う
            if (registry != null && p.isLocal && registry._IsTestOverride()) return registry._GetRank(p);
            return 0;
        }
        int r = registry._GetRank(p);
        if (!useMemberList || r == SupporterRegistry.RankUnknown) return r;
        return registry._IsMember(p) ? requiredRank : 0;
    }

    /// <summary>
    /// リストのランク。持ち主の特別枠はリストに無くても支援者（requiredRank）として扱う（リストの取得前でも）。
    /// テスト用の上書き中の自分には、特別枠を使わない（持ち主のまま、入れない人の見え方を確かめられるように）
    /// </summary>
    private int RankOf(VRCPlayerApi p)
    {
        int r = ListRank(p);
        if (r < requiredRank && IsOwnerName(p) && !(p.isLocal && registry != null && registry._IsTestOverride())) return requiredRank;
        return r;
    }

    private int LocalRank()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return SupporterRegistry.RankUnknown;
        return RankOf(local);
    }

    /// <summary>自分の表示名が、Owner Display Names にあるか（テスト用のパネルが、Build & Test で使ってよい人かを見る）</summary>
    public bool _IsLocalOwnerName()
    {
        return IsOwnerName(Networking.LocalPlayer);
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

    /// <summary>今、入れない理由</summary>
    private int DenyCode(int localRank)
    {
        if (registry == null && !ownersOnly) return ReasonOther;
        if (localRank == SupporterRegistry.RankUnknown) return registry._HasError() ? ReasonListError : ReasonLoading;
        if (mode == SupporterGateMode.SupportersOnly) return ReasonSupportersOnly;
        if (_supporterCount == 0) return ReasonNoSupporter;
        if (requireSupporterActivation && !_activated) return ReasonNotActivated;
        if (mode == SupporterGateMode.SupporterApproval) return ReasonNotApproved;
        return ReasonOther;
    }

    private string DenyReason()
    {
        if (registry == null && !ownersOnly) return T("gate.deny.other", "入場できません");
        return DenyReasonText(DenyCode(LocalRank()));
    }

    /// <summary>入ろうとして入れなかったときの文</summary>
    private string DenyReasonText(int code)
    {
        if (code == ReasonLoading) return T("gate.deny.loading", "支援者リストを読み込み中です");
        if (code == ReasonListError) return T("gate.deny.listError", "支援者リストを取得できませんでした");
        if (code == ReasonSupportersOnly) return T("gate.deny.supportersOnly", "支援者限定エリアです");
        if (code == ReasonNoSupporter) return T("gate.deny.noSupporter", "支援者が在室していないため入場できません");
        if (code == ReasonNotActivated) return T("gate.deny.notActivated", "支援者がエリアを有効化するまでお待ちください");
        if (code == ReasonNotApproved) return T("gate.deny.notApproved", "在室中の支援者の許可が必要です");
        return T("gate.deny.other", "入場できません");
    }

    /// <summary>今まで入れていた人をロビーへ戻したときの文</summary>
    private string ReturnReasonText(int code, bool approvalRevoked)
    {
        if (code == ReasonNoSupporter) return T("gate.return.noSupporter", "支援者が退出してから時間が経ったため、ロビーに戻りました");
        if (code == ReasonNotApproved)
        {
            if (approvalRevoked) return T("gate.return.revoked", "支援者が入場の許可を取り消したため、ロビーに戻りました");
            return T("gate.return.notApproved", "入場の許可が無くなったため、ロビーに戻りました");
        }
        if (code == ReasonNotActivated) return T("gate.return.deactivated", "支援者がエリアを閉じたため、ロビーに戻りました");
        if (code == ReasonSupportersOnly) return T("gate.return.supportersOnly", "支援者限定エリアのため、ロビーに戻りました");
        if (code == ReasonLoading || code == ReasonListError) return T("gate.return.listUnavailable", "支援者リストを確認できないため、ロビーに戻りました");
        return T("gate.return.other", "入場できなくなったため、ロビーに戻りました");
    }

    private void UpdateStatusText(int localRank, bool presence)
    {
        if (enterButtonText != null) enterButtonText.text = T("gate.button.enter", "入場する");
        if (returnButtonText != null) returnButtonText.text = T("gate.button.return", "ロビーへ戻る");
        if (statusText == null) return;
        string modeLabel;
        if (mode == SupporterGateMode.Open) modeLabel = T("gate.mode.open", "公開");
        else if (mode == SupporterGateMode.SupportersOnly) modeLabel = T("gate.mode.supportersOnly", "支援者限定");
        else if (mode == SupporterGateMode.SupporterApproval) modeLabel = T("gate.mode.approval", "支援者＋許可制");
        else modeLabel = T("gate.mode.presence", "支援者在室中は開放");

        string you;
        if (localRank == SupporterRegistry.RankUnknown) you = Tint(SupporterRegistry.ColorDim, T("gate.you.checking", "確認中"));
        else if (localRank >= requiredRank)
        {
            if (ownersOnly) you = T("gate.you.owner", "持ち主");
            else if (useMemberList) you = T("gate.you.member", "住人");
            else
            {
                // 呼び名は、案内のパネルと同じ（文言の表の credits.tier.<ティアの id>。無ければリストの label）
                you = registry._GetTierLabel(localRank);
                if (you == null || you.Length == 0) you = T("gate.you.supporter", "支援者");
                string id = registry._GetTierId(localRank);
                if (id.Length > 0) you = T("credits.tier." + id, you);
            }
            // 役割の名前には色を付ける（支援者はティアの色、メンバーはメンバーの色）
            you = Tint(ownersOnly ? SupporterRegistry.ColorOk : useMemberList ? SupporterRegistry.ColorMember : registry._GetTierColorHex(localRank), you);
        }
        else if (_IsApproved(Networking.LocalPlayer.playerId)) you = Tint(SupporterRegistry.ColorOk, T("gate.you.approved", "許可済み"));
        else you = "<b>" + T("gate.you.guest", "一般") + "</b>";

        string act = "";
        if (requireSupporterActivation) act = " / " + (_activated ? T("gate.act.on", "有効化済み") : T("gate.act.off", "未有効化"));
        // 項目名は控えめな色、値は太字。入場の可否は色で分かるようにする
        statusText.text = Label("gate.status.mode", "モード") + "<b>" + modeLabel + act + "</b>\n"
            + Label("gate.status.supporters", "在室支援者") + "<b>" + _supporterCount.ToString() + "</b>\n"
            + Label("gate.status.you", "あなた") + you + "\n"
            + Label("gate.status.entry", "入場")
            + (_localAllowed ? Tint(SupporterRegistry.ColorOk, T("gate.entry.yes", "可")) : Tint(SupporterRegistry.ColorWarn, T("gate.entry.no", "不可")));
    }

    private string Label(string key, string fallback)
    {
        return "<color=" + SupporterRegistry.ColorDim + ">" + T(key, fallback) + ":</color> ";
    }

    private string Tint(string colorHex, string s)
    {
        return "<color=" + colorHex + "><b>" + s + "</b></color>";
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
