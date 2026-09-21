using System.Runtime.InteropServices;
using System.Text;

namespace CcdAffinityManager.Native;

internal static class NativeMethods
{
    internal const int ErrorInsufficientBuffer = 122;

    [Flags]
    internal enum ProcessAccessRights : uint
    {
        QueryLimitedInformation = 0x1000,
        SetInformation = 0x0200
    }

    internal enum LogicalProcessorRelationship
    {
        RelationProcessorCore = 0,
        RelationNumaNode = 1,
        RelationCache = 2,
        RelationProcessorPackage = 3,
        RelationGroup = 4,
        RelationProcessorDie = 5,
        RelationNumaNodeEx = 6,
        RelationProcessorModule = 7
    }

    internal enum ProcessorCacheType
    {
        CacheUnified = 0,
        CacheInstruction = 1,
        CacheData = 2,
        CacheTrace = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GroupAffinity
    {
        public UIntPtr Mask;
        public ushort Group;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CacheRelationship
    {
        public byte Level;
        public byte Associativity;
        public ushort LineSize;
        public uint CacheSize;
        public ProcessorCacheType Type;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] Reserved;

        public GroupAffinity GroupMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        ProcessAccessRights desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessAffinityMask(
        IntPtr processHandle,
        out UIntPtr processAffinityMask,
        out UIntPtr systemAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessAffinityMask(
        IntPtr processHandle,
        UIntPtr processAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLogicalProcessorInformationEx(
        LogicalProcessorRelationship relationshipType,
        IntPtr buffer,
        ref uint returnedLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(
        IntPtr processHandle,
        uint flags,
        StringBuilder executablePath,
        ref int size);

    [DllImport("kernel32.dll")]
    internal static extern ushort GetActiveProcessorGroupCount();

    [DllImport("kernel32.dll")]
    internal static extern uint GetActiveProcessorCount(ushort groupNumber);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumProcesses(
        [Out] uint[] processIds,
        uint arraySizeBytes,
        out uint bytesReturned);
}
