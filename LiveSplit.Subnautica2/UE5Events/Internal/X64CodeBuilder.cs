using System;
using System.Collections.Generic;
 
namespace LiveSplit.Subnautica2.UE5Events.Internal
{
    internal sealed class X64CodeBuilder
    {
        private sealed class Fixup
        {
            public int DisplacementOffset;
            public string Label;
        }

        private readonly List<byte> bytes = new List<byte>();
        private readonly Dictionary<string, int> labels = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<Fixup> fixups = new List<Fixup>();

        public int Position => bytes.Count;

        public void Emit(params byte[] values)
        {
            bytes.AddRange(values);
        }

        public void EmitInt32(int value)
        {
            bytes.AddRange(BitConverter.GetBytes(value));
        }

        public void EmitInt64(long value)
        {
            bytes.AddRange(BitConverter.GetBytes(value));
        }

        public void MarkLabel(string label)
        {
            if (labels.ContainsKey(label))
                throw new InvalidOperationException("Duplicate x64 label: " + label);
            labels.Add(label, bytes.Count);
        }

        public void JumpEqual(string label)
        {
            EmitRelativeJump(0x84, label);
        }

        public void JumpNotEqual(string label)
        {
            EmitRelativeJump(0x85, label);
        }

        private void EmitRelativeJump(byte conditionOpcode, string label)
        {
            Emit(0x0F, conditionOpcode);
            int displacementOffset = bytes.Count;
            Emit(0, 0, 0, 0);
            fixups.Add(new Fixup { DisplacementOffset = displacementOffset, Label = label });
        }

        public byte[] Build()
        {
            byte[] result = bytes.ToArray();
            foreach (Fixup fixup in fixups)
            {
                if (!labels.TryGetValue(fixup.Label, out int target))
                    throw new InvalidOperationException("Unknown x64 label: " + fixup.Label);

                int nextInstruction = fixup.DisplacementOffset + sizeof(int);
                int displacement = target - nextInstruction;
                Array.Copy(BitConverter.GetBytes(displacement), 0, result, fixup.DisplacementOffset, sizeof(int));
            }
            return result;
        }
    }
}
