// SupporterRankTag.cs
// 支援者の頭上にティア名を色付きで表示する（世界側の「階級プレート」）。
// tagObjects にプールした TextMeshPro (world space) を割り当てる。
// 公開ワールドにも置ける。

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using TMPro;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SupporterRankTag : UdonSharpBehaviour
{
    [SerializeField] private SupporterRegistry registry;
    [Tooltip("プールしたタグ（TextMeshPro を持つ GameObject）。同時に表示できる支援者数の上限")]
    [SerializeField] private GameObject[] tagObjects;
    [SerializeField] private float heightOffset = 0.35f;
    [Tooltip("自分自身にもタグを出すか")]
    [SerializeField] private bool showOnLocalPlayer = false;
    [Tooltip("表示するランクの下限")]
    [SerializeField] private int minRank = 1;

    private int[] _tagPlayerIds = new int[0];
    private TextMeshPro[] _tagTexts = new TextMeshPro[0];
    private Transform[] _tagTransforms = new Transform[0];
    private VRCPlayerApi[] _players = new VRCPlayerApi[128];
    private bool _active;

    void Start()
    {
        int n = tagObjects == null ? 0 : tagObjects.Length;
        _tagPlayerIds = new int[n];
        _tagTexts = new TextMeshPro[n];
        _tagTransforms = new Transform[n];
        for (int i = 0; i < n; i++)
        {
            if (tagObjects[i] == null) continue;
            _tagTexts[i] = tagObjects[i].GetComponent<TextMeshPro>();
            _tagTransforms[i] = tagObjects[i].transform;
            tagObjects[i].SetActive(false);
        }
        if (registry != null) registry._RegisterListener(this);
    }

    public void _OnRegistryUpdated() { Reassign(); }
    public override void OnPlayerJoined(VRCPlayerApi player) { SendCustomEventDelayedSeconds(nameof(_Reassign), 1f); }
    public override void OnPlayerLeft(VRCPlayerApi player) { SendCustomEventDelayedSeconds(nameof(_Reassign), 0.1f); }
    public void _Reassign() { Reassign(); }

    private void Reassign()
    {
        if (registry == null || tagObjects == null) return;
        int count = VRCPlayerApi.GetPlayerCount();
        if (_players.Length < count) _players = new VRCPlayerApi[count + 16];
        VRCPlayerApi.GetPlayers(_players);

        int used = 0;
        for (int i = 0; i < count && used < tagObjects.Length; i++)
        {
            VRCPlayerApi p = _players[i];
            if (p == null || !p.IsValid()) continue;
            if (p.isLocal && !showOnLocalPlayer) continue;
            int rank = registry._GetRank(p);
            if (rank < minRank) continue;
            TextMeshPro tag = _tagTexts[used];
            if (tag == null) continue;
            _tagPlayerIds[used] = p.playerId;
            tag.text = registry._GetTierLabel(rank);
            tag.color = registry._GetTierColor(rank);
            tagObjects[used].SetActive(true);
            used++;
        }
        for (int i = used; i < tagObjects.Length; i++)
        {
            _tagPlayerIds[i] = 0;
            if (tagObjects[i] != null) tagObjects[i].SetActive(false);
        }
        _active = used > 0;
    }

    public override void PostLateUpdate()
    {
        if (!_active || tagObjects == null) return;
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return;
        Vector3 eye = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

        for (int i = 0; i < _tagPlayerIds.Length; i++)
        {
            int id = _tagPlayerIds[i];
            if (id == 0) continue;
            Transform t = _tagTransforms[i];
            if (t == null) continue;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(id);
            if (p == null || !p.IsValid())
            {
                tagObjects[i].SetActive(false);
                _tagPlayerIds[i] = 0;
                continue;
            }
            Vector3 head = p.GetBonePosition(HumanBodyBones.Head);
            if (head == Vector3.zero) head = p.GetPosition() + Vector3.up * 1.6f;
            Vector3 pos = head + Vector3.up * heightOffset;
            t.position = pos;
            Vector3 dir = pos - eye;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(dir);
        }
    }
}
