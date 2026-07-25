using System;
using System.Collections.Generic;
using System.Linq;
using Voxif.Memory;

namespace Voxif.Helpers.Unity {
    public class MonoNestedPointerFactory : NestedPointerFactory {

        public readonly int StringHeaderSize;

        protected IMonoHelper mono;

        public MonoNestedPointerFactory(TickableProcessWrapper wrapper, IMonoHelper monoHelper)
            : this(wrapper, null, monoHelper, EDerefType.Auto) { }
        public MonoNestedPointerFactory(TickableProcessWrapper wrapper, string moduleName, IMonoHelper monoHelper)
            : this(wrapper, moduleName, monoHelper, EDerefType.Auto) { }
        public MonoNestedPointerFactory(TickableProcessWrapper wrapper, string moduleName, IMonoHelper monoHelper, EDerefType derefType)
            : base(wrapper, moduleName, derefType) {
            mono = monoHelper;
            StringHeaderSize = wrapper.PointerSize * 2 + 0x4;
        }


        public MonoBasePointer Make(string className) {
            return Make(mono.MainImage, className, out _);
        }
        public MonoBasePointer Make(string className, out IntPtr klass) {
            return Make(mono.MainImage, className, out klass);
        }
        public MonoBasePointer Make(IntPtr image, string className, out IntPtr klass) {
            klass = mono.FindClass(className, image);
            var monoBase = new MonoBasePointer(wrapper, mono, klass);
            _ = monoBase.New;
            nodeLink.Add(monoBase, new HashSet<IPointer> { });
            return monoBase;
        }


        public Pointer<T> Make<T>(MonoBasePointer parent, string staticFieldName, string fieldName, params int[] offsets) where T : unmanaged {
            mono.GetStaticField(parent.Base, staticFieldName, out var staticOffset);
            return Make<T>(parent, offsets.Prepend(mono.GetFieldOffset(parent.Base, fieldName)).Prepend(staticOffset).ToArray());
        }
        public Pointer<T> Make<T>(MonoBasePointer parent, string staticFieldName, params int[] offsets) where T : unmanaged {
            mono.GetStaticField(parent.Base, staticFieldName, out var staticOffset);
            return Make<T>(parent, offsets.Prepend(staticOffset).ToArray());
        }

        public StringPointer MakeString(MonoBasePointer parent, string staticFieldName, string fieldName, params int[] offsets) {
            mono.GetStaticField(parent.Base, staticFieldName, out var staticOffset);
            return MakeString(parent, offsets.Prepend(mono.GetFieldOffset(parent.Base, fieldName)).Prepend(staticOffset).ToArray());
        }
        public StringPointer MakeString(MonoBasePointer parent, string staticFieldName, params int[] offsets) {
            mono.GetStaticField(parent.Base, staticFieldName, out var staticOffset);
            return MakeString(parent, offsets.Prepend(staticOffset).ToArray());
        }


        public Pointer<T> Make<T>(string className, string staticFieldName, params int[] offsets) where T : unmanaged {
            return Make<T>(mono.MainImage, className, staticFieldName, out _, offsets);
        }
        public Pointer<T> Make<T>(string className, string staticFieldName, out IntPtr klass, params int[] offsets) where T : unmanaged {
            return Make<T>(mono.MainImage, className, staticFieldName, out klass, offsets);
        }
        public Pointer<T> Make<T>(IntPtr image, string className, string staticFieldName, params int[] offsets) where T : unmanaged {
            return Make<T>(image, className, staticFieldName, out _, offsets);
        }
        public Pointer<T> Make<T>(IntPtr image, string className, string staticFieldName, out IntPtr klass, params int[] offsets) where T : unmanaged {
            return (Pointer<T>)Make(typeof(T), image, className, staticFieldName, out klass, offsets);
        }

