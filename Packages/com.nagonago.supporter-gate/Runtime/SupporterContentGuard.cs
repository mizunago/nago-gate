// SupporterContentGuard.cs
// 入れない人（支援者・住人でない人）の画面で、ワールド本体を見せない・聞かせない。
// あやしいけんきゅうじょの NonResidentGuard（2026-10-06・07）を、パッケージに取り込んだもの。
//
// 状態は 3 つ。ゲートの通知（_OnGateUpdated）を受けて、変わったときだけ切り替える。
//   見せる   : 入れる人。何もしない（roots を一度も止めない。起動時に止めて戻すと、音源の空間化などが崩れることがあるため。0.9.1）
//   確かめ中 : リストを読み込み中で、入れるかがまだ決まっていない。見た目（Renderer・Canvas の enabled）と音（mute）だけ消す。
//              roots の下の見た目も rootRenderers・rootCanvases で消し、SetActive はしない
//   入れない : 入れないと決まった。roots を SetActive(false) で止め、見た目と音を消したままにする
// 入れない人の画面では、Animator・スクリプト・動画プレイヤーが表示や mute を戻すことがあるので、PostLateUpdate で毎フレーム消し直す。
// 入れるようになったら、隠し始めた時点の値（Renderer・Canvas の enabled、mute）に戻す。
//
// Gate の contentRoots との違い: ゲートは毎秒すべての根を SetActive し直す。ここでは、状態が変わったときだけ切り替える。
// 同期する物・ほかのギミックが切り替える物・音源を含む部分は、roots にしない（エディタのツールが選ぶ。見た目と音だけ消す）。
// 何を隠すかは、エディタのツール（Tools > SupporterGate > Set Up Content Guard (auto)）が選んで入れる。
// 入口の部屋・通知の板・ゲート一式・keep に入れた物は、隠さない。

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
[DefaultExecutionOrder(100)]   // ゲート（既定の順番）の Start のあとに動く。持ち主だけのゲートなら、その時点で判定が決まっている
public class SupporterContentGuard : UdonSharpBehaviour
{
    private const int StateNone = 0;
    private const int StateShown = 1;
    private const int StatePending = 2;
    private const int StateDenied = 3;

    [SerializeField] private SupporterGate gate;
    [Tooltip("入れないと決まったら、GameObject ごと止める物（同期する物・ほかのギミックが切り替える物・音源を含まない部分木の根）")]
    [SerializeField] private GameObject[] roots;
    [Tooltip("roots の下の Renderer。確かめ中は、止めずに見た目だけ消す")]
    [SerializeField] private Renderer[] rootRenderers;
    [Tooltip("roots の下の Canvas。確かめ中は、止めずに見た目だけ消す")]
    [SerializeField] private Canvas[] rootCanvases;
    [Tooltip("入れない間、mute にする音源")]
    [SerializeField] private AudioSource[] sources;
    [Tooltip("入れない間、見た目だけ消す Renderer（roots に入らなかった、同期する物・動く物・切り替えられる物）")]
    [SerializeField] private Renderer[] hideRenderers;
    [Tooltip("入れない間、見た目だけ消す Canvas")]
    [SerializeField] private Canvas[] hideCanvases;
    [Tooltip("自動の選び方で、隠さない物（入口の部屋をゲート一式の外に作った場合など）。この下は、隠す物にも、音を消す物にもしない。ツールが読む")]
    [SerializeField] private GameObject[] keep;
    [Tooltip("ゲート一式の下にあっても、中身として扱う物（Gate の Content Roots から引き継いだ ContentRoot など）。ツールが読み書きする")]
    [SerializeField] private GameObject[] content;
    [SerializeField] private bool debugLog;

    private bool[] _savedMute;
    private bool[] _savedRenderer;
    private bool[] _savedCanvas;
    private bool[] _savedRootRenderer;
    private bool[] _savedRootCanvas;
    private int _state = StateNone;
    private bool _visualsHidden;
    private bool _rootsStopped;
    private int _rootToggles;   // roots を SetActive した回数（テストの検査から見る）
    private int _hideCount;     // 見た目と音を消し始めた回数（テストの検査から見る）

    void Start()
    {
        if (roots == null) roots = new GameObject[0];
        sources = CompactSources(sources);
        hideRenderers = CompactRenderers(hideRenderers);
        hideCanvases = CompactCanvases(hideCanvases);
        rootRenderers = CompactRenderers(rootRenderers);
        rootCanvases = CompactCanvases(rootCanvases);
        _savedMute = new bool[sources.Length];
        _savedRenderer = new bool[hideRenderers.Length];
        _savedCanvas = new bool[hideCanvases.Length];
        _savedRootRenderer = new bool[rootRenderers.Length];
        _savedRootCanvas = new bool[rootCanvases.Length];
        if (gate != null) gate._RegisterListener(this);   // 登録した時点で一度 _OnGateUpdated が呼ばれる
        else ApplyState(StatePending);                    // ゲートが無ければ、決まらないので隠したままにする
    }

