using System.Buffers.Binary;
using System.Diagnostics;

namespace StellaViewer.Infrastructure;

/// <summary>Extracts only the stored PNG in an isolated, read-only native helper.</summary>
internal static class ClipStoredPreview
{
    private const long PixelLimit = 256L * 1024 * 1024;
    internal sealed record Result(int Width, int Height, int OriginalWidth, int OriginalHeight, byte[] Png);
    internal static async Task<Result> ReadAsync(Stream source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var helper = Path.Combine(AppContext.BaseDirectory,"StellaViewer.ClipPreview.exe");
        if (!File.Exists(helper)) throw new NotSupportedException("CLIPの読み込み部品が見つかりません。配布フォルダ全体を配置してください。");
        using var process = new Process { StartInfo = new(helper) {
            UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true
        }};
        process.Start();
        void Stop() { try { if (!process.HasExited) process.Kill(entireProcessTree:true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
        using var cancel = token.Register(Stop);
        async Task Send() {
            try { await source.CopyToAsync(process.StandardInput.BaseStream,65536,token).ConfigureAwait(false); }
            finally { process.StandardInput.Close(); }
        }
        async Task<Result> Receive() {
            var output = process.StandardOutput.BaseStream;
            var header = new byte[28]; await output.ReadExactlyAsync(header,token).ConfigureAwait(false);
            if (!header.AsSpan(0,8).SequenceEqual("SVCP0001"u8)) throw new InvalidDataException("CLIP読み込み部品の応答が不正です。");
            var width = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
            var height = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12));
            var originalWidth = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16));
            var originalHeight = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20));
            var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24));
            if (width <= 0 || height <= 0 || originalWidth <= 0 || originalHeight <= 0 || (long)width * height > PixelLimit / 4
                || length < 33 || length > PixelLimit + 16 * 1024 * 1024) throw new InvalidDataException("CLIP保存済み画像の寸法または容量が不正です。");
            var png = new byte[length]; await output.ReadExactlyAsync(png,token).ConfigureAwait(false);
            if (await output.ReadAsync(new byte[1],token).ConfigureAwait(false) != 0) throw new InvalidDataException("CLIP読み込み部品に余分な出力があります。");
            return new(width,height,originalWidth,originalHeight,png);
        }
        var input = Send(); var errors = process.StandardError.ReadToEndAsync(token); var received = Receive();
        try {
            await Task.WhenAll(input,received,process.WaitForExitAsync(token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw Failure(await errors.ConfigureAwait(false));
            return await received.ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidDataException) {
            token.ThrowIfCancellationRequested(); Stop();
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            var reason = await errors.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(reason)) throw Failure(reason);
            throw new InvalidDataException("CLIPの保存済み画像を取得できませんでした。" + error.Message,error);
        }
        finally {
            Stop(); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            // Observe pipe tasks even when cancellation or a failed helper interrupted them.
            try { await input.ConfigureAwait(false); } catch (Exception) { }
            try { await received.ConfigureAwait(false); } catch (Exception) { }
            try { await errors.ConfigureAwait(false); } catch (Exception) { }
        }
    }
    private static Exception Failure(string reason)
    {
        var code = reason.Trim();
        return code switch {
            "LIMIT_PIXELS" => new NotSupportedException("CLIPの保存済み画像が表示用画素の上限256 MiBを超えるため、読み込みを中止しました。"),
            "LIMIT_METADATA" => new NotSupportedException("CLIPのメタデータが読み込み上限256 MiBを超えています。"),
            "LIMIT_ENCODED" => new NotSupportedException("CLIPの保存済みPNGが読み込み上限272 MiBを超えています。"),
            "LIMIT_WORKING_MEMORY" => new NotSupportedException("CLIPの読み込みに必要な作業メモリが上限を超えました。"),
            "LIMIT_QUERY" => new NotSupportedException("CLIPのメタデータ解析が処理上限に達したため、読み込みを中止しました。"),
            "MISSING_PREVIEW" => new NotSupportedException("このCLIPには対応する保存済み画像がありません。レイヤーの再合成は行いません。"),
            "UNSUPPORTED_CANVAS_DIMENSIONS" => new NotSupportedException("CLIPの原寸情報を取得できない形式のため、検証待ちです。"),
            _ when code.StartsWith("UNSUPPORTED_",StringComparison.Ordinal) => new NotSupportedException("このCLIPの形式または保存済み画像は未対応です（"+code+"）。"),
            _ => new InvalidDataException("CLIPのデータが破損しているか、読み込みに失敗しました（"+code+"）。")
        };
    }
}
