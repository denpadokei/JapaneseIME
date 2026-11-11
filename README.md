# Japanese IME for Beat Saber (1.40.8)

Beat Saber のゲーム内キーボードで、Windows の日本語 IME を使えるようにする BSIPA プラグインです。
曲検索・ゲスト名・マルチプレイのサーバー設定など、`InputFieldView` を使う入力欄が対象です。

## できること

- 入力欄を開いている間だけ Windows IME を有効にし、閉じると自動で元に戻します。
- ローマ字入力 → ひらがな → 漢字変換を、いつもの Windows IME（Microsoft IME / Google 日本語入力など）で行えます。
- 変換中の文字列（未確定文字）をゲーム内の入力欄にそのまま表示します。
- ゲーム内キーボード（ポインタ入力）のローマ字も、IME が日本語モードなら、漢字変換まで行えます。
- 変換候補の一覧をゲーム内の正面に表示します。選択中の候補は色付きで強調され、複数ページある場合はページ番号も表示されます。
- `Space` で変換、`Enter` で確定、`Esc` で変換取消、`Backspace` で編集。
- `Ctrl+V` でクリップボードの日本語を貼り付け（設定で無効化可能）。
- 入力欄の文字数上限を尊重します。

## 必要環境

| 項目 | 内容 |
| --- | --- |
| Beat Saber | **1.40.8** (PC / Steam・Oculus・BSManager) |
| Mod loader | BSIPA 4.3.6 以上 |
| UI | Beat Saber Markup Language (BSML) **1.12.5 以上** |
| OS | Windows 10 / 11 |
| 入力 | 物理キーボード + 日本語 IME |
| 表示 | 日本語フォント。**SiraLocalizer** を入れると確実です |

> Beat Saber の標準フォントは日本語グリフを含まない場合があります。
> 日本語が □ になる場合は SiraLocalizer（日本語ローカライズ MOD）を入れてください。
> この 1.40.8 環境では SiraLocalizer 2.0.3 で動作確認しています。

## インストール

1. Beat Saber を終了します。
2. `JapaneseIME.dll` を `Beat Saber\Plugins\` にコピーします。
3. **BSML** (`BSML.dll`) が `Plugins` にない場合は導入します。Mod Assistant / BSManager では
   マニフェストの依存として自動導入されます。
4. ゲームを起動します。BSIPA のログに次のように出ればロード成功です。

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

### ゲーム内キーボード（ポインタ入力）

ゲーム内キーボードのキーをポインタで押した場合も、**Windows IME が日本語モードなら、ローマ字がかなに変換されて入力されます**。
`nihongo` と押すと `にほんご`、`gakkou` と押すと `がっこう` になります。
カタカナモードなら `ニホンゴ` のようにカタカナで入力されます。

ゲームはキー入力を入力欄へ直接書き込むため、この変換はプラグインが行います。
漢字への変換（`Space` での候補選択）は Windows IME を経由する物理キーボード入力でのみ行えます。

> `SimulatePhysicalKeyboard` を `true` にすると、押されたキーを物理キーボードの信号としてゲームへ送り直します。
> ただし Beat Saber は UI をクリックするたびに IME の変換中文字列を確定してしまうため、1 タップで 1 文字しか入らず、
> 読みをまとめて変換することはできません。通常は `false` のままにしてください。
> OS の候補ウィンドウはゲーム内表示と重複しないよう、常に抑制します。

- かなに変換するのは、IME が日本語モードのときだけです。`半角英数` モードでは従来どおり半角の英数が入ります。
- `Backspace` は、未確定のローマ字（例: `k`）があればそれを 1 文字消し、なければ入力済みの文字を消します。
- この動作は `ConvertOnScreenKeyboard` で無効にできます。

FPFC（起動引数 `fpfc`）またはデスクトップミラーで、**物理キーボード**を使うと漢字変換まで行えます。

FPFC（起動引数 `fpfc`）またはデスクトップミラーで、**物理キーボード**を使うと漢字変換まで行えます。

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

この MOD は Windows IME の候補一覧をゲームウィンドウの IME メッセージ（`WM_IME_NOTIFY`）から読み取り、
ゲーム内の正面に表示します（`ShowCandidatesInGame`、既定で有効）。
OS の候補ウィンドウはゲーム内表示と重複しないよう抑制します。
選択中の候補とページ番号も VR 内で確認できます。

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
  "ConvertOnScreenKeyboard": true,
  "SimulatePhysicalKeyboard": false,
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
| `ConvertOnScreenKeyboard` | `true` | ゲーム内キーボードのローマ字を、日本語モードのとき、かなに変換して入力する |
| `SimulatePhysicalKeyboard` | `false` | ゲーム内キーボードのキーを物理キーの信号として送る（試験用。UI クリックのたびに IME が確定するため通常は無効） |
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
- 変換候補の一覧は、ゲームウィンドウの `WM_IME_NOTIFY`（候補の開閉・変更）で `ImmGetCandidateList` から取得し、BSML の `FloatingScreen` 上に表示します。
- 取得した候補はページ単位で表示し、選択中の候補を強調します。OS の候補ウィンドウはデスクトップ側にそのまま残ります。
- Input System が利用できない場合は、従来の `Input.compositionString` / `Input.inputString` にフォールバックします。
- 旧 `VRTextEntryController` + `UIKeyboard` を使う画面にも対応しています。
- ゲーム内キーボードのキー入力は、IME が日本語モードのときだけ横取りし、物理キーの信号として送り直します
  （`SimulatePhysicalKeyboard`）。IME が反応しない場合はローマ字 → かな変換で入力欄へ書き込みます。
  追加したモード切替キーはゲームの UI から `Button.onClick` を受け取り、IME のモードを切り替えます。

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
| 変換候補が見えない | `ShowCandidatesInGame` が `true` か。`Space` で変換して候補一覧が出ているか。`VerboseLogging` を `true` にすると `IMe candidates:` のログが出る。 |
| 入力欄を開いても IME がオンにならない | `UserData\Japanese IME.json` の `Enabled` が `true` か。BSIPA ログに `activated for ...` が出るか。 |
| 特定の入力欄だけで動かない | その画面が `InputFieldView` を使っているか。パスワード欄は既定で対象外。 |

ログは `Beat Saber\Logs\` に出力されます。`VerboseLogging` を `true` にすると変換内容が記録されます。

## 既知の制限

- Windows IME の候補一覧は、OS の候補ウィンドウではなくゲームウィンドウの IME メッセージから取得します。
  OS 側の候補ウィンドウは表示されません（`ShowCandidatesInGame` を `false` にすると元に戻ります）。
- ゲーム内キーボードのポインタ入力は、内蔵のローマ字 → かな変換で「かな入力」までを行います。
  漢字への変換は Windows IME を経由する物理キーボード入力で行ってください。
- Beat Saber のバージョンが変わると `InputFieldView` の API が変わる可能性があります。対象は 1.40.8 です。

## アンインストール

`Plugins\JapaneseIME.dll` を削除し、必要なら `UserData\Japanese IME.json` も削除してください。
