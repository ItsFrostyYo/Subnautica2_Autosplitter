using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Voxif.Memory {

    public enum EDerefType { Auto, Bit64, Bit32 }
    public enum EStringType { Auto, AutoSized, UTF8, UTF8Sized, UTF16, UTF16Sized }

    public class ProcessWrapper {
        public ProcessWrapper(Process process) {
            Process = process;
            NativeMethods.IsWow64Process(process.Handle, out bool isWow64);
            Is64Bit = Environment.Is64BitOperatingSystem && !isWow64;
            PointerSize = (byte)(Is64Bit ? 8 : 4);
        }

        public Process Process { get; }

        public bool Is64Bit { get; }
        public byte PointerSize { get; }

        public byte[] Read(IntPtr address, int numBytes) {
            return Process.ReadBytes(address, numBytes);
        }

        //Read type
        public T Read<T>(IntPtr address) where T : unmanaged {
            return Read<T>(Is64Bit, address);
        }
        public T Read<T>(EDerefType derefType, IntPtr address) where T : unmanaged {
            return Read<T>(derefType == EDerefType.Auto ? Is64Bit : derefType == EDerefType.Bit64, address);
        }
        private unsafe T Read<T>(bool is64Bit, IntPtr address) where T : unmanaged {
            int size = typeof(T) == typeof(IntPtr) ? (is64Bit ? 8 : 4) : sizeof(T);
            byte[] buffer = Process.ReadBytes(address, size);
            return buffer?.To<T>() ?? default;
        }

        //Read type offsets
        public T Read<T>(IntPtr address, params int[] offsets) where T : unmanaged {
            return Read<T>(Is64Bit, address, offsets);
        }
        public T Read<T>(EDerefType derefType, IntPtr address, params int[] offsets) where T : unmanaged {
            return Read<T>(derefType == EDerefType.Auto ? Is64Bit : derefType == EDerefType.Bit64, address, offsets);
        }
        private T Read<T>(bool is64Bit, IntPtr address, params int[] offsets) where T : unmanaged {
            address = Read(is64Bit, address, offsets);
            if(address == default) {
                return default;
            }
            return Read<T>(is64Bit, address);
        }

        //Read offsets
        public IntPtr Read(IntPtr address, params int[] offsets) {
            return Read(Is64Bit, address, offsets);
        }
        public IntPtr Read(EDerefType derefType, IntPtr address, params int[] offsets)  {
            return Read(derefType == EDerefType.Auto ? Is64Bit : derefType == EDerefType.Bit64, address, offsets);
        }
        public IntPtr Read(bool is64Bit, IntPtr address, params int[] offsets) {
            if(offsets.Length == 0 || address == default) {
                return address;
            }
            for(int i = 0; i < offsets.Length - 1; i++) {
                address = Read<IntPtr>(is64Bit, address + offsets[i]);
                if(address == default) {
                    return default;
                }
            }
            return address += offsets[offsets.Length - 1];
        }


        //Write bytes
        public void Write(byte[] value, IntPtr address, params int[] offsets) {
            Write(value, Is64Bit, address, offsets);
        }
        public void Write(byte[] value, EDerefType derefType, IntPtr address, params int[] offsets) {
            Write(value, derefType == EDerefType.Auto ? Is64Bit : derefType == EDerefType.Bit64, address, offsets);
        }
        private void Write(byte[] value, bool is64Bit, IntPtr address, params int[] offsets) {
            address = Read(is64Bit, address, offsets);
            if(address == default) {
                return;
            }
            NativeMethods.WriteProcessMemory(Process.Handle, address, value, value.Length, out _);
        }

        //Write type
        public void Write<T>(T value, IntPtr address, params int[] offsets) where T : unmanaged {
            Write(value, Is64Bit, address, offsets);
        }
        public void Write<T>(T value, EDerefType derefType, IntPtr address, params int[] offsets) where T : unmanaged {
            Write(value, derefType == EDerefType.Auto ? Is64Bit : derefType == EDerefType.Bit64, address, offsets);
        }
        private unsafe void Write<T>(T value, bool is64Bit, IntPtr address, params int[] offsets) where T : unmanaged {
            Write(value.ToBytes(typeof(T) == typeof(IntPtr) ? (is64Bit ? 8 : 4) : sizeof(T)), is64Bit, address, offsets);
        }


        public string ReadString(IntPtr address, int size, EStringType type = EStringType.Auto) {
            const string empty = "";

            if(address == default) {
                return empty;
            }

            Encoding encoding;

            byte[] buffer = new byte[size];
            if(NativeMethods.ReadProcessMemory(Process.Handle, address, buffer, size, out int readLength) && readLength == buffer.Length) {
                if(type == EStringType.Auto) {
                    encoding = readLength > 1 && buffer[1] == 0 ? Encoding.Unicode : Encoding.UTF8;
                } else {
                    encoding = type == EStringType.UTF16 ? Encoding.Unicode : Encoding.UTF8;
                }
                return encoding.GetString(buffer);
            }

            return empty;
        }

        public string ReadString(IntPtr address, EStringType type = EStringType.Auto) {
            const string empty = "";

            if(address == default) {
                return empty;
            }

            const int maxSize = 1024;

            bool isUnicode;
            Encoding encoding;
            int charSize;
            SetEncoding(type == EStringType.UTF16 || type == EStringType.UTF16Sized);

            if(type == EStringType.AutoSized || type == EStringType.UTF8Sized || type == EStringType.UTF16Sized) {
                int size = Read<int>(address - 0x4);
                if(size >= maxSize) {
                    return empty;
                }
                if(type == EStringType.AutoSized && Read<byte>(address + 0x1) == 0) {
                    SetEncoding(true);
                }
                byte[] stringBytes = new byte[size * charSize];
                if(!NativeMethods.ReadProcessMemory(Process.Handle, address, stringBytes, stringBytes.Length, out int readLength) || readLength != stringBytes.Length) {
                    return empty;
                }
                return encoding.GetString(stringBytes);
            } else {
                byte[] buffer = new byte[64];
                List<byte> stringBytes = new List<byte>(buffer.Length);
                int offset = 0;
                while(NativeMethods.ReadProcessMemory(Process.Handle, address + offset, buffer, buffer.Length, out int readLength) && readLength == buffer.Length) {
                    if(offset >= maxSize) {
                        return empty;
                    }

                    if(type == EStringType.Auto && offset == 0 && buffer[1] == 0) {
                        isUnicode = true;
                        encoding = Encoding.Unicode;
                        charSize = 2;
                    }

                    for(int c = 0; c < readLength; c += charSize) {
                        if(buffer[c] == 0) {
                            return encoding.GetString(stringBytes.ToArray());
                        } else {
                            stringBytes.Add(buffer[c]);
                            if(isUnicode) {
                                stringBytes.Add(buffer[c + 1]);
                            }
                        }
                    }

                    offset += readLength;
                }
            }
            return empty;

            void SetEncoding(bool _isUnicode) {
                isUnicode = _isUnicode;
                encoding = _isUnicode ? Encoding.Unicode : Encoding.UTF8;
                charSize = _isUnicode ? 2 : 1;
            }
        }


        public IntPtr FromAssemblyAddress(IntPtr asmAddress) {
            return Is64Bit ? FromRelativeAddress(asmAddress) : FromAbsoluteAddress(asmAddress);
        }
        public IntPtr FromAbsoluteAddress(IntPtr asmAddress) {
            return (IntPtr)Read<int>(asmAddress);
        }
        public IntPtr FromRelativeAddress(IntPtr asmAddress) {
            return asmAddress + 0x4 + Read<int>(asmAddress);
        }

        public IEnumerable<MemoryBasicInformation> MemoryPages(bool allPages = false) {
            long min = 0x10000;
            long max = Is64Bit ? 0x00007FFFFFFEFFFF : 0x7FFEFFFF;

            int mbiSize = Marshal.SizeOf(typeof(MemoryBasicInformation));

            long addr = min;
            do {
                if(NativeMethods.VirtualQueryEx(Process.Handle, (IntPtr)addr, out MemoryBasicInformation mbi, mbiSize) == 0) {
                    break;
                }
                addr += (long)mbi.RegionSize;

                if(mbi.State != MemPageState.MEM_COMMIT
                || !allPages && ((mbi.Protect & MemPageProtect.PAGE_GUARD) != 0 || mbi.Type != MemPageType.MEM_PRIVATE)) {
                    continue;
                }

                yield return mbi;
            } while(addr < max);
        }
    }

    public interface ITickable {
        uint Tick { get; }
        void IncreaseTick();
    }

    public class TickableProcessWrapper : ProcessWrapper, ITickable {
        public TickableProcessWrapper(Process process) : base(process) { }

        public uint Tick { get; private set; } = 1;
        public void IncreaseTick() => ++Tick;
    }
}