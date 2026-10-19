using System.Windows;
using System.Windows.Controls;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

// A recycled visual owns only its current thumbnail; there is no unbounded image cache.
public sealed class Thumbnail : Image
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(Thumbnail), new PropertyMetadata(null, OnPathChanged));
    public string? FilePath { get => (string?)GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }
    public static readonly DependencyProperty DecodeSizeProperty = DependencyProperty.Register(nameof(DecodeSize),typeof(int),typeof(Thumbnail),new PropertyMetadata(96,OnPathChanged));
    public int DecodeSize { get=>(int)GetValue(DecodeSizeProperty);set=>SetValue(DecodeSizeProperty,value); }
    public static readonly DependencyProperty AllowNetworkProperty = DependencyProperty.Register(
        nameof(AllowNetwork), typeof(bool), typeof(Thumbnail), new PropertyMetadata(false, OnPathChanged));
    public bool AllowNetwork { get => (bool)GetValue(AllowNetworkProperty); set => SetValue(AllowNetworkProperty, value); }
    public static readonly DependencyProperty ArchiveSessionProperty = DependencyProperty.Register(
        nameof(ArchiveSession), typeof(ArchiveSession), typeof(Thumbnail), new PropertyMetadata(null, OnPathChanged));
    public ArchiveSession? ArchiveSession { get => (ArchiveSession?)GetValue(ArchiveSessionProperty); set => SetValue(ArchiveSessionProperty, value); }
    public static readonly DependencyProperty PdfSessionProperty = DependencyProperty.Register(
        nameof(PdfSession), typeof(PdfSession), typeof(Thumbnail), new PropertyMetadata(null, OnPathChanged));
    public PdfSession? PdfSession { get => (PdfSession?)GetValue(PdfSessionProperty); set => SetValue(PdfSessionProperty, value); }
    public static readonly DependencyProperty TemporaryStoreProperty = DependencyProperty.Register(
        nameof(TemporaryStore), typeof(ArchiveTemporaryStore), typeof(Thumbnail), new PropertyMetadata(null, OnPathChanged));
    public ArchiveTemporaryStore? TemporaryStore { get => (ArchiveTemporaryStore?)GetValue(TemporaryStoreProperty); set => SetValue(TemporaryStoreProperty, value); }
    private static readonly SemaphoreSlim Gate = new(2, 2);
    private CancellationTokenSource? pending;
    private readonly List<Task> operations = [];

    public Thumbnail()
    {
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(this, System.Windows.Media.BitmapScalingMode.HighQuality);
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Cancel();
    }
    private static void OnPathChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e) => ((Thumbnail)obj).Start();
    internal void Refresh() => Start();
    internal Task StopAsync() { Cancel(); return Task.WhenAll(operations.ToArray()); }
    private void Cancel() { pending?.Cancel(); pending?.Dispose(); pending = null; Source = null; }
    private void Start()
    {
        Cancel();
        ToolTip = null;
        if (!IsLoaded || FilePath is not string path) return;
        pending = new(); var token = pending.Token;
        var size=(uint)Math.Clamp((int)Math.Ceiling(DecodeSize*System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip),1,2048);
        var operation = LoadAsync(path, token,size); operations.Add(operation);
        _ = ForgetAsync(operation);
    }
    private async Task ForgetAsync(Task operation) { await operation; operations.Remove(operation); }
    private async Task LoadAsync(string path, CancellationToken token,uint size)
    {
        try
        {
            await Gate.WaitAsync(token);
            try
            {
                if (PdfLocation.Parse(path) is { Page: int page } pdf) {
                    var session = PdfSession ?? throw new InvalidOperationException("PDFが開かれていません。");
                    if (!StringComparer.OrdinalIgnoreCase.Equals(session.SourcePath, pdf.DocumentPath)) return;
                    var rendered = await session.DecodeAsync(page, token, size, AllowNetwork);
                    if (!token.IsCancellationRequested) Source = BitmapPresenter.Create(rendered);
                    return;
                }
                var location = ArchiveLocation.Parse(path);
                if (location is { IsFolder: false } && System.IO.Path.GetExtension(location.EntryPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) {
                    using var document = await StellaViewer.Infrastructure.PdfSession.OpenArchiveAsync(
                        ArchiveSession ?? throw new InvalidOperationException("書庫が開かれていません。"), location,
                        TemporaryStore ?? throw new InvalidOperationException("一時フォルダを準備できません。"), token, AllowNetwork);
                    var rendered = await document.DecodeAsync(1, token, size, AllowNetwork);
                    if (!token.IsCancellationRequested) Source = BitmapPresenter.Create(rendered);
                    return;
                }
                var image = location is null ? await LocationPreview.DecodeAsync(path, token, AllowNetwork,size)
                    : await (ArchiveSession ?? throw new InvalidOperationException("書庫が開かれていません。"))
                        .DecodeAsync(location, token, size, AllowNetwork);
                if (!token.IsCancellationRequested) Source = BitmapPresenter.Create(image);
            }
            finally { Gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested) ToolTip = error.Message; }
    }
}
