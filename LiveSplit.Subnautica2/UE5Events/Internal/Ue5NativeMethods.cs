using System;
using System.Runtime.InteropServices;
 
namespace LiveSplit.Subnautica2.UE5Events.Internal
{
    internal static class Ue5NativeMethods
    {
        internal const uint MemCommit = 0x1000;
        internal const uint MemReserve = 0x2000;
        internal const uint PageExecuteReadWrite = 0x40;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr VirtualAllocEx(
            IntPtr process,
            IntPtr address,
            UIntPtr size,
            uint allocationType,
            uint protection);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool VirtualProtectEx(
            IntPtr process,
            IntPtr address,
            UIntPtr size,
            uint newProtection,
            out uint oldProtection);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FlushInstructionCache(
            IntPtr process,
            IntPtr address,
            UIntPtr size);
    }
}
