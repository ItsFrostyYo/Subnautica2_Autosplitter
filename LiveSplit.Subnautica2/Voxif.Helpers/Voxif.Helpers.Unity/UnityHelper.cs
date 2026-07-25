using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Voxif.Helpers.MemoryHelper;
using Voxif.Helpers.StructReflector;
using Voxif.IO;
using Voxif.Memory;

namespace Voxif.Helpers.Unity {
    public class UnityHelperTask : HelperTask {

        protected readonly ProcessWrapper wrapper;

        public static class UnityNameUtil
        {
            public static bool NameHas(string name, params string[] needles)
            {
                name = name ?? string.Empty;
                foreach (var s in needles)
                {
                    if (name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                return false;
            }
        }

        public UnityHelperTask(ProcessWrapper wrapper, Logger logger = null) : base(logger) {
            this.wrapper = wrapper;
        }

        protected override void Log(string msg) => logger?.Log("[Unity] " + msg);

        public Task Run(Action<IMonoHelper> action) {
            return Run(null, action);
        }
        public Task Run(Version version, Action<IMonoHelper> action) {
            return Run(() => {
                UnityHelperBase unity;

                string monoModule;
                while(true) {
                    monoModule = wrapper.Process.Modules().FirstOrDefault(
                        m => m.ModuleName.Equals(UnityHelperBase.monoV1Assembly)
                          || m.ModuleName.Equals(UnityHelperBase.monoV2Assembly)
                          || m.ModuleName.Equals(UnityHelperBase.il2cppAssembly))?.ModuleName;

                    if(monoModule != null) {
                        break;
                    }

                    Sleep();
                }

                if(monoModule.Equals(UnityHelperBase.monoV1Assembly)) {
                    unity = new Mono1Helper(wrapper, token, "v1", logger);
                } else if(monoModule.Equals(UnityHelperBase.monoV2Assembly)) {
                    unity = new Mono2Helper(wrapper, token, "v2", logger);
                } else {
                    if(version == null) {
                        version = FetchVersionFromFiles();
                    }
                    string fileVersion;
                    if(version.CompareTo(new Version(2019, 0)) >= 0) {
                        fileVersion = "2019";
                    } else {
                        //2018.3+
                        fileVersion = "Base";
                    }
                    unity = new Il2CppHelper(wrapper, token, fileVersion, logger);
                }

                action(unity);
            });
        }

        private Version FetchVersionFromFiles() {
            const string ggm = "globalgamemanagers";
            const string md = "mainData";
            const string du3d = "data.unity3d";

            Log("Try to retreive version from files");

            string path = wrapper.Process.MainModule.FileName.Substring(0, wrapper.Process.MainModule.FileName.Length - 4) + "_Data\\";
            bool globalGameExist = File.Exists(path + ggm);
            if(globalGameExist || File.Exists(path + md)) {
                BinaryReaderEndian reader = new BinaryReaderEndian(path + (globalGameExist ? ggm : md), true);

                uint metadataSize = reader.ReadUInt32();
                long fileSize = (long)(ulong)reader.ReadUInt32();
                uint version = reader.ReadUInt32();
                reader.ReadUInt32();

                if(version >= 9) {
                    reader.ReadByte();
                    reader.ReadBytes(3);
                } else {
                    reader.BaseStream.Position = fileSize - (long)(ulong)metadataSize;
                    reader.ReadByte();
                }
                if(version >= 22) {
                    reader.ReadUInt32();
                    reader.ReadInt64();
                    reader.ReadInt64();
                    reader.ReadInt64();
                }
                if(version >= 7) {
                    char[] chars = reader.ReadCharsNullTerminated();
                    Log("  -> " + new string(chars));
                    return UnityVersionToString(chars);
                }
            } else if(File.Exists(path + du3d)) {
                BinaryReaderEndian reader = new BinaryReaderEndian(path + du3d, true);

                reader.ReadCharsNullTerminated();
                reader.ReadInt32();
                reader.ReadCharsNullTerminated();
                char[] chars = reader.ReadCharsNullTerminated();
                Log("  -> " + new string(chars));
                return UnityVersionToString(chars);
            }
            Log("  -> not found");
            return new Version();

            Version UnityVersionToString(char[] chars) {
                for(int i = chars.Length - 1; i >= 0; i--) {
                    if(!Char.IsDigit(chars[i])) {
                        chars[i] = '.';
                        break;
                    }
                }
                return new Version(new string(chars));
            }
        }

        public class BinaryReaderEndian : BinaryReader {

            public bool isBigEndian;

            public BinaryReaderEndian(Stream stream, bool isBigEndian = false) : base(stream) {
                this.isBigEndian = isBigEndian;
            }
            public BinaryReaderEndian(string path, bool isBigEndian = false)
                : this(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), isBigEndian) { }

            public override uint ReadUInt32() => isBigEndian ? (uint)ReadBigEndian(4) : base.ReadUInt32();
            public override int ReadInt32() => isBigEndian ? (int)ReadBigEndian(4) : base.ReadInt32();

            public override ulong ReadUInt64() => isBigEndian ? ReadBigEndian(8) : base.ReadUInt64();
            public override long ReadInt64() => isBigEndian ? (long)ReadBigEndian(8) : base.ReadInt64();

            private ulong ReadBigEndian(int bytes) {
                byte[] b = ReadBytes(bytes);
                ulong value = b[bytes - 1];
                for(int i = bytes - 2; i >= 0; i--) {
                    value |= (uint)b[i] << ((bytes - 1 - i) * 8);
                }
                return value;
            }

            public char[] ReadCharsNullTerminated() {
                List<byte> stringBytes = new List<byte>();
                while(BaseStream.Position != BaseStream.Length && stringBytes.Count < 2048) {
                    byte c = ReadByte();
                    if(c == 0) {
                        break;
                    }
                    stringBytes.Add(c);
                }
                return Encoding.UTF8.GetChars(stringBytes.ToArray());
            }
        }

