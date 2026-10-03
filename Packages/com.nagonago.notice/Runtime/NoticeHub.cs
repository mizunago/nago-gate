// NoticeHub.cs
// ワールド共通の通知（トースト）。シーンに 1 つ置く。完全ローカル（同期なし）。
//
// できること:
//   - 頭の前に通知を出す。最前面に描くので壁に埋まらない。壁際では壁の手前へ寄せる
//   - 文の長さから表示秒数を決める（読み始めの秒数＋1 文字あたりの秒数。下限と上限つき）
//   - 幅に合わせて自動で折り返し、背景は文に合わせて伸び縮みする（uGUI のレイアウト任せ）
//   - 同時に複数を縦に積む。あふれた分は順番待ち。同じ文が出ている間は時間だけ延ばす
//   - 貼り付き通知（カウントダウンなど。消すまで出続け、文を差し替えられる）
//   - 多言語（VRChat の言語設定に自動で合わせる。表は NoticeTable の JSON）
//
// 使い方（呼び出し側の U#）:
//   [SerializeField] private NagoNotice.NoticeHub notice;
//   notice._Show("訳済みの文をそのまま出す");
//   notice._ShowLevel("満室です", NagoNotice.NoticeHub.LevelWarning);
//   notice._ShowKeyArgs("item.unlocked", new string[] { itemName, rankName });   // 表の {0} {1} に差し込む
//   notice._SetSticky("gate.countdown", "あと 1:59 でロビーに戻ります", NagoNotice.NoticeHub.LevelWarning);
//   notice._ClearSticky("gate.countdown");
//
// ほかのクライアントに出したいときは、呼び出し側がネットワークイベントで相手に頼み、相手のクライアントで _Show を呼ぶ。

