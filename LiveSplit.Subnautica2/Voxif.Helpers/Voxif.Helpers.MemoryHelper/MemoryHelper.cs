using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Voxif.IO;
using Voxif.Memory;

namespace Voxif.Helpers.MemoryHelper {
    public class ScanHelperTask : HelperTask {

        public bool pagesScanAll = false;

        public int msBetweenSeep = 2000;

        protected readonly ProcessWrapper wrapper;

        public ScanHelperTask(ProcessWrapper wrapper, Logger logger = null) : base(logger) {
            this.wrapper = wrapper;
        }

        protected override void Log(string msg) => logger?.Log("[Scan] " + msg);

        public Task Run(ScannableData scanDict, Action<ScannableResult> action = null) {
            return Run(() => {
                var result = ScanMemory(scanDict);
                action?.Invoke(result);
            });
        }

        protected virtual ScannableResult ScanMemory(ScannableData scanDict) {
            Log("Scanning memory");

            var scansResult = new ScannableResult();
            foreach(var moduleScan in scanDict) {
                scansResult.Add(moduleScan.Key, new Dictionary<string, IntPtr>());
                foreach(var holderScan in moduleScan.Value) {
                    scansResult[moduleScan.Key].Add(holderScan.Key, default);
                }
            }

            bool AllSigsFound() {
                foreach(var moduleScan in scanDict) {
                    foreach(var holderScan in moduleScan.Value) {
                        if(holderScan.Value.doScan && scansResult[moduleScan.Key][holderScan.Key] == default) {
                            return default;
                        }
                    }
                }
                return true;
            }

            while(true) {
                token.ThrowIfCancellationRequested();

                foreach(var moduleScan in scanDict) {
                    token.ThrowIfCancellationRequested();

                    if(String.IsNullOrEmpty(moduleScan.Key)) {
                        foreach(MemoryBasicInformation page in wrapper.MemoryPages(pagesScanAll)) {
                            token.ThrowIfCancellationRequested();

                            SearchAllSigs(moduleScan, new SignatureScanner(wrapper, page.BaseAddress, (int)page.RegionSize));
                            if(AllSigsFound()) {
                                break;
                            }
                        }
                    } else {
                        Memory.ProcessModule module = wrapper.Process.Modules().FirstOrDefault(m => m.ModuleName == moduleScan.Key);
                        if(module == null) {
                            continue;
                        }
                        SearchAllSigs(moduleScan, new SignatureScanner(wrapper, module.BaseAddress, module.ModuleMemorySize));
                        if(AllSigsFound()) {
                            break;
                        }
                    }
                }

                token.ThrowIfCancellationRequested();

                if(!AllSigsFound()) {
                    Sleep(msBetweenSeep);
                    continue;
                }

                Log("Done scanning");
                break;
            }

            return scansResult;

            void SearchAllSigs(KeyValuePair<string, Dictionary<string, ScanTarget>> moduleTargets, SignatureScanner scanner) {
                foreach(var kvpHolders in moduleTargets.Value) {
                    token.ThrowIfCancellationRequested();

                    if(!kvpHolders.Value.doScan || scansResult[moduleTargets.Key][kvpHolders.Key] != default) {
                        continue;
                    }

                    IntPtr result = scanner.Scan(kvpHolders.Value);
                    if(result != default) {
                        Log(kvpHolders.Key + " Found at " + result.ToString("X"));
                        scansResult[moduleTargets.Key][kvpHolders.Key] = result;
                    }
                }
            }
        }
    }

    public class ScannableData : Dictionary<string, Dictionary<string, ScanTarget>> { }

    public class ScannableResult : Dictionary<string, Dictionary<string, IntPtr>> { }

    public class ScanTarget {
        public struct Signature {
            public byte[] pattern;
            public bool[] mask;
            public int offset;
            public string version;
        }

        public bool doScan = true;

        public Func<ProcessWrapper, IntPtr, string, bool> IsGoodMatch = null;

        public delegate void OnScanFoundCallback(IntPtr ptr, string version);
        public OnScanFoundCallback OnFound { get; set; }

        private readonly List<Signature> signatures = new List<Signature>();
        public System.Collections.ObjectModel.ReadOnlyCollection<Signature> Signatures {
            get => signatures.AsReadOnly();
        }

        public ScanTarget(int offset, string signature) {
            AddSignature(offset, signature);
        }

        public ScanTarget(int offset, params byte[] signature) {
            AddSignature(offset, signature);
        }

        public ScanTarget() { }

        public ScanTarget AddSignature(string version, int offset, string signature) {
            signatures.Add(CreateSignature(version, offset, signature));
            return this;
        }
        public ScanTarget AddSignature(int offset, string signature) {
            signatures.Add(CreateSignature("", offset, signature));
            return this;
        }
        private Signature CreateSignature(string version, int offset, string signature) {
            string sigStr = signature.Replace(" ", "");
            if(sigStr.Length % 2 != 0) {
                throw new ArgumentException(nameof(signature));
            }

            List<byte> sigBytes = new List<byte>();
            List<bool> sigMask = new List<bool>();
            bool hasMask = false;

            for(int i = 0; i < sigStr.Length; i += 2) {
                if(Byte.TryParse(sigStr.Substring(i, 2), NumberStyles.HexNumber, null, out byte b)) {
                    sigBytes.Add(b);
                    sigMask.Add(false);
                } else {
                    sigBytes.Add(0);
                    sigMask.Add(true);
                    hasMask = true;
                }
            }

            return new Signature {
                pattern = sigBytes.ToArray(),
                mask = hasMask ? sigMask.ToArray() : null,
                offset = offset,
                version = version
            };
        }

