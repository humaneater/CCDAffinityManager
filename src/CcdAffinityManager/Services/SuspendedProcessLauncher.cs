using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using CcdAffinityManager.Native;

namespace CcdAffinityManager.Services;

internal sealed record SuspendedLaunchResult(
    int ProcessId,
    ulong OriginalMask,
    string? AffinityError);

internal static class SuspendedProcessLauncher
{
    public static SuspendedLaunchResult LaunchWithAffinity(
        string executablePath,
        string? arguments,
        string? workingDirectory,
        ulong desiredMask)
    {
        var commandLine = new StringBuilder();
        commandLine.Append('"').Append(executablePath).Append('"');
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            commandLine.Append(' ').Append(arguments);
        }

        var startupInfo = new NativeMethods.StartupInfo
        {
            cb = Marshal.SizeOf<NativeMethods.StartupInfo>()
        };

        if (!NativeMethods.CreateProcess(
                executablePath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                NativeMethods.CreateSuspended | NativeMethods.CreateUnicodeEnvironment,
                IntPtr.Zero,
                workingDirectory,
                ref startupInfo,
                out var processInformation))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "创建挂起进程失败");
        }

        try
        {
            ulong originalMask = 0;
            string? affinityError = null;

            if (!NativeMethods.GetProcessAffinityMask(
                    processInformation.hProcess,
                    out var originalMaskValue,
                    out _))
            {
                affinityError = FormatLastError("读取初始亲和度失败");
            }
            else
            {
                originalMask = originalMaskValue.ToUInt64();
                if (originalMask != desiredMask &&
                    !NativeMethods.SetProcessAffinityMask(
                        processInformation.hProcess,
                        new UIntPtr(desiredMask)))
                {
                    affinityError = FormatLastError("启动时设置亲和度失败");
                }
            }

            var resumeResult = NativeMethods.ResumeThread(processInformation.hThread);
            if (resumeResult == uint.MaxValue)
            {
                var errorCode = Marshal.GetLastWin32Error();
                NativeMethods.TerminateProcess(processInformation.hProcess, 1);
                throw new Win32Exception(errorCode, "恢复挂起进程失败");
            }

            return new SuspendedLaunchResult(
                processInformation.dwProcessId,
                originalMask,
                affinityError);
        }
        finally
        {
            NativeMethods.CloseHandle(processInformation.hThread);
            NativeMethods.CloseHandle(processInformation.hProcess);
        }
    }

    private static string FormatLastError(string action)
    {
        var errorCode = Marshal.GetLastWin32Error();
        var message = new Win32Exception(errorCode).Message;
        var hint = errorCode == 5 ? "，请尝试关闭目标程序的反作弊保护或使用官方启动器" : string.Empty;
        return $"{action}：{message}{hint}（Win32 {errorCode}）";
    }
}
