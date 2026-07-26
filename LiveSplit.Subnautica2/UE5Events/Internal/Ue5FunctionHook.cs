using System;
using System.Collections.Generic;
using Voxif.Memory;
 
namespace LiveSplit.Subnautica2.UE5Events.Internal
{
    internal sealed class Ue5FunctionHook : IDisposable
    {
        internal struct FunctionMatch
        {
            public readonly int ClassNameIndex;
            public readonly int ObjectNameIndex;
            public readonly int FunctionNameIndex;

            public FunctionMatch(int classNameIndex, int objectNameIndex, int functionNameIndex)
            {
                ClassNameIndex = classNameIndex;
                ObjectNameIndex = objectNameIndex;
                FunctionNameIndex = functionNameIndex;
            }
        }

        private const int AllocationSize = 0x20000;
        private const int HandlerOffset = 0x0000;
        private const int TrampolineOffset = 0x1000;
        private const int HeaderOffset = 0x2000;
        private const int RecordsOffset = 0x3000;
        private const int CountersOffset = 0x18000;
        private const int RecordSize = 24;
        private const int MaximumRecords = (CountersOffset - RecordsOffset) / RecordSize;
        private const int MaximumCounters = (AllocationSize - CountersOffset) / sizeof(ulong);

        private readonly ProcessWrapper game;
        private readonly Action<string> log;
        private readonly RemoteProcessMemory memory;
        private readonly int uObjectClassOffset;
        private readonly int uObjectNameOffset;

        private IntPtr allocation;
        private IntPtr processEvent;
        private byte[] originalProcessEventBytes;
        private byte[] installedPatch;
        private int recordCount;
        private int counterCount;
        private bool disposed;

