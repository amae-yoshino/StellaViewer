# Windows x64 / Q8 の再構築と差し替え

対象はMagick.NET 15.0.0 / Magick.Native 2026.1010.1943に対応する外部部品です。
StellaViewer本体のソースは必要ありません。この手順と補助スクリプトは
`RelinkTools.LICENSE.txt`（MIT）で利用・変更・再配布できます。
外部ソースとその改変には、各部品の元のライセンスが適用されます。

## 前提

- Windows x64、PowerShell 7、Visual Studio 2026のC++ビルド環境とWindows SDK。
- MFCのx64/x86部品（`Microsoft.VisualStudio.Component.VC.ATLMFC`）。
- ソース・中間生成物用に十分な空き容量。ソースZIPだけで約300 MiBあり、
  展開・ビルド時にはさらに大きな領域を使用します。
- 実行時のStellaViewerには.NET 10 Desktop Runtime x64が必要です。

使用環境の例: Visual Studio Community 2026 18.11.12306.390、MSVC 14.51.36231、Windows SDK 10.0.26100.0。

## 別添ソース集からのビルド

1. ソース集ZIPを専用の短いパス（例 `D:\sv-sources`）へ展開します。
   ZIP内の44個の上流ソースZIPはそのまま残します。
2. PowerShell 7でソース集のルートへ移動し、次を実行します。

```powershell
./tools/Prepare-NativeSourceTree.ps1 `
    -SourceDirectory . `
    -ManifestPath ./Magick.Native.SourceManifest.json `
    -BuildDirectory D:\sv-rebuild
```

指定先は新規フォルダにしてください。既存のフォルダは上書きしません。
チェックサムを確認し、ZIP内の一段目を除いて、上流が想定する場所へ展開します。
既存Gitチェックアウトのリセット・削除や、ネットワークからの追加取得は行いません。
本構成は同梱44件のソースをビルド入力として使用します。
libjxlのWindows向け構成は同梱の別リポジトリのbrotli・highway・lcmsを参照し、
上流Gitサブモジュールを使う別構成は本手順の対象外です。

3. 必要な外部部品のソースを変更します。技術的な差し替え確認だけを再現する場合は、
   次の任意のテスト用変更を適用できます。

```powershell
./tools/Apply-NativeRelinkTestMarkers.ps1 -BuildDirectory D:\sv-rebuild
```

libheifのバージョン文字列へ目印を付け、Magick.Nativeにその文字列を取得する
`StellaViewer_RelinkProbe` を追加します。元の著作権・ライセンスのヘッダーは保持します。
StellaViewer本体や元の配布DLLへの変更ではありません。

4. 次を実行します。

```powershell
./tools/Build-NativeLibraries.ps1 -BuildDirectory D:\sv-rebuild
```

Configureをソースからビルドし、依存物38個、ImageMagick、Magick.Nativeの順に
ビルドします。Windows用の元の上流構成と同じx64 / Q8 / no HDRI / no OpenMP /
static linked runtimeを指定します。Configureに含まれるNASM 2.16.01も使います。
その利用条件は `NASM.BuildTool.LICENSE.txt` を参照してください。

依存物のビルドが成功した後の段階だけを再試行する場合は
`-ContinueAfterDependencies` を指定できます。依存物のソースを変更した場合は
この指定を使わず、依存物もビルドしてください。

生成するDLL:

```text
D:\sv-rebuild\Magick.Native\src\Magick.Native\bin\ReleaseQ8\x64\Magick.Native-Q8-x64.dll
```

任意のテスト用変更を適用した場合は、同梱の補助ツールで目印を確認できます。
この操作は、自分で再構築したDLLを実際に読み込みます。

```powershell
./tools/Test-NativeRelinkLibrary.ps1 -NativeLibraryPath D:\sv-rebuild\Magick.Native\src\Magick.Native\bin\ReleaseQ8\x64\Magick.Native-Q8-x64.dll
```

## アプリのコピーで差し替える

StellaViewerの配布フォルダを別の場所へコピーし、起動中でないことを確認してから、
コピー内の `Magick.Native-Q8-x64.dll` を生成したDLLへ置き換えます。
ファイル名、x64、Q8、Magick.NETの対応版を揃えてください。
本体を再コンパイルする必要はありません。元の配布物は保管してください。

テスト用変更を適用したDLLでは、`MagickNET.ImageMagickVersion` と
追加エクスポート `StellaViewer_RelinkProbe` から目印を確認できます。
目印は `1.23.6-stellaviewer-relink-test` です。変更したDLLを使用する前に、コピーしたアプリで画像表示を確認してください。
再構築したDLLが元のDLLとバイト単位で一致することや、任意の改変の動作を保証するものではありません。
アプリの通常配布には元の未改変DLLを使用します。
