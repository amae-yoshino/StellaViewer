# 画像ライブラリの対応ソースと差し替え

対象: Windows x64 / Magick.NET-Q8-x64 15.0.0 / Magick.Native 2026.1010.1943。
対応する外部ソースと再構築資料は、StellaViewerと同じ配布場所の external-sources.zip に含まれます。
通常の画像閲覧には、このソース集のダウンロードは不要です。

## バイナリからソースへの対応

| 部品 | 固定した上流コミット |
| --- | --- |
| Magick.NET 15.0.0 | `79bb8700d7fe3a564f62926ab299fc2520cb2269` |
| Magick.Native 2026.1010.1943 | `4dcdad28530c14c8d35687fdeb1ecc8d39b01e30` |
| ImageMagick 7.1.2-33 | `211e5b975a0019a88dcef5f84c1193f46738d815` |
| ImageMagick/Dependencies 2026.10.09.2225 | `f586cbad542b5d7f7927c7ebac376e497c869131` |
| ImageMagick/Configure 2026.09.21.1729 | `08ea96dd18267b90a56204a598ba2b30db20c6b7` |

配布DLL `Magick.Native-Q8-x64.dll` のSHA256:
`E24D1B5ECF421C9A5EC4FD592B795DA228D7ADDE5964B1687BBCAF9C56151CC8`

対応付けの根拠は、固定したMagick.NETの
[`src/Magick.Native/metadata/libraries.md`](https://github.com/dlemstra/Magick.NET/blob/79bb8700d7fe3a564f62926ab299fc2520cb2269/src/Magick.Native/metadata/libraries.md)、
Magick.Nativeの[`ImageMagick.commit`](https://github.com/dlemstra/Magick.Native/blob/4dcdad28530c14c8d35687fdeb1ecc8d39b01e30/ImageMagick.commit)、
ImageMagickの[依存物取得スクリプト](https://github.com/ImageMagick/ImageMagick/blob/211e5b975a0019a88dcef5f84c1193f46738d815/.github/build/windows/download-dependencies.sh)、
Dependenciesの[固定コミット一覧](https://github.com/ImageMagick/Dependencies/blob/f586cbad542b5d7f7927c7ebac376e497c869131/clone-dependencies.sh)です。

`Magick.Native.SourceManifest.json` に、上記と依存物39件の固定コミットを記載しています。
39件はビルド入力の保守的な一覧であり、全件が配布DLLにリンクされている
との断定や、全件がLGPLとの分類ではありません。Windowsでもlinked-runtime構成では
`/includeNonWindows` が指定されるため、fontconfigも含めています。
任意のGPL部品は含めていません。WindowsのNativeラッパーがリンクする38個の
依存ライブラリの列挙は `src/Magick.Native/Stdafx.h` を参照してください。
fontconfigは構成ツールの通知生成用の入力として含まれます。配布DLLへのリンク対象であることを示すものではありません。

## ソース集の内容

上位5件と依存物39件、合計44件の固定ソースZIPを含みます。
`Magick.Native.SourceDownloads.json` に、取得元・固定コミット・ファイル名・容量・SHA256を記載しています。
ソース集の `download-receipt.json` も同じ一覧で、展開スクリプトがチェックサムの照合に使用します。
SHA256は内容の変更を検出するための値であり、上流による電子署名ではありません。

`Magick.Dependencies.Upstream.Sbom.json` は上流の部品表、
`Magick.Dependencies.BuildLibraries.txt` は依存ライブラリ一覧です。
これらは各部品のライセンス原文を置き換えません。

## 再構築と差し替え

対象部品を変更してMagick.Native全体を再構築し、アプリフォルダの
`Magick.Native-Q8-x64.dll` を交換できます。本体の再コンパイルは不要です。
依存物には静的リンクされるものがあるため、個別の依存DLLを置くだけでは差し替えになりません。

具体的な環境と手順は `Magick.Native.Rebuild.md` を参照してください。
再構築資料と補助スクリプトは `RelinkTools.LICENSE.txt`（MIT）で提供します。
外部ソースにはそれぞれのライセンスが適用されます。

StellaViewerの利用条件は、第三者ライセンスが認める差し替え・改変や、
そのデバッグに必要なリバースエンジニアリングを制限しません。
ライセンス原文は `Magick.NET.Native.Notice.txt` を参照してください。
