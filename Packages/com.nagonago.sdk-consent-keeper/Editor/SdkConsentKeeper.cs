// SdkConsentKeeper.cs
// VRChat SDK の Build and Upload の前に出る著作権の同意（Copyright ownership agreement）を、Unity を起動し直しても覚えておく。
//
// SDK の作り（3.10.5 の VRCCopyrightAgreement.cs）:
//   同意は、最初に OK を押した時点で VRChat のサーバーに記録される（ワールド・アバターの ID ごと）。
//   SDK はそれとは別に、「この起動中に同意した ID」を SessionState（Unity を閉じると消える）に持ち、
//   サーバーの記録と両方がそろわないと同意を求め直す。
// この拡張は、SDK が SessionState に入れた ID を UserSettings/ に写しておき、次の起動で SessionState に戻す。
//   - 最初の 1 回は、本人が SDK の画面で OK を押す（押していない ID は覚えない）
//   - サーバーに本人の同意の記録が無い ID は、SDK が今までどおり同意を求める（ログインしている人ごとに確かめるため）
//   - SDK の作り（SessionState の名前）が変わったら、何もしなくなる（元どおり同意を求められるだけ）
//   Tools > Nago SDK Consent Keeper > Forget Remembered Agreements で、覚えた同意を消す（この起動中の分も消す）

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SdkConsentKeeper
{
    // SDK が「この起動中に同意した ID」を ; 区切りで持つ SessionState の名前（VRCCopyrightAgreement.CONTENT_LIST_KEY と同じ）
    public const string SessionKey = "VRCSdkControlPanel.CopyrightAgreement.ContentList";
    private const string MenuRoot = "Tools/Nago SDK Consent Keeper/";
    private static string _lastSeen;
    private static double _nextCheck;

    // プロジェクトごと・使う人ごとの置き場（UserSettings/ は、ふつう git に入れない）
    public static string StorePath => Path.Combine(Directory.GetCurrentDirectory(), "UserSettings", "NagoSdkConsentKeeper.txt");

    static SdkConsentKeeper()
    {
        int restored = Restore();
        if (restored > 0) Debug.Log("[SdkConsentKeeper] 前に同意した " + restored + " 件を、この起動でも同意済みにしました（サーバーに同意の記録がある物だけが通ります）");
        EditorApplication.update += Watch;
    }

    /// <summary>覚えた ID を SessionState に戻す。戻した数を返す</summary>
    public static int Restore()
    {
        List<string> session = Split(SessionState.GetString(SessionKey, ""));
        List<string> add = Load().Where(id => !session.Contains(id)).ToList();
        if (add.Count > 0) SessionState.SetString(SessionKey, string.Join(";", session.Concat(add)));
        _lastSeen = SessionState.GetString(SessionKey, "");
        return add.Count;
    }

    /// <summary>SDK が SessionState に足した ID を、覚える。足した数を返す</summary>
    public static int Remember()
    {
        string now = SessionState.GetString(SessionKey, "");
        _lastSeen = now;
        List<string> stored = Load();
        List<string> add = Split(now).Where(id => !stored.Contains(id)).ToList();
        if (add.Count == 0) return 0;
        Save(stored.Concat(add));
        Debug.Log("[SdkConsentKeeper] 著作権の同意を覚えました: " + string.Join(", ", add));
        return add.Count;
    }

    // 同意は SDK の画面で押されるので、数秒おきに SessionState を見て、変わっていたら覚える
    private static void Watch()
    {
        if (EditorApplication.timeSinceStartup < _nextCheck) return;
        _nextCheck = EditorApplication.timeSinceStartup + 3;
        if (SessionState.GetString(SessionKey, "") != _lastSeen) Remember();
    }

    [MenuItem(MenuRoot + "Show Remembered Agreements", false, 1)]
    private static void Show()
    {
        List<string> ids = Load();
        EditorUtility.DisplayDialog("Nago SDK Consent Keeper",
            ids.Count == 0 ? "覚えている同意はありません。" : "覚えている同意（" + ids.Count + " 件）:\n" + string.Join("\n", ids), "OK");
    }

    [MenuItem(MenuRoot + "Forget Remembered Agreements", false, 2)]
    private static void ForgetMenu()
    {
        if (!EditorUtility.DisplayDialog("Nago SDK Consent Keeper", "覚えた著作権の同意を消します。\n次の Build and Upload で、また同意を求められます。", "消す", "やめる")) return;
        Debug.Log("[SdkConsentKeeper] 覚えた同意を消しました（" + Forget() + " 件）");
    }

    /// <summary>覚えた同意を消す。この起動中に SDK が持っている分も消す（次のアップロードで、また同意を求められる）。消した数を返す</summary>
    public static int Forget()
    {
        int count = Load().Count;
        if (File.Exists(StorePath)) File.Delete(StorePath);
        SessionState.EraseString(SessionKey);
        _lastSeen = "";
        return count;
    }

    private static List<string> Load()
    {
        if (!File.Exists(StorePath)) return new List<string>();
        return File.ReadAllLines(StorePath).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
    }

    private static void Save(IEnumerable<string> ids)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
        File.WriteAllLines(StorePath, ids.Distinct());
    }

    private static List<string> Split(string s)
    {
        return (s ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
    }
}
#endif
