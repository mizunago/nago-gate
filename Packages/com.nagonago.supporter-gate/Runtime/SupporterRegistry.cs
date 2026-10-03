// SupporterRegistry.cs
// 支援者リスト JSON (Bot が公開したもの) をダウンロードし、各プレイヤーのランクを判定する。
// シーンに 1 つだけ置く。他のコンポーネントはこれを参照してランクを問い合わせる。
//
// JSON スキーマ (bot/src/sync.ts SupportersJson):
// {
//   "v": 1, "generatedAt": "...",
//   "tiers": [ {"id":"supporter","rank":1,"label":"Supporter","color":"#F5C542"}, ... ],
//   "access": { "<sha256(normalized displayName)>": rank, ... },
//   "credits": [ {"n":"DisplayName","r":rank}, ... ]
// }

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
[DefaultExecutionOrder(-100)]
public class SupporterRegistry : UdonSharpBehaviour
{
    public const int RankUnknown = -1;
    private const int MaxSlots = 128;
    private const int MaxListeners = 32;

    [Header("Data Source")]
    [Tooltip("Bot が公開した supporters.json の URL。*.github.io / gist.githubusercontent.com など信頼ドメインを推奨")]
    public VRCUrl dataUrl = new VRCUrl("");

    [Tooltip("再取得の間隔（秒）。支援者の追加・猶予切れはこの間隔で反映される")]
    [SerializeField] private float refreshIntervalSeconds = 600f;

    [Tooltip("ハッシュ計算用コンポーネント（同じ GameObject に付ける）")]
    [SerializeField] private SupporterHash hasher;

    [SerializeField] private bool debugMode;

    // ---- 取得データ ----
    private DataDictionary _access;
    private DataList _credits;
    private int _tierCount;
    private int[] _tierRanks = new int[0];
    private string[] _tierLabels = new string[0];
    private string[] _tierColorHex = new string[0];
    private Color[] _tierColors = new Color[0];

    private bool _loaded;
    private bool _downloading;
    private int _retryCount;
    private string _lastError = "";
    private int _dataRevision;

    // ---- プレイヤーごとのキャッシュ ----
    private int[] _slotIds = new int[MaxSlots];
    private string[] _slotHashes = new string[MaxSlots];
    private int[] _slotRanks = new int[MaxSlots];
    private int _pendingHashes;
    private bool _hashLoopRunning;

    // ---- リスナー ----
    private UdonSharpBehaviour[] _listeners = new UdonSharpBehaviour[MaxListeners];
    private int _listenerCount;

    void Start()
    {
        if (hasher == null) hasher = GetComponent<SupporterHash>();
        if (hasher == null) Debug.LogError("[SupporterRegistry] SupporterHash が見つかりません");
        for (int i = 0; i < MaxSlots; i++) _slotIds[i] = 0;
        _StartDownload();
    }

    // ================= 公開 API =================

    /// <summary>リスナー登録。データ更新時に _OnRegistryUpdated が呼ばれる。登録時点で既に読み込み済みなら即通知</summary>
    public void _RegisterListener(UdonSharpBehaviour listener)
    {
        if (listener == null || _listenerCount >= MaxListeners) return;
        for (int i = 0; i < _listenerCount; i++) if (_listeners[i] == listener) return;
        _listeners[_listenerCount++] = listener;
        if (_loaded) listener.SendCustomEvent("_OnRegistryUpdated");
    }

    public bool _IsLoaded() { return _loaded; }
    public bool _HasError() { return !_loaded && _lastError.Length > 0; }
    public string _GetLastError() { return _lastError; }
    public int _GetDataRevision() { return _dataRevision; }

