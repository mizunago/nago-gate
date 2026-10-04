// NoticeJoinLeave.cs
// 入退室の通知。ほかの人がインスタンスに入ったとき・出たときに、自分の画面へ通知を出す。完全ローカル（同期なし）。
//
// できること:
//   - ON / OFF を人ごとに切り替える。設定は PlayerData に保存し、次に来たときも引き継ぐ
//   - 自分より前からいた人の「入室」は出さない（入った直後に全員ぶん並ぶのを防ぐ）
//   - 文は 5 言語（ja / en / ko / zh-CN / zh-TW）を内蔵。NoticeTable に下のキーがあれば、そちらを使う
//       joinleave.join / joinleave.leave（{0} に名前）、joinleave.on / joinleave.off
//
// 使い方:
//   Tools > Nago Notice > Add Join-Leave Notice To Scene で置く（シーンに 1 つ）
//   切り替えのボタン（uGUI の Button や、ほかの U#）から _Toggle / _TurnOn / _TurnOff を呼ぶ
//   コライダーを付けたオブジェクトに置けば、Interact（Use）でも切り替わる
//   今の状態は _IsOn()。変わったときは、_RegisterListener した相手に _OnJoinLeaveNoticeChanged が届く

using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace NagoNotice
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NoticeJoinLeave : UdonSharpBehaviour
    {
        private const int MaxListeners = 16;

        private const int TextJoin = 0;
        private const int TextLeave = 1;
        private const int TextOn = 2;
        private const int TextOff = 3;

        [SerializeField] private NoticeHub notice;

        [Header("設定")]
        [Tooltip("初めて来た人の状態。ON にすると、切り替えなくても通知が出る")]
        [SerializeField] private bool defaultOn = false;
        [Tooltip("入室を知らせる")]
        [SerializeField] private bool notifyJoin = true;
        [Tooltip("退室を知らせる")]
        [SerializeField] private bool notifyLeave = true;
        [Tooltip("表示秒数。0 以下なら文の長さから自動")]
        [SerializeField] private float seconds = 4f;
        [Tooltip("切り替えたときに、今の状態（ON / OFF）を通知で知らせる")]
        [SerializeField] private bool confirmOnToggle = true;

        [Header("音")]
        [Tooltip("入退室の通知で音を鳴らすか")]
        [SerializeField] private bool playSound = true;
        [Tooltip("入室の音。空なら NoticeHub の「情報」の音")]
        [SerializeField] private AudioClip joinClip;
        [Tooltip("退室の音。空なら NoticeHub の「情報」の音")]
        [SerializeField] private AudioClip leaveClip;

        [Header("保存")]
        [Tooltip("PlayerData のキー。同じワールドのほかのキーと重ならない名前にする。空なら保存しない")]
        [SerializeField] private string saveKey = "nago.notice.joinleave";

        // 注意: Inspector で入れる配列のフィールドには、初期値（= new T[n]）を書かない（NoticeHub の注意書きを参照）
        [Header("状態の表示（任意）")]
        [Tooltip("今の状態（ON / OFF）を書き込む文字。複数可。空でよい")]
        [SerializeField] private TextMeshProUGUI[] stateLabels;

        [SerializeField] private bool debugLog = false;

        private bool _inited;
        private bool _on;
        private bool _restored;
        private bool _changedBeforeRestore;
        private UdonSharpBehaviour[] _listeners = new UdonSharpBehaviour[MaxListeners];
        private int _listenerCount;

        private void Start()
        {
            EnsureInit();
            if (notice != null) notice._RegisterListener(this);
            RefreshLabels();
        }

        private void EnsureInit()
        {
            if (_inited) return;
            _inited = true;
            _on = defaultOn;
        }

        // =====================================================================
        // 切り替え
        // =====================================================================

        public void _Toggle()
        {
            _SetOn(!_IsOn());
        }

        public void _TurnOn()
        {
            _SetOn(true);
        }

        public void _TurnOff()
        {
            _SetOn(false);
        }

        public override void Interact()
        {
            _Toggle();
        }

        public void _SetOn(bool on)
        {
            EnsureInit();
            if (_on == on) return;
            _on = on;
            // 保存したデータが届く前の書き込みは捨てられるので、届いてから書く
            if (_restored) Save();
            else _changedBeforeRestore = true;
            Changed();
            if (confirmOnToggle && notice != null) notice._ShowEx(StateText(), NoticeHub.LevelInfo, 3f, null, false);
        }

        public bool _IsOn()
        {
            EnsureInit();
            return _on;
        }

        /// <summary>今の状態を表す文（例「入退室の通知: ON」）。自分の UI に使う</summary>
        public string _GetStateText()
        {
            EnsureInit();
            return StateText();
        }

        /// <summary>状態が変わったときに _OnJoinLeaveNoticeChanged を受け取る</summary>
        public void _RegisterListener(UdonSharpBehaviour listener)
        {
            if (listener == null || _listenerCount >= MaxListeners) return;
            for (int i = 0; i < _listenerCount; i++) if (_listeners[i] == listener) return;
            _listeners[_listenerCount++] = listener;
        }

        // =====================================================================
        // イベント
        // =====================================================================

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            EnsureInit();
            _restored = true;
            if (_changedBeforeRestore)
            {
                // 届く前に本人が切り替えていたら、そちらを優先して保存する
                _changedBeforeRestore = false;
                Save();
                return;
            }
            if (saveKey == null || saveKey.Length == 0 || !PlayerData.HasKey(player, saveKey)) return;
            bool saved = PlayerData.GetBool(player, saveKey);
            if (debugLog) Debug.Log("[NoticeJoinLeave] restored on=" + saved);
            if (saved == _on) return;
            _on = saved;
            Changed();
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            EnsureInit();
            if (!_on || !notifyJoin || !Utilities.IsValid(player) || player.isLocal) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;
            // 自分より前からいた人の入室は、入った直後にまとめて届く。番号（入った順）が自分より小さい人は知らせない
            if (player.playerId < local.playerId) return;
            Announce(TextJoin, player.displayName, joinClip);
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            EnsureInit();
            if (!_on || !notifyLeave || !Utilities.IsValid(player) || player.isLocal) return;
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
            Announce(TextLeave, player.displayName, leaveClip);
        }

        /// <summary>NoticeHub から届く。言語が変わったら、状態の表示を書き直す</summary>
        public void _OnNoticeLanguageChanged()
        {
            RefreshLabels();
        }

        // =====================================================================
        // 内部
        // =====================================================================

        private void Announce(int kind, string displayName, AudioClip clip)
        {
            if (notice == null) return;
            string text = TextFor(kind).Replace("{0}", notice._Escape(displayName));
            if (debugLog) Debug.Log("[NoticeJoinLeave] " + text);
            notice._ShowEx(text, NoticeHub.LevelInfo, seconds, clip, playSound);
        }

        private void Save()
        {
            if (saveKey == null || saveKey.Length == 0) return;
            PlayerData.SetBool(saveKey, _on);
            if (debugLog) Debug.Log("[NoticeJoinLeave] saved on=" + _on);
        }

        private void Changed()
        {
            RefreshLabels();
            for (int i = 0; i < _listenerCount; i++)
            {
                if (_listeners[i] != null) _listeners[i].SendCustomEvent("_OnJoinLeaveNoticeChanged");
            }
        }

        private void RefreshLabels()
        {
            if (stateLabels == null) return;
            string s = StateText();
            for (int i = 0; i < stateLabels.Length; i++)
            {
                if (stateLabels[i] != null) stateLabels[i].text = s;
            }
        }

        private string StateText()
        {
            return TextFor(_on ? TextOn : TextOff);
        }

        /// <summary>表（NoticeTable）にキーがあればその文、無ければ内蔵の文</summary>
        private string TextFor(int kind)
        {
            string lang = "en";
            if (notice != null)
            {
                string key = kind == TextJoin ? "joinleave.join" : kind == TextLeave ? "joinleave.leave" : kind == TextOn ? "joinleave.on" : "joinleave.off";
                string s = notice._Text(key);
                if (s != key) return s;
                lang = notice._GetLanguage();
            }
            return Builtin(kind, lang);
        }

        private string Builtin(int kind, string lang)
        {
            if (lang == null) lang = "en";
            if (lang.StartsWith("ja"))
            {
                if (kind == TextJoin) return "{0} が入室しました";
                if (kind == TextLeave) return "{0} が退室しました";
                return kind == TextOn ? "入退室の通知: ON" : "入退室の通知: OFF";
            }
            if (lang.StartsWith("ko"))
            {
                if (kind == TextJoin) return "{0} 님이 입장했습니다";
                if (kind == TextLeave) return "{0} 님이 퇴장했습니다";
                return kind == TextOn ? "입퇴장 알림: ON" : "입퇴장 알림: OFF";
            }
            if (lang.StartsWith("zh"))
            {
                bool traditional = lang.IndexOf("TW") >= 0 || lang.IndexOf("HK") >= 0 || lang.IndexOf("Hant") >= 0;
                if (kind == TextJoin) return "{0} 已加入";
                if (kind == TextLeave) return traditional ? "{0} 已離開" : "{0} 已离开";
                if (traditional) return kind == TextOn ? "進出通知: ON" : "進出通知: OFF";
                return kind == TextOn ? "进出通知: ON" : "进出通知: OFF";
            }
            if (kind == TextJoin) return "{0} joined";
            if (kind == TextLeave) return "{0} left";
            return kind == TextOn ? "Join/leave notices: ON" : "Join/leave notices: OFF";
        }
    }
}
