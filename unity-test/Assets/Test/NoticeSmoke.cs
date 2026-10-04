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

    private int _tick;

    void Start()
    {
        SendCustomEventDelayedSeconds(nameof(_Step1), 2f);
        SendCustomEventDelayedSeconds(nameof(_Report), 1f);
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
