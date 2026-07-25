using System;
using System.Collections.Generic;
using System.Linq;
using Voxif.Memory;

namespace Voxif.Helpers.Unreal {
    public class UnrealNestedPointerFactory : NestedPointerFactory {
        protected readonly IUnrealHelper unreal;

        public UnrealNestedPointerFactory(TickableProcessWrapper wrapper, IUnrealHelper unrealHelper)
            : this(wrapper, null, unrealHelper, EDerefType.Auto) { }

        public UnrealNestedPointerFactory(TickableProcessWrapper wrapper, string moduleName, IUnrealHelper unrealHelper)
            : this(wrapper, moduleName, unrealHelper, EDerefType.Auto) { }

        public UnrealNestedPointerFactory(TickableProcessWrapper wrapper, string moduleName, IUnrealHelper unrealHelper, EDerefType derefType)
            : base(wrapper, moduleName, derefType) {
            unreal = unrealHelper;
        }

        public UnrealObjectPointer MakeObject(string className) {
            return MakeObject(className, out _);
        }

        public UnrealObjectPointer MakeObject(string className, out IntPtr uobject) {
            foreach(UnrealObjectPointer basePointer in BasePointers().OfType<UnrealObjectPointer>()) {
                if(basePointer.ClassName == className) {
                    uobject = basePointer.New;
                    return basePointer;
                }
            }

            uobject = unreal.FindLiveUObject(className);
            var objectPointer = new UnrealObjectPointer(wrapper, unreal, className, uobject);
            _ = objectPointer.New;
            nodeLink.Add(objectPointer, new HashSet<IPointer>());
            return objectPointer;
        }

        public Pointer<T> Make<T>(string objectClassName, string fieldName, params int[] offsets) where T : unmanaged {
            return Make<T>(objectClassName, objectClassName, fieldName, offsets);
        }

        public Pointer<T> Make<T>(string objectClassName, string fieldOwnerClassName, string fieldName, params int[] offsets) where T : unmanaged {
            UnrealObjectPointer objectPointer = MakeObject(objectClassName);
            return Make<T>(objectPointer, fieldOwnerClassName, fieldName, offsets);
        }

        public Pointer<T> Make<T>(UnrealObjectPointer objectPointer, string fieldName, params int[] offsets) where T : unmanaged {
            return Make<T>(objectPointer, objectPointer.ClassName, fieldName, offsets);
        }

        public Pointer<T> Make<T>(UnrealObjectPointer objectPointer, string fieldOwnerClassName, string fieldName, params int[] offsets) where T : unmanaged {
            int fieldOffset = unreal.GetFieldOffset(fieldOwnerClassName, fieldName);
            if(fieldOffset == default) {
                throw new InvalidOperationException($"Unreal field not found: {fieldOwnerClassName}.{fieldName}");
            }

            Pointer<T> pointer = (Pointer<T>)Make(typeof(T), defaultDerefType, objectPointer, offsets.Prepend(fieldOffset).ToArray());
            pointer.UpdateOnNullPointer = false;
            _ = pointer.New;
            return pointer;
        }

        public bool TryMake<T>(string objectClassName, string fieldName, out Pointer<T> pointer, params int[] offsets) where T : unmanaged {
            return TryMake(objectClassName, objectClassName, fieldName, out pointer, offsets);
        }

        public bool TryMake<T>(string objectClassName, string fieldOwnerClassName, string fieldName, out Pointer<T> pointer, params int[] offsets) where T : unmanaged {
            try {
                pointer = Make<T>(objectClassName, fieldOwnerClassName, fieldName, offsets);
                return true;
            } catch {
                pointer = null;
                return false;
            }
        }
    }

    public class UnrealObjectPointer : BasePointer<IntPtr> {
        protected readonly IUnrealHelper unreal;

        private DateTime nextResolveTime = DateTime.MinValue;

        public string ClassName { get; }
        public TimeSpan ResolveRetryInterval { get; set; } = TimeSpan.FromSeconds(1);
        public bool RefreshEveryUpdate { get; set; }

        public UnrealObjectPointer(TickableProcessWrapper wrapper, IUnrealHelper unrealHelper, string className, IntPtr uobject)
            : base(wrapper, uobject, EDerefType.Auto) {
            unreal = unrealHelper;
            ClassName = className;
        }

        public void Refresh() {
            Base = unreal.FindLiveUObject(ClassName);
            ForceUpdate(true);
        }

        public void Refresh(Predicate<IntPtr> predicate) {
            Base = unreal.FindLiveUObject(ClassName, predicate);
            ForceUpdate(true);
        }

        public void SetBase(IntPtr uobject) {
            Base = uobject;
            ForceUpdate(true);
        }

        protected override void Update() {
            Old = (IntPtr)(newValue ?? default(IntPtr));

            if((Base == default || RefreshEveryUpdate) && DateTime.Now >= nextResolveTime) {
                Base = unreal.FindLiveUObject(ClassName);
                nextResolveTime = DateTime.Now.Add(ResolveRetryInterval);
            }

            New = Base;
        }

        protected override IntPtr DerefOffsets() {
            throw new NotImplementedException();
        }
    }
}
