using System;
using Voxif.Helpers.MemoryScan;
using Voxif.Memory;
 
namespace LiveSplit.Subnautica2.UE5Events.Internal
{
    internal static class Ue5EventScanner
    {
        internal struct ProcessEventLocation
        {
            public IntPtr Address;
            public int OverwriteLength;
        }

        private sealed class CandidatePattern
        {
            public string Signature;
            public int OverwriteLength;
        }

        private static readonly CandidatePattern[] Patterns =
        {
            new CandidatePattern
            {
                Signature = "40 55 56 57 41 54 41 55 41 56 41 57 48 81 EC ?? ?? 00 00 " +
                            "48 8D 6C 24 ?? 48 89 9D ?? ?? 00 00 48 8B 05 ?? ?? ?? ?? " +
                            "48 33 C5 48 89 85 ?? 00 00 00",
                OverwriteLength = 19
            },
            new CandidatePattern
            {
                Signature = "41 55 41 56 41 57 48 81 EC ?? ?? 00 00 48 8D 6C 24 ?? " +
                            "48 89 9D ?? ?? 00 00 48 8B 05 ?? ?? ?? ?? 48 33 C5 " +
                            "48 89 85 ?? 00 00 00",
                OverwriteLength = 18
            }
        };

        private static readonly ScanTarget Checkpoints = new ScanTarget()
            .AddSignature(0, "F7 82 ?? 00 00 00 00 ?? 00 00")
            .AddSignature(0, "F7 86 ?? 00 00 00 ?? ?? 00 00");

        public static ProcessEventLocation FindProcessEvent(ProcessWrapper game, Action<string> log)
        {
            System.Diagnostics.ProcessModule module = game.Process.MainModule;
            if (module == null)
                throw new InvalidOperationException("The game main module is unavailable.");

            var scanner = new SignatureScanner(game, module.BaseAddress, module.ModuleMemorySize);
            foreach (CandidatePattern pattern in Patterns)
            {
                var target = new ScanTarget(0, pattern.Signature);
                foreach (IntPtr candidate in scanner.ScanAll(target))
                {
                    // Uhara validates these characteristic ProcessEvent flag tests
                    // shortly after the function prologue to avoid a false positive.
                    var checkpointScanner = new SignatureScanner(game, candidate, 0x200);
                    if (checkpointScanner.Scan(Checkpoints) == IntPtr.Zero)
                        continue;

                    log?.Invoke("[UE5Events] UObject::ProcessEvent found at 0x" + candidate.ToInt64().ToString("X"));
                    return new ProcessEventLocation
                    {
                        Address = candidate,
                        OverwriteLength = pattern.OverwriteLength
                    };
                }
            }

            throw new InvalidOperationException(
                "Could not locate UObject::ProcessEvent with the bundled UE5 signatures. " +
                "The normal autosplitter memory reader will continue to work.");
        }
    }
}
