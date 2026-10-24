# Nago SDK Consent Keeper

VRChat SDK の Build and Upload の前に出る著作権の同意（Copyright ownership agreement）を、Unity を起動し直しても覚えておくエディター拡張です。

## しくみ

- 同意は、最初に OK を押した時点で VRChat のサーバーに記録されます（ワールド・アバターの ID ごと）。SDK はそれとは別に「この起動中に同意した ID」を Unity の中に持ち、両方がそろわないと同意を求め直します（起動のたびに 1 回）。
- この拡張は、SDK の画面で本人が OK を押した ID を `UserSettings/NagoSdkConsentKeeper.txt` に覚え、次の起動で SDK の「この起動中に同意した ID」に戻します。
- 最初の 1 回は、本人が SDK の画面で OK を押します。押していない ID は覚えません。
- サーバーに本人の同意の記録が無い ID（別のアカウントでログインしたときなど）は、SDK が今までどおり同意を求めます。
- SDK の作りが変わったら、何もしなくなります（元どおり同意を求められるだけです）。

## 使い方

- 入れるだけで動きます。
- `Tools > Nago SDK Consent Keeper > Show Remembered Agreements`: 覚えている ID を見る
- `Tools > Nago SDK Consent Keeper > Forget Remembered Agreements`: 覚えた同意を消す（次の Build and Upload で、また同意を求められます）

`UserSettings/` は、ふつう git に入れないフォルダです。覚えた同意は、プロジェクトごと・使う人ごとになります。

## 履歴

- 0.1.0: 最初の版（VRChat SDK 3.10.5 の作りに合わせた）