        public Pointer<T> Make<T>(string className, string staticFieldName, string fieldName, params int[] offsets) where T : unmanaged {
            return Make<T>(mono.MainImage, className, staticFieldName, fieldName, offsets);
        }
        public Pointer<T> Make<T>(IntPtr image, string className, string staticFieldName, string fieldName, params int[] offsets) where T : unmanaged {
            return (Pointer<T>)Make(typeof(T), image, className, staticFieldName, fieldName, offsets);
        }


        public StringPointer MakeString(string className, string staticFieldName, params int[] offsets) {
            return MakeString(mono.MainImage, className, staticFieldName, out _, offsets);
        }
        public StringPointer MakeString(string className, string staticFieldName, out IntPtr klass, params int[] offsets) {
            return MakeString(mono.MainImage, className, staticFieldName, out klass, offsets);
        }
        public StringPointer MakeString(IntPtr image, string className, string staticFieldName, params int[] offsets) {
            return MakeString(image, className, staticFieldName, out _, offsets);
        }
        public StringPointer MakeString(IntPtr image, string className, string staticFieldName, out IntPtr klass, params int[] offsets) {
            return (StringPointer)Make(typeof(string), image, className, staticFieldName, out klass, offsets);
        }

        public StringPointer MakeString(string className, string staticFieldName, string fieldName, params int[] offsets) {
            return MakeString(mono.MainImage, className, staticFieldName, fieldName, offsets);
        }
        public StringPointer MakeString(IntPtr image, string className, string staticFieldName, string fieldName, params int[] offsets) {
            return (StringPointer)Make(typeof(string), image, className, staticFieldName, fieldName, offsets);
        }


        protected Pointer Make(Type type, IntPtr image, string className, string staticFieldName, out IntPtr klass, params int[] offsets) {
            IntPtr staticBase = mono.GetStaticField(image, className, staticFieldName, out klass, out int instanceOffset);
            return CreateBaseAndNode(type, staticBase, offsets.Prepend(instanceOffset).ToArray());
        }
        protected Pointer Make(Type type, IntPtr image, string className, string staticFieldName, string fieldName, params int[] offsets) {
            IntPtr staticBase = mono.GetStaticField(image, className, staticFieldName, out IntPtr klass, out int instanceOffset);
            return CreateBaseAndNode(type, staticBase, offsets.Prepend(mono.GetFieldOffset(klass, fieldName)).Prepend(instanceOffset).ToArray());
        }
        protected Pointer CreateBaseAndNode(Type type, IntPtr ptr, params int[] offsets) {
            bool baseExists = false;
            MonoBasePointer monoBase = null;
            foreach(IBasePointer basePointer in BasePointers()) {
                if(basePointer.Base == ptr && basePointer is MonoBasePointer monoBasePointer) {
                    monoBase = monoBasePointer;
                    baseExists = true;
                    break;
                }
            }
            if(monoBase == null) {
                monoBase = new MonoBasePointer(wrapper, mono, ptr);
                _ = monoBase.New;
            }

            Pointer pointer = (Pointer)CreateNodeStructOrString(type, monoBase, defaultDerefType, offsets);
            _ = pointer.New;

            if(baseExists) {
                nodeLink[monoBase].Add(pointer);
            } else {
                nodeLink.Add(monoBase, new HashSet<IPointer> { pointer });
            }

            return pointer;
        }
    }

    public class MonoBasePointer : BasePointer<IntPtr> {
        protected IMonoHelper mono;

        public MonoBasePointer(TickableProcessWrapper wrapper, IMonoHelper mono, IntPtr basePtr)
            : this(wrapper, mono, basePtr, EDerefType.Auto) { }
        public MonoBasePointer(TickableProcessWrapper wrapper, IMonoHelper mono, IntPtr basePtr, EDerefType derefType)
            : base(wrapper, basePtr, derefType) {
            this.mono = mono;
        }

        protected override void Update() {
            Old = (IntPtr)(newValue ?? default(IntPtr));
            New = mono.GetStaticAddress(Base);
        }

        protected override IntPtr DerefOffsets() => throw new NotImplementedException();
    }
}