# JapaneseIME

[![Build](https://github.com/denpadokei/JapaneseIME/actions/workflows/build.yml/badge.svg)](https://github.com/denpadokei/JapaneseIME/actions/workflows/build.yml)

Beat Saber のゲーム内テキスト入力で Windows の日本語 IME を使えるようにする BSIPA プラグインです。曲検索・ゲスト名・マルチプレイのサーバー設定など、`InputFieldView` を使う入力欄が対象で、日本語での曲検索（ひらがな／カタカナを区別しない部分一致）にも対応します。

## 機能

- 入力欄を開いている間だけ Windows IME を有効にし、閉じると元に戻す
- ローマ字 → ひらがな → 漢字変換を、いつもの Windows IME（Microsoft IME / Google 日本語入力など）で実行
- 変換中の未確定文字をゲーム内の入力欄にそのまま表示し、確定時にゲーム標準の `onValueChanged` を呼ぶ
- 変換候補の一覧をゲーム内の正面に表示（選択中の候補を強調、ページ番号も表示）
- ゲーム内キーボード（ポインタ入力）のローマ字をかなに変換し、`Space` で漢字に変換
- ゲーム内キーボードに IME の入力モード切替キー（`ひらがな` / `半角英数`）を追加
- `Space` で変換、`Enter` で確定、`Esc` で変換取消、`Backspace` で編集、`Ctrl+V` で貼り付け
- 曲検索で日本語の部分一致検索（ひらがな／カタカナ、全角／半角英数を同一視）
- BetterSongList と併用可能（必須ではありません）

## 動作環境

| 項目 | 内容 |
| --- | --- |
| Beat Saber | 1.40.8 |
| 依存 MOD | BSIPA 4.3.6 / BeatSaberMarkupLanguage 1.12.5 |
| OS | Windows 10 / 11 |
| 入力 | 物理キーボード + 日本語 IME（ゲーム内キーボードのみでも可） |
| 表示 | 日本語フォント。日本語が □ になる場合は SiraLocalizer などを導入 |

## インストール

1. Beat Saber を 1.40.8 に合わせる (BSManager などのバージョン管理ツールを推奨)
2. BSIPA と BeatSaberMarkupLanguage をインストールする
3. GitHub Actions の **Build** ワークフローの Artifact をダウンロードし、`Plugins` フォルダをゲームフォルダへ上書きコピーする

`Plugins\JapaneseIME.dll` が配置され、BSIPA のログに次のように出ればロード成功です。

```text
JapaneseIME started: Windows IME input is enabled for in-game text fields.
```

配布用に zip 化する場合の名前は `JapaneseIME-1.4.0-bs1.40.8.zip` です。

## 使い方

1. 曲検索など、ゲーム内の文字入力欄を開きます。
2. Windows IME を日本語入力モードにします（`半角/全角` キー、または `Alt` + `` ` ``）。
3. ローマ字で入力します。変換中の文字は入力欄に表示されます。
4. `Space` で変換し、候補一覧から選んで `Enter` で確定します。
5. ゲーム内キーボードの OK / 検索ボタンで決定します。

例: `nihongo` → `Space` → `Enter` で「日本語」。

### ゲーム内キーボード（ポインタ入力）

ゲーム内キーボードのキーをポインタで押した場合も、ローマ字がかなに変換され、`Space` で漢字に変換できます。

1. `nihongo` とタップ → 入力欄に `にほんご` が入ります。
2. `Space` をタップ → 漢字（例: `日本語`）に変換され、候補一覧が表示されます。
3. もう一度 `Space` をタップすると、漢字 → カタカナ → ひらがな の順に候補が切り替わります。
4. `OK` / 検索ボタンで確定します。候補表示中に別のキーを押すと、その候補を確定して入力を続けます。

ゲームはキー入力を入力欄へ直接書き込み、UI をクリックするたびに Windows IME の変換中文字列を確定してしまうため、Windows IME の変換は使えません。そのため、かなを漢字に変換する候補はオンライン API と内蔵辞書から集めます。

- 候補は次の順で集めます（重複は除去、最大 8 件）。
  1. オンライン API（Google CGI API for Japanese Input）… 既読の読みはキャッシュ。結果が届くと候補一覧が自動で更新されます。
  2. 内蔵の JMdict 由来辞書 … オフライン時や API が使えないときのフォールバック（例: `かみ` → `神` / `紙` / `髪`）
- オンライン API は読み（かな）を `https://www.google.com/transliterate` へ HTTPS で送ります。送信を避けたい場合は `UseOnlineDictionary` を `false` にしてください。
- 変換はすべて非同期です。候補はすぐに表示され、オンライン API の結果が届くと一覧が自動で更新されます。
- かなに変換するのは、IME が日本語モードのときだけです。`半角英数` モードでは従来どおり半角の英数が入ります。
- この動作は `ConvertOnScreenKeyboard` で無効にできます。

### 日本語での曲検索

- 曲名・サブタイトル・アーティスト名の部分一致で絞り込めます。
- ひらがなとカタカナは同じ文字として扱います（例: `よる` で `ヨル` を含む曲名もヒットします）。
- 全角英数は半角として扱います。
- 漢字の読み（ふりがな）検索には対応しません。曲名に含まれる文字で検索してください。
- BetterSongList が入っている場合も動作します。BetterSongList の検索パッチをリフレクションで検出し、日本語検索時だけ干渉しないように調整します（BetterSongList.dll への参照はありません）。

### 入力モード切替キー

ゲーム内キーボードの `OK` の右（`0` の左）に、IME の入力モードを切り替えるキーを 1 つ追加します。既存キーの置き換えはしません。

| ラベル | タップしたとき |
| --- | --- |
| `ひらがな` | `半角英数` へ切り替え |
| `半角英数` | `ひらがな` へ切り替え |

- ラベルは実際の Windows IME の状態を 0.5 秒ごとに読み直して表示します。
- このキーはモードを切り替えるだけで、文字入力は一切行いません。
- このキーで選んだモードは、入力欄を開き直しても維持されます。

### 「a」が半角の「a」になる場合

`ForceJapaneseInput` が `true` でも IME が英数モードのまま残ることがあります（Windows 側の IME 状態）。その場合は入力欄を開いた状態で `Ctrl` + `Shift` + `J` を押すと日本語モードを再適用します。

## 設定

初回起動時に `UserData/Japanese IME.json` が生成されます。

| 項目 | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| Enabled | bool | true | MOD 全体の有効／無効 |
| EnableForPasswordFields | bool | false | パスワード系入力欄でも IME を有効にする |
| EnableClipboardPaste | bool | true | `Ctrl+V` 貼り付けを許可 |
| PlaceCandidateWindowNearField | bool | true | IME 候補ウィンドウを入力欄の近くに置く |
| VerboseLogging | bool | false | 変換文字列などをログに出す（トラブルシュート用） |
| SelfTestMode | bool | false | 入力欄なしで IME イベントをログに出す診断モード。`F9` で入力欄の一覧と書き込みテストを実行 |
| ForceJapaneseInput | bool | true | 入力欄を開いたとき Windows IME をひらがなモードへ切り替える |
| ReapplyOnFocus | bool | true | ゲームにフォーカスが戻ったとき日本語モードを再適用する |
| ShowImeModeKey | bool | true | ゲーム内キーボードに IME モード切替キーを追加する |
| ShowCandidatesInGame | bool | true | 変換候補の一覧をゲーム内の正面に表示する |
| ConvertOnScreenKeyboard | bool | true | ゲーム内キーボードのローマ字をかなに変換し、`Space` で漢字に変換する |
| UseOnlineDictionary | bool | true | 変換候補をオンライン API から取得する。失敗時は内蔵の JMdict 辞書を使う |
| SimulatePhysicalKeyboard | bool | false | ゲーム内キーボードのキーを物理キーの信号として送る（試験用） |
| ExtraKeyWidthScale | float | 1.8 | 追加キーの幅（通常キーに対する倍率。1.0〜3.0） |

入力欄まわりの不具合を切り分けるときは `VerboseLogging` と `SelfTestMode` を有効にしてください。ログは `Beat Saber/Logs/` に出力されます。

## ビルド

```powershell
git clone https://github.com/denpadokei/JapaneseIME.git
cd JapaneseIME
dotnet build .\Japanese_IME.csproj -c Release
```

`BeatSaberDir` に Beat Saber のインストール先を設定すると、ゲームの参照アセンブリを解決できます。

GitHub Actions の `build.yml` が push ごとにビルドし、`bin/Release/Artifact` を `JapaneseIME.zip` として Artifact にアップロードします（リリースは作成しません）。`Directory.Build.props` の `GameVersion` と `DependsOn` を CI が読み、BeatMods から 1.40.8 の参照アセンブリと BSIPA / BSML を取得します。

## ライセンス

ライセンスファイルはありません。

同梱するかな漢字辞書データは JMdict（EDRDG）由来で **CC BY-SA 4.0** です。詳細は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。
