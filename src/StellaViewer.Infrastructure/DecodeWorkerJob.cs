using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StellaViewer.Infrastructure;

/// <summary>Resource and lifetime boundary, not a low-privilege security sandbox.</summary>
internal static class DecodeWorkerJob
{
    // Windows x64 JOBOBJECT_EXTENDED_LIMIT_INFORMATION.
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct Limits
    {
        [FieldOffset(16)] public uint Flags;
        [FieldOffset(112)] public ulong ProcessMemory;
        [FieldOffset(120)] public ulong JobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref Limits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    internal static SafeFileHandle Attach(Process process)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("読み込み処理の隔離にはWindows x64が必要です。");
        var job = CreateJobObject(IntPtr.Zero, null);
        try {
            var limits = new Limits { Flags = 0x2000 | 0x100 | 0x200,
                ProcessMemory = 2UL * 1024 * 1024 * 1024, JobMemory = 2UL * 1024 * 1024 * 1024 };
            if (job.IsInvalid || !SetInformationJobObject(job, 9, ref limits, 144) || !AssignProcessToJobObject(job, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "読み込みプロセスの保護設定に失敗しました。");
            return job;
        }
        catch { job.Dispose(); throw; }
    }
}
