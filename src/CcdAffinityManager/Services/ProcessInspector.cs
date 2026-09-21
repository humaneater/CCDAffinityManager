using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CcdAffinityManager.Native;

namespace CcdAffinityManager.Services;

internal static class ProcessInspector
{
    internal static string? TryGetExecutablePath(Process process)
    {
        return TryGetExecutablePath(process.Id);
    }

    internal static string? TryGetExecutablePath(int processId)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessAccessRights.QueryLimitedInformation,
            false,
            processId);

        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(32768);
            var size = buffer.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size))
            {
                return null;
            }

            return buffer.ToString();
        }
        catch (Exception exception) when (
            exception is Win32Exception or NotSupportedException)
        {
            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    internal static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static string GetProcessName(string executablePath)
    {
        return Path.GetFileNameWithoutExtension(executablePath);
    }

    internal static bool TryGetStartTimeUtc(Process process, out long ticks)
    {
        try
        {
            ticks = process.StartTime.ToUniversalTime().Ticks;
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            ticks = 0;
            return false;
        }
    }
}
