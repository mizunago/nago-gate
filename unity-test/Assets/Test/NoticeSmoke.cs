// Runtime smoke test driver (test project only). Drives NoticeHub and logs what is on screen.
using NagoNotice;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class NoticeSmoke : UdonSharpBehaviour
{
    public NoticeHub hub;
    public SupporterGate gate;
    public GameObject canvas;
    public GameObject[] rows;
    public TextMeshProUGUI[] texts;
    public Transform contentSpawn;
    public TextMeshProUGUI gateStatus;
    public TextMeshProUGUI gateMessage;
    public TextMeshProUGUI creditsText;
    public TextMeshProUGUI infoText;
    public SupporterRegistry registry;
    public bool skipEnter;
    public bool noticeGallery;
    public bool skipLangSwitch;
    public NoticeJoinLeave joinLeave;
    public TextMeshProUGUI joinLeaveLabel;
    public bool joinLeaveToggle;
    public SupporterInviteLink invite;
    public TMP_InputField inviteField;
    public TextMeshProUGUI inviteHelp;
    public GameObject inviteOpenButton;
    public SupporterTestPanel testPanel;
    public bool testPanelSteps;
    public bool testPanelHold;
    public SupporterContentGuard guard;
    public GameObject guardPlain;
    public Renderer guardSyncedRenderer;
    public AudioSource guardAudio;
    public bool guardSteps;
    public NoticeSmoke peer;   // 編集時の検査だけで使う（ほかの Udon から呼ばれる Udon の見本）

    private int _tick;

    void Start()
    {
        SendCustomEventDelayedSeconds(nameof(_Step1), 2f);
        SendCustomEventDelayedSeconds(nameof(_Report), 1f);
        if (testPanelSteps) SendCustomEventDelayedSeconds(nameof(_TestPanelA), 4f);
        if (guardSteps) SendCustomEventDelayedSeconds(nameof(_GuardA), 4f);
    }

    private string GuardState()
    {
        return "hiding=" + guard._IsHiding() + " plain=" + guardPlain.activeSelf + " syncedRenderer=" + guardSyncedRenderer.enabled + " mute=" + guardAudio.mute;
    }

    // 入れない人に見せない仕組み: 入れない間は隠れ、住人にすると戻り、リストどおりに戻すとまた隠れる。
    // 入れない間に、ほかの処理が表示や音を戻しても、消し直す
    public void _GuardA()
    {
        Debug.Log("[SMOKE] guard before " + GuardState());
        testPanel._ResidentOn();
        Debug.Log("[SMOKE] guard resident " + GuardState());
        testPanel._ResetToList();
        Debug.Log("[SMOKE] guard back " + GuardState());
        guardSyncedRenderer.enabled = true;   // Animator やスクリプトが表示を戻した
        guardAudio.mute = false;              // 動画プレイヤーなどが音を戻した
        SendCustomEventDelayedFrames(nameof(_GuardB), 2);
        SendCustomEventDelayedSeconds(nameof(_GuardC), 1.5f);
    }

    public void _GuardB() { Debug.Log("[SMOKE] guard rehide syncedRenderer=" + guardSyncedRenderer.enabled); }

    public void _GuardC() { Debug.Log("[SMOKE] guard remute mute=" + guardAudio.mute); }

    private string TestState()
    {
        return "allowed=" + gate._IsLocalAllowed() + " rank=" + registry._GetLocalRank() + " member=" + registry._IsLocalMember() + " override=" + registry._IsTestOverride();
    }

    // テスト用のパネル: Play モードでは出ていて、持ち主でなくても使える。押すと扱いが変わり、戻すとリストどおりになる
    public void _TestPanelA()
    {
        if (testPanel == null) { Debug.Log("[SMOKE] testpanel missing"); return; }
        Debug.Log("[SMOKE] testpanel active=" + testPanel.gameObject.activeInHierarchy + " canUse=" + testPanel._CanUse() + " before " + TestState());
        testPanel._ResidentOn();
        Debug.Log("[SMOKE] testpanel residentOn " + TestState());
        if (testPanelHold) return;
        testPanel._RankHigh();
        Debug.Log("[SMOKE] testpanel rankHigh " + TestState());
        testPanel._RankNone();
        testPanel._ResidentOff();
        Debug.Log("[SMOKE] testpanel none " + TestState());
        SendCustomEventDelayedSeconds(nameof(_TestPanelB), 2f);
    }

    public void _TestPanelB()
    {
        testPanel._ResetToList();
        Debug.Log("[SMOKE] testpanel reset " + TestState());
    }

    public void _Report()
    {
        _tick++;
        VRCPlayerApi local = Networking.LocalPlayer;
        string s = "[SMOKE] t=" + _tick + " lang=" + hub._GetLanguage() + " canvas=" + canvas.activeSelf;
        if (local != null)
        {
            Vector3 head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 d = canvas.transform.position - head;
            s += " dist=" + d.magnitude.ToString("0.00") + " scale=" + canvas.transform.localScale.x.ToString("0.0000")
                + " pos=" + local.GetPosition().ToString("0.0");
        }
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i].activeSelf) s += " | row" + i + "=" + texts[i].text.Replace("\n", "/");
        }
        if (gate != null) s += " || gate allowed=" + gate._IsLocalAllowed() + " inside=" + gate._IsLocalInside() + " supporters=" + gate._GetSupporterCount()
            + " msg=" + gate._GetLastMessage().Replace("\n", "/");
        Debug.Log(s);
        if (_tick < 40) SendCustomEventDelayedSeconds(nameof(_Report), 1f);
    }

    public void _Step1()
    {
        Debug.Log("[SMOKE] step1: basic toasts");
        hub._Show("こんにちは");
        hub._Show("こんにちは");                                   // duplicate: must not stack
        hub._ShowLevel("満室です", NoticeHub.LevelWarning);
        hub._ShowKeyArgs("gate.guestJoined", new string[] { hub._Escape("<b>Guest</b>") });
        hub._ShowEx("四つ目（待ち行列）", NoticeHub.LevelError, 3f, null, false);
        hub._ShowKey("gate.approved");                             // 5th: queued
        hub.Show("互換の Show");                                    // 6th: queued
        Debug.Log("[SMOKE] text(gate.approved)=" + hub._Text("gate.approved") + " pick=" + hub._Pick("日本語", "English", "한국어", "中文")
            + " missing=" + hub._Text("no.such.key"));
        SendCustomEventDelayedSeconds(nameof(_Step2), 3f);
    }

    public void _Step2()
    {
        Debug.Log("[SMOKE] step2: sticky + language override");
        if (joinLeave != null && joinLeaveToggle)
        {
            joinLeave._Toggle();
            Debug.Log("[SMOKE] joinleave toggled on=" + joinLeave._IsOn() + " label=" + joinLeaveLabel.text);
        }
        hub._SetSticky("cd", "あと 1:59", NoticeHub.LevelWarning);
        hub._SetSticky("cd", "あと 1:58", NoticeHub.LevelWarning);
        hub._SetLanguageOverride("en");
        Debug.Log("[SMOKE] en text(gate.approved)=" + hub._Text("gate.approved") + " zhTW via override next");
        hub._SetLanguageOverride("zh-TW");
        Debug.Log("[SMOKE] zh-TW text(gate.approved)=" + hub._Text("gate.approved"));
        hub._SetLanguageOverride("fr");
        Debug.Log("[SMOKE] fr text(gate.approved)=" + hub._Text("gate.approved"));
        hub._SetLanguageOverride("");
        SendCustomEventDelayedSeconds(nameof(_Step3), 3f);
    }

    public void _Step3()
    {
        Debug.Log("[SMOKE] step3: clear sticky, enter content");
        hub._ClearSticky("cd");
        hub._ClearAll();
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!skipEnter && local != null && contentSpawn != null) local.TeleportTo(contentSpawn.position, contentSpawn.rotation);
        if (noticeGallery)
        {
            SendCustomEventDelayedSeconds(nameof(_GalleryA), 1.2f);
            SendCustomEventDelayedSeconds(nameof(_GalleryB), 4.6f);
        }
        Debug.Log("[SMOKE] status=" + gateStatus.text.Replace("\n", "/"));
        if (registry != null) Debug.Log("[SMOKE] link(discord)=" + registry._GetLink("discord") + " link(none)=[" + registry._GetLink("nothing") + "] loaded=" + registry._IsLoaded()
            + " hasMembers=" + registry._HasMemberList() + " localRank=" + registry._GetLocalRank() + " localMember=" + registry._IsLocalMember()
            + " rank(Paula)=" + registry._GetRankOfName("Paula") + " rank(Alice)=" + registry._GetRankOfName("Alice"));
        if (creditsText != null) Debug.Log("[SMOKE] credits(ja)=" + creditsText.text.Replace("\n", "/"));
        if (infoText != null) Debug.Log("[SMOKE] info(ja)=" + infoText.text.Replace("\n", "/"));
        if (invite != null)
        {
            // 「URL をコピー・QR」を押した状態を見る。押す前は閉じている
            bool before = invite._IsOpen();
            invite._Toggle();
            Debug.Log("[SMOKE] invite(ja) button=" + inviteOpenButton.activeSelf + " before=" + before + " open=" + invite._IsOpen() + " url=" + invite._GetUrl()
                + " field=" + inviteField.text + " qr=" + invite._IsQrShown() + " help=" + inviteHelp.text.Replace("\n", "/"));
            inviteField.text = "changed";
            invite._ResetField();
            invite._Close();
            Debug.Log("[SMOKE] invite closed open=" + invite._IsOpen() + " field=" + inviteField.text);
        }
        if (!skipLangSwitch)
        {
            hub._SetLanguageOverride("en");
            SendCustomEventDelayedSeconds(nameof(_Step4), 1f);
        }
        SendCustomEventDelayedSeconds(nameof(_Step5), 12f);
    }

    public void _Step4()
    {
        if (creditsText != null) Debug.Log("[SMOKE] credits(en)=" + creditsText.text.Replace("\n", "/"));
        if (infoText != null) Debug.Log("[SMOKE] info(en)=" + infoText.text.Replace("\n", "/"));
        if (gateStatus != null) Debug.Log("[SMOKE] status(en)=" + gateStatus.text.Replace("\n", "/"));
        hub._SetLanguageOverride("");
    }

    // every kind of gate notice, shown through the hub with the real texts (screenshots only)
    public void _GalleryA()
    {
        hub._ClearAll();
        hub._ShowKeyLevel("gate.approved", NoticeHub.LevelSuccess);
        hub._ShowKeyArgs("gate.guestJoined", new string[] { "Hanako" });
        hub._ShowKeyLevel("gate.return.revoked", NoticeHub.LevelWarning);
        hub._ShowKeyLevel("gate.deny.supportersOnly", NoticeHub.LevelError);
    }

    public void _GalleryB()
    {
        hub._ClearAll();
        hub._SetSticky("cd", hub._Text("gate.countdown").Replace("{0}", "1:58"), NoticeHub.LevelWarning);
        hub._ShowKeyLevel("gate.supporterBack", NoticeHub.LevelSuccess);
        hub._ShowKeyLevel("gate.return.noSupporter", NoticeHub.LevelWarning);
        hub._ShowKeyLevel("gate.deny.notApproved", NoticeHub.LevelError);
    }

    public void _Step5()
    {
        Debug.Log("[SMOKE] step5: late state allowed=" + gate._IsLocalAllowed());
        if (joinLeave != null) Debug.Log("[SMOKE] joinleave(late) on=" + joinLeave._IsOn() + " text=" + joinLeave._GetStateText() + " label=" + joinLeaveLabel.text);
        if (creditsText != null) Debug.Log("[SMOKE] credits(late)=" + creditsText.text.Replace("\n", "/"));
        if (infoText != null) Debug.Log("[SMOKE] info(late)=" + infoText.text.Replace("\n", "/"));
        if (gateStatus != null) Debug.Log("[SMOKE] status(late)=" + gateStatus.text.Replace("\n", "/"));
    }
}
