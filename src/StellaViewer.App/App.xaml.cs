using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        if(e.Args.Length==1 && e.Args[0]==DecodeWorker.Argument) {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Shutdown(await DecodeWorker.RunAsync()); return;
        }
        base.OnStartup(e);
        try { ImageDecoderSecurity.Initialize(); }
        catch (Exception error) {
            MessageBox.Show("画像読み込みの保護設定を初期化できませんでした。\n" + error.Message, "StellaViewer");
            Shutdown(1); return;
        }
        if(e.Args.Length==3 && e.Args[0]=="--ui-loading-safety-test") {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try {
                var window=new MainWindow(false); await window.VerifyLoadingSafetyAsync(e.Args[1], e.Args[2]);
                window.Close(); await window.TemporaryCleanup; Shutdown(0);
            }
            catch(Exception error) { await File.AppendAllTextAsync(e.Args[2], "FAIL: "+error); Shutdown(1); }
            return;
        }
        if(e.Args.Length==3 && e.Args[0]=="--ui-loupe-test") {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try {
                var window=new MainWindow(false){ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
                window.Show();await window.VerifyLoupeMatrixAsync(e.Args[1],e.Args[2]);window.Close();await window.TemporaryCleanup;
                await File.AppendAllTextAsync(e.Args[2],"PASS: loupe pixels, rotation, zoom, grid, spread, alpha and settings");Shutdown(0);
            }
            catch(Exception error){await File.AppendAllTextAsync(e.Args[2],"FAIL: "+error);Shutdown(1);}
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--ui-security-test") {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { var window = new MainWindow(false); await window.VerifySecurityRecoveryAsync(e.Args[1]); window.Close(); await window.TemporaryCleanup; await File.WriteAllTextAsync(e.Args[2], "PASS: native decoder policy, rejected-input navigation and settings recovery notification"); Shutdown(0); }
            catch (Exception error) { await File.WriteAllTextAsync(e.Args[2], "FAIL: " + error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--ui-data-tools-test") {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { var window = new MainWindow(false); await window.VerifyDataToolsAsync(e.Args[1], e.Args[2]); window.Close(); await window.TemporaryCleanup; await File.WriteAllTextAsync(e.Args[2], "PASS: multiple selection, operation handoff, cache management, settings transfer and history controls"); Shutdown(0); }
            catch (Exception error) { await File.WriteAllTextAsync(e.Args[2], "FAIL: " + error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--ui-slideshow-test") {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { var window = new MainWindow(false); await window.VerifySlideshowAsync(e.Args[1]); window.Close(); await window.TemporaryCleanup; await File.WriteAllTextAsync(e.Args[2], "PASS: slideshow timer, pause, manual paging, wrap, failed images, spreads, settings and cleanup"); Shutdown(0); }
            catch (Exception error) { await File.WriteAllTextAsync(e.Args[2], "FAIL: " + error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--ui-list-layout-test") {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { var window = new MainWindow(false); await window.VerifyListSettingsAsync(); await window.VerifyTileLayoutAsync(); window.Close(); await window.TemporaryCleanup; await File.WriteAllTextAsync(e.Args[2], "PASS: rendered filenames and tile column layout during pane resizing"); Shutdown(0); }
            catch (Exception error) { await File.WriteAllTextAsync(e.Args[2], "FAIL: " + error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--ui-list-settings-test") {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { var window = new MainWindow(false); await window.VerifyListSettingsAsync(); await window.VerifyTileScrollingAsync(); window.Close(); await window.TemporaryCleanup; await File.WriteAllTextAsync(e.Args[2], "PASS: list font, wheel amount, partial deltas, Apply/reset and bottom scrolling"); Shutdown(0); }
            catch (Exception error) { await File.WriteAllTextAsync(e.Args[2], "FAIL: " + error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--ui-tile-scroll-test") {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { var window = new MainWindow(false); await window.VerifyTileScrollingAsync(); window.Close(); await window.TemporaryCleanup; await File.WriteAllTextAsync(e.Args[2], "PASS: tile wheel scrolling from bottom with short viewports"); Shutdown(0); }
            catch (Exception error) { await File.WriteAllTextAsync(e.Args[2], "FAIL: " + error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--ui-sizing-test") {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { var window = new MainWindow(false); await window.VerifyUiSizingAsync(e.Args[1], e.Args[2]); window.Close(); await window.TemporaryCleanup; await File.WriteAllTextAsync(e.Args[2], "PASS: UI and font sizing, physical image zoom, fullscreen overlays, dialog sizing, reset and restoration"); Shutdown(0); }
            catch (Exception error) { await File.WriteAllTextAsync(e.Args[2], "FAIL: " + error); Shutdown(1); }
            return;
        }
        if(e.Args.Length==3&&e.Args[0]=="--ui-archive-cache-test"){
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{var window=new MainWindow(false);await window.VerifyArchiveCacheAsync(e.Args[1]);window.Close();await window.TemporaryCleanup;await File.WriteAllTextAsync(e.Args[2],"PASS: archive and nested cache, prefetch, F5, search, spread, zero limits, failures and temporary release");Shutdown(0);}
            catch(Exception error){await File.WriteAllTextAsync(e.Args[2],"FAIL: "+error);Shutdown(1);}return;
        }
        if(e.Args.Length==3&&e.Args[0]=="--ui-pdf-cache-test"){
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{var window=new MainWindow(false);await window.VerifyPdfCacheAsync(e.Args[1]);window.Close();await window.TemporaryCleanup;await File.WriteAllTextAsync(e.Args[2],"PASS: physical, archive and nested PDF cache, prefetch, F5, filtering, zero limits and temporary cleanup");Shutdown(0);}
            catch(Exception error){await File.WriteAllTextAsync(e.Args[2],"FAIL: "+error);Shutdown(1);}return;
        }
        if(e.Args.Length==3&&e.Args[0]=="--ui-mouse-test"){
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{var window=new MainWindow(false);await window.VerifyMouseAsync(e.Args[1],e.Args[2]+".png");window.Close();await window.TemporaryCleanup;await File.WriteAllTextAsync(e.Args[2],"PASS: mouse assignments, click and drag priority, wheel, settings Apply/Cancel/reset and themes");Shutdown(0);}
            catch(Exception error){await File.WriteAllTextAsync(e.Args[2],"FAIL: "+error);Shutdown(1);}return;
        }
        if(e.Args.Length==2&&e.Args[0]=="--ui-menu-render-test"){
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{var window=new MainWindow(false){ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};window.Show();await window.VerifyMenuRenderingAsync(e.Args[1]);window.Close();await window.TemporaryCleanup;await File.WriteAllTextAsync(e.Args[1],"PASS: repeated menu opening, nested popups, themes and auto/software rendering");Shutdown(0);}
            catch(Exception error){await File.WriteAllTextAsync(e.Args[1],"FAIL: "+error);Shutdown(1);}return;
        }
        if(e.Args.Length==2&&e.Args[0]=="--ui-about-test"){
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{var window=new MainWindow(false){ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};window.Show();window.VerifyAbout(e.Args[1]+".png");window.Close();await window.TemporaryCleanup;await File.WriteAllTextAsync(e.Args[1],"PASS: About information, lowercase architecture, Windows details, themes and copy");Shutdown(0);}
            catch(Exception error){await File.WriteAllTextAsync(e.Args[1],"FAIL: "+error);Shutdown(1);}return;
        }
        if(e.Args.Length==3&&e.Args[0]=="--ui-metadata-test"){
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{var window=new MainWindow(false);await window.VerifyMetadataAsync(e.Args[1],e.Args[2]+".png");window.Close();await window.TemporaryCleanup;await File.WriteAllTextAsync(e.Args[2],"PASS: metadata sidebar, supported tools, cache hits, navigation cancellation, reload, archives, nested archives and themes");Shutdown(0);}
            catch(Exception error){await File.WriteAllTextAsync(e.Args[2],"FAIL: "+error);Shutdown(1);}return;
        }
        if(e.Args.Length==4&&e.Args[0]=="--instance-probe"){
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            await File.WriteAllTextAsync(e.Args[3],InstanceRouter.Forward(e.Args[2].Length==0?null:e.Args[2],e.Args[1]).ToString());Shutdown(0);return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--ui-preferences-test") {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try {
                var window = new MainWindow(false);
                await window.VerifyPreferencesRepeatedAsync(e.Args[1]);
                window.Close(); await window.TemporaryCleanup;
                await File.WriteAllTextAsync(e.Args[2], "PASS: repeated preferences persistence"); Shutdown(0);
            }
            catch (Exception error) { await File.WriteAllTextAsync(e.Args[2], "FAIL: " + error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--ui-performance-test") {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try {
                var window = new MainWindow(false); await window.MeasurePerformanceAsync(e.Args[1], e.Args[2]);
                window.Close(); await window.TemporaryCleanup; Shutdown(0);
            }
            catch (Exception error) { await File.WriteAllTextAsync(e.Args[2], "FAIL: " + error); Shutdown(1); }
            return;
        }
        if(e.Args.Length==3&&e.Args[0]=="--ui-tray-test"){
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{var window=new MainWindow(false);await window.VerifyTrayAsync(e.Args[1],e.Args[2]+".png");await window.VerifyInstancesAsync(e.Args[1]);window.Close();await window.TemporaryCleanup;await File.WriteAllTextAsync(e.Args[2],"PASS: tray registration, restore, minimize preference, callbacks, multiple windows, cleanup, persistence and cross-process instance routing");Shutdown(0);}
            catch(Exception error){await File.WriteAllTextAsync(e.Args[2],"FAIL: "+error);Shutdown(1);}return;
        }
        if(e.Args.Length==3&&e.Args[0]=="--ui-deletion-test"){
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{var window=new MainWindow(false);await window.VerifyDeletionAsync(e.Args[1],e.Args[2]+".png");window.Close();await window.TemporaryCleanup;await File.WriteAllTextAsync(e.Args[2],"PASS: deletion settings, confirmation, folder and file recycle, undo, navigation and themes");Shutdown(0);}
            catch(Exception error){await File.WriteAllTextAsync(e.Args[2],"FAIL: "+error);Shutdown(1);}return;
        }
        if(e.Args.Length==3&&e.Args[0]=="--ui-registration-order-test"){
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{var window=new MainWindow(false);await window.VerifyRegistrationOrderAsync(e.Args[1],e.Args[2]);window.Close();await window.TemporaryCleanup;await File.WriteAllTextAsync(e.Args[2],"PASS: toolbar fit/100 percent toggle restoration, manual/automatic zoom and pan, manual changes, reset/reload, size limits, resize, rotation; favorites/folder link Up/Down buttons, selection, endpoints, sidebar and persisted order, shared removal, dark/light editors");Shutdown(0);}
            catch(Exception error){await File.WriteAllTextAsync(e.Args[2],"FAIL: "+error);Shutdown(1);}return;
        }
        if(e.Args.Length==3&&e.Args[0]=="--ui-location-restore-test"){
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try{var window=new MainWindow(false);await window.VerifyLocationScrollAsync(e.Args[1],e.Args[2]+".scroll.txt");await window.VerifyFastLocationsAsync(e.Args[1],e.Args[2]);
                await window.VerifyLocationNavigationRegressionsAsync(e.Args[1],e.Args[2]);
                await File.AppendAllTextAsync(e.Args[2],"\nPASS: network restrictions, nested archive/PDF Back and Up, live folder monitoring regression checks");window.Close();await window.TemporaryCleanup;Shutdown(0);}
            catch(Exception error){await File.WriteAllTextAsync(e.Args[2],"FAIL: "+error);Shutdown(1);}return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--ui-requested-features-test")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try { var window=new MainWindow(false);await window.VerifyRequestedFeaturesAsync(e.Args[1],e.Args[2]+".png");await window.VerifyLocationScrollAsync(e.Args[1],e.Args[2]+".scroll.txt");window.Close();await window.TemporaryCleanup;
                await File.WriteAllTextAsync(e.Args[2],"PASS: descending lists, optional enlargement, settings Apply/Cancel, resizable/restored/fullscreen information panel, LNK resolution and registration drops");Shutdown(0); }
            catch(Exception error){await File.WriteAllTextAsync(e.Args[2],"FAIL: "+error);Shutdown(1);}return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--ui-page-navigation-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try {
                var window = new MainWindow(false);
                await window.VerifyPageNavigationAsync(e.Args[1], e.Args[2] + ".png");
                window.Close(); await window.TemporaryCleanup;
                await File.WriteAllTextAsync(e.Args[2], "PASS: first/last menus, assigned shortcuts, filtered/sorted/empty lists, spread boundaries, archive navigation and PDF boundaries");
                Shutdown(0);
            }
            catch (Exception error) { await File.WriteAllTextAsync(e.Args[2], "FAIL: " + error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--ui-smoke-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var window = new MainWindow(false);
                await window.VerifyAsync(e.Args[1], e.Args[2] + ".png");
                await File.WriteAllTextAsync(e.Args[2], "PASS: independent location display modes, archive previews and tile virtualization, startup restoration, close-time save flushing, Apply and Cancel, zoom alignment, high-quality reduction, pane drags, dark tree, fullscreen overlays, menus, history, search, cancellation");
                window.Close(); await window.TemporaryCleanup; Shutdown(0);
            }
            catch (Exception error) { await File.WriteAllTextAsync(e.Args[2], "FAIL: " + error); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--smoke-test")
        {
            // Explicit test mode: initializes compiled XAML and WPF rendering without showing a window.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var resultPath = Path.GetFullPath(e.Args[2]);
            try
            {
                _ = new MainWindow(false);
                var decoded = await new MagickImageDecoder().DecodeAsync(e.Args[1], CancellationToken.None);
                var source = BitmapPresenter.Create(decoded);
                var visual = new Image { Source = source, Width = decoded.Width, Height = decoded.Height };
                visual.Measure(new Size(decoded.Width, decoded.Height));
                visual.Arrange(new Rect(0, 0, decoded.Width, decoded.Height));
                var rendered = new RenderTargetBitmap(decoded.Width, decoded.Height, 96, 96, PixelFormats.Pbgra32);
                rendered.Render(visual);
                var bytes = new byte[checked(decoded.Width * decoded.Height * 4)];
                rendered.CopyPixels(bytes, decoded.Stride, 0);
                if (!bytes.Any(value => value != 0))
                    throw new InvalidDataException("WPF rendering returned an empty image.");
                await File.WriteAllTextAsync(resultPath, $"PASS: WPF XAML, native decode, render {decoded.Width}x{decoded.Height}");
                Shutdown(0);
            }
            catch (Exception error)
            {
                await File.WriteAllTextAsync(resultPath, "FAIL: " + error);
                Shutdown(1);
            }
            return;
        }
        MainWindow main;
        using(var startupGate=new Mutex(false,@"Local\"+InstanceRouter.Scope+".Startup")){
            var entered=false;
            try{
                try{entered=startupGate.WaitOne(TimeSpan.FromSeconds(10));}catch(AbandonedMutexException){entered=true;}
                if(!entered){MessageBox.Show("別の起動処理が実行中です。少し待ってから再度起動してください。","StellaViewer");Shutdown(1);return;}
                var allowMultiple=true;try{allowMultiple=PreferencesStore.Load().AllowMultipleInstances;}catch{/* MainWindow reports settings read errors. */}
                if(!allowMultiple){
                    var delivery=InstanceRouter.Forward(e.Args.Length==1?e.Args[0]:null);
                    if(delivery!=InstanceDelivery.None){if(delivery==InstanceDelivery.Unavailable)MessageBox.Show("既存のウィンドウが操作中か、応答していません。操作完了後に再度開いてください。","StellaViewer");Shutdown(delivery==InstanceDelivery.Delivered?0:1);return;}
                }
                main=new MainWindow();main.Show();
            }finally{if(entered)startupGate.ReleaseMutex();}
        }
        await(main.InitialStartup=main.RestoreStartupAsync(e.Args.Length == 1 ? e.Args[0] : null));
    }
}
