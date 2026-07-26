using System;
using System.Collections.Generic;
using System.Linq;
using LiveSplit.Subnautica2.UE5Events.Internal;
using Voxif.Helpers.Unreal;
using Voxif.Memory;
 
namespace LiveSplit.Subnautica2.UE5Events
{
    /// <summary>
    /// Minimal Uhara-style UE5 ProcessEvent function flag reader.
    /// It is independent of the autosplitter's existing pointer watchers.
    /// </summary>
    public sealed class Ue5FunctionEvents : IDisposable
    {
        private const int MaximumExpandedMatches = 3000;

        private readonly ProcessWrapper game;
        private readonly IUnrealHelper unreal;
        private readonly Action<string> log;
        private Ue5FunctionHook hook;
        private readonly Dictionary<string, Ue5FunctionFlag> flags =
            new Dictionary<string, Ue5FunctionFlag>(StringComparer.Ordinal);

        private bool disposed;

        public Ue5FunctionEvents(ProcessWrapper game, IUnrealHelper unreal, Action<string> log = null)
        {
            this.game = game ?? throw new ArgumentNullException(nameof(game));
            this.unreal = unreal ?? throw new ArgumentNullException(nameof(unreal));

            this.log = log;
        }

        public Ue5FunctionFlag this[string watcherName]
        {
            get
            {
                if (!flags.TryGetValue(watcherName, out Ue5FunctionFlag flag))
                    throw new KeyNotFoundException("Unknown UE5 function flag: " + watcherName);
                return flag;
            }
        }

        public bool TryGetFlag(string watcherName, out Ue5FunctionFlag flag)
        {
            return flags.TryGetValue(watcherName, out flag);
        }

        /// <summary>
        /// Registers an Unreal ProcessEvent counter. A null name disables that
        /// part of the filter. Leading/trailing '*' wildcards use Uhara-style
        /// Contains, StartsWith, and EndsWith matching against currently loaded
        /// FName entries.
        /// </summary>
        public Ue5FunctionFlag FunctionFlag(
            string watcherName,
            string className,
            string objectName,
            string functionName)
        {
            ThrowIfDisposed();

            if (String.IsNullOrWhiteSpace(watcherName))
                throw new ArgumentException("A watcher name is required.", nameof(watcherName));
            if (flags.ContainsKey(watcherName))
                throw new InvalidOperationException("A UE5 function flag named '" + watcherName + "' already exists.");
            if (functionName == null)
                throw new ArgumentNullException(nameof(functionName), "A function name is required.");

            string[] patterns = new[] { className, objectName, functionName }
                .Where(p => p != null)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            IReadOnlyDictionary<string, IReadOnlyList<int>> resolved =
                unreal.FindFNameIndices(patterns);

            return FunctionFlag(watcherName, className, objectName, functionName, resolved);
        }

        internal IReadOnlyDictionary<string, IReadOnlyList<int>> ResolveFNamePatterns(
            IEnumerable<string> patterns)
        {
            ThrowIfDisposed();
            return unreal.FindFNameIndices(
                patterns.Where(pattern => pattern != null).Distinct(StringComparer.Ordinal).ToArray());
        }

        internal Ue5FunctionFlag FunctionFlag(
            string watcherName,
            string className,
            string objectName,
            string functionName,
            IReadOnlyDictionary<string, IReadOnlyList<int>> resolved)
        {
            ThrowIfDisposed();

            if (String.IsNullOrWhiteSpace(watcherName))
                throw new ArgumentException("A watcher name is required.", nameof(watcherName));
            if (flags.ContainsKey(watcherName))
                throw new InvalidOperationException("A UE5 function flag named '" + watcherName + "' already exists.");
            if (functionName == null)
                throw new ArgumentNullException(nameof(functionName), "A function name is required.");
            if (resolved == null)
                throw new ArgumentNullException(nameof(resolved));

            int[] classIndices = ResolvePattern(className, resolved, nameof(className));
            int[] objectIndices = ResolvePattern(objectName, resolved, nameof(objectName));
            int[] functionIndices = ResolvePattern(functionName, resolved, nameof(functionName));

            long expandedCount = (long)classIndices.Length * objectIndices.Length * functionIndices.Length;
            if (expandedCount <= 0 || expandedCount > MaximumExpandedMatches)
            {
                throw new InvalidOperationException(
                    "The UE5 event filter expands to " + expandedCount +
                    " native matches; the supported maximum is " + MaximumExpandedMatches + ".");
            }

            var matches = new List<Ue5FunctionHook.FunctionMatch>((int)expandedCount);
            foreach (int classIndex in classIndices)
            foreach (int objectIndex in objectIndices)
            foreach (int functionIndex in functionIndices)
            {
                matches.Add(new Ue5FunctionHook.FunctionMatch(
                    classIndex,
                    objectIndex,
                    functionIndex));
            }

            EnsureHook();
            IntPtr counter = hook.AddFunctionFlag(matches);
            var flag = new Ue5FunctionFlag(watcherName, counter);
            flag.Prime(game);
            flags.Add(watcherName, flag);
            return flag;
        }

        public void Update()
        {
            ThrowIfDisposed();
            foreach (Ue5FunctionFlag flag in flags.Values)
                flag.Update(game);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            flags.Clear();
            hook?.Dispose();
            hook = null;
        }

        private void EnsureHook()
        {
            if (hook != null)
                return;

            if (!Environment.Is64BitProcess || !game.Is64Bit)
            {
                throw new PlatformNotSupportedException(
                    "The UE5 event hook requires 64-bit LiveSplit and a 64-bit game process.");
            }

            hook = new Ue5FunctionHook(
                game,
                unreal.UObjectClassOffset,
                unreal.UObjectNameOffset,
                log);
        }

        private static int[] ResolvePattern(
            string pattern,
            IReadOnlyDictionary<string, IReadOnlyList<int>> resolved,
            string parameterName)
        {
            // A zero record value means "do not filter this field" in the hook.
            if (pattern == null)
                return new[] { 0 };

            if (!resolved.TryGetValue(pattern, out IReadOnlyList<int> values) || values.Count == 0)
                throw new InvalidOperationException("No loaded UE5 FName matches " + parameterName + " '" + pattern + "'.");

            return values.Distinct().ToArray();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(Ue5FunctionEvents));
        }
    }
}
