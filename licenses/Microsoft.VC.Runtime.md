# Microsoft C++ランタイム

StellaViewerのCLIP読み取り・Windowsファイル操作用部品には、Microsoft C++ランタイムが静的リンクされています。
Windows付属のUCRT・システムDLLも使用します。通常の利用にVisual StudioやMFC開発環境は不要です。
コンパイラ、SDK、MFC開発部品、デバッグ用ランタイムはアプリに同梱していません。

Microsoftの部品にはMicrosoftの利用条件が適用されます。本体のMITライセンスはこれを置き換えません。
静的リンクされたランタイムの更新は、StellaViewerの更新版に含まれます。
公式の利用条件と再配布の説明は次を参照してください。

- [Visual Studio Community 2026の利用条件](https://visualstudio.microsoft.com/license-terms/vs2026-ga-community/)
- [Visual Studio 2026の再配布対象一覧](https://learn.microsoft.com/en-us/visualstudio/releases/2026/redistribution)
- [Microsoft C++の配置方法・静的リンク](https://learn.microsoft.com/en-us/cpp/windows/deployment-in-visual-cpp?view=msvc-170)
- [Visual C++ファイルの再配布](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files?view=msvc-170)