        public abstract class UnityHelperBase : IMonoHelper {            

            public const string monoV1Assembly = "mono.dll";
            public const string monoV2Assembly = "mono-2.0-bdwgc.dll";
            public const string il2cppAssembly = "GameAssembly.dll";

            protected const int FIELD_ATTRIBUTE_STATIC = 0x10;

            protected CancellationToken token;

            protected readonly ProcessWrapper wrapper;

            protected readonly Logger logger;

            protected IStructReflector data;

            protected abstract string AssemblyName { get; }
            public IntPtr MainImage { get; }

            protected UnityHelperBase(ProcessWrapper game, CancellationToken token, string fileName, string fileVersion, Logger logger = null) {
                this.wrapper = game;
                this.token = token;
                this.logger = logger;
                Log($"Version: {fileName}.{fileVersion}");
                data = StructReflector.StructReflector.Load(fileName + "_" + fileVersion, game.PointerSize);
                MainImage = FindImage("Assembly-CSharp");
            }

            protected virtual IntPtr AssemblyImage(IntPtr assembly) {
                return wrapper.Read<IntPtr>(assembly + data.GetOffset("MonoAssembly", "image"));
            }

            protected abstract IEnumerable<IntPtr> ImageSequence();
            public virtual string ImageName(IntPtr image) {
                return GetName(image + data.GetOffset("MonoImage", "assembly_name"));
            }
            protected virtual IntPtr ImageClass(IntPtr table, int offset) {
                return wrapper.Read<IntPtr>(table + offset * wrapper.PointerSize);
            }
            public abstract IntPtr FindImage(string imageToFind);


