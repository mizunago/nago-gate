// SupporterContentGuard.cs
// 入れない人（支援者・住人でない人）の画面で、ワールド本体を見せない・聞かせない。
// あやしいけんきゅうじょの NonResidentGuard（2026-10-06・07）を、パッケージに取り込んだもの。
//
// Gate の contentRoots との違い:
// - ゲートは毎秒すべての根を SetActive し直すので、根が多いと毎秒の引っかかりになる。
//   ここでは、ゲートの通知（_OnGateUpdated）を受けて、入れる・入れないが変わったときだけ切り替える
// - 同期する物（同期ありの Udon・ObjectSync・Pickup・Station）と、ほかのギミックが SetActive で切り替える物は、
//   GameObject を止めると同期やアニメーションが壊れるので、根にしない。代わりに Renderer と Canvas の enabled だけを消す。
//   Animator やスクリプトが表示を戻すことがあるので、入れない間は PostLateUpdate で毎フレーム消し直す
// - 音源は、入れない間は mute にする。動画プレイヤーなどが mute を戻すので、ゲートの通知のたびにかけ直す
// 入れるようになったら、入れないに変わった時点の値（Renderer・Canvas の enabled、mute）に戻す。
// リストの読み込みが終わるまでの数秒は「入れない」扱いなので、その間は隠れて音も出ない（入口の部屋にいる間）。
//
// 何を隠すかは、エディタのツール（Tools > SupporterGate > Set Up Content Guard (auto)）が選んで入れる。
// 入口の部屋・通知の板・ゲート一式・keep に入れた物は、隠さない。

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class SupporterContentGuard : UdonSharpBehaviour
{
    [SerializeField] private SupporterGate gate;
    [Tooltip("入れない間、GameObject ごと止める物（同期する物と、ほかのギミックが切り替える物を含まない部分木の根）")]
    [SerializeField] private GameObject[] roots;
    [Tooltip("入れない間、mute にする音源")]
    [SerializeField] private AudioSource[] sources;
    [Tooltip("入れない間、見た目だけ消す Renderer（roots に入らなかった、同期する物・動く物・切り替えられる物）")]
    [SerializeField] private Renderer[] hideRenderers;
    [Tooltip("入れない間、見た目だけ消す Canvas")]
    [SerializeField] private Canvas[] hideCanvases;
    [Tooltip("自動の選び方で、隠さない物（入口の部屋をゲート一式の外に作った場合や、止めると困る管理役など）。この下は根にも、見た目を消す物にもしない。ツールが読む")]
    [SerializeField] private GameObject[] keep;
    [Tooltip("ゲート一式の下にあっても、中身として扱う物（Gate の Content Roots から引き継いだ ContentRoot など）。ツールが読み書きする")]
    [SerializeField] private GameObject[] content;
    [SerializeField] private bool debugLog;

    private bool[] _savedMute;
    private bool[] _savedRenderer;
    private bool[] _savedCanvas;
    private bool _applied;
    private bool _allowed;

    void Start()
    {
        if (roots == null) roots = new GameObject[0];
        if (sources == null) sources = new AudioSource[0];
        hideRenderers = CompactRenderers(hideRenderers);
        hideCanvases = CompactCanvases(hideCanvases);
        _savedMute = new bool[sources.Length];
        _savedRenderer = new bool[hideRenderers.Length];
        _savedCanvas = new bool[hideCanvases.Length];
        Apply(false);
        if (gate != null) gate._RegisterListener(this);   // 登録した時点で一度 _OnGateUpdated が呼ばれる
    }

    /// <summary>ゲートから呼ばれる（評価のたびに来る）</summary>
    public void _OnGateUpdated()
    {
        Apply(gate != null && gate._IsLocalAllowed());
    }

    /// <summary>今、隠しているか（テストの検査から見る）</summary>
    public bool _IsHiding() { return _applied && !_allowed; }

    // 入れない間だけ、ほかの処理（Animator・Update・LateUpdate）が戻した表示を、最後に消し直す。入れる人には何もしない
    public override void PostLateUpdate()
    {
        if (!_applied || _allowed) return;
        for (int i = 0; i < hideRenderers.Length; i++)
            if (hideRenderers[i].enabled) hideRenderers[i].enabled = false;
        for (int i = 0; i < hideCanvases.Length; i++)
            if (hideCanvases[i].enabled) hideCanvases[i].enabled = false;
    }

    private void Apply(bool allowed)
    {
        if (_applied && allowed == _allowed)
        {
            // 入れない間は、動画プレイヤーなどが mute を戻しても、通知のたびにかけ直す
            if (!allowed)
                for (int i = 0; i < sources.Length; i++)
                    if (Utilities.IsValid(sources[i])) sources[i].mute = true;
            return;
        }

        for (int i = 0; i < roots.Length; i++)
            if (Utilities.IsValid(roots[i])) roots[i].SetActive(allowed);

        // 根に入らなかった物は、見た目だけ消す・戻す（入れないに変わった時点の表示を覚えておく）
        for (int i = 0; i < hideRenderers.Length; i++)
        {
            if (!allowed) { _savedRenderer[i] = hideRenderers[i].enabled; hideRenderers[i].enabled = false; }
            else if (_applied) hideRenderers[i].enabled = _savedRenderer[i];
        }
        for (int i = 0; i < hideCanvases.Length; i++)
        {
            if (!allowed) { _savedCanvas[i] = hideCanvases[i].enabled; hideCanvases[i].enabled = false; }
            else if (_applied) hideCanvases[i].enabled = _savedCanvas[i];
        }

        for (int i = 0; i < sources.Length; i++)
        {
            if (!Utilities.IsValid(sources[i])) continue;
            if (!allowed)
            {
                _savedMute[i] = sources[i].mute;
                sources[i].mute = true;
            }
            else if (_applied)
            {
                sources[i].mute = _savedMute[i];
            }
        }
        if (debugLog) Debug.Log("[SupporterContentGuard] " + (allowed ? "見せる" : "隠す") + " roots=" + roots.Length + " renderers=" + hideRenderers.Length + " canvases=" + hideCanvases.Length + " sources=" + sources.Length);
        _allowed = allowed;
        _applied = true;
    }

    // 毎フレームの消し直しで null を調べなくて済むよう、最初に無効な要素を除いておく
    private Renderer[] CompactRenderers(Renderer[] src)
    {
        if (src == null) return new Renderer[0];
        int n = 0;
        for (int i = 0; i < src.Length; i++) if (Utilities.IsValid(src[i])) n++;
        Renderer[] dst = new Renderer[n];
        n = 0;
        for (int i = 0; i < src.Length; i++) if (Utilities.IsValid(src[i])) dst[n++] = src[i];
        return dst;
    }

    private Canvas[] CompactCanvases(Canvas[] src)
    {
        if (src == null) return new Canvas[0];
        int n = 0;
        for (int i = 0; i < src.Length; i++) if (Utilities.IsValid(src[i])) n++;
        Canvas[] dst = new Canvas[n];
        n = 0;
        for (int i = 0; i < src.Length; i++) if (Utilities.IsValid(src[i])) dst[n++] = src[i];
        return dst;
    }
}
