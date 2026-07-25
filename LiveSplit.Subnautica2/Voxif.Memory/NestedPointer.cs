using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Voxif.Memory {
    public class NestedPointerFactory {

        protected TickableProcessWrapper wrapper;
        protected EDerefType defaultDerefType;
        protected IntPtr defaultModuleBase;

        protected Dictionary<IPointer, HashSet<IPointer>> nodeLink = new Dictionary<IPointer, HashSet<IPointer>>();

        public NestedPointerFactory(TickableProcessWrapper wrapper) : this(wrapper, null, EDerefType.Auto) { }
        public NestedPointerFactory(TickableProcessWrapper wrapper, string moduleName) : this(wrapper, moduleName, EDerefType.Auto) { }
        public NestedPointerFactory(TickableProcessWrapper wrapper, string moduleName, EDerefType derefType) {
            this.wrapper = wrapper;
            defaultDerefType = derefType;
            if(moduleName == null) {
                defaultModuleBase = wrapper.Process.MainModule.BaseAddress;
            } else {
                defaultModuleBase = wrapper.Process.Modules().FirstOrDefault(
                    m => m.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))?.BaseAddress ?? default;
            }
        }

        protected HashSet<IPointer> BasePointers() => new HashSet<IPointer>(nodeLink.Keys.Where(n => n is IBasePointer));

        public Pointer<T> Make<T>(int moduleOffset, params int[] offsets) where T : unmanaged {
            return Make<T>(defaultDerefType, defaultModuleBase + moduleOffset, offsets);
        }
        public Pointer<T> Make<T>(IntPtr basePtr, params int[] offsets) where T : unmanaged {
            return Make<T>(defaultDerefType, basePtr, offsets);
        }
        public Pointer<T> Make<T>(EDerefType derefType, IntPtr basePtr, params int[] offsets) where T : unmanaged {
            var pointer = (Pointer<T>)Make(typeof(T), derefType, basePtr, offsets);
            _ = pointer.New;
            return pointer;
        }

        public StringPointer MakeString(int moduleOffset, params int[] offsets) {
            return MakeString(defaultDerefType, defaultModuleBase + moduleOffset, offsets);
        }
        public StringPointer MakeString(IntPtr basePtr, params int[] offsets) {
            return MakeString(defaultDerefType, basePtr, offsets);
        }
        public StringPointer MakeString(EDerefType derefType, IntPtr basePtr, params int[] offsets) {
            var pointer = (StringPointer)Make(typeof(string), derefType, basePtr, offsets);
            _ = pointer.New;
            return pointer;
        }

        public Pointer<T> Make<T>(Pointer parent, params int[] offsets) where T : unmanaged {
            return Make<T>(defaultDerefType, parent, offsets);
        }
        public Pointer<T> Make<T>(EDerefType derefType, Pointer parent, params int[] offsets) where T : unmanaged {
            var pointer = (Pointer<T>)Make(typeof(T), derefType, parent, offsets);
            _ = pointer.New;
            return pointer;
        }

        public StringPointer MakeString(Pointer parent, params int[] offsets) {
            return MakeString(defaultDerefType, parent, offsets);
        }
        public StringPointer MakeString(EDerefType derefType, Pointer parent, params int[] offsets) {
            var pointer = (StringPointer)Make(typeof(string), derefType, parent, offsets);
            _ = pointer.New;
            return pointer;
        }

        protected IPointer Make(Type type, EDerefType derefType, IntPtr basePtr, params int[] offsets) {
            IPointer pointer;
            foreach(IBasePointer basePointer in BasePointers()) {
                if(basePointer.Base != basePtr) {
                    continue;
                }
                if(offsets.Length == 0 || basePointer.Offsets.SequenceEqual(offsets)) {
                    return basePointer;
                }
                for(int i = 0; i < basePointer.Offsets.Length; i++) {
                    if(i < offsets.Length && basePointer.Offsets[i] == offsets[i]) {
                        continue;
                    }
                    BasePointer<IntPtr> newBase = new BasePointer<IntPtr>(wrapper, basePointer.Base, derefType, basePointer.Offsets.Take(i).ToArray());
                    Type baseType = basePointer.GetType().IsGenericType ? basePointer.GetType().GenericTypeArguments[0] : typeof(string);
                    INodePointer nodeBase = CreateNodeStructOrString(baseType, newBase, derefType, basePointer.Offsets.Skip(i).ToArray());
                    nodeLink.Add(nodeBase, new HashSet<IPointer>());
                    foreach(INodePointer node in nodeLink[basePointer]) {
                        node.Parent = nodeBase;
                        nodeLink[nodeBase].Add(node);
                    }
                    nodeLink.Remove(basePointer);
                    if(i != offsets.Length) {
                        INodePointer newNode = CreateNodeStructOrString(type, newBase, derefType, offsets.Skip(i).ToArray());
                        nodeLink.Add(newBase, new HashSet<IPointer> { nodeBase, newNode });
                        return newNode;
                    } else {
                        nodeLink.Add(newBase, new HashSet<IPointer> { nodeBase });
                        return newBase;
                    }
                }

                pointer = AddPointer(type, nodeLink[basePointer], derefType, offsets.Skip(basePointer.Offsets.Length).ToArray());
                if(pointer == null) {
                    pointer = CreateNodeStructOrString(type, basePointer, derefType, offsets.Skip(basePointer.Offsets.Length).ToArray());
                    nodeLink[basePointer].Add(pointer);
                }
                return pointer;
            }
            IBasePointer newBasePointer = CreateBaseStructOrString(type, basePtr, derefType, offsets);
            nodeLink.Add(newBasePointer, new HashSet<IPointer>());
            return newBasePointer;
        }

        protected IPointer Make(Type type, EDerefType derefType, IPointer parent, params int[] offsets) {
            IPointer pointer = null;
            if(nodeLink.TryGetValue(parent, out HashSet<IPointer> value)) {
                pointer = AddPointer(type, value, derefType, offsets);
            } else {
                nodeLink.Add(parent, new HashSet<IPointer>());
            }
            if(pointer == null) {
                pointer = CreateNodeStructOrString(type, parent, derefType, offsets);
                nodeLink[parent].Add(pointer);
            }
            return pointer;
        }

        protected IPointer AddPointer(Type type, HashSet<IPointer> pointers, EDerefType derefType, int[] offsets) {
            IPointer newNode = null;
            foreach(INodePointer ptr in pointers) {
                if(ptr.Offsets[0] != offsets[0]) {
                    continue;
                }
                if(ptr.Offsets.SequenceEqual(offsets)) {
                    return ptr;
                }
                for(int i = 1; i < ptr.Offsets.Length; i++) {
                    if(i < offsets.Length && ptr.Offsets[i] == offsets[i]) {
                        continue;
                    }
                    NodePointer<IntPtr> newParent = new NodePointer<IntPtr>(wrapper, ptr.Parent, derefType, ptr.Offsets.Take(i).ToArray());
                    pointers.Remove(ptr);
                    pointers.Add(newParent);
                    ptr.Parent = newParent;
                    ptr.Offsets = ptr.Offsets.Skip(i).ToArray();
                    if(i != offsets.Length) {
                        newNode = CreateNodeStructOrString(type, newParent, derefType, offsets.Skip(i).ToArray());
                        nodeLink.Add(newParent, new HashSet<IPointer> { ptr, newNode });
                        return newNode;
                    } else {
                        nodeLink.Add(newParent, new HashSet<IPointer> { ptr });
                        return newParent;
                    }
                }
                if(nodeLink.ContainsKey(ptr) && (newNode = AddPointer(type, nodeLink[ptr], derefType, offsets.Skip(ptr.Offsets.Length).ToArray())) != null) {
                    return newNode;
                }
                newNode = CreateNodeStructOrString(type, ptr, derefType, offsets.Skip(ptr.Offsets.Length).ToArray());
                if(nodeLink.ContainsKey(ptr)) {
                    nodeLink[ptr].Add(newNode);
                } else {
                    nodeLink[ptr] = new HashSet<IPointer> { newNode };
                }
            }
            return newNode;
        }

        protected IBasePointer CreateBaseStructOrString(Type type, IntPtr basePointer, EDerefType derefType, params int[] offsets) {
            return type == typeof(string)
                 ? new BaseStringPointer(wrapper, basePointer, derefType, offsets)
                 : (IBasePointer)Activator.CreateInstance(typeof(BasePointer<>).MakeGenericType(type), new object[] { wrapper, basePointer, derefType, offsets });
        }

        protected INodePointer CreateNodeStructOrString(Type type, IPointer basePointer, EDerefType derefType, params int[] offsets) {
            return type == typeof(string)
                 ? new NodeStringPointer(wrapper, basePointer, derefType, offsets)
                 : (INodePointer)Activator.CreateInstance(typeof(NodePointer<>).MakeGenericType(type), new object[] { wrapper, basePointer, derefType, offsets });
        }

        protected string NodeToString(Pointer pointer) {
            string str = Environment.NewLine + new string(' ', 19 + GetTreeDepth(pointer) * 5) + pointer.OffsetsToString();

            if(nodeLink.ContainsKey(pointer)) {
                foreach(Pointer subNode in nodeLink[pointer]) {
                    str += NodeToString(subNode);
                }
            }
            return str;
        }

        public int GetTreeDepth(IPointer pointer) => pointer is INodePointer node ? 1 + GetTreeDepth(node.Parent) : 0;

        public override string ToString() {
            string str = "";
            foreach(IBasePointer baseNode in BasePointers()) {
                str += Environment.NewLine + "0x" + baseNode.Base.ToString("X16")
                    + " " + ((Pointer)baseNode).OffsetsToString();
                foreach(Pointer node in nodeLink[baseNode].OrderBy(n => n.Offsets[0])) {
                    str += NodeToString(node);
                }
            }
            return str;
        }
    }

    public interface IPointer {
        int[] Offsets { get; set; }
        object Old { get; set; }
        object New { get; set; }
    }

    public abstract class Pointer : IPointer {

        protected TickableProcessWrapper wrapper;

        protected uint lastTickUpdate = 0;

        protected object oldValue;
        public object Old {
            get {
                if(lastTickUpdate != wrapper.Tick) {
                    lastTickUpdate = wrapper.Tick;
                    Update();
                }
                return oldValue;
            }
            set => oldValue = value;
        }

        protected object newValue;
        public object New {
            get {
                if(lastTickUpdate != wrapper.Tick) {
                    lastTickUpdate = wrapper.Tick;
                    Update();
                }
                return newValue;
            }
            set => newValue = value;
        }

        public bool Changed => !Old.Equals(newValue);

        public int[] Offsets { get; set; }

        public bool UpdateOnNullPointer { get; set; } = true;

        public EDerefType derefType;

        public Pointer(TickableProcessWrapper wrapper, EDerefType derefType, params int[] offsets) {
            this.wrapper = wrapper;
            this.derefType = derefType;
            Offsets = offsets;
        }

        protected abstract IntPtr DerefOffsets();

        protected abstract void Update();

        public void ForceUpdate(bool ignoreTicks = false) {
            if(ignoreTicks) {
                Update();
            } else {
                _ = New;
            }
        }

        public string OffsetsToString() {
            if(Offsets.Length == 0) {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("[0x").Append(Offsets[0].ToString("X"));
            for(int i = 1; i < Offsets.Length; i++) {
                sb.Append(", 0x").Append(Offsets[i].ToString("X"));
            }
            return sb.Append(']').ToString();
        }
    }

    public abstract class Pointer<T> : Pointer where T : unmanaged {

        public bool Deref64(EDerefType derefType) {
            return derefType == EDerefType.Auto ? wrapper.Is64Bit : derefType == EDerefType.Bit64;
        }

        public new T Old {
            get => (T)base.Old;
            set => base.Old = value;
        }
        public new T New {
            get => (T)base.New;
            set => base.New = value;
        }

        public Pointer(TickableProcessWrapper wrapper, EDerefType derefType, params int[] offsets)
            : base(wrapper, derefType, offsets) { }

        protected override void Update() {
            IntPtr resPtr = DerefOffsets();
            if(resPtr != default || UpdateOnNullPointer) {
                Old = (T)(newValue ?? default(T));
                New = wrapper.Read<T>(derefType, resPtr);
            }
        }
    }

    public abstract class StringPointer : Pointer {

        public EStringType StringType { get; set; }

        public new string Old {
            get => (string)base.Old;
            set => base.Old = value;
        }
        public new string New {
            get => (string)base.New;
            set => base.New = value;
        }

        public StringPointer(TickableProcessWrapper wrapper, EDerefType derefType, params int[] offsets)
            : base(wrapper, derefType, offsets) { }
        protected override void Update() {
            IntPtr resPtr = DerefOffsets();
            if(resPtr != default || UpdateOnNullPointer) {
                Old = (string)(newValue ?? default(string));
                New = wrapper.ReadString(resPtr, StringType);
            }
        }
    }

    public interface IBasePointer : IPointer {
        IntPtr Base { get; }
    }

    public class BasePointer<T> : Pointer<T>, IBasePointer where T : unmanaged {
        public IntPtr Base { get; protected set; }

        public BasePointer(TickableProcessWrapper wrapper, IntPtr basePtr, params int[] offsets)
            : this(wrapper, basePtr, EDerefType.Auto, offsets) { }
        public BasePointer(TickableProcessWrapper wrapper, IntPtr basePtr, EDerefType derefType, params int[] offsets)
            : base(wrapper, derefType, offsets) {
            Base = basePtr;
        }

        protected override IntPtr DerefOffsets() {
            return wrapper.Read(derefType, Offsets.Length > 0 ? wrapper.Read<IntPtr>(Base) : Base, Offsets);
        }
    }

    public class BaseStringPointer : StringPointer, IBasePointer {
        public IntPtr Base { get; protected set; }

        public BaseStringPointer(TickableProcessWrapper wrapper, IntPtr basePtr, params int[] offsets)
            : this(wrapper, basePtr, EDerefType.Auto, offsets) { }
        public BaseStringPointer(TickableProcessWrapper wrapper, IntPtr basePtr, EDerefType derefType, params int[] offsets)
            : base(wrapper, derefType, offsets) {
            Base = basePtr;
        }

        protected override IntPtr DerefOffsets() {
            return wrapper.Read(derefType, Offsets.Length > 0 ? wrapper.Read<IntPtr>(Base) : Base, Offsets);
        }
    }

    public interface INodePointer : IPointer {
        IPointer Parent { get; set; }
    }

    public class NodePointer<T> : Pointer<T>, INodePointer where T : unmanaged {

        public IPointer Parent { get; set; }

        public NodePointer(TickableProcessWrapper wrapper, IPointer parent, params int[] offsets)
            : this(wrapper, parent, EDerefType.Auto, offsets) { }
        public NodePointer(TickableProcessWrapper wrapper, IPointer parent, EDerefType derefType, params int[] offsets)
            : base(wrapper, derefType, offsets) {
            Parent = parent;
        }

        protected override IntPtr DerefOffsets() {
            return wrapper.Read(derefType, (IntPtr)Parent.New, Offsets);
        }
    }

    public class NodeStringPointer : StringPointer, INodePointer {

        public IPointer Parent { get; set; }

        public NodeStringPointer(TickableProcessWrapper wrapper, IPointer parent, params int[] offsets)
            : this(wrapper, parent, EDerefType.Auto, offsets) { }
        public NodeStringPointer(TickableProcessWrapper wrapper, IPointer parent, EDerefType derefType, params int[] offsets)
            : base(wrapper, derefType, offsets) {
            Parent = parent;
        }

        protected override IntPtr DerefOffsets() {
            return wrapper.Read(derefType, (IntPtr)Parent.New, Offsets);
        }
    }

    public class FakePointer<T> {
        
        protected readonly Func<T> func = null;

        public FakePointer() { }
        public FakePointer(Func<T> func) {
            this.func = func;
        }

        public T Old { get; set; }
        public T New { get; set; }
        public bool Changed => !Old.Equals(New);

        public bool Update() {
            Old = New;
            if(func != null) {
                New = func.Invoke();
            }
            return Changed;
        }

        public bool Update(T newValue) {
            Old = New;
            New = newValue;
            return Changed;
        }
    }
}