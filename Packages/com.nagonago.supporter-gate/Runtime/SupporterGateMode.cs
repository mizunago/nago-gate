// SupporterGateMode.cs
// SupporterGate の動作モード。UdonSharp はユーザー定義 enum を int として扱う。

public enum SupporterGateMode
{
    /// <summary>ゲートなし（クレジット・ランクタグのみ）</summary>
    Open = 0,
    /// <summary>requiredRank 以上の支援者のみ</summary>
    SupportersOnly = 1,
    /// <summary>支援者 ＋ 在室支援者が個別に許可した人</summary>
    SupporterApproval = 2,
    /// <summary>支援者が在室している間は誰でも</summary>
    SupporterPresence = 3,
}
