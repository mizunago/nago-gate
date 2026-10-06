// SupporterTestPanelBuild.cs
// テスト用のパネル（SupporterTestPanel）を、シーンを使う場面に合わせて出し分ける。
//   Play モード        : 出す。誰でも使える（パネルの中の目印 PlayModeMark を表示にする）
//   SDK の Build & Test : 出す。Gate の Owner Display Names の人だけが使える（パネルの ownerOnly が ON のとき）
//   それ以外のビルド    : パネルごと取り除く（Build and Upload を含む。どちらか分からないときも取り除く）
// Build & Test かどうかは、SDK がビルドの前に立てる印（VRC_SdkBuilder.ActiveBuildType）で見る。
// シーンのファイルそのものは変えない（ビルドや Play モードのために読み込んだシーンだけを変える）。

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Udon;

public class SupporterTestPanelBuild : IProcessSceneWithReport
{
    public enum Kind { Remove, BuildAndTest, PlayMode }

    // Udon の前処理（callbackOrder 0）より先に行う
    public int callbackOrder => -100;

    public const string PlayModeMarkName = "PlayModeMark";

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        Kind kind = Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ? Kind.PlayMode
            : IsBuildAndTest() ? Kind.BuildAndTest
            : Kind.Remove;
        int n = Apply(scene, kind);
        if (n > 0 && kind != Kind.PlayMode) Debug.Log("[SupporterGate] テスト用のパネル " + n + " 個: " + (kind == Kind.Remove ? "公開用のビルドなので取り除きました" : "Build & Test なので出します（持ち主の名前のときだけ使えます）"));
    }

    /// <summary>シーンの中のテスト用のパネルを、kind に合わせて出す・取り除く。戻り値は扱ったパネルの数</summary>
    public static int Apply(Scene scene, Kind kind)
    {
        List<UdonBehaviour> panels = FindPanels(scene);
        foreach (UdonBehaviour ub in panels)
        {
            if (kind == Kind.Remove)
            {
                UnityEngine.Object.DestroyImmediate(ub.gameObject);
                continue;
            }
            // Udon の変数は、Play モードでは読み込みの順番によって届かないことがあるので、目印の表示で伝える
            Transform mark = ub.transform.Find(PlayModeMarkName);
            if (mark != null) mark.gameObject.SetActive(kind == Kind.PlayMode);
            ub.gameObject.SetActive(true);
        }
        return panels.Count;
    }

    /// <summary>SDK が今、Build & Test のビルドをしているか。SDK の作りが変わって読めないときは false（取り除く側に倒す）</summary>
    public static bool IsBuildAndTest()
    {
        try
        {
            Type builder = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                builder = asm.GetType("VRC.SDKBase.Editor.VRC_SdkBuilder", false);
                if (builder != null) break;
            }
            if (builder == null) return false;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            object value = null;
            PropertyInfo prop = builder.GetProperty("ActiveBuildType", flags);
            if (prop != null) value = prop.GetValue(null);
            else
            {
                FieldInfo field = builder.GetField("ActiveBuildType", flags);
                if (field != null) value = field.GetValue(null);
            }
            return value != null && value.ToString() == "Test";
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SupporterGate] Build & Test かどうかを読めませんでした。テスト用のパネルは取り除きます: " + ex.Message);
            return false;
        }
    }

    private static List<UdonBehaviour> FindPanels(Scene scene)
    {
        List<UdonBehaviour> found = new List<UdonBehaviour>();
        UdonSharp.UdonSharpProgramAsset program = UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(SupporterTestPanel));
        if (program == null || !scene.IsValid()) return found;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (UdonBehaviour ub in root.GetComponentsInChildren<UdonBehaviour>(true))
            {
                if (ub.programSource == program) found.Add(ub);
            }
        }
        return found;
    }

}
#endif
