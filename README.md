# Codex Assistant 0.2.1 — Visual Studio 2022

Visual Studio 2022用の非公式VSIX拡張です。右側にドッキングするチャットウィンドウからCodex CLIに質問できます。コード範囲の選択は不要です。C# / WPF / AsyncPackage / .NET Framework 4.7.2で実装しています。

## 0.2.1: Astraの候補追加

モデル候補に `gpt-6-astra`（Astra）を追加しました。明示的モデル指定の模擬CLIテストにも追加し、VSIXを再生成しています。

## モデル選択（0.2.0以降）

ウィンドウ上部の「モデル」欄で候補を選ぶか、モデルIDを直接入力できます。

- **CLIの既定設定を使用**: `--model` を付けず、CLIの設定に従います。空欄も同じ扱いです。
- **モデルIDを指定**: `--model <ID>` を付けて実行します。候補は利用可能モデルの自動取得結果ではなく入力例です。利用可否はCLI・アカウントに依存し、不正なIDや利用不可のモデルはエラー表示します。
- **保存**または**Send**で、モデル選択と直近10件の入力履歴をVisual Studioの拡張設定に保存します。次回ウィンドウ作成時に復元します。`config.toml` は書き換えません。
- 実行中はモデル欄を固定し、応答履歴には送信時のモデル指定を表示します。既定設定の場合、CLIが最終的に選んだモデルの自動表示は未対応です。

既存版の更新は、Visual Studioを終了し、`CodexAssistant-0.2.1.vsix` を実行してください。拡張IDは0.1.0と同じです。

## この版でできること

- View → Codex Assistantで開くTool Window。初期位置はSolution Explorerと同じ右側のタブグループです。ドラッグして変更できます。
- チャット入力、Send、Ctrl+Enter、Cancel、履歴表示、New chat。
- モデル選択・ID直接入力・選択保存・最近使用したモデル一覧。
- 送信時にSolution、ActiveDocumentの所属Project、ファイル名、カーソルの行・列、周辺約120行、開いているドキュメント名を自動取得。
- 未保存の編集内容はActiveDocumentのエディターバッファから取得します。自動保存はしません。
- Error Listの先頭40件を可能な範囲で取得。Git status、未ステージ・ステージ済みdiffを取得。
- Contextタブで取得情報を確認。Context更新はプレビュー、Send時には最新情報を再取得します。
- 保存済みSolutionのあるディレクトリをCLIのWorkingDirectoryに設定。Codex自身が必要なファイルを読めます。
- 応答内の `diff` コードブロックをProposed diffタブに表示し、UTF-8のpatchファイルとして保存、Rejectで破棄。

**Applyは無効なボタンだけの骨組みです。自動適用、差分検証、VS標準比較エディターとの連携は未実装です。** CLIは読み取り専用で実行し、この拡張は提案を自動適用しません。

## 必要な環境

1. Windows x64、Visual Studio 2022 Community / Professional / Enterprise（17.x）。VSIXはamd64限定、Visual Studio 2026とARM64はインストール対象外です。
2. ビルドにはVisual Studio Installerの「.NET デスクトップ開発」「Visual Studio 拡張機能の開発」と.NET Framework 4.7.2 targeting packを用意してください。
3. NuGetへの接続。SDK 17.0.32112.339、VSSDK BuildTools 17.0.5232を固定しています。
4. Codex CLIのWindowsネイティブ実行ファイルと、CLIで事前に完了したログイン。CLI設定・認証は既存のユーザー環境を使用します。拡張内でキーやトークンは保存しません。
5. Gitコンテキストを使う場合はPATH上の `git.exe`。

## ビルド

このREADMEと同じフォルダーで実行します。

```powershell
.\build.ps1
```

またはVisual Studioで `CodexAssistant.sln` を開き、NuGet復元後にReleaseビルドします。Developer PowerShellの場合:

```powershell
MSBuild.exe .\CodexAssistant.sln /restore /m /p:Configuration=Release
.\tests\bin\Release\CodexAssistant.Tests.exe
```

生成先: `src\CodexAssistant\bin\Release\net472\CodexAssistant.vsix`。
VSIXのパッケージングにはフルMSBuildを使用してください。`dotnet build`だけでのVSIX生成は対象外です。

## インストール・使い方

1. Visual Studioを終了し、生成されたVSIXをダブルクリックしてVS2022にインストールします。
2. Visual Studioを開き、Tools → Options → Codex Assistant → Generalで **Codex executable** に `codex.exe` の絶対パスを設定します。PATH上の `codex.exe` も利用可能です。
3. その実行ファイルでCLIログインを完了してください。認証UIは拡張内にはありません。
4. 保存済みの `.sln` を開き、コードファイルの対象箇所にカーソルを置きます。
5. View → Codex Assistantを開き、「この関数を高速化して」などと入力してSend。コードを選択する必要はありません。
6. 提案がある場合はProposed diffで確認・保存できます。Applyは未実装です。

npm経由の `codex.cmd` / `codex.ps1` はこの版では直接実行しません。npmパッケージのvendorディレクトリに含まれるWindowsネイティブ `codex.exe` を指定してください。インストール先の確認例:

```powershell
$npmRoot = npm root -g
Get-ChildItem -LiteralPath (Join-Path $npmRoot '@openai') -Filter codex.exe -Recurse |
  Select-Object -ExpandProperty FullName
```

