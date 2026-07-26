using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Voxif.Memory;
 
namespace LiveSplit.Subnautica2.UE5Events.Internal
{
    internal sealed class RemoteProcessMemory
    {
        private readonly Process process;

        public RemoteProcessMemory(ProcessWrapper game)
        {
            if (game == null)
                throw new ArgumentNullException(nameof(game));
            process = game.Process;
        }

        public IntPtr AllocateExecutable(int size)
        {
            IntPtr result = Ue5NativeMethods.VirtualAllocEx(
                process.Handle,
                IntPtr.Zero,
                (UIntPtr)size,
                Ue5NativeMethods.MemCommit | Ue5NativeMethods.MemReserve,
                Ue5NativeMethods.PageExecuteReadWrite);

            if (result == IntPtr.Zero)
                throw new Win32Exception("VirtualAllocEx failed for the UE5 event hook.");

            return result;
        }

        public byte[] Read(IntPtr address, int size)
        {
            byte[] bytes = new byte[size];
            if (!NativeMethods.ReadProcessMemory(process.Handle, address, bytes, size, out int read) || read != size)
                throw new Win32Exception("ReadProcessMemory failed for the UE5 event hook.");
            return bytes;
        }

        public void Write(IntPtr address, byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (!NativeMethods.WriteProcessMemory(process.Handle, address, bytes, bytes.Length, out int written) || written != bytes.Length)
                throw new Win32Exception("WriteProcessMemory failed for the UE5 event hook.");
        }

        public void PatchCode(IntPtr address, byte[] bytes)
        {
            if (!Ue5NativeMethods.VirtualProtectEx(
                    process.Handle,
                    address,
                    (UIntPtr)bytes.Length,
                    Ue5NativeMethods.PageExecuteReadWrite,
                    out uint oldProtection))
            {
                throw new Win32Exception("VirtualProtectEx failed for the UE5 event hook.");
            }

            try
            {
                Write(address, bytes);
                Ue5NativeMethods.FlushInstructionCache(process.Handle, address, (UIntPtr)bytes.Length);
            }
            finally
            {
                Ue5NativeMethods.VirtualProtectEx(
                    process.Handle,
                    address,
                    (UIntPtr)bytes.Length,
                    oldProtection,
                    out _);
            }
        }

        public bool Matches(IntPtr address, byte[] expected)
        {
            try
            {
                return Read(address, expected.Length).SequenceEqual(expected);
            }
            catch
            {
                return false;
            }
        }
    }
}
