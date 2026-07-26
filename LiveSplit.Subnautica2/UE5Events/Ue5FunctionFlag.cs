using System;
using Voxif.Memory;
 
namespace LiveSplit.Subnautica2.UE5Events
{
    /// <summary>
    /// A remote 64-bit counter that is incremented whenever the configured
    /// Unreal Engine function event is observed.
    /// </summary>
    public sealed class Ue5FunctionFlag
    {
        internal Ue5FunctionFlag(string name, IntPtr address)
        {
            if (String.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A watcher name is required.", nameof(name));
            if (address == IntPtr.Zero)
                throw new ArgumentException("The remote counter address cannot be zero.", nameof(address));

            Name = name;
            Address = address;
        }

        public string Name { get; }
        internal IntPtr Address { get; }

        public ulong Old { get; private set; }
        public ulong Current { get; private set; }
        public ulong Delta { get; private set; }
        public bool Triggered { get; private set; }

        internal void Prime(ProcessWrapper game)
        {
            Current = game.Read<ulong>(Address);
            Old = Current;
            Delta = 0;
            Triggered = false;
        }

        internal void Update(ProcessWrapper game)
        {
            Old = Current;
            Current = game.Read<ulong>(Address);
            Triggered = Current != Old;

            if (!Triggered)
            {
                Delta = 0;
                return;
            }

            Delta = Current >= Old
                ? Current - Old
                : unchecked((ulong.MaxValue - Old) + Current + 1UL);
        }
    }
}