        public ScanTarget AddSignature(string version, int offset, params byte[] binary) {
            signatures.Add(CreateSignature(version, offset, binary));
            return this;
        }
        public ScanTarget AddSignature(int offset, params byte[] binary) {
            signatures.Add(CreateSignature("", offset, binary));
            return this;
        }
        private Signature CreateSignature(string version, int offset, params byte[] binary) {
            return new Signature {
                pattern = binary,
                mask = null,
                offset = offset,
                version = version
            };
        }
    }

    public class SignatureScanner {
        private readonly ProcessWrapper wrapper;
        private readonly IntPtr address;
        private readonly int size;
        private byte[] memory;

        public SignatureScanner(ProcessWrapper wrapper, IntPtr address, int size) {
            this.wrapper = wrapper ?? throw new ArgumentNullException(nameof(wrapper));
            this.address = address != default ? address : throw new ArgumentException("addr cannot be IntPtr.Zero.", nameof(address));
            this.size = size > 0 ? size : throw new ArgumentException("size cannot be less than zero.", nameof(size));
            memory = new byte[1];
        }

        public SignatureScanner(byte[] memory) {
            this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
            size = memory.Length;
        }

        public IntPtr Scan(ScanTarget target, int align = 1) {
            if((long)address % align != 0) {
                throw new ArgumentOutOfRangeException(nameof(align), "start address must be aligned");
            }
            return ScanAll(target, align).FirstOrDefault();
        }

        public IEnumerable<IntPtr> ScanAll(ScanTarget target, int align = 1) {
            if((long)address % align != 0) {
                throw new ArgumentOutOfRangeException(nameof(align), "start address must be aligned");
            }
            return ScanInternal(target, align);
        }

        private IEnumerable<IntPtr> ScanInternal(ScanTarget target, int align) {
            if(memory == null || memory.Length != size) {
                if(!wrapper.Process.ReadBytes(address, size, out byte[] bytes)) {
                    memory = null;
                    yield break;
                }
                memory = bytes;
            }

            foreach(ScanTarget.Signature sig in target.Signatures) {
                foreach(int off in new ScanEnumerator(memory, align, sig)) {
                    IntPtr ptr = address + off + sig.offset;
                    if(target.IsGoodMatch?.Invoke(wrapper, ptr, sig.version) ?? true) {
                        target.OnFound?.Invoke(ptr, sig.version);
                        yield return ptr;
                    }
                }
            }
        }

        private class ScanEnumerator : IEnumerator<int>, IEnumerable<int> {
            // IEnumerator
            public int Current { get; private set; }
            object IEnumerator.Current => Current;

            private readonly byte[] memory;
            private readonly int align;
            private readonly ScanTarget.Signature sig;

            private readonly int sigLen;
            private readonly int end;

            private int nextIndex;

            public ScanEnumerator(byte[] mem, int align, ScanTarget.Signature sig) {
                if(mem.Length < sig.pattern.Length) {
                    throw new ArgumentOutOfRangeException(nameof(mem), "memory buffer length must be >= pattern length");
                }

                memory = mem;
                this.align = align;
                this.sig = sig;

                sigLen = this.sig.pattern.Length;
                end = memory.Length - sigLen;
            }

            // IEnumerator
            public bool MoveNext() {
                return sig.mask != null ? NextPattern() : NextBytes();
            }
            public void Reset() {
                nextIndex = 0;
            }
            public void Dispose() {
            }

            // IEnumerable
            public IEnumerator<int> GetEnumerator() {
                return this;
            }
            IEnumerator IEnumerable.GetEnumerator() {
                return this;
            }

            private unsafe bool NextPattern() {
                fixed(bool* mask = sig.mask)
                fixed(byte* memory = this.memory, sig = this.sig.pattern) {
                    int end = this.end;
                    int sigLen = this.sigLen;
                    int align = this.align;

                    for(int index = nextIndex; index < end; index += align) {
                        for(int sigIndex = 0; sigIndex < sigLen; sigIndex++) {
                            if(mask[sigIndex]) {
                                continue;
                            }
                            if(sig[sigIndex] != memory[index + sigIndex]) {
                                goto next;
                            }
                        }

                        Current = index;
                        nextIndex = index + align;
                        return true;
next:
                        ;
                    }

                    return false;
                }
            }

            private unsafe bool NextBytes() {
                fixed(byte* memory = this.memory, sig = this.sig.pattern) {
                    int end = this.end;
                    int align = this.align;
                    int sigLen = this.sigLen;

                    for(int index = nextIndex; index < end; index += align) {
                        for(int sigIndex = 0; sigIndex < sigLen; sigIndex++) {
                            if(sig[sigIndex] != memory[index + sigIndex]) {
                                goto next;
                            }
                        }

                        Current = index;
                        nextIndex = index + align;
                        return true;
next:
                        ;
                    }

                    return false;
                }
            }
        }
    }
}