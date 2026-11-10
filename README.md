# Japanese IME for Beat Saber (1.40.8)

Beat Saber のゲーム内キーボードで、Windows の日本語 IME を使えるようにする BSIPA プラグインです。
曲検索・ゲスト名・マルチプレイのサーバー設定など、`InputFieldView` を使う入力欄が対象です。

## できること

- 入力欄を開いている間だけ Windows IME を有効にし、閉じると自動で元に戻します。
- ローマ字入力 → ひらがな → 漢字変換を、いつもの Windows IME（Microsoft IME / Google 日本語入力など）で行えます。
- 変換中の文字列（未確定文字）をゲーム内の入力欄にそのまま表示します。
- 変換候補の一覧をゲーム内の正面に表示します。選択中の候補は色付きで強調され、複数ページある場合はページ番号も表示されます。
- `Space` で変換、`Enter` で確定、`Esc` で変換取消、`Backspace` で編集。
- `Ctrl+V` でクリップボードの日本語を貼り付け（設定で無効化可能）。
- 入力欄の文字数上限を尊重します。

## 必要環境

| 項目 | 内容 |
| --- | --- |
| Beat Saber | **1.40.8** (PC / Steam・Oculus・BSManager) |
| Mod loader | BSIPA 4.3.6 以上 |
| OS | Windows 10 / 11 |
| 入力 | 物理キーボード + 日本語 IME |
| 表示 | 日本語フォント。**SiraLocalizer** を入れると確実です |

> Beat Saber の標準フォントは日本語グリフを含まない場合があります。
> 日本語が □ になる場合は SiraLocalizer（日本語ローカライズ MOD）を入れてください。
> この 1.40.8 環境では SiraLocalizer 2.0.3 で動作確認しています。

## インストール

1. Beat Saber を終了します。
2. `JapaneseIME.dll` を `Beat Saber\Plugins\` にコピーします。
3. ゲームを起動します。BSIPA のログに次のように出ればロード成功です。

```text
[Japanese IME] JapaneseIME started: Windows IME input is enabled for in-game text fields.
```

BSManager を使っている場合は、対象インスタンスの `Plugins` フォルダにコピーしてください。

## 使い方

1. 曲検索など、ゲーム内の文字入力欄を開きます。
2. Windows IME を日本語入力モードにします（`半角/全角` キー、または `Alt` + `` ` ``）。
3. ローマ字で入力します。変換中の文字は入力欄に表示されます。
4. `Space` で変換します。候補一覧がゲーム内の正面に表示されるので、候補を選んで `Enter` で確定します。
5. ゲーム内キーボードの OK / 検索ボタンで決定します。

例: `nihongo` → `Space` → `Enter` で「日本語」。

### ゲーム内キーボード（ポインタ入力）は対象外

ゲーム内キーボードのキーをポインタで押した場合の**文字入力は対象外**です。
ゲームの UI をクリックするたびに Windows は IME の変換中文字列を確定してしまうため、
ローマ字を積み上げてかな・漢字に変換することができません（1 クリック = 1 文字で確定）。

FPFC（起動引数 `fpfc`）またはデスクトップミラーで、**物理キーボード**から入力してください。

### 日本語入力モード切替キー

ゲーム内キーボードの `OK` の右（`0` の左）に、IME の入力モードを切り替えるキーを 1 つ追加します。
既存キーの置き換えはしません。

| ラベル | タップしたとき |
| --- | --- |
| `ひらがな` | `半角英数` へ切り替え |
| `半角英数` | `ひらがな` へ切り替え |

- ラベルは **実際の Windows IME の状態**を 0.5 秒ごとに読み直して表示します（`次:` は次のモード）。
- このキーはモードを切り替えるだけで、**文字入力は一切行いません**。
- このキーで選んだモードは、入力欄を開き直しても維持されます
  （以降は `ForceJapaneseInput` の自動適用を行いません）。

### VR での注意

Windows の IME 候補ウィンドウ自体はデスクトップ（モニター）に表示され、VR ヘッドセット内には出ません。
この MOD は Windows IME から同じ候補一覧を読み取り、ゲーム内の正面に表示します
（`ShowCandidatesInGame`、既定で有効）。選択中の候補とページ番号も VR 内で確認できます。
ページ移動は IME のページ送りキー（Microsoft IME では `PageDown` / `PageUp` など）で行えます。
入力欄自体にも変換中の文字が表示されるため、確定後の文字は VR 内でも確認できます。

## 設定

初回起動時に `UserData\Japanese IME.json` が生成されます。

```json
{
  "Enabled": true,
  "EnableForPasswordFields": false,
  "EnableClipboardPaste": true,
  "PlaceCandidateWindowNearField": true,
  "VerboseLogging": false,
  "SelfTestMode": false,
  "ForceJapaneseInput": true,
  "ReapplyOnFocus": true,
  "ShowImeModeKey": true,
  "ShowCandidatesInGame": true,
  "ExtraKeyWidthScale": 1.8
}
```

