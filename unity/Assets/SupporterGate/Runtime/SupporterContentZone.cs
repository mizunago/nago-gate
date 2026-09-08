// SupporterContentZone.cs
// ワールド本体エリアを覆う Trigger Collider に付ける。
// ローカルプレイヤーの出入りを SupporterGate に伝え、非許可者が入り込んだらロビーへ戻せるようにする。
// 複数のゾーンを置いてよい（それぞれ同じ gate を参照する）。
//
// 注意: SupporterGate.contentRoots に含めた GameObject の下に置かないこと
//       （非表示化されるとトリガーが動かなくなる）。

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SupporterContentZone : UdonSharpBehaviour
{
    [SerializeField] private SupporterGate gate;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (gate == null || player == null || !player.isLocal) return;
        gate._SetLocalInside(true);
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (gate == null || player == null || !player.isLocal) return;
        gate._SetLocalInside(false);
    }
}
