# 外部ライブラリとライセンス

基準: 0.2.0-preview.13 / Windows x64。ライセンス本文の正式な対象範囲・例外は、必ず `licenses/` の原文を確認してください。本体のMITライセンスは第三者コードをMITに変更しません。

## アプリの直接・主要な推移依存

| 部品 | 固定版・来歴 | 条件／通知 | 用途 |
|---|---|---|---|
| Magick.NET-Q8-x64 / Magick.NET.Core | 15.0.0 | Apache-2.0。`Magick.NET.Copyright.txt`、`Magick.NET.Native.Notice.txt` | 一般画像・色変換・アニメーション |
| ImageMagick / Magick.Native | ImageMagick 7.1.2-33、Native 2026.1010.1943。固定コミット・DLLハッシュはSourceManifest | ImageMagickライセンスとdelegateごとの条件。下表・原文参照 | ネイティブ画像処理 |
| SharpCompress | 0.50.4 | MIT。`SharpCompress.LICENSE.txt` | ZIP／7z／RAR読み取り |
| PDFtoImage | 5.4.0 | MIT。`PDFtoImage.LICENSE.txt` | PDFレンダリングAPI |
| SkiaSharp / NativeAssets.Win32 | 4.150.1 | MITおよびnative第三者条件。`SkiaSharp.LICENSE.txt`、`SkiaSharp.Native.Notices.txt` | PDFの画素処理 |
| bblanchon.PDFium.Win32 | 152.0.7961 | パッケージ表記Apache-2.0、バイナリ配布元MIT、PDFium等の原文を全て保持。`PDFium.Binaries.LICENSE.txt`、`licenses/PDFium/` | PDFiumエンジン |
| bzPSD | commit `06b3f52dd77a42a20328dd315ab5939a2d846c64` | BSD-3-Clause。`third_party/bzPSD/LICENSE`、`bzPSD.SourceNotice.txt` | PSDの画素変換。固定ソースを同梱 |
| SQLite | 3.54.0、amalgamation ZIPのSHA256固定 | public domain。`SQLite.PublicDomain.txt` | CLIP保存済み画像の読み取り。C++部品へ静的リンク |
| clipparse | commit `322a273c0e059d1dee9132af78289efa12a59522` | MIT。`clipparse.LICENSE.txt` | CLIPの構造・クエリの参照 |
| .NET Windows x64 apphost | 10.0.13 | MITおよび同梱の第三者通知。`DotNET.AppHost.*` | EXEランチャー。.NET Desktop Runtime自体は別途導入 |
| Microsoft C++ Runtime | 使用したVS 2026のRelease構成へ静的リンク | Microsoftの再配布条件。`Microsoft.VC.Runtime.md` | 独自ネイティブ部品 |

## ImageMagickのネイティブ依存

以下は保存済みの**上流SBOM申告値**です。SBOM自体にはImageMagick本体版などの未置換プレースホルダーがあり、StellaViewer独自の完全な最終バイナリSBOMではありません。個々のバージョン・代替ライセンス・ファイル単位の例外を独立に全件再認定した一覧ではありません。実リンクライブラリ一覧は `Magick.Dependencies.BuildLibraries.txt`（38ライブラリ名）、通知は `Magick.NET.Native.Notice.txt`、固定ソースは `Magick.Native.SourceManifest.json` です。

| 部品 | 上流SBOMの版 | 上流SBOMのライセンス表記 |
|---|---|---|
| aom | 3.15.1 | BSD-2-Clause |
| brotli | 1.2.0 | MIT |
| libbzip2 | 1.0.8 | bzip2-1.0.6 |
| cairo | 1.18.6 | MPL-1.1 |
| libcroco | 0.6.13 | LGPL-2.0-or-later |
| libde265 | 1.1.3 | LGPL-3.0-or-later |
| openexr | 3.5.2 | BSD-3-Clause |
| libffi | 3.8.0 | MIT |
| freetype | 2.14.3 | FTL |
| fribidi | 1.0.17 | LGPL-2.1-or-later |
| gdk-pixbuf | 2.44.8 | LGPL-2.1-or-later |
| glib | 2.64.3 | LGPL-2.1-or-later |
| harfbuzz | 14.6.0 | MIT-Modern-Variant |
| libheif | 1.23.6 | LGPL-3.0-or-later |
| libhwy | 1.4.0 | Apache-2.0 |
| imath | 3.2.3 | BSD-3-Clause |
| libjpeg-turbo | 3.2.0 | BSD-3-Clause |
| libjxl | 0.12.0 | BSD-3-Clause |
| lcms | 2.19.1 | MIT |
| liblqr | 0.4.2 | LGPL-3.0-or-later |
| liblzma | 5.8.4 | LGPL-2.1-or-later |
| openh264 | 2.6.0 | BSD-2-Clause |
| openjpeg | 2.5.4 | BSD-2-Clause |
| openjph | 0.32.0 | BSD-2-Clause |
| pango | 1.45.3 | LGPL-2.0-only |
| pixman | 0.46.4 | MIT |
| libpng | 1.6.59 | libpng-2.0 |
| libraqm | 0.11.0 | MIT |
| libraw | 0.22.2 | LGPL-2.1-or-later |
| librsvg | 2.40.20 | LGPL-2.0-or-later |
| libtiff | 4.7.2 | libtiff |
| libwebp | 1.6.0 | BSD-3-Clause |
| libxml2 | 2.15.4 | MIT |
| libzip | 1.11.4 | BSD-3-Clause |
| zlib | 1.3.2 | Zlib |
| zstd | 1.5.7 | BSD-3-Clause |
| fontconfig | 2.18.3 | MIT-Modern-Variant |

上表のcairo等には代替条件、liblzma等にはファイルごとの条件があり、短いSPDX表記だけで判断しないでください。SBOMに載る部品と実リンク名は1対1ではありません。未使用のcoderをポリシーで禁止していても、同梱・静的リンクしたコードの通知義務が消えるわけではありません。

## 外部ソースと再構築資料

画像ライブラリの対応ソースと再構築・差し替え資料は、同じ配布場所の `StellaViewer-0.2.0-preview.13-external-sources.zip` に含まれます。通常の閲覧にはダウンロード不要です。

詳細は[ソースガイド](../licenses/Magick.Native.SourceGuide.md)と[再構築資料](../licenses/Magick.Native.Rebuild.md)を参照してください。補助スクリプトのライセンスは `licenses/RelinkTools.LICENSE.txt`、再構築用ツールNASMの通知は `licenses/NASM.BuildTool.LICENSE.txt` にあります。
