using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using StellaViewer.Core;

namespace StellaViewer.Infrastructure;

/// <summary>One reusable decoder process per viewer. Only bounded metadata and BGRA cross the pipes.</summary>
public static partial class DecodeWorker
{
    public const string Argument = "--decode-worker";
    public const int TimeoutSeconds = 60;
    internal static TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(TimeoutSeconds);
    internal static int? WorkerProcessId => process is { HasExited: false } running ? running.Id : null;
    private const int MetadataLimit = 1024 * 1024;
    private const long InputLimit = 512L * 1024 * 1024;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Process? process;
    private static SafeFileHandle? job;
    internal static bool IsWorker { get; private set; }
    private sealed record Request(string Operation, string? Path = null, uint Thumbnail = 0, int Page = 0, AnimationLimits? Limits = null,
        string? Member = null, string? FormatPath = null, long ByteLimit = 0);
    private sealed record Frame(int Width, int Height, int OriginalWidth, int OriginalHeight, double Delay);
    private sealed record Reply(int Status = 0, string? Error = null, long Number = 0, string? Warning = null, Frame[]? Frames = null);
    private sealed record Result(Reply Reply, DecodedImage[] Images, ArchiveItem[]? Catalog = null, PngTextResult? Text = null);

    static DecodeWorker() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
    private static void Stop()
    {
        var current = process; process = null;
        // Closing the Job also terminates CLIP subprocesses, including after parent failure.
        job?.Dispose(); job = null;
        if (current is null) return;
        try { if (!current.HasExited) current.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        current.Dispose();
    }
    private static Process Start()
    {
        if (process is { HasExited: false }) return process;
        Stop();
        var executable = Environment.ProcessPath ?? throw new IOException("実行ファイルの場所を取得できません。");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (System.IO.Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        start.ArgumentList.Add(Argument);
        var created = new Process { StartInfo = start };
        try {
            created.Start(); process = created;
            job = DecodeWorkerJob.Attach(created);
            // Drain errors without retaining arbitrary native diagnostics in memory.
            _ = DrainErrorsAsync(created.StandardError.BaseStream);
            return created;
        }
        catch { Stop(); created.Dispose(); throw; }
    }
    private static async Task DrainErrorsAsync(Stream stream)
    {
        try { await stream.CopyToAsync(Stream.Null, 4096).ConfigureAwait(false); }
        catch(Exception error) when(error is IOException or ObjectDisposedException) { }
    }
    private static async Task WriteIntAsync(Stream stream, int value, CancellationToken token)
    {
        var bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
    }
    private static async Task<int> ReadIntAsync(Stream stream, CancellationToken token)
    {
        var bytes = new byte[4]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }
    private static async Task WriteJsonAsync<T>(Stream stream, T value, CancellationToken token, int limit = MetadataLimit)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > limit) throw new InvalidDataException("読み込み処理のメタデータが上限を超えました。");
        await WriteIntAsync(stream, bytes.Length, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
    }
    private static async Task<T> ReadJsonAsync<T>(Stream stream, CancellationToken token, int limit = MetadataLimit)
    {
        var count = await ReadIntAsync(stream, token).ConfigureAwait(false);
        if (count <= 0 || count > limit) throw new InvalidDataException("読み込み処理の応答サイズが不正です。");
        var bytes = new byte[count]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("読み込み処理の応答が不正です。");
    }
    private static async Task<Result> SendAsync(Request request, CancellationToken token, Stream? input = null, Stream? destination = null)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try {
            using var deadline = new CancellationTokenSource(RequestTimeout);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            var cancellation = timeout.Token;
            var current = Start();
            // A blocking native call cannot observe CancellationToken; terminate its process instead.
            using var cancel = cancellation.Register(Stop);
            var reusableFailure=false;
            try {
                await WriteJsonAsync(current.StandardInput.BaseStream, request, cancellation).ConfigureAwait(false);
                async Task Upload() {
                    if (input is not null) {
                        var buffer = new byte[65536]; long total = 0;
                        while (true) {
                            var read = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false);
                            if (read == 0) break;
                            total += read;
                            if (total > InputLimit) throw new NotSupportedException("書庫内画像の読み込み容量が上限512 MiBを超えました。");
                            await WriteIntAsync(current.StandardInput.BaseStream, read, cancellation).ConfigureAwait(false);
                            await current.StandardInput.BaseStream.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                        }
                        await WriteIntAsync(current.StandardInput.BaseStream, 0, cancellation).ConfigureAwait(false);
                    }
                    await current.StandardInput.BaseStream.FlushAsync(cancellation).ConfigureAwait(false);
                }
                async Task<Result> Receive() {
                    var stream = current.StandardOutput.BaseStream;
                    var reply = await ReadJsonAsync<Reply>(stream, cancellation).ConfigureAwait(false);
                    if(reply.Status==0 && request.Operation.StartsWith("archive-", StringComparison.Ordinal)) {
                        var special=await ReceiveArchiveAsync(request,reply,stream,destination,cancellation).ConfigureAwait(false);
                        if(special is not null)return special;
                    }
                    var frames = reply.Frames ?? [];
                    if (frames.Length > 1000) throw new InvalidDataException("読み込み処理のフレーム数が不正です。");
                    long total = 0;
                    foreach (var frame in frames) {
                        total += ImageReadGuard.CheckSize(frame.Width, frame.Height);
                        if(frame.OriginalWidth <= 0 || frame.OriginalHeight <= 0)throw new InvalidDataException("元画像の寸法が不正です。");
                        if (total > ImageReadGuard.MaximumPixelBytes || !double.IsFinite(frame.Delay) || frame.Delay <= 0)
                            throw new InvalidDataException("読み込み処理の画素またはフレーム時間が不正です。");
                    }
                    var images = new DecodedImage[frames.Length];
                    for (var i = 0; i < frames.Length; i++) {
                        var frame = frames[i]; var pixels = new byte[checked(frame.Width * frame.Height * 4)];
                        await stream.ReadExactlyAsync(pixels, cancellation).ConfigureAwait(false);
                        images[i] = new(frame.Width, frame.Height, frame.OriginalWidth, frame.OriginalHeight, pixels);
                    }
                    return new(reply, images);
                }
                var upload = Upload(); var receive = Receive();
                // A failed pipe/upload must abort its sibling rather than wait for the timeout.
                try { await await Task.WhenAny(upload, receive).ConfigureAwait(false); }
                catch { timeout.Cancel(); try { await Task.WhenAll(upload, receive).ConfigureAwait(false); } catch { } throw; }
                await Task.WhenAll(upload, receive).ConfigureAwait(false);
                var result = await receive.ConfigureAwait(false);
                if (result.Reply.Status != 0) {
                    // A complete, bounded rejection response leaves no open payload on either pipe.
                    // Keep catalog state for unsupported thumbnails instead of re-parsing a huge archive.
                    reusableFailure=result.Reply.Status is 1 or 2;
                    throw result.Reply.Status switch {
                        1 => new NotSupportedException(result.Reply.Error),
                        2 => new InvalidDataException(result.Reply.Error),
                        _ => new IOException(result.Reply.Error)
                    };
                }
                return result;
            }
            catch (Exception error) when (token.IsCancellationRequested || deadline.IsCancellationRequested) {
                Stop(); token.ThrowIfCancellationRequested();
                throw new IOException($"読み込みが{TimeoutSeconds}秒を超えたため中止しました。次の画像を開けます。", error);
            }
            catch (Exception error) when (error is EndOfStreamException or System.ComponentModel.Win32Exception or ObjectDisposedException) {
                Stop(); throw new IOException("読み込みプロセスが終了しました。メモリ上限・破損データ等が原因の可能性があります。次の画像を開けます。", error);
            }
            catch when(reusableFailure) { throw; }
            catch { Stop(); throw; }
        }
        finally { Gate.Release(); }
    }
    internal static async Task<DecodedImage> ImageAsync(string? path, Stream? input, uint thumbnail, CancellationToken token)
    {
        var result = await SendAsync(new(path is null ? "stream" : "image", path, thumbnail), token, input).ConfigureAwait(false);
        return result.Images.Length == 1 ? result.Images[0] : throw new InvalidDataException("読み込み処理の画像数が不正です。");
    }
    internal static async Task<long> ProbeAsync(string? path, Stream? input, CancellationToken token)
        => (await SendAsync(new(path is null ? "probe-stream" : "probe", path), token, input).ConfigureAwait(false)).Reply.Number;
    internal static async Task<AnimationDecodeResult> AnimationAsync(string path, AnimationLimits limits, CancellationToken token)
    {
        var result = await SendAsync(new("animation", path, Limits: limits), token).ConfigureAwait(false);
        return new(result.Images.Length < 2 ? null : new(result.Images.Select((image, i) => new AnimationFrame(image, result.Reply.Frames![i].Delay)).ToArray()), result.Reply.Warning);
    }
    internal static async Task<long> PdfNumberAsync(string operation, string path, int page, CancellationToken token)
        => (await SendAsync(new(operation, path, Page: page), token).ConfigureAwait(false)).Reply.Number;
    internal static async Task<DecodedImage> PdfImageAsync(string path, int page, uint thumbnail, CancellationToken token)
    {
        var result = await SendAsync(new("pdf-render", path, thumbnail, page), token).ConfigureAwait(false);
        return result.Images.Single();
    }

    /// <summary>Must run before normal app initialization. Reads requests only from inherited pipes.</summary>
    public static async Task<int> RunAsync()
    {
        IsWorker = true;
        try {
            ImageDecoderSecurity.Initialize();
            using var input = Console.OpenStandardInput(); using var output = Console.OpenStandardOutput();
            while (true) {
                Request request;
                try { request = await ReadJsonAsync<Request>(input, default).ConfigureAwait(false); }
                catch (EndOfStreamException) { return 0; }
                if(request.Operation.StartsWith("archive-",StringComparison.Ordinal)) {
                    await ServeArchiveAsync(request,output).ConfigureAwait(false); continue;
                }
                Reply reply; DecodedImage[] images = [];
                using var payload = request.Operation is "stream" or "probe-stream" ? new PayloadStream(input) : null;
                try {
                    var decoder = new MagickImageDecoder();
                    if (request.Operation == "image" || request.Operation == "stream") {
                        var image = payload is null ? await decoder.DecodeAsync(request.Path!, default, request.Thumbnail).ConfigureAwait(false)
                            : await decoder.DecodeStreamAsync(payload, default, request.Thumbnail).ConfigureAwait(false);
                        images = [image]; reply = new(Frames: [Describe(image)]);
                    }
                    else if (request.Operation is "probe" or "probe-stream")
                        reply = new(Number: payload is null ? await decoder.EstimatePixelBytesAsync(request.Path!, default).ConfigureAwait(false)
                            : await decoder.EstimateStreamPixelBytesAsync(payload, default).ConfigureAwait(false));
                    else if (request.Operation == "animation") {
                        var animation = await decoder.DecodeAnimationAsync(request.Path!, default, request.Limits).ConfigureAwait(false);
                        images = animation.Animation?.Frames.Select(f => f.Image).ToArray() ?? [];
                        reply = new(Warning: animation.Warning, Frames: animation.Animation?.Frames.Select(f => Describe(f.Image, f.DelayMilliseconds)).ToArray());
                    }
                    else if (request.Operation.StartsWith("pdf-", StringComparison.Ordinal)) {
                        using var pdf = await PdfSession.OpenAsync(request.Path!, default, allowNetwork: true).ConfigureAwait(false);
                        if (request.Operation == "pdf-count") reply = new(Number: pdf.PageCount);
                        else if (request.Operation == "pdf-probe") reply = new(Number: await pdf.EstimatePixelBytesAsync(request.Page, default, true).ConfigureAwait(false));
                        else if (request.Operation == "pdf-render") {
                            var image = await pdf.DecodeAsync(request.Page, default, request.Thumbnail, true).ConfigureAwait(false);
                            images = [image]; reply = new(Frames: [Describe(image)]);
                        }
                        else throw new InvalidDataException("読み込み要求が不正です。");
                    }
                    else throw new InvalidDataException("読み込み要求が不正です。");
                }
                catch (Exception error) {
                    images = [];
                    var message = error.Message.Length > 4096 ? error.Message[..4096] : error.Message;
                    reply = new(error is NotSupportedException ? 1 : error is InvalidDataException ? 2 : 3, message);
                }
                if (payload is not null) {
                    var buffer = new byte[65536]; while (payload.Read(buffer) > 0) { }
                }
                await WriteJsonAsync(output, reply, default).ConfigureAwait(false);
                foreach (var image in images) await output.WriteAsync(image.BgraPixels).ConfigureAwait(false);
                await output.FlushAsync().ConfigureAwait(false);
            }
        }
        catch { return 1; }
    }
    private static Frame Describe(DecodedImage image, double delay = 100) => new(image.Width, image.Height, image.OriginalWidth, image.OriginalHeight, delay);

    private sealed class PayloadStream(Stream source) : Stream
    {
        private int remaining; private long total; private bool ended;
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty || ended) return 0;
            if (remaining == 0) {
                Span<byte> header = stackalloc byte[4]; source.ReadExactly(header); remaining = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (remaining == 0) { ended = true; return 0; }
                if (remaining < 0 || remaining > 65536 || (total += remaining) > InputLimit) throw new InvalidDataException("読み込みデータが上限を超えています。");
            }
            var count = source.Read(buffer[..Math.Min(buffer.Length, remaining)]);
            if (count == 0) throw new EndOfStreamException();
            remaining -= count; return count;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
