// NoticeTable.cs
// 通知や UI の文言の表。JSON（TextAsset）を 1 つ持ち、キーと言語から文を引く。
//
// JSON の形:
// {
//   "gate.approved": { "ja": "入場が許可されました", "en": "You have been approved.", "ko": "...", "zh-CN": "...", "zh-TW": "..." },
//   ...
// }
//
// 言語が無いときの落とし方（上から順に探す）:
//   1. 指定の言語そのもの（例 zh-TW）
//   2. zh で始まる言語は zh-CN → zh-TW → zh
//   3. ハイフンの前（例 pt-BR → pt）
//   4. en
//   5. ja
// どれも無ければ null を返す（呼び出し側がキーや既定の文に落とす）。

using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;

namespace NagoNotice
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NoticeTable : UdonSharpBehaviour
    {
        [Tooltip("文言の JSON。{ \"キー\": { \"ja\": \"…\", \"en\": \"…\", \"ko\": \"…\", \"zh-CN\": \"…\", \"zh-TW\": \"…\" } }")]
        [SerializeField] private TextAsset json;

        private DataDictionary _dict;
        private bool _parsed;

        private void EnsureParsed()
        {
            if (_parsed) return;
            _parsed = true;
            if (json == null)
            {
                Debug.LogWarning("[NoticeTable] json が未設定です: " + gameObject.name);
                return;
            }
            DataToken root;
            if (!VRCJson.TryDeserializeFromJson(json.text, out root) || root.TokenType != TokenType.DataDictionary)
            {
                Debug.LogError("[NoticeTable] JSON を読めません: " + gameObject.name);
                return;
            }
            _dict = root.DataDictionary;
        }

        /// <summary>キーがこの表にあるか</summary>
        public bool _Has(string key)
        {
            EnsureParsed();
            if (_dict == null || key == null) return false;
            return _dict.ContainsKey(key);
        }

        /// <summary>キーと言語から文を引く。キーが無ければ null</summary>
        public string _Get(string key, string lang)
        {
            EnsureParsed();
            if (_dict == null || key == null) return null;
            DataToken entryToken;
            if (!_dict.TryGetValue(key, TokenType.DataDictionary, out entryToken)) return null;
            DataDictionary entry = entryToken.DataDictionary;

            string s;
            if (lang != null && lang.Length > 0)
            {
                s = Lookup(entry, lang);
                if (s != null) return s;
                if (lang.StartsWith("zh"))
                {
                    s = Lookup(entry, "zh-CN");
                    if (s != null) return s;
                    s = Lookup(entry, "zh-TW");
                    if (s != null) return s;
                    s = Lookup(entry, "zh");
                    if (s != null) return s;
                }
                int dash = lang.IndexOf('-');
                if (dash > 0)
                {
                    s = Lookup(entry, lang.Substring(0, dash));
                    if (s != null) return s;
                }
            }
            s = Lookup(entry, "en");
            if (s != null) return s;
            return Lookup(entry, "ja");
        }

        private string Lookup(DataDictionary entry, string lang)
        {
            DataToken v;
            if (!entry.TryGetValue(lang, TokenType.String, out v)) return null;
            string s = v.String;
            if (s == null || s.Length == 0) return null;
            return s;
        }
    }
}
