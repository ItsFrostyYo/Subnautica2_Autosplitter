using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Voxif.Memory {
    public class ProcessModule {
        public IntPtr BaseAddress;
        public IntPtr EntryPointAddress;
        public string FileName;
        public int ModuleMemorySize;
        public string ModuleName;
        public FileVersionInfo FileVersionInfo => FileVersionInfo.GetVersionInfo(FileName);
        public override string ToString() => ModuleName ?? base.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ModuleInfo {
        public IntPtr BaseAddress;
        public uint ModuleSize;
        public IntPtr EntryPoint;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryBasicInformation {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public MemPageState State;
        public MemPageProtect Protect;
        public MemPageType Type;
    }

    public enum MemPageState : uint {
        MEM_COMMIT = 0x1000,
        MEM_RESERVE = 0x2000,
        MEM_FREE = 0x10000,
    }

    [Flags]
    public enum MemPageProtect : uint {
        PAGE_NOACCESS = 0x1,
        PAGE_READONLY = 0x2,
        PAGE_READWRITE = 0x4,
        PAGE_WRITECOPY = 0x8,
        PAGE_EXECUTE = 0x10,
        PAGE_EXECUTE_READ = 0x20,
        PAGE_EXECUTE_READWRITE = 0x40,
        PAGE_EXECUTE_WRITECOPY = 0x80,
        PAGE_GUARD = 0x100,
        PAGE_NOCACHE = 0x200,
        PAGE_WRITECOMBINE = 0x400,
    }

    public enum MemPageType : uint {
        MEM_PRIVATE = 0x20000,
        MEM_MAPPED = 0x40000,
        MEM_IMAGE = 0x1000000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SymbolInfo {
        public uint sizeOfStruct;
        public uint typeIndex;
        public ulong reserved1;
        public ulong reserved2;
        public uint index;
        public uint size;
        public ulong modBase;
        public uint flags;
        public ulong value;
        public ulong address;
        public uint register;
        public uint scope;
        public uint tag;
        public int nameLen;
        public int maxNameLen;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string name;
    }

    public static class ExtensionMethods {
        public static byte[] ReadBytes(this Process process, IntPtr address, int numBytes) {
            process.ReadBytes(address, numBytes, out byte[] bytes);
            return bytes;
        }
        public static bool ReadBytes(this Process process, IntPtr address, int numBytes, out byte[] bytes) {
            bytes = new byte[numBytes];
            if(address == default) {
                return false;
            }
            return NativeMethods.ReadProcessMemory(process.Handle, address, bytes, numBytes, out _);
        }

        //
        // POINTER
        //
        public unsafe static T To<T>(this byte[] bytes) where T : unmanaged {
            fixed(byte* p = bytes) {
                return *(T*)p;
            }
        }
        public unsafe static T To<T>(this byte[] bytes, int offset) where T : unmanaged {
            fixed(byte* p = bytes) {
                return *(T*)(p + offset);
            }
        }

        public unsafe static byte[] ToBytes<T>(this T value) where T : unmanaged {
            int size = sizeof(T);
            byte[] bytes = new byte[size];
            return value.ToBytes(bytes, size);
        }
        public unsafe static byte[] ToBytes<T>(this T value, byte[] bytes) where T : unmanaged {
            int size = sizeof(T);
            return value.ToBytes(bytes, size);
        }
        public unsafe static byte[] ToBytes<T>(this T value, int size) where T : unmanaged {
            byte[] bytes = new byte[size];
            return value.ToBytes(bytes, size);
        }
        public unsafe static byte[] ToBytes<T>(this T value, byte[] bytes, int size) where T : unmanaged {
            fixed(byte* p = bytes) {
                Buffer.MemoryCopy(&value, p, bytes.Length, size);
            }
            return bytes;
        }


        //
        // MODULE
        //
        private static readonly Dictionary<int, ProcessModule[]> ModuleCache = new Dictionary<int, ProcessModule[]>();
        public static ProcessModule[] Modules(this Process process) {
            const uint LIST_MODULES_ALL = 3;
            const int MAX_PATH = 260;

            lock(ModuleCache) {
                if(ModuleCache.Count > 100) {
                    ModuleCache.Clear();
                }
                IntPtr[] buffer = new IntPtr[1024];
                uint cb = (uint)(IntPtr.Size * buffer.Length);
                if(!NativeMethods.EnumProcessModulesEx(process.Handle, buffer, cb, out uint cnNeeded, LIST_MODULES_ALL)) {
                    return new ProcessModule[0];
                }

                uint numModules = cnNeeded / (uint)IntPtr.Size;
                int key = process.StartTime.GetHashCode() + process.Id + (int)numModules;
                if(ModuleCache.ContainsKey(key)) {
                    return ModuleCache[key];
                }

                List<ProcessModule> processList = new List<ProcessModule>();
                StringBuilder stringBuilder = new StringBuilder(MAX_PATH);
                for(int i = 0; i < numModules; i++) {
                    stringBuilder.Clear();
                    if(NativeMethods.GetModuleFileNameEx(process.Handle, buffer[i], stringBuilder, (uint)stringBuilder.Capacity) == 0) {
                        return processList.ToArray();
                    }
                    string fileName = stringBuilder.ToString();
                    stringBuilder.Clear();
                    if(NativeMethods.GetModuleBaseName(process.Handle, buffer[i], stringBuilder, (uint)stringBuilder.Capacity) == 0) {
                        return processList.ToArray();
                    }
                    string moduleName = stringBuilder.ToString();
                    ModuleInfo moduleInfo = default;
                    if(!NativeMethods.GetModuleInformation(process.Handle, buffer[i], out moduleInfo, (uint)Marshal.SizeOf(moduleInfo))) {
                        return processList.ToArray();
                    }
                    processList.Add(new ProcessModule {
                        FileName = fileName,
                        BaseAddress = moduleInfo.BaseAddress,
                        ModuleMemorySize = (int)moduleInfo.ModuleSize,
                        EntryPointAddress = moduleInfo.EntryPoint,
                        ModuleName = moduleName
                    });
                }
                ModuleCache.Add(key, processList.ToArray());
                return processList.ToArray();
            }
        }


        //
        // SYMBOL
        //
        public static IntPtr SymbolAddress(this Process process, string moduleName, string symbol) {
            try {
                ProcessModule module = process.Modules().FirstOrDefault(m => m.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
                if(module == null) {
                    return default;
                }
                SymbolInfo[] symbols = process.AllSymbols(module, symbol);
                return symbols.Length > 0 ? (IntPtr)symbols[0].address : default;
            } catch(Exception e) {
                Trace.TraceError(e.ToString());
                return default;
            }
        }

        public static SymbolInfo[] AllSymbols(this Process process, ProcessModule module, string symbol = "*") {
            IntPtr hProcess = process.Handle;

            if(!NativeMethods.SymInitialize(hProcess, null, false)) {
                throw new Exception("Failed to initialize symbols");
            }

            List<SymbolInfo> symbolList = new List<SymbolInfo>();
            try {
                if(NativeMethods.SymLoadModuleEx(hProcess, default, module.ModuleName, null, (long)module.BaseAddress, module.ModuleMemorySize, default, 0) == 0) {
                    throw new Exception("Failed to load module's symbols");
                }

                if(!NativeMethods.SymEnumSymbols(hProcess, (ulong)module.BaseAddress, symbol, EnumSyms, default)) {
                    throw new Exception("Failed to enumerate symbols");
                }
            } finally {
                NativeMethods.SymCleanup(hProcess);
            }

            return symbolList.ToArray();

            bool EnumSyms(ref SymbolInfo pSymInfo, uint SymbolSize, IntPtr UserContext) {
                symbolList.Add(pSymInfo);
                return true;
            }
        }

    }

    public static class NativeMethods {

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWow64Process(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int nSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int nSize, out int lpNumberOfBytesWritten);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumProcessModulesEx(IntPtr hProcess, [Out] IntPtr[] lphModule, uint cb, out uint lpcbNeeded, uint dwFilterFlag);

        [DllImport("psapi.dll")]
        public static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] StringBuilder lpFilename, uint nSize);

        [DllImport("psapi.dll")]
        public static extern uint GetModuleBaseName(IntPtr hProcess, IntPtr hModule, [Out] StringBuilder lpBaseName, uint nSize);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out ModuleInfo lpmodinfo, uint cb);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MemoryBasicInformation lpBuffer, int dwLength);



        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymInitialize(IntPtr hProcess, string UserSearchPath, [MarshalAs(UnmanagedType.Bool)] bool fInvadeProcess);

        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymCleanup(IntPtr hProcess);

        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern ulong SymLoadModuleEx(IntPtr hProcess, IntPtr hFile, string ImageName, string ModuleName, long BaseOfDll, int DllSize, IntPtr Data, int Flags);

        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymEnumSymbols(IntPtr hProcess, ulong BaseOfDll, string Mask, PSYM_ENUMERATESYMBOLS_CALLBACK EnumSymbolsCallback, IntPtr UserContext);

        public delegate bool PSYM_ENUMERATESYMBOLS_CALLBACK(ref SymbolInfo pSymInfo, uint SymbolSize, IntPtr UserContext);
    }
}