            protected virtual IntPtr ClassParent(IntPtr klass) {
                return wrapper.Read<IntPtr>(klass + data.GetOffset("MonoClass", "parent"));
            }
            protected virtual string ClassName(IntPtr klass) {
                return GetName(klass + data.GetOffset("MonoClass", "name"));
            }
            protected virtual string ClassNamespace(IntPtr klass) {
                return GetName(klass + data.GetOffset("MonoClass", "name_space"));
            }
            protected virtual bool ClassHasFields(IntPtr klass, out IntPtr fields) {
                return (fields = wrapper.Read<IntPtr>(klass + data.GetOffset("MonoClass", "fields"))) != default;
            }
            protected virtual int ClassFieldCount(IntPtr klass) {
                return wrapper.Read<int>(klass + data.GetOffset("MonoClass", "field_count"));
            }
            protected abstract IEnumerable<IntPtr> ClassSequence(IntPtr image);
            public virtual IntPtr FindClass(string classToFind) {
                return FindClass(classToFind, MainImage);
            }
            public virtual IntPtr FindClass(string classToFind, IntPtr image) {
                Log("Looking for class: " + classToFind);
                int namespaceId = classToFind.LastIndexOf('.');
                string namespaceStr = null;
                if(namespaceId != -1) {
                    namespaceStr = classToFind.Substring(0, namespaceId);
                    classToFind = classToFind.Substring(namespaceId + 1);
                }
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(IntPtr klass in ClassSequence(image)) {
                        if(classToFind.Equals(ClassName(klass)) && (namespaceId == -1 || namespaceStr.Equals(ClassNamespace(klass)))) {
                            Log("  -> " + klass.ToString("X"));
                            return klass;
                        }
                    }
                    Sleep();
                }
            }

            protected virtual string FieldName(IntPtr field) {
                return GetName(field + data.GetOffset("MonoClassField", "name"));
            }
            protected virtual IntPtr FieldParent(IntPtr field) {
                return wrapper.Read<IntPtr>(field + data.GetOffset("MonoClassField", "parent"));
            }
            protected virtual int FieldOffset(IntPtr field) {
                return wrapper.Read<int>(field + data.GetOffset("MonoClassField", "offset"));
            }
            protected virtual bool FieldIsStatic(IntPtr field) {
                IntPtr type = wrapper.Read<IntPtr>(field + data.GetOffset("MonoClassField", "type"));
                return (wrapper.Read<ushort>(type + data.GetOffset("MonoType", "attrs")) & FIELD_ATTRIBUTE_STATIC) != 0;
            }
            protected virtual IEnumerable<IntPtr> FieldSequence(IntPtr klass, bool includeParents) {
                int classFieldSize = data.GetSelfAlignedSize("MonoClassField");
                while(klass != default) {
                    if(ClassHasFields(klass, out IntPtr fields)) {
                        for(int i = 0; i < ClassFieldCount(klass); i++) {
                            yield return fields + classFieldSize * i;
                        }
                    }
                    if(includeParents) {
                        klass = ClassParent(klass);
                    } else {
                        break;
                    }
                }
            }
            public virtual int GetFieldOffset(string className, string fieldName, bool includeParents = true) {
                return GetFieldOffset(MainImage, className, fieldName, out _, includeParents);
            }
            public virtual int GetFieldOffset(IntPtr image, string className, string fieldName, bool includeParents = true) {
                IntPtr klass = FindClass(className, image);
                return GetFieldOffset(klass, fieldName, includeParents);
            }
            public virtual int GetFieldOffset(string className, string fieldName, out IntPtr klass, bool includeParents = true) {
                return GetFieldOffset(MainImage, className, fieldName, out klass, includeParents);
            }
            public virtual int GetFieldOffset(IntPtr image, string className, string fieldName, out IntPtr klass, bool includeParents = true) {
                klass = FindClass(className, image);
                return GetFieldOffset(klass, fieldName, includeParents);
            }
            public virtual int GetFieldOffset(IntPtr klass, string fieldName, bool includeParents = true) {
                Log("Looking for field: " + fieldName);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(IntPtr field in FieldSequence(klass, includeParents)) {
                        if(!fieldName.Equals(FieldName(field))) {
                            continue;
                        }
                        int offset = FieldOffset(field);
                        Log("  -> " + offset.ToString("X"));
                        return offset;
                    }
                    Sleep();
                }
            }
            public virtual IntPtr GetStaticField(IntPtr image, string className, string fieldName, out IntPtr klass, out int staticOffset, bool includeParents = true) {
                klass = FindClass(className, image);
                return GetStaticField(klass, fieldName, out staticOffset, includeParents);
            }
            public virtual IntPtr GetStaticField(IntPtr klass, string fieldName, out int staticOffset, bool includeParents = true) {
                Log("Looking for static: " + fieldName);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(IntPtr field in FieldSequence(klass, includeParents)) {
                        if(!FieldIsStatic(field) || !fieldName.Equals(FieldName(field))) {
                            continue;
                        }
                        staticOffset = FieldOffset(field);
                        IntPtr staticClass = FieldParent(field);
                        Log("  -> " + staticClass.ToString("X") + " " + staticOffset.ToString("X"));
                        return staticClass;
                    }
                    Sleep();
                }
            }