    /// <summary>プレイヤーのランク。-1 = 未判定（リスト未取得/ハッシュ未計算）, 0 = 支援者でない, 1以上 = ランク</summary>
    public int _GetRank(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid()) return 0;
        int slot = FindSlot(player.playerId);
        if (slot < 0)
        {
            slot = AssignSlot(player);
            if (slot < 0) return RankUnknown;
        }
        if (_slotHashes[slot] == null) ComputeSlotHash(slot, player);
        if (!_loaded) return RankUnknown;
        return _slotRanks[slot];
    }

    public int _GetLocalRank()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return RankUnknown;
        return _GetRank(local);
    }

    /// <summary>任意の名前のランク（キャッシュなし。UI の確認用途向け）</summary>
    public int _GetRankOfName(string displayName)
    {
        if (!_loaded || hasher == null) return RankUnknown;
        string hash = hasher._Sha256Hex(hasher._NormalizeName(displayName));
        return LookupRank(hash);
    }

    public int _GetTierCount() { return _tierCount; }
    public int _GetMaxRank() { return _tierCount > 0 ? _tierRanks[_tierCount - 1] : 0; }

    public string _GetTierLabel(int rank)
    {
        int i = TierIndex(rank);
        return i >= 0 ? _tierLabels[i] : "";
    }

    public Color _GetTierColor(int rank)
    {
        int i = TierIndex(rank);
        return i >= 0 ? _tierColors[i] : Color.white;
    }

    /// <summary>"#RRGGBB" 形式（TMP のリッチテキスト用）</summary>
    public string _GetTierColorHex(int rank)
    {
        int i = TierIndex(rank);
        return i >= 0 ? _tierColorHex[i] : "#FFFFFF";
    }

    public int _GetCreditCount() { return _credits == null ? 0 : _credits.Count; }

    public string _GetCreditName(int index)
    {
        if (_credits == null || index < 0 || index >= _credits.Count) return "";
        DataToken entry = _credits[index];
        if (entry.TokenType != TokenType.DataDictionary) return "";
        DataToken n;
        if (!entry.DataDictionary.TryGetValue("n", TokenType.String, out n)) return "";
        return n.String;
    }

    public int _GetCreditRank(int index)
    {
        if (_credits == null || index < 0 || index >= _credits.Count) return 0;
        DataToken entry = _credits[index];
        if (entry.TokenType != TokenType.DataDictionary) return 0;
        DataToken r;
        if (!entry.DataDictionary.TryGetValue("r", TokenType.Double, out r)) return 0;
        return (int)r.Double;
    }

    /// <summary>手動で再取得</summary>
    public void _Refresh()
    {
        _StartDownload();
    }

    // ================= ダウンロード =================

    public void _StartDownload()
    {
        if (_downloading) return;
        if (dataUrl == null || dataUrl.Get().Length == 0)
        {
            _lastError = "dataUrl が未設定";
            Debug.LogError("[SupporterRegistry] dataUrl が未設定です");
            return;
        }
        _downloading = true;
        VRCStringDownloader.LoadUrl(dataUrl, (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _downloading = false;
        if (!ParseJson(result.Result))
        {
            _lastError = "JSON の解析に失敗";
            Debug.LogError("[SupporterRegistry] JSON parse failed");
            ScheduleRetry();
            return;
        }
        _retryCount = 0;
        _lastError = "";
        _loaded = true;
        _dataRevision++;
        RecomputeAllSlots();
        if (debugMode) Debug.Log($"[SupporterRegistry] loaded rev={_dataRevision} access={_access.Count} credits={_GetCreditCount()} tiers={_tierCount}");
        NotifyListeners();
        SendCustomEventDelayedSeconds(nameof(_StartDownload), Mathf.Max(refreshIntervalSeconds, 30f));
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _downloading = false;
        _lastError = $"{result.ErrorCode}: {result.Error}";
        Debug.LogWarning($"[SupporterRegistry] download error {_lastError}");
        ScheduleRetry();
        NotifyListeners();
    }

    private void ScheduleRetry()
    {
        _retryCount++;
        float delay = _retryCount <= 1 ? 15f : (_retryCount == 2 ? 30f : 60f);
        if (_loaded) delay = Mathf.Max(refreshIntervalSeconds, 60f);
        SendCustomEventDelayedSeconds(nameof(_StartDownload), delay);
    }

    private bool ParseJson(string json)
    {
        DataToken root;
        if (!VRCJson.TryDeserializeFromJson(json, out root)) return false;
        if (root.TokenType != TokenType.DataDictionary) return false;
        DataDictionary dict = root.DataDictionary;

        DataToken accessToken;
        if (!dict.TryGetValue("access", TokenType.DataDictionary, out accessToken)) return false;

        DataToken creditsToken;
        DataList credits = null;
        if (dict.TryGetValue("credits", TokenType.DataList, out creditsToken)) credits = creditsToken.DataList;

        DataToken tiersToken;
        int tierCount = 0;
        int[] ranks = new int[0];
        string[] labels = new string[0];
        string[] hexes = new string[0];
        Color[] colors = new Color[0];
        if (dict.TryGetValue("tiers", TokenType.DataList, out tiersToken))
        {
            DataList tiers = tiersToken.DataList;
            tierCount = tiers.Count;
            ranks = new int[tierCount];
            labels = new string[tierCount];
            hexes = new string[tierCount];
            colors = new Color[tierCount];
            for (int i = 0; i < tierCount; i++)
            {
                DataToken t = tiers[i];
                if (t.TokenType != TokenType.DataDictionary) continue;
                DataDictionary td = t.DataDictionary;
                DataToken v;
                ranks[i] = td.TryGetValue("rank", TokenType.Double, out v) ? (int)v.Double : 0;
                labels[i] = td.TryGetValue("label", TokenType.String, out v) ? v.String : "";
                hexes[i] = td.TryGetValue("color", TokenType.String, out v) ? v.String : "#FFFFFF";
                colors[i] = ParseHexColor(hexes[i]);
            }
            // rank 昇順に並べ替え（挿入ソート）
            for (int i = 1; i < tierCount; i++)
            {
                int r = ranks[i]; string l = labels[i]; string h = hexes[i]; Color c = colors[i];
                int j = i - 1;
                while (j >= 0 && ranks[j] > r)
                {
                    ranks[j + 1] = ranks[j]; labels[j + 1] = labels[j]; hexes[j + 1] = hexes[j]; colors[j + 1] = colors[j];
                    j--;
                }
                ranks[j + 1] = r; labels[j + 1] = l; hexes[j + 1] = h; colors[j + 1] = c;
            }
        }

        _access = accessToken.DataDictionary;
        _credits = credits;
        _tierCount = tierCount;
        _tierRanks = ranks;
        _tierLabels = labels;
        _tierColorHex = hexes;
        _tierColors = colors;
        return true;
    }

    private Color ParseHexColor(string hex)
    {
        if (hex == null || hex.Length < 7 || hex[0] != '#') return Color.white;
        int r = HexPair(hex, 1), g = HexPair(hex, 3), b = HexPair(hex, 5);
        if (r < 0 || g < 0 || b < 0) return Color.white;
        return new Color(r / 255f, g / 255f, b / 255f, 1f);
    }

    private int HexPair(string s, int offset)
    {
        int hi = HexDigit(s[offset]), lo = HexDigit(s[offset + 1]);
        if (hi < 0 || lo < 0) return -1;
        return hi * 16 + lo;
    }

    private int HexDigit(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    private int TierIndex(int rank)
    {
        for (int i = 0; i < _tierCount; i++) if (_tierRanks[i] == rank) return i;
        return -1;
    }

    private int LookupRank(string hash)
    {
        if (_access == null || hash == null) return 0;
        DataToken v;
        if (!_access.TryGetValue(hash, out v)) return 0;
        if (v.TokenType == TokenType.Double) return (int)v.Double;
        if (v.TokenType == TokenType.Int) return v.Int;
        return 0;
    }

    // ================= プレイヤースロット =================

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid()) return;
        AssignSlot(player);
        _pendingHashes++;
        if (!_hashLoopRunning)
        {
            _hashLoopRunning = true;
            SendCustomEventDelayedFrames(nameof(_ProcessPendingHash), 1);
        }
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (player == null) return;
        int slot = FindSlot(player.playerId);
        if (slot >= 0)
        {
            _slotIds[slot] = 0;
            _slotHashes[slot] = null;
            _slotRanks[slot] = 0;
        }
    }

    /// <summary>1 フレームに 1 人ずつハッシュ計算（大人数インスタンスでの参加時ヒッチ回避）</summary>
    public void _ProcessPendingHash()
    {
        int processed = 0;
        for (int i = 0; i < MaxSlots; i++)
        {
            if (_slotIds[i] == 0 || _slotHashes[i] != null) continue;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(_slotIds[i]);
            if (p == null || !p.IsValid())
            {
                _slotIds[i] = 0;
                continue;
            }
            ComputeSlotHash(i, p);
            processed++;
            break;
        }
        _pendingHashes = 0;
        for (int i = 0; i < MaxSlots; i++) if (_slotIds[i] != 0 && _slotHashes[i] == null) _pendingHashes++;

        if (_pendingHashes > 0)
        {
            SendCustomEventDelayedFrames(nameof(_ProcessPendingHash), 1);
        }
        else
        {
            _hashLoopRunning = false;
            if (processed > 0 && _loaded) NotifyListeners();
        }
    }

    private int FindSlot(int playerId)
    {
        if (playerId <= 0) return -1;
        for (int i = 0; i < MaxSlots; i++) if (_slotIds[i] == playerId) return i;
        return -1;
    }

    private int AssignSlot(VRCPlayerApi player)
    {
        int existing = FindSlot(player.playerId);
        if (existing >= 0) return existing;
        for (int i = 0; i < MaxSlots; i++)
        {
            if (_slotIds[i] == 0)
            {
                _slotIds[i] = player.playerId;
                _slotHashes[i] = null;
                _slotRanks[i] = 0;
                return i;
            }
        }
        return -1;
    }

    private void ComputeSlotHash(int slot, VRCPlayerApi player)
    {
        if (hasher == null) return;
        string hash = hasher._Sha256Hex(hasher._NormalizeName(player.displayName));
        _slotHashes[slot] = hash;
        _slotRanks[slot] = _loaded ? LookupRank(hash) : 0;
        if (debugMode) Debug.Log($"[SupporterRegistry] {player.displayName} -> {hash} rank={_slotRanks[slot]}");
    }

    private void RecomputeAllSlots()
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            if (_slotIds[i] == 0 || _slotHashes[i] == null) continue;
            _slotRanks[i] = LookupRank(_slotHashes[i]);
        }
    }

    private void NotifyListeners()
    {
        for (int i = 0; i < _listenerCount; i++)
        {
            if (_listeners[i] != null) _listeners[i].SendCustomEvent("_OnRegistryUpdated");
        }
    }
}