using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace NagoNotice
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DefaultExecutionOrder(-200)]
    public class NoticeHub : UdonSharpBehaviour
    {
        public const int LevelInfo = 0;
        public const int LevelSuccess = 1;
        public const int LevelWarning = 2;
        public const int LevelError = 3;

        private const int MaxTables = 32;
        private const int MaxListeners = 32;

        [Header("参照（プレハブで配線済み。ふつうは触らない）")]
        [SerializeField] private GameObject canvasRoot;
        [SerializeField] private Transform canvasTransform;
        [SerializeField] private GameObject[] rowObjects;
        [SerializeField] private Transform[] rowTransforms;
        [SerializeField] private CanvasGroup[] rowGroups;
        [SerializeField] private TextMeshProUGUI[] rowTexts;
        [SerializeField] private Image[] rowAccents;
        [SerializeField] private AudioSource audioSource;

        [Header("文言の表（任意。実行時に _RegisterTable でも足せる）")]
        [SerializeField] private NoticeTable[] tables;

        [Header("位置")]
        [Tooltip("頭からの距離（m・目の高さ 1.6m のアバター基準）")]
        [SerializeField] private float distance = 1.2f;
        [Tooltip("視線の水平から下げる角度（度・負で下）。-12 で視界の中央より少し下")]
        [SerializeField] private float pitchOffsetDegrees = -12f;
        [Tooltip("見上げ・見下ろしにどれだけ付いていくか（0 = 水平に固定、1 = 視線どおり）")]
        [Range(0f, 1f)]
        [SerializeField] private float pitchFollow = 0.5f;
        [Tooltip("上下に付いていく角度の上限（度）")]
        [SerializeField] private float maxPitchDegrees = 35f;
        [Tooltip("付いてくる速さ（秒）。小さいほど機敏、大きいほどゆったり")]
        [SerializeField] private float followSmoothSeconds = 0.15f;
        [Tooltip("Canvas の拡大率（1px が何 m か・距離 distance のとき）。0.001 で幅 900px が 0.9m")]
        [SerializeField] private float canvasScale = 0.001f;
        [Tooltip("アバターの目の高さに合わせて距離と大きさを拡縮する（小さいアバターでも同じ見え方にする）")]
        [SerializeField] private bool scaleWithAvatar = true;
        [Tooltip("拡縮の基準にする目の高さ（m）")]
        [SerializeField] private float referenceEyeHeight = 1.6f;
        [Tooltip("壁からこの距離だけ手前に置く（m）")]
        [SerializeField] private float wallMargin = 0.25f;
        [Tooltip("壁際でもこれより近くには寄せない（m）")]
        [SerializeField] private float minDistance = 0.4f;
        [Tooltip("壁とみなすレイヤー（Trigger は当たらない）。既定は Default と Environment")]
        [SerializeField] private LayerMask wallLayers = (1 << 0) | (1 << 11);

        [Header("表示時間（文の長さから決める）")]
        [Tooltip("読み始めるまでの秒数")]
        [SerializeField] private float readLeadSeconds = 2f;
        [Tooltip("1 文字あたりの秒数（全角 1 文字。半角は半分で数える）")]
        [SerializeField] private float secondsPerUnit = 0.2f;
        [Tooltip("表示秒数の下限")]
        [SerializeField] private float minSeconds = 5f;
        [Tooltip("表示秒数の上限")]
        [SerializeField] private float maxSeconds = 12f;
        [SerializeField] private float fadeInSeconds = 0.15f;
        [SerializeField] private float fadeOutSeconds = 0.35f;

        // 注意: Inspector で入れる配列のフィールドには、初期値（= new T[n]）を書かない。
        // UdonSharp は、初期値と同じ長さの配列を Inspector で入れても Udon 側へ保存しない（既定の配列へ写すだけ）。
        [Header("見た目")]
        [Tooltip("左の色帯の色。順に 情報・成功・警告・エラー（4 つ）。空なら既定の色")]
        [SerializeField] private Color[] levelColors;

        [Header("音")]
        [Tooltip("レベルごとの音。順に 情報・成功・警告・エラー（4 つ）。空の枠は鳴らさない")]
        [SerializeField] private AudioClip[] levelClips;
        [Range(0f, 1f)]
        [SerializeField] private float volume = 0.6f;
        [Tooltip("音を鳴らすか（実行時は _SetSoundEnabled で切り替え）")]
        [SerializeField] private bool soundEnabled = true;

        [Header("言語")]
        [Tooltip("確認用の強制上書き（例 ja / en / ko / zh-CN / zh-TW）。空なら VRChat の言語設定に従う。公開時は空にする")]
        [SerializeField] private string debugLanguageOverride = "";

        [Header("その他")]
        [Tooltip("通知を出すか（実行時は _SetEnabled で切り替え）")]
        [SerializeField] private bool noticesEnabled = true;
        [Tooltip("同じ文が表示中・順番待ち中なら、積まずに時間だけ延ばす（ボタン連打の対策）")]
        [SerializeField] private bool suppressDuplicates = true;
        [Tooltip("順番待ちの上限。あふれたら古い物から捨てる")]
        [SerializeField] private int maxPending = 16;
        [SerializeField] private bool debugLog = false;

        // ---- 行の状態 ----
        private bool _inited;
        private int _rows;
        private int[] _state;          // 0 = 空き, 1 = 表示中, 2 = 消えていく途中
        private float[] _alpha;
        private float[] _hideAt;
        private string[] _text;
        private string[] _stickyId;    // null = ふつうの通知
        private bool _anyActive;

        // ---- 順番待ち ----
        private string[] _qText;
        private int[] _qLevel;
        private float[] _qSeconds;
        private AudioClip[] _qClip;
        private bool[] _qSound;
        private int _qHead;
        private int _qCount;

        // ---- 追従 ----
        private Vector3 _targetPos;
        private Quaternion _targetRot = Quaternion.identity;
        private float _targetScale = 0.001f;
        private float _lastYaw;

        // ---- 言語・表・リスナー ----
        private string _lang = "en";
        private string _languageOverride = "";
        private NoticeTable[] _tables = new NoticeTable[MaxTables];
        private int _tableCount;
        private UdonSharpBehaviour[] _listeners = new UdonSharpBehaviour[MaxListeners];
        private int _listenerCount;

        private void Start()
        {
            EnsureInit();
        }

        private void EnsureInit()
        {
            if (_inited) return;
            _inited = true;

            _rows = rowObjects == null ? 0 : rowObjects.Length;
            _state = new int[_rows];
            _alpha = new float[_rows];
            _hideAt = new float[_rows];
            _text = new string[_rows];
            _stickyId = new string[_rows];
            for (int i = 0; i < _rows; i++)
            {
                if (rowObjects[i] != null) rowObjects[i].SetActive(false);
            }
            if (canvasRoot != null) canvasRoot.SetActive(false);

            if (maxPending < 1) maxPending = 1;
            _qText = new string[maxPending];
            _qLevel = new int[maxPending];
            _qSeconds = new float[maxPending];
            _qClip = new AudioClip[maxPending];
            _qSound = new bool[maxPending];

            if (tables != null)
            {
                for (int i = 0; i < tables.Length; i++) AddTable(tables[i]);
            }
            ResolveLanguage();
        }

        // =====================================================================
        // 通知を出す
        // =====================================================================

        /// <summary>訳済みの文をそのまま出す（旧 PointGainToast.Show と同じ形。移行用）</summary>
        public void Show(string message)
        {
            ShowInternal(message, LevelInfo, 0f, null, true);
        }

        /// <summary>訳済みの文をそのまま出す</summary>
        public void _Show(string text)
        {
            ShowInternal(text, LevelInfo, 0f, null, true);
        }

        /// <summary>レベル（色帯と音）を指定して出す</summary>
        public void _ShowLevel(string text, int level)
        {
            ShowInternal(text, level, 0f, null, true);
        }

        /// <summary>
        /// 細かく指定して出す。
        /// seconds: 表示秒数。0 以下なら文の長さから自動。
        /// clip: 鳴らす音。null ならレベルの既定の音。
        /// playSound: false ならこの通知は鳴らさない。
        /// </summary>
        public void _ShowEx(string text, int level, float seconds, AudioClip clip, bool playSound)
        {
            ShowInternal(text, level, seconds, clip, playSound);
        }

        /// <summary>表のキーで出す</summary>
        public void _ShowKey(string key)
        {
            ShowInternal(_Text(key), LevelInfo, 0f, null, true);
        }

        public void _ShowKeyLevel(string key, int level)
        {
            ShowInternal(_Text(key), level, 0f, null, true);
        }

        /// <summary>表のキーで出す。文中の {0} {1} … に args を順に差し込む</summary>
        public void _ShowKeyArgs(string key, string[] args)
        {
            ShowInternal(_Format(key, args), LevelInfo, 0f, null, true);
        }

        public void _ShowKeyArgsLevel(string key, string[] args, int level)
        {
            ShowInternal(_Format(key, args), level, 0f, null, true);
        }

        // =====================================================================
        // 貼り付き通知（消すまで出続ける。同じ id で呼ぶと文を差し替える）
        // =====================================================================

        public void _SetSticky(string id, string text, int level)
        {
            EnsureInit();
            if (!noticesEnabled || id == null || text == null || text.Length == 0 || _rows == 0) return;
            level = ClampLevel(level);

            int row = FindSticky(id);
            if (row >= 0)
            {
                if (_text[row] != text)
                {
                    _text[row] = text;
                    if (rowTexts[row] != null) rowTexts[row].text = text;
                }
                if (rowAccents[row] != null) rowAccents[row].color = LevelColor(level);
                _state[row] = 1;
                return;
            }

            row = FindFreeRow();
            if (row < 0) row = StealOldestToast();
            if (row < 0) return;
            Activate(row, text, level, 0f, null, true, id);
        }

        public void _ClearSticky(string id)
        {
            EnsureInit();
            int row = FindSticky(id);
            if (row < 0) return;
            _stickyId[row] = null;
            _state[row] = 2;
        }

        public bool _HasSticky(string id)
        {
            EnsureInit();
            return FindSticky(id) >= 0;
        }

        /// <summary>表示中と順番待ちを全部消す</summary>
        public void _ClearAll()
        {
            EnsureInit();
            for (int i = 0; i < _rows; i++)
            {
                if (_state[i] != 0)
                {
                    _stickyId[i] = null;
                    _state[i] = 2;
                }
            }
            _qHead = 0;
            _qCount = 0;
        }

        // =====================================================================
        // 文言
        // =====================================================================

        /// <summary>表から今の言語の文を引く。どの表にも無ければキーをそのまま返す</summary>
        public string _Text(string key)
        {
            EnsureInit();
            if (key == null) return "";
            for (int i = 0; i < _tableCount; i++)
            {
                NoticeTable t = _tables[i];
                if (t == null) continue;
                string s = t._Get(key, _lang);
                if (s != null) return s;
            }
            return key;
        }

        /// <summary>表から引いて {0} {1} … に args を差し込む</summary>
        public string _Format(string key, string[] args)
        {
            string s = _Text(key);
            if (args == null) return s;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == null) a = "";
                s = s.Replace("{" + i.ToString() + "}", a);
            }
            return s;
        }

        /// <summary>4 言語の文から今の言語の物を選ぶ（表を使わない移行用。zh は簡体・繁体とも zh を使う）</summary>
        public string _Pick(string ja, string en, string ko, string zh)
        {
            EnsureInit();
            string s = null;
            if (_lang.StartsWith("ja")) s = ja;
            else if (_lang.StartsWith("ko")) s = ko;
            else if (_lang.StartsWith("zh")) s = zh;
            else s = en;
            if (s == null || s.Length == 0) s = en;
            if (s == null || s.Length == 0) s = ja;
            if (s == null) s = "";
            return s;
        }

        /// <summary>
        /// プレイヤー名など、外から来た文字列を文に入れる前に通す。
        /// &lt; &gt; がリッチテキストのタグとして読まれないようにする。
        /// </summary>
        public string _Escape(string s)
        {
            if (s == null) return "";
            if (s.IndexOf('<') < 0) return s;
            return "<noparse>" + s.Replace("</noparse>", "") + "</noparse>";
        }

        // =====================================================================
        // 設定
        // =====================================================================

        /// <summary>今の言語（例 ja / en / ko / zh-CN / zh-TW）</summary>
        public string _GetLanguage()
        {
            EnsureInit();
            return _lang;
        }

        /// <summary>言語を外から決める。空文字で VRChat の言語設定に戻す</summary>
        public void _SetLanguageOverride(string code)
        {
            EnsureInit();
            _languageOverride = code == null ? "" : code;
            string before = _lang;
            ResolveLanguage();
            if (before != _lang) NotifyLanguageChanged();
        }

        public void _SetEnabled(bool on)
        {
            EnsureInit();
            noticesEnabled = on;
            if (!on) _ClearAll();
        }

        public bool _IsEnabled() { return noticesEnabled; }

        public void _SetSoundEnabled(bool on) { soundEnabled = on; }

        public bool _IsSoundEnabled() { return soundEnabled; }

        /// <summary>文言の表を足す（利用側の Start から呼ぶ）</summary>
        public void _RegisterTable(NoticeTable table)
        {
            EnsureInit();
            AddTable(table);
        }

        /// <summary>言語が変わったときに _OnNoticeLanguageChanged を受け取る</summary>
        public void _RegisterListener(UdonSharpBehaviour listener)
        {
            if (listener == null || _listenerCount >= MaxListeners) return;
            for (int i = 0; i < _listenerCount; i++) if (_listeners[i] == listener) return;
            _listeners[_listenerCount++] = listener;
        }

        public override void OnLanguageChanged(string language)
        {
            EnsureInit();
            string before = _lang;
            ResolveLanguage();
            if (before != _lang) NotifyLanguageChanged();
        }

        // =====================================================================
        // 内部
        // =====================================================================

        private void AddTable(NoticeTable table)
        {
            if (table == null || _tableCount >= MaxTables) return;
            for (int i = 0; i < _tableCount; i++) if (_tables[i] == table) return;
            _tables[_tableCount++] = table;
        }

        private void ResolveLanguage()
        {
            string l = _languageOverride;
            if (l == null || l.Length == 0) l = debugLanguageOverride;
            if (l == null || l.Length == 0) l = VRCPlayerApi.GetCurrentLanguage();
            if (l == null || l.Length == 0) l = "en";
            _lang = l;
        }

        private void NotifyLanguageChanged()
        {
            if (debugLog) Debug.Log("[NoticeHub] language = " + _lang);
            for (int i = 0; i < _listenerCount; i++)
            {
                if (_listeners[i] != null) _listeners[i].SendCustomEvent("_OnNoticeLanguageChanged");
            }
        }

        private int ClampLevel(int level)
        {
            if (level < 0) return 0;
            if (level > 3) return 3;
            return level;
        }

        private Color LevelColor(int level)
        {
            if (levelColors != null && level < levelColors.Length) return levelColors[level];
            if (level == LevelSuccess) return new Color(0.45f, 0.85f, 0.50f, 1f);
            if (level == LevelWarning) return new Color(1.00f, 0.75f, 0.30f, 1f);
            if (level == LevelError) return new Color(1.00f, 0.42f, 0.42f, 1f);
            return new Color(0.36f, 0.67f, 1.00f, 1f);
        }

        private void ShowInternal(string text, int level, float seconds, AudioClip clip, bool playSound)
        {
            EnsureInit();
            if (!noticesEnabled || text == null || text.Length == 0 || _rows == 0) return;
            level = ClampLevel(level);
            if (debugLog) Debug.Log("[NoticeHub] " + text);

            if (suppressDuplicates)
            {
                for (int i = 0; i < _rows; i++)
                {
                    if (_state[i] == 1 && _stickyId[i] == null && _text[i] == text)
                    {
                        _hideAt[i] = Time.time + (seconds > 0f ? seconds : SecondsFor(text));
                        return;
                    }
                }
                for (int i = 0; i < _qCount; i++)
                {
                    if (_qText[(_qHead + i) % maxPending] == text) return;
                }
            }

            int row = FindFreeRow();
            if (row >= 0)
            {
                Activate(row, text, level, seconds, clip, playSound, null);
                return;
            }

            // 行が埋まっている: 順番待ちへ。あふれたら古い物を捨てる
            if (_qCount >= maxPending)
            {
                _qHead = (_qHead + 1) % maxPending;
                _qCount--;
            }
            int slot = (_qHead + _qCount) % maxPending;
            _qText[slot] = text;
            _qLevel[slot] = level;
            _qSeconds[slot] = seconds;
            _qClip[slot] = clip;
            _qSound[slot] = playSound;
            _qCount++;
        }

        private void Activate(int row, string text, int level, float seconds, AudioClip clip, bool playSound, string stickyId)
        {
            _text[row] = text;
            _stickyId[row] = stickyId;
            _state[row] = 1;
            _alpha[row] = 0f;
            _hideAt[row] = Time.time + (seconds > 0f ? seconds : SecondsFor(text));

            if (rowTexts[row] != null) rowTexts[row].text = text;
            if (rowAccents[row] != null) rowAccents[row].color = LevelColor(level);
            if (rowGroups[row] != null) rowGroups[row].alpha = 0f;
            if (rowTransforms[row] != null)
            {
                // 貼り付きは上、ふつうの通知は下（新しい物ほど下）に積む
                if (stickyId != null) rowTransforms[row].SetAsFirstSibling();
                else rowTransforms[row].SetAsLastSibling();
            }
            if (rowObjects[row] != null) rowObjects[row].SetActive(true);

            if (!_anyActive)
            {
                _anyActive = true;
                SnapToHead();
                if (canvasRoot != null) canvasRoot.SetActive(true);
            }

            if (playSound && soundEnabled && audioSource != null)
            {
                AudioClip c = clip;
                if (c == null && levelClips != null && level < levelClips.Length) c = levelClips[level];
                if (c != null) audioSource.PlayOneShot(c, volume);
            }
        }

        private void FreeRow(int row)
        {
            _state[row] = 0;
            _alpha[row] = 0f;
            _text[row] = null;
            _stickyId[row] = null;
            if (rowObjects[row] != null) rowObjects[row].SetActive(false);
        }

        private int FindFreeRow()
        {
            for (int i = 0; i < _rows; i++) if (_state[i] == 0) return i;
            return -1;
        }

        private int FindSticky(string id)
        {
            if (id == null) return -1;
            for (int i = 0; i < _rows; i++)
            {
                if (_state[i] != 0 && _stickyId[i] == id) return i;
            }
            return -1;
        }

        /// <summary>貼り付き通知の場所を空けるため、一番早く消える予定のふつうの通知を消す</summary>
        private int StealOldestToast()
        {
            int best = -1;
            float bestAt = float.MaxValue;
            for (int i = 0; i < _rows; i++)
            {
                if (_state[i] == 0 || _stickyId[i] != null) continue;
                if (_hideAt[i] < bestAt)
                {
                    bestAt = _hideAt[i];
                    best = i;
                }
            }
            if (best >= 0) FreeRow(best);
            return best;
        }

        /// <summary>空いた行に、順番待ちの通知を出す。1 件でも出したら true</summary>
        private bool DrainPending()
        {
            bool any = false;
            while (_qCount > 0)
            {
                int row = FindFreeRow();
                if (row < 0) break;
                int slot = _qHead;
                _qHead = (_qHead + 1) % maxPending;
                _qCount--;
                string text = _qText[slot];
                _qText[slot] = null;
                AudioClip clip = _qClip[slot];
                _qClip[slot] = null;
                Activate(row, text, _qLevel[slot], _qSeconds[slot], clip, _qSound[slot], null);
                any = true;
            }
            return any;
        }

        /// <summary>
        /// 文の長さに合わせた表示秒数。全角（CJK）は 1、それ以外は 0.5 で数える。
        /// 空白・改行・リッチテキストのタグ（&lt;…&gt;）は数えない。
        /// </summary>
        private float SecondsFor(string text)
        {
            float units = 0f;
            int n = text.Length;
            bool inTag = false;
            for (int i = 0; i < n; i++)
            {
                char c = text[i];
                if (inTag)
                {
                    if (c == '>') inTag = false;
                    continue;
                }
                if (c == '<' && text.IndexOf('>', i) > i)
                {
                    inTag = true;
                    continue;
                }
                if (c == '\n' || c == ' ' || c == '　') continue;
                units += (c >= '⺀') ? 1f : 0.5f;
            }
            float t = readLeadSeconds + units * secondsPerUnit;
            if (t < minSeconds) t = minSeconds;
            if (t > maxSeconds) t = Mathf.Max(minSeconds, maxSeconds);
            return t;
        }

        // =====================================================================
        // 追従と消える処理（何か出ている間だけ動く）
        // =====================================================================

        private void Update()
        {
            if (!_anyActive) return;

            float dt = Time.deltaTime;
            float now = Time.time;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local != null && local.IsValid() && canvasTransform != null)
            {
                ComputeTarget(local);
                float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, followSmoothSeconds));
                canvasTransform.position = Vector3.Lerp(canvasTransform.position, _targetPos, k);
                canvasTransform.rotation = Quaternion.Slerp(canvasTransform.rotation, _targetRot, k);
                float s = Mathf.Lerp(canvasTransform.localScale.x, _targetScale, k);
                canvasTransform.localScale = new Vector3(s, s, s);
            }

            bool any = false;
            bool freed = false;
            for (int i = 0; i < _rows; i++)
            {
                int st = _state[i];
                if (st == 0) continue;
                if (st == 1)
                {
                    if (_alpha[i] < 1f)
                    {
                        _alpha[i] = Mathf.Min(1f, _alpha[i] + dt / Mathf.Max(0.01f, fadeInSeconds));
                        if (rowGroups[i] != null) rowGroups[i].alpha = _alpha[i];
                    }
                    if (_stickyId[i] == null && now >= _hideAt[i]) _state[i] = 2;
                    any = true;
                }
                else
                {
                    _alpha[i] -= dt / Mathf.Max(0.01f, fadeOutSeconds);
                    if (_alpha[i] <= 0f)
                    {
                        FreeRow(i);
                        freed = true;
                    }
                    else
                    {
                        if (rowGroups[i] != null) rowGroups[i].alpha = _alpha[i];
                        any = true;
                    }
                }
            }

            if (freed && DrainPending()) any = true;

            if (!any)
            {
                _anyActive = false;
                if (canvasRoot != null) canvasRoot.SetActive(false);
            }
        }

        /// <summary>出した瞬間は、頭の前へ一気に置く（前に出した場所から滑ってこないように）</summary>
        private void SnapToHead()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || !local.IsValid() || canvasTransform == null) return;
            ComputeTarget(local);
            canvasTransform.position = _targetPos;
            canvasTransform.rotation = _targetRot;
            canvasTransform.localScale = new Vector3(_targetScale, _targetScale, _targetScale);
        }

        /// <summary>
        /// 頭の前の目標位置・向き・大きさを決める。
        /// 壁際では壁の手前へ寄せ、寄せた分だけ小さくして見かけの大きさを保つ。
        /// 最前面描画だけだと、VR で「奥にあるのに手前の壁に隠れない」矛盾で目が疲れるため、位置も寄せる。
        /// </summary>
        private void ComputeTarget(VRCPlayerApi local)
        {
            VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 f = head.rotation * Vector3.forward;

            // 真上・真下を向いたときは向きが決まらないので、直前の向きを使う
            if (f.x * f.x + f.z * f.z > 0.01f) _lastYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            float tp = Mathf.Clamp(pitch * pitchFollow, -maxPitchDegrees, maxPitchDegrees) + pitchOffsetDegrees;
            tp = Mathf.Clamp(tp, -80f, 80f);
            Vector3 dir = Quaternion.Euler(-tp, _lastYaw, 0f) * Vector3.forward;

            float s = 1f;
            if (scaleWithAvatar && referenceEyeHeight > 0.01f)
            {
                float eye = local.GetAvatarEyeHeightAsMeters();
                if (eye > 0.01f) s = Mathf.Clamp(eye / referenceEyeHeight, 0.1f, 10f);
            }

            float want = distance * s;
            float d = want;
            RaycastHit hit;
            if (Physics.Raycast(head.position, dir, out hit, want + wallMargin * s, wallLayers.value, QueryTriggerInteraction.Ignore))
            {
                float c = hit.distance - wallMargin * s;
                float min = minDistance * s;
                if (c < min) c = min;
                if (c < d) d = c;
            }

            _targetPos = head.position + dir * d;
            _targetRot = Quaternion.LookRotation(dir, Vector3.up);
            _targetScale = canvasScale * s * (d / Mathf.Max(0.0001f, want));
        }
    }
}