該当ファイルがなければ、利用中のCLI配布方法に従ってWindows版をインストールしてください。

## CLIとの連携

実行形式:

```text
codex.exe exec --sandbox read-only --skip-git-repo-check --color never --output-last-message <一時ファイル> -
```

質問・過去最大6往復・IDE情報をUTF-8標準入力へ送ります。プロンプトをシェルやコマンド引数に埋め込みません。標準出力と標準エラーを並行して読み取り、最終応答ファイルを表示後に削除します。CLIの非ゼロ終了、未ログイン、実行ファイル不在は履歴へエラー表示します。

既定タイムアウトは10分、Optionsで1〜60分に調整できます。CancelはWindowsのprocess tree終了を試み、失敗時にはCLIプロセスを終了します。CLI設定や接続済みツールの構成は利用中のCLI環境に依存します。

## 自動コンテキストの範囲と制限

| 情報 | 取得方法・制限 |
|---|---|
| Solution | DTE.Solution.FullName。未保存Solution、フォルダーだけを開くモードは対象外 |
| Project | ActiveDocument.ProjectItem.ContainingProject。所属しないファイルでは空欄 |
| ActiveDocument / Cursor | DTE TextDocument。デザイナー等では取得できない場合あり |
| 未保存コード | ActiveDocument周辺最大18,000文字。ほかの未保存ファイルは名前と未保存フラグのみ |
| Open files | DTE.Documentsの先頭60件 |
| Error List | DTE2.ToolWindows.ErrorListの先頭40件。全診断を保証せず、各説明500文字まで |
| Git | Solutionディレクトリでstatus/diffを実行。コマンドごと10,000文字、全体10秒まで。未追跡ファイル内容は含まない |
| 履歴 | メモリー内。直近6往復を再送（各質問6,000文字・応答12,000文字まで）。CLIセッションresumeは未実装 |

Solutionルートが変わった次の送信・Context更新時に履歴をクリアします。閉じて同じSolutionを再度開いた場合はルートが同じなので履歴は残ります。明示的に切り替えるにはNew chatを使ってください。

ContextはCLI経由でモデルへ送信されます。Error List/Git diffはOptionsで除外できます。大きなファイル・出力は切り詰めを表示します。ストリーミング描画、Markdownの装飾、履歴のディスク保存、インライン補完は未実装です。

## 構成・拡張ポイント

- `CodexPackage.cs`: パッケージ、メニュー、Options、右側Tool Window登録。
- `ContextCollector.cs`: UIスレッド上でDTEをスナップショット化。新しいコンテキストソースはここに追加。
- `CliRunner.cs`: プロセス実行、UTF-8入出力、キャンセル、Git情報取得。
- `ModelSelection.cs`: モデルID検証、既定設定の扱い、最近使用したモデル一覧。
- `ChatControl.xaml` / `.xaml.cs`: チャット、履歴の構築、Context表示、diff抽出・保存・破棄。
- `tests/`: アカウントを使用しない模擬CLI統合テスト。

Applyを実装する場合は、表示中の提案をSolutionルートと元ファイルのハッシュに結び付け、未保存バッファ・外部変更を検出してから適用するサービスを追加してください。パスのルート外参照、リンク、競合を検証し、VSのUndo/変更通知と連携する必要があります。現在のdiff抽出は表示専用で、適用可能性の検証は行いません。

## 検証記録（2026-09-06）

0.2.0では再コンパイル・VSIX再生成に成功。従来の模擬CLIテストに加え、明示的モデル指定3種類、既定選択への復帰、不正ID5種類の拒否、モデル履歴の重複除去と順序を検証しました。VS2022上での選択保存・再起動後の復元は実機未検証です。

- Visual Studio 2022 SDK 17.0.32112.339を直接参照したC# / WPFコンパイル: 成功。
- VSSDK BuildTools 17.0.5232でメニューコンパイル、pkgdef生成、VSIXパッケージ生成: 成功。
- 模擬CLIによる8項目: 成功。空・日本語・引用符・末尾バックスラッシュの引数、UTF-8標準入力と作業ディレクトリと最終応答、標準出力/エラーの並行読み取りと終了コード、8秒以内のキャンセル、cmdラッパー拒否を確認。
- この環境のユーザーNuGet.Configにアクセスできず、通常の `/restore` は完了していません。検証はNuGet公式配布のSDK参照とBuildToolsを作業フォルダーへ直接取得して実施しました。配布ソリューションの通常NuGet復元からの一括ビルドは別環境での確認が必要です。
- ビルド実行ツールはMSBuild 18です。VS2022実機でのインストール・画面操作、実アカウントでのCodex応答は未検証です。

実機確認: 起動位置、テーマ、カーソル移動後のContext、未保存バッファ、Solution切替、CLIログイン、キャンセル、diff保存を確認してください。

## 参考資料

- [OpenAI: Codex非対話実行](https://learn.chatgpt.com/docs/non-interactive-mode)
- [OpenAI: CLIコマンドリファレンス](https://learn.chatgpt.com/docs/developer-commands?surface=cli)
- [Microsoft: Tool Window拡張](https://learn.microsoft.com/en-us/visualstudio/extensibility/creating-an-extension-with-a-tool-window?view=vs-2022)

OpenAIやMicrosoftの公式拡張ではありません。
