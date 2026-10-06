// SupporterRegistry.cs
// 支援者リスト JSON (Bot が公開したもの) をダウンロードし、各プレイヤーのランクを判定する。
// シーンに 1 つだけ置く。他のコンポーネントはこれを参照してランクを問い合わせる。
//
// JSON スキーマ (bot/src/sync.ts SupportersJson):
// {
//   "v": 1, "generatedAt": "...",
//   "tiers": [ {"id":"supporter","rank":1,"label":"Supporter","color":"#F5C542"}, ... ],
//   "access": { "<sha256(normalized displayName)>": rank, ... },
//   "credits": [ {"n":"DisplayName","r":rank}, ... ],
//   "members": { "<sha256(normalized displayName)>": 1, ... },   // 任意。支援とは別の「メンバー」。名前は載らない
//   "links": { "discord": "https://discord.gg/xxxx" }            // 任意。案内用のリンク
// }
//
// 鍵つきのリスト（"v": 2。Bot に LIST_KEY を設定したとき。規則は bot/src/protect.ts と同じ）:
//   "keyed": [ { "k": 鍵の番号, "n": nonce, "c": 暗号化したクレジット（16 進）, "access": {...}, "members": {...} }, ... ]
//   鍵ごとに区画が 1 つ入る（鍵を入れ替えている間は、新旧 2 つ）。鍵の番号は sha256(鍵 + "\nid") の先頭 16 文字。
//   区画の "access" と "members" のハッシュは sha256(鍵 + "\n" + normalized displayName)。
//   "plain": false のときは、上の鍵なしの部分（access・credits・members）は空。true なら、鍵なしの部分にも中身がある（移行中）
//   このコンポーネントの List Key に鍵を入れると、その鍵の区画を読む。区画が無ければ、鍵なしの部分に中身があるときだけ、そちらを読む。
//   List Key を入れていても、鍵なしのリスト（"v": 1）はそのまま読める

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

    // 表示の色（どのパネルでも同じ意味で使う。支援者の呼び名は、リストのティアの色を使う）
    public const string ColorHeading = "#C9B8FF";   // 見出し
    public const string ColorDim = "#AEB4BE";       // ラベル・補足
    public const string ColorOk = "#7CFC9A";        // 入れる・許可済み
    public const string ColorWarn = "#FF8A80";      // 入れない・取得できない
    public const string ColorMember = "#9BE7A8";    // メンバーの呼び名

    private const int MaxSlots = 128;
    private const int MaxListeners = 32;

    [Header("Data Source")]
    [Tooltip("Bot が公開した supporters.json の URL。*.github.io / gist.githubusercontent.com など信頼ドメインを推奨")]
    public VRCUrl dataUrl = new VRCUrl("");

    [Tooltip("再取得の間隔（秒）。支援者の追加・猶予切れはこの間隔で反映される")]
    [SerializeField] private float refreshIntervalSeconds = 600f;

    [Tooltip("鍵つきのリストの鍵（Bot の LIST_KEY と同じ文字列）。鍵なしのリストだけを使うなら空のまま")]
    [SerializeField] private string listKey = "";

    [Tooltip("ハッシュ計算用コンポーネント（同じ GameObject に付ける）")]
    [SerializeField] private SupporterHash hasher;

    [SerializeField] private bool debugMode;

    [Tooltip("リストを読まない（持ち主だけのワールド。Gate の Owners Only と一緒に使う）。テスト用のパネルの上書きだけを受け付ける")]
    [SerializeField] private bool noList;

    // ---- 取得データ ----
    private DataDictionary _access;
    private DataList _credits;
    private DataDictionary _members;
    private DataDictionary _links;
    private int _tierCount;
    private int[] _tierRanks = new int[0];
    private string[] _tierIds = new string[0];
    private string[] _tierLabels = new string[0];
    private string[] _tierColorHex = new string[0];
    private Color[] _tierColors = new Color[0];

    private bool _loaded;
    private bool _downloading;
    private int _retryCount;
    private string _lastError = "";
    private int _dataRevision;

    // ---- 鍵つきのリスト ----
    private const int DecryptBlocksPerFrame = 1;   // 1 フレームに戻すブロック数（1 ブロック = 32 バイト。1 ブロックに 3 ミリ秒ほどかかる）
    private bool _listKeyed;            // 今のリストが鍵つきか
    private bool _hashKeyed;            // _slotHashes が、鍵を混ぜたハッシュか
    private bool _creditsReady = true;  // クレジットを表示できるか（鍵つきのリストは、戻し終わるまで false）
    private string _parseError = "";
    private string _keyId;              // 自分の鍵の番号（最初にリストを読むときに作る。鍵が無ければ空文字）
    private string _encCipher = "";     // 戻し終えた（または戻している途中の）暗号文。同じなら戻し直さない
    private string _encNonce = "";
    private byte[] _encData;
    private int _encBlock;
    private int _encDecoded;
    private System.Text.StringBuilder _encText;
    private bool _decrypting;
    private float _encStartedAt;
    private int _encStartFrame;

    // ---- プレイヤーごとのキャッシュ ----
    private int[] _slotIds = new int[MaxSlots];
    private string[] _slotHashes = new string[MaxSlots];
    private int[] _slotRanks = new int[MaxSlots];
    private bool[] _slotMember = new bool[MaxSlots];
    private int _pendingHashes;
    private bool _hashLoopRunning;

    // ---- リスナー ----
    private UdonSharpBehaviour[] _listeners = new UdonSharpBehaviour[MaxListeners];
    private int _listenerCount;

    // ---- テスト用の上書き（SupporterTestPanel から。Play モードと Build & Test だけで使う） ----
    // 自分（ローカルプレイヤー）のランクと住人の扱いを、リストの結果の代わりに使う。ほかの人の画面には影響しない
    private bool _testOn;
    private int _testRank;
    private bool _testMember;

    void Start()
    {
        if (hasher == null) hasher = GetComponent<SupporterHash>();
        if (hasher == null) Debug.LogError("[SupporterRegistry] SupporterHash が見つかりません");
        for (int i = 0; i < MaxSlots; i++) _slotIds[i] = 0;
        // 鍵が入っていれば、リストも鍵つきだと見込んでハッシュを作る（違っていたら、読み込んだときに作り直す）
        _hashKeyed = listKey != null && listKey.Length > 0;
        if (noList)
        {
            if (debugMode) Debug.Log("[SupporterRegistry] リストを読まない設定です（持ち主だけのワールド）");
            return;
        }
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
        if (_testOn && player.isLocal) return _testRank;
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

    /// <summary>リストに「メンバー」の欄があるか（Bot でメンバー登録を使っているか）</summary>
    public bool _HasMemberList() { return _members != null; }

    /// <summary>プレイヤーがメンバー（支援とは別の軸）か。リスト未取得・ハッシュ未計算の間は false</summary>
    public bool _IsMember(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid()) return false;
        if (_testOn && player.isLocal) return _testMember;
        int slot = FindSlot(player.playerId);
        if (slot < 0)
        {
            slot = AssignSlot(player);
            if (slot < 0) return false;
        }
        if (_slotHashes[slot] == null) ComputeSlotHash(slot, player);
        if (!_loaded) return false;
        return _slotMember[slot];
    }

    public bool _IsLocalMember()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return false;
        return _IsMember(local);
    }

    /// <summary>任意の名前のランク（キャッシュなし。UI の確認用途向け）</summary>
    public int _GetRankOfName(string displayName)
    {
        if (_testOn)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local != null && local.displayName == displayName) return _testRank;
        }
        if (!_loaded || hasher == null) return RankUnknown;
        return LookupRank(HashOfName(displayName));
    }

    /// <summary>テスト用の上書きを始める・変える。ランクと住人の扱いを、両方とも指定する</summary>
    public void _SetTestOverride(int rank, bool member)
    {
        _testOn = true;
        _testRank = rank < 0 ? 0 : rank;
        _testMember = member;
        Debug.Log($"[SupporterRegistry] テスト用の上書き: rank={_testRank} member={_testMember}");
        NotifyListeners();
    }

    /// <summary>テスト用の上書きをやめて、リストの結果に戻す</summary>
    public void _ClearTestOverride()
    {
        if (!_testOn) return;
        _testOn = false;
        Debug.Log("[SupporterRegistry] テスト用の上書きをやめました（リストどおり）");
        NotifyListeners();
    }

    public bool _IsTestOverride() { return _testOn; }

    public int _GetTierCount() { return _tierCount; }
    public int _GetMaxRank() { return _tierCount > 0 ? _tierRanks[_tierCount - 1] : 0; }
    public int _GetMinRank() { return _tierCount > 0 ? _tierRanks[0] : 0; }

    public string _GetTierLabel(int rank)
    {
        int i = TierIndex(rank);
        return i >= 0 ? _tierLabels[i] : "";
    }

    /// <summary>ティアの id（"supporter" / "platinum" など）。文言の表から呼び名を引くときに使う</summary>
    public string _GetTierId(int rank)
    {
        int i = TierIndex(rank);
        return i >= 0 ? _tierIds[i] : "";
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

    /// <summary>クレジットの名前を出せるか。鍵つきのリストは、読み込んでから名前を元に戻し終わるまで false</summary>
    public bool _AreCreditsReady() { return _creditsReady; }

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

    /// <summary>リストに入っている案内用のリンク（"discord" など）。無ければ空文字</summary>
    public string _GetLink(string key)
    {
        if (_links == null || key == null) return "";
        DataToken v;
        if (!_links.TryGetValue(key, TokenType.String, out v)) return "";
        return v.String;
    }

    /// <summary>手動で再取得</summary>
    public void _Refresh()
    {
        _StartDownload();
    }

    // ================= ダウンロード =================

    public void _StartDownload()
    {
        if (_downloading || noList) return;
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
        _parseError = "";
        if (!ParseJson(result.Result))
        {
            _lastError = _parseError.Length > 0 ? _parseError : "JSON の解析に失敗";
            Debug.LogError("[SupporterRegistry] " + _lastError);
            ScheduleRetry();
            NotifyListeners();
            return;
        }
        _retryCount = 0;
        _lastError = "";
        _loaded = true;
        _dataRevision++;
        if (_hashKeyed != _listKeyed)
        {
            // 見込みと違う種類のリストだった: ハッシュを作り直す（問い合わせがあればその場で、残りは 1 フレームに 1 人ずつ）
            _hashKeyed = _listKeyed;
            for (int i = 0; i < MaxSlots; i++)
            {
                if (_slotIds[i] == 0) continue;
                _slotHashes[i] = null;
                _slotRanks[i] = 0;
                _slotMember[i] = false;
            }
            if (!_hashLoopRunning)
            {
                _hashLoopRunning = true;
                SendCustomEventDelayedFrames(nameof(_ProcessPendingHash), 1);
            }
        }
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

        DataToken membersToken;
        DataDictionary members = null;
        if (dict.TryGetValue("members", TokenType.DataDictionary, out membersToken)) members = membersToken.DataDictionary;

        DataToken linksToken;
        DataDictionary links = null;
        if (dict.TryGetValue("links", TokenType.DataDictionary, out linksToken)) links = linksToken.DataDictionary;

        // 鍵つきのリスト: 自分の鍵の区画を探す（区画の "k" が鍵の番号）
        if (_keyId == null) _keyId = (listKey != null && listKey.Length > 0 && hasher != null) ? hasher._Sha256Hex(listKey + "\nid").Substring(0, 16) : "";
        DataToken keyedToken;
        bool hasKeyed = dict.TryGetValue("keyed", TokenType.DataList, out keyedToken);
        DataDictionary section = null;
        if (hasKeyed && _keyId.Length > 0)
        {
            DataList sections = keyedToken.DataList;
            for (int i = 0; i < sections.Count; i++)
            {
                DataToken st = sections[i];
                if (st.TokenType != TokenType.DataDictionary) continue;
                DataToken kid;
                if (st.DataDictionary.TryGetValue("k", TokenType.String, out kid) && kid.String == _keyId)
                {
                    section = st.DataDictionary;
                    break;
                }
            }
        }
        bool keyed = section != null;
        string cipher = "";
        string nonce = "";
        if (keyed)
        {
            DataToken sv;
            if (!section.TryGetValue("access", TokenType.DataDictionary, out accessToken)) return false;
            members = section.TryGetValue("members", TokenType.DataDictionary, out sv) ? sv.DataDictionary : null;
            nonce = section.TryGetValue("n", TokenType.String, out sv) ? sv.String : "";
            cipher = section.TryGetValue("c", TokenType.String, out sv) ? sv.String : "";
        }
        else if (hasKeyed)
        {
            // 自分の鍵の区画が無い。鍵なしの部分に中身があれば（移行中）、そちらを読む。
            // 無ければ、取得できなかったときと同じ扱いにする（空のまま使うと、全員が支援者でない扱いになる）
            DataToken plainToken;
            if (!dict.TryGetValue("plain", TokenType.Boolean, out plainToken) || !plainToken.Boolean)
            {
                _parseError = _keyId.Length == 0 ? "鍵つきのリストですが、List Key が未設定です" : "List Key が、リストのどの鍵とも一致しません";
                return false;
            }
        }

        DataToken tiersToken;
        int tierCount = 0;
        int[] ranks = new int[0];
        string[] ids = new string[0];
        string[] labels = new string[0];
        string[] hexes = new string[0];
        Color[] colors = new Color[0];
        if (dict.TryGetValue("tiers", TokenType.DataList, out tiersToken))
        {
            DataList tiers = tiersToken.DataList;
            tierCount = tiers.Count;
            ranks = new int[tierCount];
            ids = new string[tierCount];
            labels = new string[tierCount];
            hexes = new string[tierCount];
            colors = new Color[tierCount];
            for (int i = 0; i < tierCount; i++)
            {
                DataToken t = tiers[i];
                ids[i] = "";
                labels[i] = "";
                hexes[i] = "#FFFFFF";
                colors[i] = Color.white;
                if (t.TokenType != TokenType.DataDictionary) continue;
                DataDictionary td = t.DataDictionary;
                DataToken v;
                ranks[i] = td.TryGetValue("rank", TokenType.Double, out v) ? (int)v.Double : 0;
                ids[i] = td.TryGetValue("id", TokenType.String, out v) ? v.String : "";
                labels[i] = td.TryGetValue("label", TokenType.String, out v) ? v.String : "";
                hexes[i] = td.TryGetValue("color", TokenType.String, out v) ? v.String : "#FFFFFF";
                colors[i] = ParseHexColor(hexes[i]);
            }
            // rank 昇順に並べ替え（挿入ソート）
            for (int i = 1; i < tierCount; i++)
            {
                int r = ranks[i]; string d = ids[i]; string l = labels[i]; string h = hexes[i]; Color c = colors[i];
                int j = i - 1;
                while (j >= 0 && ranks[j] > r)
                {
                    ranks[j + 1] = ranks[j]; ids[j + 1] = ids[j]; labels[j + 1] = labels[j]; hexes[j + 1] = hexes[j]; colors[j + 1] = colors[j];
                    j--;
                }
                ranks[j + 1] = r; ids[j + 1] = d; labels[j + 1] = l; hexes[j + 1] = h; colors[j + 1] = c;
            }
        }

        _access = accessToken.DataDictionary;
        _listKeyed = keyed;
        if (!keyed)
        {
            _credits = credits;
            _creditsReady = true;
            _encCipher = "";
        }
        else if (cipher != _encCipher)
        {
            // 名前を元に戻す（数フレームに分ける）。前のクレジットがあれば、戻し終わるまでそれを出しておく
            if (_encCipher.Length == 0) { _credits = null; _creditsReady = false; }
            StartDecrypt(cipher, nonce);
        }
        _members = members;
        _links = links;
        _tierCount = tierCount;
        _tierRanks = ranks;
        _tierIds = ids;
        _tierLabels = labels;
        _tierColorHex = hexes;
        _tierColors = colors;
        return true;
    }

    // ================= 鍵つきのリストの復号 =================

    private void StartDecrypt(string cipher, string nonce)
    {
        _encCipher = cipher;
        _encNonce = nonce;
        _encData = new byte[cipher.Length / 2];
        _encBlock = 0;
        _encDecoded = 0;
        _encText = new System.Text.StringBuilder(_encData.Length);
        _encStartedAt = Time.realtimeSinceStartup;
        _encStartFrame = Time.frameCount;
        if (!_decrypting)
        {
            _decrypting = true;
            SendCustomEventDelayedFrames(nameof(_DecryptStep), 1);
        }
    }

    /// <summary>
    /// 暗号文を 32 バイトずつ元に戻し、UTF-8 として文字にしていく。
    /// 鍵の流れ: ブロック i = sha256(鍵 + "\n" + nonce + "\n" + i)
    /// </summary>
    public void _DecryptStep()
    {
        if (_encData == null)
        {
            _decrypting = false;
            return;
        }
        int n = _encData.Length;
        for (int step = 0; step < DecryptBlocksPerFrame && _encBlock * 32 < n; step++)
        {
            byte[] stream = hasher._Sha256Bytes(listKey + "\n" + _encNonce + "\n" + _encBlock.ToString());
            int from = _encBlock * 32;
            for (int i = 0; i < 32 && from + i < n; i++)
            {
                int v = HexPair(_encCipher, (from + i) * 2);
                _encData[from + i] = (byte)((v < 0 ? 0 : v) ^ stream[i]);
            }
            _encBlock++;
            // 戻し終えたところまでを文字にする。複数バイトの文字がブロックをまたぐので、最後のブロック以外は 4 バイト手前で止める
            int ready = _encBlock * 32 < n ? _encBlock * 32 - 4 : n;
            while (_encDecoded < ready) DecodeOne(n);
        }
        if (_encBlock * 32 < n)
        {
            SendCustomEventDelayedFrames(nameof(_DecryptStep), 1);
            return;
        }

        _decrypting = false;
        DataToken list;
        if (VRCJson.TryDeserializeFromJson(_encText.ToString(), out list) && list.TokenType == TokenType.DataList) _credits = list.DataList;
        else
        {
            _credits = null;
            Debug.LogError("[SupporterRegistry] クレジットを元に戻せませんでした");
        }
        _encData = null;
        _encText = null;
        _creditsReady = true;
        if (debugMode) Debug.Log($"[SupporterRegistry] credits decrypted count={_GetCreditCount()} blocks={_encBlock} frames={Time.frameCount - _encStartFrame} seconds={Time.realtimeSinceStartup - _encStartedAt}");
        NotifyListeners();
    }

    /// <summary>_encData の _encDecoded の位置から、UTF-8 の 1 文字を読んで _encText に足す</summary>
    private void DecodeOne(int n)
    {
        int b = _encData[_encDecoded];
        _encDecoded++;
        int cp;
        int more;
        if (b < 0x80) { cp = b; more = 0; }
        else if (b >= 0xC0 && b < 0xE0) { cp = b & 0x1F; more = 1; }
        else if (b >= 0xE0 && b < 0xF0) { cp = b & 0x0F; more = 2; }
        else if (b >= 0xF0 && b < 0xF8) { cp = b & 0x07; more = 3; }
        else { cp = 0x3F; more = 0; }   // 壊れた並びは "?" にする
        while (more > 0 && _encDecoded < n)
        {
            cp = (cp << 6) | (_encData[_encDecoded] & 0x3F);
            _encDecoded++;
            more--;
        }
        if (cp < 0x10000) _encText.Append((char)cp);
        else
        {
            // 絵文字など: 上位と下位の 2 文字に分ける
            cp -= 0x10000;
            _encText.Append((char)(0xD800 + (cp >> 10)));
            _encText.Append((char)(0xDC00 + (cp & 0x3FF)));
        }
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

    private bool LookupMember(string hash)
    {
        if (_members == null || hash == null) return false;
        return _members.ContainsKey(hash);
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
            _slotMember[slot] = false;
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
                _slotMember[i] = false;
                return i;
            }
        }
        return -1;
    }

    private void ComputeSlotHash(int slot, VRCPlayerApi player)
    {
        if (hasher == null) return;
        string hash = HashOfName(player.displayName);
        _slotHashes[slot] = hash;
        _slotRanks[slot] = _loaded ? LookupRank(hash) : 0;
        _slotMember[slot] = _loaded && LookupMember(hash);
        if (debugMode) Debug.Log($"[SupporterRegistry] {player.displayName} -> {hash} rank={_slotRanks[slot]} member={_slotMember[slot]}");
    }

    /// <summary>名前から、リストを引くためのハッシュを作る。鍵つきのリストでは、鍵を混ぜる</summary>
    private string HashOfName(string displayName)
    {
        string normalized = hasher._NormalizeName(displayName);
        return hasher._Sha256Hex(_hashKeyed ? listKey + "\n" + normalized : normalized);
    }

    private void RecomputeAllSlots()
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            if (_slotIds[i] == 0 || _slotHashes[i] == null) continue;
            _slotRanks[i] = LookupRank(_slotHashes[i]);
            _slotMember[i] = LookupMember(_slotHashes[i]);
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