        public Ue5FunctionHook(
            ProcessWrapper game,
            int uObjectClassOffset,
            int uObjectNameOffset,
            Action<string> log)
        {
            this.game = game ?? throw new ArgumentNullException(nameof(game));
            this.log = log;
            if (uObjectClassOffset <= 0)
                throw new ArgumentOutOfRangeException(nameof(uObjectClassOffset));
            if (uObjectNameOffset <= 0)
                throw new ArgumentOutOfRangeException(nameof(uObjectNameOffset));

            this.uObjectClassOffset = uObjectClassOffset;
            this.uObjectNameOffset = uObjectNameOffset;
            memory = new RemoteProcessMemory(game);

            try
            {
                Initialize();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public IntPtr AddFunctionFlag(IReadOnlyList<FunctionMatch> matches)
        {
            ThrowIfDisposed();
            if (matches == null || matches.Count == 0)
                throw new ArgumentException("At least one native function match is required.", nameof(matches));
            if (recordCount + matches.Count > MaximumRecords)
                throw new InvalidOperationException("The UE5 event hook has no remaining match-record capacity.");
            if (counterCount >= MaximumCounters)
                throw new InvalidOperationException("The UE5 event hook has no remaining counter capacity.");

            IntPtr counterAddress = allocation + CountersOffset + counterCount * sizeof(ulong);
            memory.Write(counterAddress, new byte[sizeof(ulong)]);

            byte[] records = new byte[matches.Count * RecordSize];
            for (int i = 0; i < matches.Count; i++)
            {
                FunctionMatch match = matches[i];
                int offset = i * RecordSize;
                Array.Copy(BitConverter.GetBytes(match.ClassNameIndex), 0, records, offset + 0, 4);
                Array.Copy(BitConverter.GetBytes(match.ObjectNameIndex), 0, records, offset + 4, 4);
                Array.Copy(BitConverter.GetBytes(match.FunctionNameIndex), 0, records, offset + 8, 4);
                // offset + 12 is reserved/alignment.
                Array.Copy(BitConverter.GetBytes((ulong)counterAddress.ToInt64()), 0, records, offset + 16, 8);
            }

            IntPtr writeAddress = allocation + RecordsOffset + recordCount * RecordSize;
            memory.Write(writeAddress, records);

            // Publish the new records only after all record bytes and the counter
            // have been written. ProcessEvent reads this count on every call.
            recordCount += matches.Count;
            counterCount++;
            memory.Write(allocation + HeaderOffset, BitConverter.GetBytes(recordCount));

            return counterAddress;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            try
            {
                if (processEvent != IntPtr.Zero &&
                    originalProcessEventBytes != null &&
                    installedPatch != null &&
                    !game.Process.HasExited)
                {
                    // Do not overwrite a later third-party hook. Restore only when
                    // the bytes at ProcessEvent are still exactly our patch.
                    if (memory.Matches(processEvent, installedPatch))
                    {
                        memory.PatchCode(processEvent, originalProcessEventBytes);
                        log?.Invoke("[UE5Events] ProcessEvent hook restored");
                    }
                    else
                    {
                        log?.Invoke("[UE5Events] ProcessEvent changed after installation; original bytes were not restored");
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("[UE5Events] Hook cleanup failed: " + ex.Message);
            }

            // The remote allocation is intentionally not freed while the process
            // is alive. A game thread may already be returning through the
            // trampoline. The allocation disappears when the game process exits.
        }

        private void Initialize()
        {
            Ue5EventScanner.ProcessEventLocation location =
                Ue5EventScanner.FindProcessEvent(game, log);

            processEvent = location.Address;
            originalProcessEventBytes = memory.Read(processEvent, location.OverwriteLength);
            allocation = memory.AllocateExecutable(AllocationSize);

            IntPtr handlerAddress = allocation + HandlerOffset;
            IntPtr trampolineAddress = allocation + TrampolineOffset;
            IntPtr headerAddress = allocation + HeaderOffset;
            IntPtr recordsAddress = allocation + RecordsOffset;

            byte[] handler = BuildHandler(
                headerAddress,
                uObjectClassOffset,
                uObjectNameOffset);
            if (handler.Length >= TrampolineOffset - HandlerOffset)
                throw new InvalidOperationException("The generated UE5 event handler is too large.");

            memory.Write(handlerAddress, handler);

            byte[] header = new byte[16];
            Array.Copy(BitConverter.GetBytes(0), 0, header, 0, 4);
            Array.Copy(BitConverter.GetBytes(RecordSize), 0, header, 4, 4);
            Array.Copy(BitConverter.GetBytes((ulong)recordsAddress.ToInt64()), 0, header, 8, 8);
            memory.Write(headerAddress, header);

            byte[] trampoline = BuildTrampoline(
                trampolineAddress,
                handlerAddress,
                originalProcessEventBytes,
                processEvent + location.OverwriteLength);
            memory.Write(trampolineAddress, trampoline);

            byte[] absoluteJump = BuildAbsoluteJump(trampolineAddress);
            installedPatch = new byte[location.OverwriteLength];
            Array.Copy(absoluteJump, installedPatch, absoluteJump.Length);
            for (int i = absoluteJump.Length; i < installedPatch.Length; i++)
                installedPatch[i] = 0x90;

            memory.PatchCode(processEvent, installedPatch);
            log?.Invoke("[UE5Events] ProcessEvent function hook installed");
        }

        private static byte[] BuildTrampoline(
            IntPtr trampolineAddress,
            IntPtr handlerAddress,
            byte[] stolenBytes,
            IntPtr returnAddress)
        {
            var bytes = new List<byte>();

            // call handlerAddress (both addresses are within one nearby allocation)
            bytes.Add(0xE8);
            long nextInstruction = trampolineAddress.ToInt64() + 5;
            long relative = handlerAddress.ToInt64() - nextInstruction;
            if (relative < Int32.MinValue || relative > Int32.MaxValue)
                throw new InvalidOperationException("The generated UE5 handler is outside rel32 call range.");
            bytes.AddRange(BitConverter.GetBytes((int)relative));

            bytes.AddRange(stolenBytes);
            bytes.AddRange(BuildAbsoluteJump(returnAddress));
            return bytes.ToArray();
        }

        private static byte[] BuildAbsoluteJump(IntPtr destination)
        {
            var bytes = new List<byte>(14)
            {
                0xFF, 0x25, 0x00, 0x00, 0x00, 0x00
            };
            bytes.AddRange(BitConverter.GetBytes((ulong)destination.ToInt64()));
            return bytes.ToArray();
        }

        private static byte[] BuildHandler(
            IntPtr headerAddress,
            int classOffset,
            int nameOffset)
        {
            var code = new X64CodeBuilder();

            // Preserve flags and every general-purpose register. No external
            // functions are called, so XMM registers are untouched.
            code.Emit(0x9C);                         // pushfq
            code.Emit(0x50, 0x53, 0x51, 0x52);       // rax, rbx, rcx, rdx
            code.Emit(0x55, 0x56, 0x57);             // rbp, rsi, rdi
            code.Emit(0x41, 0x50, 0x41, 0x51);       // r8, r9
            code.Emit(0x41, 0x52, 0x41, 0x53);       // r10, r11
            code.Emit(0x41, 0x54, 0x41, 0x55);       // r12, r13
            code.Emit(0x41, 0x56, 0x41, 0x57);       // r14, r15

            code.Emit(0x48, 0x85, 0xC9);             // test rcx, rcx
            code.JumpEqual("done");
            code.Emit(0x48, 0x85, 0xD2);             // test rdx, rdx
            code.JumpEqual("done");

            code.Emit(0x49, 0xBB);                   // mov r11, imm64
            code.EmitInt64(headerAddress.ToInt64());
            code.Emit(0x45, 0x8B, 0x0B);             // mov r9d, [r11]
            code.Emit(0x45, 0x85, 0xC9);             // test r9d, r9d
            code.JumpEqual("done");
            code.Emit(0x4D, 0x8B, 0x43, 0x08);       // mov r8, [r11+8]

            code.Emit(0x8B, 0x82);                   // mov eax, [rdx+nameOffset]
            code.EmitInt32(nameOffset);
            code.Emit(0x4C, 0x8B, 0x91);             // mov r10, [rcx+classOffset]
            code.EmitInt32(classOffset);
            code.Emit(0x4D, 0x85, 0xD2);             // test r10, r10
            code.JumpEqual("done");
            code.Emit(0x41, 0x8B, 0x9A);             // mov ebx, [r10+nameOffset]
            code.EmitInt32(nameOffset);
            code.Emit(0x8B, 0x89);                   // mov ecx, [rcx+nameOffset]
            code.EmitInt32(nameOffset);

            code.MarkLabel("loop");
            code.Emit(0x41, 0x8B, 0x10);             // mov edx, [r8]
            code.Emit(0x85, 0xD2);                   // test edx, edx
            code.JumpEqual("class_ok");
            code.Emit(0x39, 0xDA);                   // cmp edx, ebx
            code.JumpNotEqual("next");

            code.MarkLabel("class_ok");
            code.Emit(0x41, 0x8B, 0x50, 0x04);       // mov edx, [r8+4]
            code.Emit(0x85, 0xD2);                   // test edx, edx
            code.JumpEqual("object_ok");
            code.Emit(0x39, 0xCA);                   // cmp edx, ecx
            code.JumpNotEqual("next");

            code.MarkLabel("object_ok");
            code.Emit(0x41, 0x8B, 0x50, 0x08);       // mov edx, [r8+8]
            code.Emit(0x85, 0xD2);                   // test edx, edx
            code.JumpEqual("function_ok");
            code.Emit(0x39, 0xC2);                   // cmp edx, eax
            code.JumpNotEqual("next");

            code.MarkLabel("function_ok");
            code.Emit(0x4D, 0x8B, 0x58, 0x10);       // mov r11, [r8+16]
            code.Emit(0xF0, 0x49, 0xFF, 0x03);       // lock inc qword [r11]

            code.MarkLabel("next");
            code.Emit(0x49, 0x83, 0xC0, (byte)RecordSize); // add r8, 24
            code.Emit(0x41, 0xFF, 0xC9);             // dec r9d
            code.JumpNotEqual("loop");

            code.MarkLabel("done");
            code.Emit(0x41, 0x5F, 0x41, 0x5E);       // r15, r14
            code.Emit(0x41, 0x5D, 0x41, 0x5C);       // r13, r12
            code.Emit(0x41, 0x5B, 0x41, 0x5A);       // r11, r10
            code.Emit(0x41, 0x59, 0x41, 0x58);       // r9, r8
            code.Emit(0x5F, 0x5E, 0x5D);             // rdi, rsi, rbp
            code.Emit(0x5A, 0x59, 0x5B, 0x58);       // rdx, rcx, rbx, rax
            code.Emit(0x9D, 0xC3);                   // popfq; ret

            return code.Build();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(Ue5FunctionHook));
        }
    }
}