            public abstract IntPtr GetStaticAddress(IntPtr klass);

            protected string GetName(IntPtr ptr) {
                return wrapper.ReadString(wrapper.Read<IntPtr>(ptr), EStringType.UTF8);
            }

            public int ResolveFieldOffsetByNameOrPredicate(
                IntPtr klass,
                IEnumerable<string> nameCandidates,
                Func<string, bool> namePredicate,
                bool includeParents = true)
            {
                if (nameCandidates != null)
                {
                    foreach (var n in nameCandidates)
                    {
                        if (string.IsNullOrEmpty(n)) continue;
                        int off = GetFieldOffset(klass, n, includeParents);
                        if (off != 0) return off;
                    }
                }

                foreach (IntPtr field in FieldSequence(klass, includeParents))
                {
                    string fname = FieldName(field);
                    if (namePredicate?.Invoke(fname) == true)
                    {
                        int off = FieldOffset(field);
                        Log($"  -> predicate matched '{fname}', off {off:X}");
                        return off;
                    }
                }

                return 0;
            }
            public IntPtr TryFindImageOnce(params string[] names)
            {
                foreach (var image in ImageSequence())
                {
                    var n = ImageName(image);
                    foreach (var want in names)
                    {
                        if (string.Equals(n, want, StringComparison.OrdinalIgnoreCase))
                        {
                            return image;
                        }
                    }
                }
                return IntPtr.Zero;
            }

            public IntPtr TryFindClassOnce(string className, IntPtr image)
            {
                int dot = className.LastIndexOf('.');
                string ns = dot >= 0 ? className.Substring(0, dot) : null;
                string simple = dot >= 0 ? className.Substring(dot + 1) : className;

                foreach (var klass in ClassSequence(image))
                {
                    if (string.Equals(simple, ClassName(klass), StringComparison.Ordinal)
                     && (ns == null || string.Equals(ns, ClassNamespace(klass), StringComparison.Ordinal)))
                    {
                        return klass;
                    }
                }
                return IntPtr.Zero;
            }