    /// <summary>ゲートから呼ばれる（評価のたびに来る）</summary>
    public void _OnGateUpdated()
    {
        if (gate == null) return;
        int next = gate._IsLocalAllowed() ? StateShown : gate._IsLocalDecided() ? StateDenied : StatePending;
        ApplyState(next);
    }

    /// <summary>今、隠しているか（テストの検査から見る）</summary>
    public bool _IsHiding() { return _state == StatePending || _state == StateDenied; }
    public int _GetState() { return _state; }
    public int _GetRootToggles() { return _rootToggles; }
    public int _GetHideCount() { return _hideCount; }

    // 隠している間だけ、ほかの処理（Animator・Update・LateUpdate・動画プレイヤー）が戻した表示と音を、最後に消し直す。入れる人には何もしない
    public override void PostLateUpdate()
    {
        if (!_visualsHidden) return;
        for (int i = 0; i < hideRenderers.Length; i++)
            if (hideRenderers[i].enabled) hideRenderers[i].enabled = false;
        for (int i = 0; i < hideCanvases.Length; i++)
            if (hideCanvases[i].enabled) hideCanvases[i].enabled = false;
        if (!_rootsStopped)
        {
            for (int i = 0; i < rootRenderers.Length; i++)
                if (rootRenderers[i].enabled) rootRenderers[i].enabled = false;
            for (int i = 0; i < rootCanvases.Length; i++)
                if (rootCanvases[i].enabled) rootCanvases[i].enabled = false;
        }
        for (int i = 0; i < sources.Length; i++)
            if (!sources[i].mute) sources[i].mute = true;
    }

    private void ApplyState(int next)
    {
        if (next == _state) return;
        if (next == StateShown)
        {
            if (_rootsStopped) SetRoots(true);
            if (_visualsHidden) ShowVisuals();
        }
        else
        {
            if (!_visualsHidden) HideVisuals();
            if (next == StateDenied && !_rootsStopped) SetRoots(false);
            else if (next == StatePending && _rootsStopped) SetRoots(true);   // 決まっていた判定が、読み直しで未判定に戻ったとき（ふつうは起きない）
        }
        if (debugLog)
        {
            string label = next == StateShown ? "見せる" : next == StatePending ? "確かめ中（見た目と音だけ消す）" : "入れない（止める）";
            Debug.Log("[SupporterContentGuard] " + label + " roots=" + roots.Length + " rootRenderers=" + rootRenderers.Length + " renderers=" + hideRenderers.Length
                + " canvases=" + hideCanvases.Length + " sources=" + sources.Length);
        }
        _state = next;
    }

    private void SetRoots(bool active)
    {
        for (int i = 0; i < roots.Length; i++)
            if (Utilities.IsValid(roots[i])) roots[i].SetActive(active);
        _rootsStopped = !active;
        _rootToggles++;
    }

    // 隠し始めた時点の表示と mute を覚えて、消す
    private void HideVisuals()
    {
        for (int i = 0; i < hideRenderers.Length; i++) { _savedRenderer[i] = hideRenderers[i].enabled; hideRenderers[i].enabled = false; }
        for (int i = 0; i < hideCanvases.Length; i++) { _savedCanvas[i] = hideCanvases[i].enabled; hideCanvases[i].enabled = false; }
        for (int i = 0; i < rootRenderers.Length; i++) { _savedRootRenderer[i] = rootRenderers[i].enabled; rootRenderers[i].enabled = false; }
        for (int i = 0; i < rootCanvases.Length; i++) { _savedRootCanvas[i] = rootCanvases[i].enabled; rootCanvases[i].enabled = false; }
        for (int i = 0; i < sources.Length; i++) { _savedMute[i] = sources[i].mute; sources[i].mute = true; }
        _visualsHidden = true;
        _hideCount++;
    }

    private void ShowVisuals()
    {
        for (int i = 0; i < hideRenderers.Length; i++) hideRenderers[i].enabled = _savedRenderer[i];
        for (int i = 0; i < hideCanvases.Length; i++) hideCanvases[i].enabled = _savedCanvas[i];
        for (int i = 0; i < rootRenderers.Length; i++) rootRenderers[i].enabled = _savedRootRenderer[i];
        for (int i = 0; i < rootCanvases.Length; i++) rootCanvases[i].enabled = _savedRootCanvas[i];
        for (int i = 0; i < sources.Length; i++) sources[i].mute = _savedMute[i];
        _visualsHidden = false;
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

    private AudioSource[] CompactSources(AudioSource[] src)
    {
        if (src == null) return new AudioSource[0];
        int n = 0;
        for (int i = 0; i < src.Length; i++) if (Utilities.IsValid(src[i])) n++;
        AudioSource[] dst = new AudioSource[n];
        n = 0;
        for (int i = 0; i < src.Length; i++) if (Utilities.IsValid(src[i])) dst[n++] = src[i];
        return dst;
    }
}