| キー | 既定値 | 説明 |
| --- | --- | --- |
| `Enabled` | `true` | MOD 全体の有効/無効 |
| `EnableForPasswordFields` | `false` | パスワード系入力欄でも IME を有効にする |
| `EnableClipboardPaste` | `true` | `Ctrl+V` 貼り付けを許可 |
| `PlaceCandidateWindowNearField` | `true` | IME 候補ウィンドウを入力欄の近くに置く |
| `VerboseLogging` | `false` | 変換文字列などをログに出す（トラブルシュート用） |
| `ForceJapaneseInput` | `true` | 入力欄を開いたとき Windows IME をひらがなモードへ切り替える（モード切替キーで選んだ後は適用しない） |
| `ReapplyOnFocus` | `true` | ゲームにフォーカスが戻ったとき日本語モードを再適用する |
| `SelfTestMode` | `false` | 入力欄なしで IME イベントをログに出す診断モード。`F9` で入力欄の一覧と書き込みテストを実行 |
| `ShowImeModeKey` | `true` | ゲーム内キーボードに IME モード切替キーを追加する |
| `ShowCandidatesInGame` | `true` | 変換候補の一覧をゲーム内の正面に表示する（選択中の候補を強調） |
| `ExtraKeyWidthScale` | `1.8` | 追加キーの幅（通常キーに対する倍率。1.0〜3.0） |

### 「a」が半角の「a」になる場合

`ForceJapaneseInput` が `true` でも IME が英数モードのまま残ることがあります（Windows 側の IME 状態）。
その場合は入力欄を開いた状態で **`Ctrl` + `Shift` + `J`** を押すと日本語モードを再適用します。

`ForceJapaneseInput` が `true` のときは、IME が日本語モードになったことを確認するまで 0.75 秒間隔で自動再試行します。
英語をそのまま入力したい場合は `ForceJapaneseInput` を `false` にしてください。

### 診断モード（`SelfTestMode`）

`SelfTestMode` を `true` にして起動すると、入力欄を開かなくても IME の変換・確定イベントがログに記録されます。

- `F9` を押すと、メモリ上にある `InputFieldView` を列挙し、アクティブな入力欄へ「日本語テスト」を書き込んで結果をログに出します。
- 入力欄まわりの不具合を切り分けるとき以外は `false` のままにしてください。

## 動作の仕組み

- `HMUI.InputFieldView.ActivateKeyboard` / `DeactivateKeyboard` を Harmony でフックし、入力欄がアクティブな間だけ IME を ON にします。
- 入力欄を開いた直後に Windows IME を日本語（`IME_CMODE_NATIVE | IME_CMODE_FULLSHAPE | IME_CMODE_ROMAN`）へ切り替えます。Unity は IME の「入力受付」を有効にするだけで、かな入力モードまでは選ばないため、この処理がないと `a` が半角の `a` のままになります。
- Unity Input System の `Keyboard.onIMECompositionChange` で変換中文字列を取得し、`Keyboard.onTextInput` で確定文字を取得します。
- 変換中はゲームの入力欄表示に未確定文字を反映し、確定時にゲーム標準の `onValueChanged` を呼ぶことで、検索などのゲーム側処理に確定文字を渡します。
- Input System が利用できない場合は、従来の `Input.compositionString` / `Input.inputString` にフォールバックします。
- 旧 `VRTextEntryController` + `UIKeyboard` を使う画面にも対応しています。
- ゲーム内キーボードのキー入力は横取りしません（ポインタ入力は対象外）。追加したモード切替キーだけが
  ゲームの UI から `Button.onClick` を受け取り、IME のモードを切り替えます。

## ビルド

```powershell
dotnet build .\Japanese_IME.csproj -c Release
```

BSMT を使う場合:

```powershell
bsmt build .\Japanese_IME.csproj -c Release --package
```

`bin\Release\zip\` に配布用 ZIP が生成されます。

## トラブルシューティング

| 症状 | 確認すること |
| --- | --- |
| 日本語が入力できない | ゲームウィンドウにフォーカスがあるか。Windows IME が日本語モードか。 |
| 日本語が □ になる | SiraLocalizer を入れる。 |
| 変換候補が見えない | `ShowCandidatesInGame` が `true` か。`Space` で変換して候補一覧が出ているか。`VerboseLogging` を `true` にすると候補数のログが出る。 |
| 入力欄を開いても IME がオンにならない | `UserData\Japanese IME.json` の `Enabled` が `true` か。BSIPA ログに `activated for ...` が出るか。 |
| 特定の入力欄だけで動かない | その画面が `InputFieldView` を使っているか。パスワード欄は既定で対象外。 |

ログは `Beat Saber\Logs\` に出力されます。`VerboseLogging` を `true` にすると変換内容が記録されます。

## 既知の制限

- Windows IME の候補ウィンドウ自体は VR 空間には描画されません（OS の制約）。同じ内容を
  `ShowCandidatesInGame` でゲーム内に表示します。
- ゲーム内キーボードのポインタによる文字入力は対象外です。ゲーム側の UI クリックで IME の変換中文字列が
  確定してしまうため、FPFC / デスクトップミラー + 物理キーボードで操作してください。
- Beat Saber のバージョンが変わると `InputFieldView` の API が変わる可能性があります。対象は 1.40.8 です。

## アンインストール

`Plugins\JapaneseIME.dll` を削除し、必要なら `UserData\Japanese IME.json` も削除してください。