            public int PickSlotsOffset(IntPtr hs)
            {
                int[] candidates = { 0x10, 0x18 };
                int bestOff = 0; int bestScore = -1;

                foreach (int off in candidates)
                {
                    IntPtr arr = wrapper.Read<IntPtr>(hs + off);
                    if (arr == IntPtr.Zero) continue;

                    int len = wrapper.Read<int>(arr + 0x18);
                    if (len <= 0 || len > 50000) continue;

                    IntPtr data = arr + 0x20;
                    int score12 = 0, score16 = 0, probe = Math.Min(len, 64);
                    for (int i = 0; i < probe; i++)
                    {
                        int v12 = wrapper.Read<int>(data + i * 12 + 8);
                        if (v12 > 0 && v12 < 10005) score12++;
                        int v16 = wrapper.Read<int>(data + i * 16 + 8);
                        if (v16 > 0 && v16 < 10005) score16++;
                    }
                    int score = Math.Max(score12, score16);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestOff = off;
                    }
                }
                return bestOff; // 0 = fail
            }                   

            public void Log(string msg) => logger?.Log("[Unity] " + msg);
        }

        private class Mono1Helper : UnityHelperBase {
            protected override string AssemblyName => monoV1Assembly;

            public Mono1Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : base(game, token, "Mono", fileVersion, logger)
            {
                if (fileVersion != "v1")
                    return;

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    foreach (IntPtr klass in ClassSequence(MainImage))
                    {
                        int imageOff = data.GetOffset("MonoClass", "image");
                        IntPtr imageField = klass + imageOff;

                        if (game.Read<IntPtr>(imageField) == MainImage)
                        {
                            Log("  -> Mono_v1 (no cattrs)");
                            return;
                        }

                        if (game.Read<IntPtr>(imageField + game.PointerSize) == MainImage)
                        {
                            Log("  -> Mono_v1_cattrs detected, reloading struct map");
                            data = StructReflector.StructReflector.Load(
                                "Voxif.Helpers.UnityHelper.Mono_v1_cattrs", game.PointerSize);
                            return;
                        }
                    }

                    Sleep();
                }
            }


            protected virtual IntPtr GListData(IntPtr glist) {
                return wrapper.Read<IntPtr>(glist + data.GetOffset("GList", "data"));
            }
            protected virtual IntPtr GListNext(IntPtr glist) {
                return wrapper.Read<IntPtr>(glist + data.GetOffset("GList", "next"));
            }


            public override IntPtr FindImage(string imageToFind) {
                Log("Looking for image: " + imageToFind);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(IntPtr image in ImageSequence()) {
                        if(imageToFind.Equals(ImageName(image))) {
                            Log("  -> " + image.ToString("X"));
                            return image;
                        }
                    }
                    Sleep();
                }
            }
            protected override IEnumerable<IntPtr> ImageSequence() {
                IntPtr assembliesFunc = wrapper.Process.SymbolAddress(AssemblyName, "mono_assembly_foreach");
                if(assembliesFunc == default) {
                    Sleep();
                    yield break;
                }
                ScanTarget signature = wrapper.Is64Bit ? new ScanTarget(3, "48 8B 0D")
                                                    : new ScanTarget(2, "FF 35");
                assembliesFunc = new SignatureScanner(wrapper, assembliesFunc, 0x100).Scan(signature);
                IntPtr assemblies = wrapper.Read<IntPtr>(wrapper.FromAssemblyAddress(assembliesFunc));
                while(assemblies != default) {
                    yield return AssemblyImage(GListData(assemblies));
                    assemblies = GListNext(assemblies);
                }
            }


            protected virtual IntPtr ClassVTable(IntPtr klass) {
                IntPtr runtimeInfo = wrapper.Read<IntPtr>(klass + data.GetOffset("MonoClass", "runtime_info"));
                return wrapper.Read<IntPtr>(runtimeInfo + data.GetOffset("MonoClassRuntimeInfo", "domain_vtables"));
            }
            protected virtual IntPtr ClassNextClassCache(IntPtr klass) {
                return wrapper.Read<IntPtr>(klass + data.GetOffset("MonoClass", "next_class_cache"));
            }
            protected override IEnumerable<IntPtr> ClassSequence(IntPtr image) {
                IntPtr cache = image + data.GetOffset("MonoImage", "class_cache");
                int size = wrapper.Read<int>(cache + data.GetOffset("MonoInternalHashTable", "size"));
                IntPtr table = wrapper.Read<IntPtr>(cache + data.GetOffset("MonoInternalHashTable", "table"));
                for(int i = 0; i < size; i++) {
                    IntPtr klass = ImageClass(table, i);
                    while(klass != default) {
                        yield return klass;
                        klass = ClassNextClassCache(klass);
                    }
                }
            }

            public override IntPtr GetStaticAddress(IntPtr klass) {
                int vtableOffset = data.GetOffset("MonoVTable", "data");
                return wrapper.Read<IntPtr>(ClassVTable(klass) + vtableOffset);
            }
        }

        private class Mono2Helper : Mono1Helper {
            private enum MonoTypeKind {
                MONO_CLASS_DEF = 1,
                MONO_CLASS_GTD,
                MONO_CLASS_GINST,
                MONO_CLASS_GPARAM,
                MONO_CLASS_ARRAY,
                MONO_CLASS_POINTER,
            }

            protected override string AssemblyName => monoV2Assembly;

            public Mono2Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : base(game, token, fileVersion, logger) { }

            protected virtual IntPtr ClassGenericClass(IntPtr klass) {
                IntPtr genericInst = wrapper.Read<IntPtr>(klass + data.GetOffset("MonoClassGenericInst", "generic_class"));
                return wrapper.Read<IntPtr>(genericInst + data.GetOffset("MonoGenericClass", "container_class"));
            }
            protected override int ClassFieldCount(IntPtr klass) {
                switch((MonoTypeKind)(wrapper.Read<byte>(klass + data.GetOffset("MonoClass", "class_kind")) & 0b111)) {
                    case MonoTypeKind.MONO_CLASS_DEF:
                    case MonoTypeKind.MONO_CLASS_GTD:
                        return wrapper.Read<int>(klass + data.GetOffset("MonoClassDef", "field_count"));
                    case MonoTypeKind.MONO_CLASS_GINST:
                        return ClassFieldCount(ClassGenericClass(klass));
                    default:
                        return default;
                }
            }
            protected override IntPtr ClassNextClassCache(IntPtr klass) {
                return wrapper.Read<IntPtr>(klass + data.GetOffset("MonoClassDef", "next_class_cache"));
            }


            public override IntPtr GetStaticAddress(IntPtr klass) {
                int vtableOffset = data.GetOffset("MonoVTable", "vtable");
                int vtableSize = wrapper.Read<int>(klass + data.GetOffset("MonoClass", "vtable_size"));
                return wrapper.Read<IntPtr>(ClassVTable(klass) + vtableOffset + vtableSize * wrapper.PointerSize);
            }
        }

        private class Il2CppHelper : UnityHelperBase {
            protected override string AssemblyName => il2cppAssembly;

            private bool isMasterCompiler;

            public Il2CppHelper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : base(game, token, "Il2cpp", fileVersion, logger) { }

            public override IntPtr FindImage(string imageToFind) {
                Log("Looking for image: " + imageToFind);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(IntPtr image in ImageSequence()) {
                        string imageName = ImageName(image);
                        if(imageToFind.Equals(imageName.Substring(0, imageName.Length - 4))) {
                            Log("  -> " + image.ToString("X"));
                            return image;
                        }
                    }
                    Sleep();
                }
            }
            protected override IEnumerable<IntPtr> ImageSequence() {
                IntPtr assembliesFunc = wrapper.Process.SymbolAddress(AssemblyName, "il2cpp_domain_get_assemblies");
                if(assembliesFunc == default) {
                    yield break;
                }

                IntPtr assemblies;
                if(wrapper.Is64Bit) {
                    if(wrapper.Read<byte>(assembliesFunc) == 0x40) {
                        isMasterCompiler = false;
                        assemblies = wrapper.FromRelativeAddress(wrapper.FromRelativeAddress(assembliesFunc + 0xA) + 3);
                    } else {
                        isMasterCompiler = true;
                        assemblies = wrapper.FromRelativeAddress(assembliesFunc + 0xA);
                    }
                } else {
                    if(wrapper.Read<byte>(assembliesFunc + 0x3) == 0xE8) {
                        isMasterCompiler = false;
                        assemblies = wrapper.FromAbsoluteAddress(wrapper.FromRelativeAddress(assembliesFunc + 0x4) + 1);
                    } else {
                        isMasterCompiler = true;
                        assemblies = wrapper.FromAbsoluteAddress(assembliesFunc + 0xE);
                    }
                }
                assemblies = wrapper.Read<IntPtr>(assemblies);

                IntPtr image;
                while((image = AssemblyImage(wrapper.Read<IntPtr>(assemblies))) != default) {
                    yield return image;
                    assemblies += wrapper.PointerSize;
                }
            }


            protected override int ClassFieldCount(IntPtr klass) {
                return wrapper.Read<short>(klass + data.GetOffset("MonoClass", "field_count"));
            }
            protected override IEnumerable<IntPtr> ClassSequence(IntPtr image) {
                // Might also straight search for MetadataCache::GetTypeInfoFromTypeDefinitionIndex
                IntPtr classesFunc = wrapper.Process.SymbolAddress(AssemblyName, "il2cpp_image_get_class");
                IntPtr jmpAddress = new SignatureScanner(wrapper, classesFunc, 0x100).Scan(new ScanTarget(0x1, "E9 ???????? CC"));
                IntPtr typeDefinitionFunc = wrapper.FromRelativeAddress(jmpAddress);

                if(!isMasterCompiler) {
                    typeDefinitionFunc = wrapper.FromRelativeAddress(typeDefinitionFunc + (wrapper.Is64Bit ? 0x6 : 0xE));
                }
                
                IntPtr table;
                SignatureScanner typeDefScanner = new SignatureScanner(wrapper, typeDefinitionFunc, 0x100);
                ScanTarget scanTarget;
                if(wrapper.Is64Bit) {
                    scanTarget = new ScanTarget(0xB, "48 8D 1C FD ???????? 48 8B 05");
                } else {
                    scanTarget = new ScanTarget(0x5, "8B E5 5D C3 A1 ???????? 83 3C B0");
                }
                table = wrapper.Read<IntPtr>(wrapper.FromAssemblyAddress(typeDefScanner.Scan(scanTarget)));

                int offset = wrapper.Read<int>(image + data.GetOffset("MonoImage", "table_offset"));
                int size = wrapper.Read<int>(image + data.GetOffset("MonoImage", "class_count"));
                for(int i = 0; i < size; i++) {
                    yield return ImageClass(table, offset + i);
                }
            }

            public override IntPtr GetStaticAddress(IntPtr klass) {
                return wrapper.Read<IntPtr>(klass + data.GetOffset("MonoClass", "data"));
            }
        }
    }

    public interface IMonoHelper {
        IntPtr MainImage { get; }
        
        IntPtr FindImage(string imageToFind);
        
        IntPtr FindClass(string classToFind);
        IntPtr FindClass(string classToFind, IntPtr image);

        IntPtr GetStaticField(IntPtr image, string className, string fieldName, out IntPtr klass, out int staticOffset, bool includeParents = true);
        IntPtr GetStaticField(IntPtr klass, string fieldName, out int staticOffset, bool includeParents = true);
        
        int GetFieldOffset(IntPtr image, string className, string fieldName, out IntPtr klass, bool includeParents = true);
        int GetFieldOffset(IntPtr image, string className, string fieldName, bool includeParents = true);
        int GetFieldOffset(string className, string fieldName, out IntPtr klass, bool includeParents = true);
        int GetFieldOffset(string className, string fieldName, bool includeParents = true);
        int GetFieldOffset(IntPtr klass, string fieldName, bool includeParents = true);
        
        IntPtr GetStaticAddress(IntPtr klass);
    }
}