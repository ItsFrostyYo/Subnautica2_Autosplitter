#define UE_DEBUG

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Voxif.Helpers.MemoryHelper;
using Voxif.Helpers.StructReflector;
using Voxif.IO;
using Voxif.Memory;
using static Voxif.Helpers.MemoryHelper.ScanTarget;

namespace Voxif.Helpers.Unreal {
    public class UnrealHelperTask : HelperTask {
        
        protected readonly ProcessWrapper wrapper;

        public UnrealHelperTask(ProcessWrapper wrapper, Logger logger = null) : base(logger) {
            this.wrapper = wrapper;
        }

        protected override void Log(string msg) => logger?.Log("[Unreal] " + msg);

        public Task Run(Version version, Action<IUnrealHelper> action) {
            return Run(() => {
                UnrealHelperBase unreal;

                //TODO Add UE3
                if(version.Major == 5) {
                    unreal = new Unreal5_0Helper(wrapper, token, "0", logger);
                } else if(version.Major == 4) {
                    if(version.Minor >= 25) {
                        unreal = new Unreal4_25Helper(wrapper, token, "25", logger);
                    } else if(version.Minor >= 23) {
                        unreal = new Unreal4_23Helper(wrapper, token, "23", logger);
                    } else if(version.Minor >= 22) {
                        unreal = new Unreal4_22Helper(wrapper, token, "22", logger);
                    } else if(version.Minor >= 20) {
                        unreal = new Unreal4_20Helper(wrapper, token, "20", logger);
                    } else if(version.Minor >= 11) {
                        string fileVersion;
                        if(version.Minor >= 18) {
                            fileVersion = "18";
                        } else if(version.Minor >= 14) {
                            fileVersion = "14";
                        } else if(version.Minor >= 13) {
                            fileVersion = "13";
                        } else {
                            fileVersion = "11";
                        }
                        unreal = new Unreal4_11Helper(wrapper, token, fileVersion, logger);
                    } else if(version.Minor >= 8) {
                        unreal = new Unreal4_8Helper(wrapper, token, "8", logger);
                    } else {
                        unreal = new Unreal4_0Helper(wrapper, token, "0", logger);
                    }
                } else {
                    throw new Exception("Unreal version not supported");
                }

                while(true) {
                    token.ThrowIfCancellationRequested();
                    if(unreal.ScanTask.IsCompleted) {
                        break;
                    }
                    Sleep();
                }

                action(unreal);
            });
        }

        private abstract class UnrealHelperBase : IUnrealHelper {

            protected CancellationToken token;
            public ScanHelperTask ScanTask { get; }

            protected readonly ProcessWrapper game;

            protected readonly Logger logger;

            protected IStructReflector data;

            protected IntPtr namesPtr;
            protected IntPtr objectsPtr;

            protected readonly Dictionary<int, string> fnameNameCache = new Dictionary<int, string>();
            protected readonly Dictionary<string, IntPtr> classObjectCache = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
            protected readonly Dictionary<string, bool> classInheritanceCache = new Dictionary<string, bool>(StringComparer.Ordinal);
            protected readonly Dictionary<string, int> fieldOffsetCache = new Dictionary<string, int>(StringComparer.Ordinal);

            protected UnrealHelperBase(ProcessWrapper game, CancellationToken token, string fileName, string fileVersion, Logger logger = null) {
                this.game = game;
                this.token = token;
                this.logger = logger;
                Log($"Version: {fileName}.{fileVersion}");
                data = StructReflector.StructReflector.Load("Voxif.Helpers.UnrealHelper.Unreal" + fileName + "_" + fileVersion, game.PointerSize);
                ScanTask = new ScanHelperTask(game, logger);
                var fname = FNamesTarget;
                fname.OnFound += OnFNamesFound;
                var uobject = UObjectsTarget;
                uobject.OnFound += OnUObjectsFound;
                ScanTask.Run(
                    new ScannableData { {
                        game.Process.MainModule.ModuleName,
                        new Dictionary<string, ScanTarget> {
                            { "fname", fname },
                            { "uobject", uobject}
                        }
                    } }
                );
            }

            protected abstract ScanTarget FNamesTarget { get; }
            protected abstract ScanTarget UObjectsTarget { get; }

            protected abstract OnScanFoundCallback OnFNamesFound { get; }
            protected abstract OnScanFoundCallback OnUObjectsFound { get; }

            public void Log(string msg) => logger?.Log("[Unreal] " + msg);

            protected struct FName {
                public int index;
                public string name;

                public FName(int index, string name) {
                    this.index = index;
                    this.name = name;
                }
            }

            protected abstract IEnumerable<FName> FNameSequence();
            protected abstract IEnumerable<IntPtr> UObjectSequence();

            public abstract Dictionary<string, IntPtr> GetUObjects(params string[] names);
            public abstract IntPtr GetUObject(string name);
            public abstract IntPtr GetUObject(int fname);
            public abstract IntPtr FindLiveUObject(string className);
            public abstract IntPtr FindLiveUObject(string className, Predicate<IntPtr> predicate);
            public abstract IReadOnlyList<IntPtr> FindLiveUObjects(string className, int maxCount = 256);
            public abstract string GetUObjectName(IntPtr uobject);
            public abstract string GetUObjectPath(IntPtr uobject);
            public abstract string GetUObjectClassName(IntPtr uobject);
            public abstract IntPtr ResolveWeakObject(IntPtr weakObjectPtr);

            public abstract Dictionary<string, int> GetFNames(params string[] names);
            public abstract int GetFName(string name);

            public abstract int GetFieldOffset(string className, string fieldName);
            public abstract int GetFieldOffset(IntPtr uobject, string fieldName);
            public abstract IReadOnlyDictionary<string, int> GetFieldOffsets(string className);
            public abstract string GetFieldType(string className, string fieldName);
            public abstract int GetFieldElementSize(string className, string fieldName);
            public abstract string GetFieldDebugInfo(string className, string fieldName);
            public abstract string GetObjectPropertyClassName(string className, string fieldName);
            public abstract void LogDiagnostics();

#if UE_DEBUG
            public abstract void Debug(string gameName);
#endif
        }

        private abstract class Unreal4HelperBase : UnrealHelperBase {

            protected Unreal4HelperBase(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : this(game, token, "4", fileVersion, logger) { }

            protected Unreal4HelperBase(ProcessWrapper game, CancellationToken token, string fileName, string fileVersion, Logger logger = null)
                : base(game, token, fileName, fileVersion, logger) { }

            public override IntPtr GetUObject(string name) {
                Log("Looking for object: " + name);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(IntPtr uobject in UObjectSequence()) {
                        try {
                            if(UObjectName(uobject) == name) {
                                Log("  -> " + uobject.ToString("X") + " class=" + UObjectClassName(uobject));
                                return uobject;
                            }
                        } catch {
                            continue;
                        }
                    }
                    Sleep();
                }
            }

            public override IntPtr GetUObject(int fname) {
                Log("Looking for object: " + fname);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(IntPtr uobject in UObjectSequence()) {
                        try {
                            if(UObjectFName(uobject) == fname) {
                                Log("  -> " + uobject.ToString("X") + " class=" + UObjectClassName(uobject));
                                return uobject;
                            }
                        } catch {
                            continue;
                        }
                    }
                    Sleep();
                }
            }

            public override IntPtr FindLiveUObject(string className) {
                return FindLiveUObject(className, null);
            }

            public override IntPtr FindLiveUObject(string className, Predicate<IntPtr> predicate) {
                var classMatches = new Dictionary<IntPtr, bool>();

                foreach(IntPtr uobject in UObjectSequence()) {
                    try {
                        IntPtr uclass = UObjectClass(uobject);
                        if(!classMatches.TryGetValue(uclass, out bool isClassMatch)) {
                            isClassMatch = ClassInheritsFrom(uclass, className);
                            classMatches[uclass] = isClassMatch;
                        }

                        if(!isClassMatch || IsDefaultObjectOrSubobject(uobject)) {
                            continue;
                        }

                        if(predicate != null && !predicate(uobject)) {
                            continue;
                        }

                        Log($"Live object found: {className} -> {uobject.ToString("X")} class={UObjectClassName(uobject)} name={UObjectName(uobject)}");
                        return uobject;
                    } catch {
                        continue;
                    }
                }

                Log($"Live object not found: {className}");
                return default;
            }

            public override IReadOnlyList<IntPtr> FindLiveUObjects(string className, int maxCount = 256) {
                var result = new List<IntPtr>();
                var classMatches = new Dictionary<IntPtr, bool>();

                foreach(IntPtr uobject in UObjectSequence()) {
                    try {
                        IntPtr uclass = UObjectClass(uobject);
                        if(!classMatches.TryGetValue(uclass, out bool isClassMatch)) {
                            isClassMatch = ClassInheritsFrom(uclass, className);
                            classMatches[uclass] = isClassMatch;
                        }

                        if(!isClassMatch || IsDefaultObjectOrSubobject(uobject)) {
                            continue;
                        }

                        result.Add(uobject);
                        if(result.Count >= maxCount) {
                            break;
                        }
                    } catch {
                        continue;
                    }
                }

                return result;
            }

            public override string GetUObjectName(IntPtr uobject) {
                return uobject == default ? "" : UObjectName(uobject);
            }

            public override string GetUObjectPath(IntPtr uobject) {
                return uobject == default ? "" : UObjectPath(uobject);
            }

            public override string GetUObjectClassName(IntPtr uobject) {
                return uobject == default ? "" : UObjectClassName(uobject);
            }

            public override IntPtr ResolveWeakObject(IntPtr weakObjectPtr) {
                if(weakObjectPtr == default) return default;
                int objectIndex = game.Read<int>(weakObjectPtr);
                if(objectIndex < 0) return default;
                foreach(IntPtr uobject in UObjectSequence()) {
                    if(game.Read<int>(uobject + data.GetOffset("UObjectBase", "InternalIndex")) == objectIndex) return uobject;
                }
                return default;
            }

            public override int GetFName(string name) {
                Log("Looking for name: " + name);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(FName fname in FNameSequence()) {
                        if(fname.name == name) {
                            Log("  -> " + fname.name + " " + fname.index);
                            return fname.index;
                        }
                    }
                    Sleep();
                }
            }

            public override Dictionary<string, int> GetFNames(params string[] names) {
                Log("Looking for names: " + String.Join(", ", names));
                var dict = new Dictionary<string, int>(names.Length);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(FName fname in FNameSequence()) {
                        if(Array.IndexOf(names, fname.name) != -1 && !dict.ContainsKey(fname.name)) {
                            dict.Add(fname.name, fname.index);
                            Log("  -> " + fname.name + " " + fname.index);
                            if(dict.Count == names.Length) {
                                return dict;
                            }
                        }
                    }
                    Sleep();
                }
            }

            public override Dictionary<string, IntPtr> GetUObjects(params string[] names) {
                Log("Looking for objects: " + String.Join(", ", names));
                var dict = new Dictionary<string, IntPtr>(names.Length);
                var missing = new HashSet<string>(names);
                while(true) {
                    token.ThrowIfCancellationRequested();
                    foreach(IntPtr uobject in UObjectSequence()) {
                        string objectName;
                        try {
                            objectName = UObjectName(uobject);
                        } catch {
                            continue;
                        }

                        if(missing.Remove(objectName)) {
                            dict.Add(objectName, uobject);
                            Log("  -> " + objectName + " " + uobject.ToString("X") + " class=" + UObjectClassName(uobject));
                            if(missing.Count == 0) {
                                return dict;
                            }
                        }
                    }
                    Sleep();
                }
            }

            public override void LogDiagnostics() {
                Log("Diagnostics begin");
                LogFNameDiagnostics();
                LogUObjectDiagnostics();
                LogTargetClassDiagnostics();
                LogLiveObjectDiagnostics();
                Log("Diagnostics end");
            }

            private static readonly string[] TargetClassNames = {
                "SN2GameInstance",
                "SN2GameState",
                "SN2PlayerCharacter",
                "SN2PlayerController",
                "SN2PlayerState",
                "SN2PlayerOxygenViewModel",
                "SN2PlayerHealthViewModel",
                "SN2PlayerWaterViewModel",
                "SN2InventoryViewModel",
                "SN2InventoryScreenViewModel",
                "SN2QuickSlotsBarViewModel",
                "SN2Fabricator",
                "SN2FabricatorViewModel",
                "SN2CraftingMenu",
                "SN2LoadingScreenViewModel",
                "SN2HUDViewModel",
                "SN2PdaViewModel",
                "SN2PickupItem",
                "UWEGameInstance",
                "UWEGameplayTimeComponent",
                "UWEGameplayPlayerController",
                "UWEGameplayPlayerState",
                "UWEGameStateBase",
                "UWETimeOfDayComponent",
                "UWELoadingScreenGISubsystem",
                "UWELoadingScreenManager",
                "UWEInventoryComponent",
                "UWEInventoryStorage",
                "UWEInventorySubsystem",
                "UWEToolbarComponent",
                "UWEEquipmentComponent",
                "UWECraftingComponent",
                "UWECrafterComponent",
                "UWECraftingRecipe",
                "UWEUnlockableAsset",
                "UWEDatabankWorldSubsystem",
                "UWEStoryEventSubsystem",
                "UWEStoryGoalsWorldSubsystem"
            };

            private static readonly string[] LiveObjectTargetClassNames = {
                "SN2GameInstance",
                "SN2GameState",
                "SN2PlayerCharacter",
                "SN2PlayerController",
                "SN2PlayerState",
                "SN2PlayerOxygenViewModel",
                "SN2PlayerHealthViewModel",
                "SN2PlayerWaterViewModel",
                "SN2InventoryViewModel",
                "SN2InventoryScreenViewModel",
                "SN2QuickSlotsBarViewModel",
                "SN2Fabricator",
                "SN2FabricatorViewModel",
                "SN2CraftingMenu",
                "SN2LoadingScreenViewModel",
                "SN2LocalPlayer",
                "SN2HUDViewModel",
                "SN2WorldHUD",
                "SN2PdaViewModel",
                "SN2LobbyGameMode",
                "SN2WorldGameMode",
                "UWEGameplayTimeComponent",
                "UWETimeOfDayComponent",
                "UWELoadingScreenGISubsystem",
                "UWELoadingScreenManager",
                "UWEInventoryComponent",
                "UWEInventoryStorage",
                "UWEInventorySubsystem",
                "UWEToolbarComponent",
                "UWEEquipmentComponent",
                "UWECraftingComponent",
                "UWECrafterComponent",
                "UWEDatabankWorldSubsystem",
                "UWEStoryEventSubsystem",
                "UWEStoryGoalsWorldSubsystem"
            };

            private void LogFNameDiagnostics() {
                string[] targets = {
                    "None",
                    "Class",
                    "Object",
                    "Package",
                    "Function",
                    "ScriptStruct",
                    "Enum",
                    "Actor",
                    "Pawn",
                    "PlayerController",
                    "GameInstance",
                    "World",
                    "BlueprintGeneratedClass"
                };
                var missing = new HashSet<string>(targets);
                int scanned = 0;
                int samples = 0;

                try {
                    foreach(FName fname in FNameSequence()) {
                        scanned++;

                        if(!String.IsNullOrEmpty(fname.name) && samples < 40) {
                            Log($"FName sample {samples + 1}: {fname.index} {fname.name}");
                            samples++;
                        }

                        if(missing.Remove(fname.name)) {
                            Log($"FName target found: {fname.name} = {fname.index}");
                        }

                        if(scanned >= 250000 || (missing.Count == 0 && samples >= 40)) {
                            break;
                        }
                    }

                    Log($"FName diagnostics scanned={scanned}, samples={samples}, missing=[{String.Join(", ", missing)}]");
                } catch(Exception e) {
                    Log("FName diagnostics failed: " + e.GetType().Name + ": " + e.Message);
                }
            }

            private void LogUObjectDiagnostics() {
                LogUObjectArrayDiagnostics();

                string[] targets = {
                    "Class",
                    "Object",
                    "Package",
                    "Function",
                    "ScriptStruct",
                    "Enum",
                    "Actor",
                    "Pawn",
                    "PlayerController",
                    "GameInstance",
                    "World",
                    "BlueprintGeneratedClass"
                };
                var missingNames = new HashSet<string>(targets);
                var missingClasses = new HashSet<string>(targets);
                int scanned = 0;
                int samples = 0;
                int readFailures = 0;

                try {
                    foreach(IntPtr uobject in UObjectSequence()) {
                        scanned++;
                        if(scanned > 100000) {
                            break;
                        }

                        string objectName;
                        string className;
                        try {
                            objectName = UObjectName(uobject);
                            className = UObjectClassName(uobject);
                        } catch(Exception e) {
                            readFailures++;
                            if(readFailures <= 8) {
                                Log($"UObject read failed at {uobject.ToString("X")}: {e.GetType().Name}: {e.Message}");
                            }
                            continue;
                        }

                        if(!String.IsNullOrEmpty(objectName) && !String.IsNullOrEmpty(className) && samples < 50) {
                            Log($"UObject sample {samples + 1}: {uobject.ToString("X")} class={className} name={objectName}");
                            samples++;
                        }

                        if(missingNames.Remove(objectName)) {
                            Log($"UObject target name found: {objectName} at {uobject.ToString("X")} class={className}");
                        }

                        if(missingClasses.Remove(className)) {
                            Log($"UObject target class found: {className} at {uobject.ToString("X")} name={objectName}");
                        }

                        if(missingNames.Count == 0 && missingClasses.Count == 0 && samples >= 50) {
                            break;
                        }
                    }

                    Log($"UObject diagnostics scanned={scanned}, samples={samples}, readFailures={readFailures}, missingNames=[{String.Join(", ", missingNames)}], missingClasses=[{String.Join(", ", missingClasses)}]");
                } catch(Exception e) {
                    Log("UObject diagnostics failed: " + e.GetType().Name + ": " + e.Message);
                }
            }

            protected virtual void LogUObjectArrayDiagnostics() { }

            private void LogTargetClassDiagnostics() {
                Log("Target class diagnostics begin");
                Dictionary<string, IntPtr> classObjects = FindClassObjects(TargetClassNames);
                foreach(string targetName in TargetClassNames) {
                    if(!classObjects.TryGetValue(targetName, out IntPtr classObject)) {
                        Log($"Target class {targetName}: <missing>");
                        continue;
                    }

                    string objectClassName = SafeUObjectClassName(classObject);
                    string path = SafeUObjectPath(classObject);
                    Log($"Target class {targetName}: {classObject.ToString("X")} class={objectClassName} path={path}");
                    LogClassFieldDiagnostics(classObject, targetName, 48);
                }
                Log("Target class diagnostics end");
            }

            private void LogLiveObjectDiagnostics() {
                Log("Live object diagnostics begin");
                Dictionary<string, IntPtr> classObjects = FindClassObjects(LiveObjectTargetClassNames);
                var stats = new Dictionary<string, LiveObjectStats>(LiveObjectTargetClassNames.Length);
                foreach(string targetName in LiveObjectTargetClassNames) {
                    if(!classObjects.TryGetValue(targetName, out IntPtr classObject)) {
                        Log($"Live object target {targetName}: class missing");
                        continue;
                    }

                    stats.Add(targetName, new LiveObjectStats { ClassObject = classObject });
                }

                int scanned = 0;
                int readFailures = 0;
                int defaultFiltered = 0;
                foreach(IntPtr uobject in UObjectSequence()) {
                    scanned++;
                    if(scanned > 180000) {
                        break;
                    }

                    try {
                        if(IsClassObject(uobject)) {
                            continue;
                        }

                        string objectName = UObjectName(uobject);
                        if(IsDefaultObjectOrSubobject(uobject)) {
                            defaultFiltered++;
                            continue;
                        }

                        string className = UObjectClassName(uobject);
                        string path = null;
                        foreach(string targetName in MatchingTargetClassNames(UObjectClass(uobject), stats)) {
                            LiveObjectStats targetStats = stats[targetName];
                            targetStats.Matches++;
                            if(targetStats.Logged >= 3) {
                                continue;
                            }

                            path = path ?? UObjectPath(uobject);
                            Log($"Live object {targetName} #{targetStats.Matches}: {uobject.ToString("X")} class={className} name={objectName} path={path}");
                            LogInstanceFieldPreview(uobject, targetName, className, 18);
                            targetStats.Logged++;
                        }
                    } catch(Exception e) {
                        readFailures++;
                        if(readFailures <= 3) {
                            Log($"Live object read failed at {uobject.ToString("X")}: {e.GetType().Name}: {e.Message}");
                        }
                    }
                }

                foreach(string targetName in LiveObjectTargetClassNames) {
                    if(stats.TryGetValue(targetName, out LiveObjectStats targetStats)) {
                        Log($"Live object target {targetName}: matches={targetStats.Matches}, logged={targetStats.Logged}, scanned={scanned}, readFailures={readFailures}, defaultFiltered={defaultFiltered}");
                    }
                }
                Log("Live object diagnostics end");
            }

            private class LiveObjectStats {
                public IntPtr ClassObject;
                public int Matches;
                public int Logged;
            }

            private void LogGameDiscoveryDiagnostics() {
                string[] gameplayKeywords = {
                    "Subnautica",
                    "SN2",
                    "UWE",
                    "Marmot",
                    "UnknownWorlds",
                    "Player",
                    "Character",
                    "Pawn",
                    "Controller",
                    "GameMode",
                    "GameState",
                    "GameInstance",
                    "World",
                    "Inventory",
                    "Item",
                    "Pickup",
                    "Blueprint",
                    "Tech",
                    "Unlock",
                    "Recipe",
                    "Craft",
                    "Fabricator",
                    "PDA",
                    "Biome",
                    "Loading",
                    "Menu",
                    "Save",
                    "Story",
                    "Intro",
                    "Oxygen",
                    "Health",
                    "Death"
                };
                var targetModules = new HashSet<string> {
                    "Subnautica2",
                    "UWEGameplay",
                    "UWEGameModeTypes",
                    "UWEInventory",
                    "UWEUnlockables",
                    "UWECrafting",
                    "UWEEquipment",
                    "UWEDynamicItems",
                    "UWEPlayerReady",
                    "UWEMovement",
                    "UWEInteract",
                    "UWEStoryEvents",
                    "UWEStoryGoals",
                    "UWEDatabank",
                    "UWELoadingScreen",
                    "CommonLoadingScreen",
                    "UWEFrontend",
                    "UWEVolumeTracker",
                    "UWEGameplayTime",
                    "UWESaveSystem",
                    "UWEPinnedRecipes",
                    "UWEBiomods",
                    "UWEFarming",
                    "UWEScanner",
                    "UWECamera",
                    "UWEPlayerCustomization",
                    "UWEVehicle",
                    "UWETrigger",
                    "UWECarryable",
                    "UWEAlerts",
                    "UWEPower",
                    "UWEGlobalSimulation"
                };
                int scanned = 0;
                int scriptPackages = 0;
                int ignoredScriptPackages = 0;
                int nonEngineScriptPackages = 0;
                int targetScriptPackages = 0;
                int engineKeywordClasses = 0;
                int gameClasses = 0;
                int gameObjects = 0;
                int fieldDumps = 0;

                Log("Game discovery begin");
                try {
                    foreach(IntPtr uobject in UObjectSequence()) {
                        scanned++;
                        if(scanned > 250000) {
                            break;
                        }

                        string objectName;
                        string className;
                        string path;
                        try {
                            objectName = UObjectName(uobject);
                            className = UObjectClassName(uobject);
                            path = UObjectPath(uobject);
                        } catch {
                            continue;
                        }

                        string module = ScriptModule(path);
                        bool isScriptPackage = className == "Package" && objectName.StartsWith("/Script/", StringComparison.Ordinal);
                        if(isScriptPackage) {
                            scriptPackages++;
                            string packageModule = ScriptModule(objectName);
                            if(IsIgnoredScriptModule(packageModule)) {
                                ignoredScriptPackages++;
                            } else {
                                nonEngineScriptPackages++;
                                if(targetModules.Contains(packageModule)) {
                                    targetScriptPackages++;
                                    Log($"Target script package {targetScriptPackages}: {objectName}");
                                } else if(nonEngineScriptPackages <= 80) {
                                    Log($"Game script package candidate {nonEngineScriptPackages}: {objectName}");
                                }
                            }
                        }

                        bool isIgnoredScriptObject = !String.IsNullOrEmpty(module) && IsIgnoredScriptModule(module);
                        bool isTargetScriptObject = targetModules.Contains(module);
                        bool isGameAssetObject = StartsWithAny(objectName, "/Game/", "/Subnautica", "/SN2", "/UWE")
                                              || StartsWithAny(path, "/Game/", "/Subnautica", "/SN2", "/UWE")
                                              || path.IndexOf("/Game/", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool hasGameplayKeyword = ContainsAny(objectName, gameplayKeywords) || ContainsAny(path, gameplayKeywords) || ContainsAny(className, gameplayKeywords);
                        bool isClass = className == "Class" || className == "BlueprintGeneratedClass";

                        if(isIgnoredScriptObject) {
                            if(isClass && hasGameplayKeyword && engineKeywordClasses < 40) {
                                engineKeywordClasses++;
                                Log($"Engine keyword class {engineKeywordClasses}: {uobject.ToString("X")} class={className} name={objectName} path={path}");
                            }
                            continue;
                        }

                        if(!isTargetScriptObject && !isGameAssetObject) {
                            continue;
                        }

                        if(isClass && gameClasses < 420) {
                            gameClasses++;
                            Log($"Game class candidate {gameClasses}: {uobject.ToString("X")} module={module} class={className} name={objectName} path={path}");

                            if(fieldDumps < 48 && (hasGameplayKeyword || isTargetScriptObject || isGameAssetObject)) {
                                LogClassFieldDiagnostics(uobject, objectName);
                                fieldDumps++;
                            }
                            continue;
                        }

                        if(gameObjects < 420) {
                            gameObjects++;
                            Log($"Game object candidate {gameObjects}: {uobject.ToString("X")} module={module} class={className} name={objectName} path={path}");
                        }
                    }
                } catch(Exception e) {
                    Log("Game discovery failed: " + e.GetType().Name + ": " + e.Message);
                }

                Log($"Game discovery scanned={scanned}, scriptPackages={scriptPackages}, ignoredScriptPackages={ignoredScriptPackages}, nonEngineScriptPackages={nonEngineScriptPackages}, targetScriptPackages={targetScriptPackages}, engineKeywordClasses={engineKeywordClasses}, gameClasses={gameClasses}, gameObjects={gameObjects}, fieldDumps={fieldDumps}");
                Log("Game discovery end");
            }

            private void LogClassFieldDiagnostics(IntPtr classObject, string className, int maxFields = 80) {
                int count = 0;
                try {
                    foreach(IntPtr field in FieldSequence(classObject)) {
                        if(count >= maxFields) {
                            break;
                        }

                        try {
                            int offset = game.Read<int>(field + data.GetOffset("Property", "Offset_Internal"));
                            Log($"Game class field {className}.{FieldName(field)} +0x{offset:X} type={PropertyType(field)}");
                            count++;
                        } catch(Exception e) {
                            Log($"Game class field read failed {className} field={field.ToString("X")}: {e.GetType().Name}: {e.Message}");
                            count++;
                        }
                    }
                } catch(Exception e) {
                    Log($"Game class fields failed {className}: {e.GetType().Name}: {e.Message}");
                    return;
                }

                if(count == 0) {
                    Log($"Game class fields {className}: <none>");
                } else {
                    Log($"Game class fields {className}: countLogged={count}");
                }
            }

            private void LogInstanceFieldPreview(IntPtr uobject, string targetClassName, string exactClassName, int maxFields) {
                int count = 0;
                int skipped = 0;
                bool targetClassReached = String.IsNullOrEmpty(targetClassName);

                try {
                    foreach(IntPtr classObject in ClassHierarchy(UObjectClass(uobject))) {
                        string ownerClassName = SafeUObjectName(classObject);
                        if(!targetClassReached) {
                            if(ownerClassName == targetClassName) {
                                targetClassReached = true;
                            } else {
                                continue;
                            }
                        }

                        foreach(IntPtr field in FieldSequence(classObject)) {
                            if(count >= maxFields) {
                                Log($"Live object fields {targetClassName}/{exactClassName}: countLogged={count}, skipped={skipped}");
                                return;
                            }

                            try {
                                string fieldName = FieldName(field);
                                string type = PropertyType(field);
                                int offset = game.Read<int>(field + data.GetOffset("Property", "Offset_Internal"));
                                if(TryReadPropertyPreview(uobject, field, type, out string value)) {
                                    Log($"Live object field {ownerClassName}.{fieldName} +0x{offset:X} type={type} value={value}");
                                    count++;
                                } else {
                                    skipped++;
                                }
                            } catch {
                                skipped++;
                            }
                        }
                    }
                } catch(Exception e) {
                    Log($"Live object fields failed {exactClassName}: {e.GetType().Name}: {e.Message}");
                    return;
                }

                if(count == 0) {
                    Log($"Live object fields {targetClassName}/{exactClassName}: <no primitive fields>, skipped={skipped}");
                } else {
                    Log($"Live object fields {targetClassName}/{exactClassName}: countLogged={count}, skipped={skipped}");
                }
            }

            private string UObjectPath(IntPtr uobject) {
                var names = new List<string>();
                IntPtr current = uobject;
                for(int depth = 0; depth < 12 && current != default; depth++) {
                    string name = UObjectName(current);
                    if(!String.IsNullOrEmpty(name)) {
                        names.Add(name);
                    }
                    current = UObjectOuter(current);
                }

                names.Reverse();
                return String.Join(".", names);
            }

            private string SafeUObjectName(IntPtr uobject) {
                try {
                    return UObjectName(uobject);
                } catch {
                    return "";
                }
            }

            private string SafeUObjectClassName(IntPtr uobject) {
                try {
                    return UObjectClassName(uobject);
                } catch {
                    return "";
                }
            }

            private string SafeUObjectPath(IntPtr uobject) {
                try {
                    return UObjectPath(uobject);
                } catch {
                    return "";
                }
            }

            private IntPtr FindClassObject(string className) {
                if(classObjectCache.TryGetValue(className, out IntPtr cachedClassObject)) {
                    return cachedClassObject;
                }

                Dictionary<string, IntPtr> classObjects = FindClassObjects(new[] { className });
                return classObjects.TryGetValue(className, out IntPtr classObject) ? classObject : default;
            }

            private Dictionary<string, IntPtr> FindClassObjects(string[] classNames) {
                var result = new Dictionary<string, IntPtr>(classNames.Length);
                var missing = new HashSet<string>(classNames);
                foreach(string className in classNames) {
                    if(classObjectCache.TryGetValue(className, out IntPtr cachedClassObject)) {
                        result[className] = cachedClassObject;
                        missing.Remove(className);
                    }
                }

                if(missing.Count == 0) {
                    return result;
                }

                int scanned = 0;
                foreach(IntPtr uobject in UObjectSequence()) {
                    scanned++;
                    if(scanned > 180000 || missing.Count == 0) {
                        break;
                    }
                    try {
                        string objectName = UObjectName(uobject);
                        if(missing.Contains(objectName) && (IsClassObject(uobject) || UObjectClassName(uobject) == "ScriptStruct")) {
                            result.Add(objectName, uobject);
                            classObjectCache[objectName] = uobject;
                            missing.Remove(objectName);
                        }
                    } catch {
                        continue;
                    }
                }

                return result;
            }

            private IEnumerable<string> MatchingTargetClassNames(IntPtr classObject, Dictionary<string, LiveObjectStats> stats) {
                var yielded = new HashSet<string>();
                for(int depth = 0; depth < 48 && classObject != default; depth++) {
                    string className = UObjectName(classObject);
                    if(stats.ContainsKey(className) && yielded.Add(className)) {
                        yield return className;
                    }
                    classObject = SuperStruct(classObject);
                }
            }

            private bool IsClassObject(IntPtr uobject) {
                string className = UObjectClassName(uobject);
                return className == "Class" || className == "BlueprintGeneratedClass";
            }

            private bool IsDefaultObjectName(string objectName) {
                return objectName != null && objectName.StartsWith("Default__", StringComparison.Ordinal);
            }

            private bool IsDefaultObjectOrSubobject(IntPtr uobject) {
                IntPtr current = uobject;
                for(int depth = 0; depth < 16 && current != default; depth++) {
                    if(IsDefaultObjectName(UObjectName(current))) {
                        return true;
                    }
                    current = UObjectOuter(current);
                }

                return false;
            }

            private bool IsInstanceOf(IntPtr uobject, string targetClassName) {
                IntPtr classObject = UObjectClass(uobject);
                return ClassInheritsFrom(classObject, targetClassName);
            }

            private bool ClassInheritsFrom(IntPtr classObject, string targetClassName) {
                string cacheKey = classObject.ToInt64().ToString("X") + "|" + targetClassName;
                if(classInheritanceCache.TryGetValue(cacheKey, out bool cachedResult)) {
                    return cachedResult;
                }

                bool result = false;
                for(int depth = 0; depth < 48 && classObject != default; depth++) {
                    string className = UObjectName(classObject);
                    if(className == targetClassName) {
                        result = true;
                        break;
                    }
                    classObject = SuperStruct(classObject);
                }

                classInheritanceCache[cacheKey] = result;
                return result;
            }

            private IEnumerable<IntPtr> ClassHierarchy(IntPtr classObject) {
                for(int depth = 0; depth < 24 && classObject != default; depth++) {
                    yield return classObject;
                    classObject = SuperStruct(classObject);
                }
            }

            private bool TryReadPropertyPreview(IntPtr uobject, IntPtr field, string type, out string value) {
                value = null;
                int offset = game.Read<int>(field + data.GetOffset("Property", "Offset_Internal"));
                IntPtr address = uobject + offset;

                switch(type) {
                    case "bool":
                        byte byteOffset = game.Read<byte>(field + data.GetOffset("BoolProperty", "ByteOffset"));
                        byte byteMask = game.Read<byte>(field + data.GetOffset("BoolProperty", "ByteMask"));
                        byte boolByte = game.Read<byte>(address + byteOffset);
                        value = ((boolByte & byteMask) != 0).ToString();
                        return true;

                    case "byte":
                        value = game.Read<byte>(address).ToString();
                        return true;
                    case "ushort":
                        value = game.Read<ushort>(address).ToString();
                        return true;
                    case "uint":
                        value = game.Read<uint>(address).ToString();
                        return true;
                    case "ulong":
                        value = game.Read<ulong>(address).ToString();
                        return true;
                    case "sbyte":
                        value = game.Read<sbyte>(address).ToString();
                        return true;
                    case "short":
                        value = game.Read<short>(address).ToString();
                        return true;
                    case "int":
                        value = game.Read<int>(address).ToString();
                        return true;
                    case "long":
                        value = game.Read<long>(address).ToString();
                        return true;
                    case "float":
                        value = game.Read<float>(address).ToString("G6");
                        return true;
                    case "double":
                        value = game.Read<double>(address).ToString("G8");
                        return true;
                    case "FName":
                        value = GetCachedFNameEntryName(game.Read<int>(address));
                        return true;
                    case "char[]*":
                        value = ReadFStringPreview(address);
                        return true;
                }

                if(type.StartsWith("enum ", StringComparison.Ordinal)) {
                    int elementSize = game.Read<int>(field + data.GetOffset("Property", "ElementSize"));
                    switch(elementSize) {
                        case 1:
                            value = game.Read<byte>(address).ToString();
                            return true;
                        case 2:
                            value = game.Read<ushort>(address).ToString();
                            return true;
                        case 4:
                            value = game.Read<int>(address).ToString();
                            return true;
                        case 8:
                            value = game.Read<long>(address).ToString();
                            return true;
                        default:
                            value = game.Read<byte>(address).ToString();
                            return true;
                    }
                }

                if(type.StartsWith("TArray<", StringComparison.Ordinal)) {
                    value = ReadTArrayPreview(address);
                    return true;
                }

                if(type.EndsWith("*", StringComparison.Ordinal)) {
                    IntPtr ptr = game.Read<IntPtr>(address);
                    value = ReadObjectPointerPreview(ptr);
                    return true;
                }

                return false;
            }

            private string ReadObjectPointerPreview(IntPtr ptr) {
                if(ptr == default) {
                    return "null";
                }

                if(!LooksLikeDataPointer(ptr)) {
                    return "0x" + ptr.ToString("X");
                }

                try {
                    return "0x" + ptr.ToString("X") + " " + UObjectClassName(ptr) + "." + UObjectName(ptr);
                } catch {
                    return "0x" + ptr.ToString("X");
                }
            }

            private string ReadTArrayPreview(IntPtr address) {
                IntPtr dataPtr = game.Read<IntPtr>(address);
                int num = game.Read<int>(address + game.PointerSize);
                int max = game.Read<int>(address + game.PointerSize + 4);
                return "data=0x" + dataPtr.ToString("X") + " num=" + num + " max=" + max;
            }

            private string ReadFStringPreview(IntPtr address) {
                IntPtr dataPtr = game.Read<IntPtr>(address);
                int num = game.Read<int>(address + game.PointerSize);
                int max = game.Read<int>(address + game.PointerSize + 4);

                if(num <= 0) {
                    return "\"\"";
                }

                if(num > 512 || max < num || !LooksLikeDataPointer(dataPtr)) {
                    return "data=0x" + dataPtr.ToString("X") + " num=" + num + " max=" + max;
                }

                int byteLength = Math.Max(0, (num - 1) * 2);
                string text = game.ReadString(dataPtr, byteLength, EStringType.UTF16);
                return "\"" + SanitizePreview(text) + "\"";
            }

            private static string SanitizePreview(string value) {
                if(value == null) {
                    return "";
                }

                value = value.Replace("\r", "\\r").Replace("\n", "\\n").TrimEnd('\0');
                return value.Length <= 96 ? value : value.Substring(0, 96) + "...";
            }

            private static bool LooksLikeDataPointer(IntPtr ptr) {
                long value = (long)ptr;
                return value >= 0x10000 && value <= 0x00007FFFFFFFFFFF;
            }

            protected virtual IntPtr UObjectOuter(IntPtr uobject) {
                return game.Read<IntPtr>(uobject + data.GetOffset("UObjectBase", "Outer"));
            }

            private static bool ContainsAny(string text, string[] keywords) {
                if(String.IsNullOrEmpty(text)) {
                    return false;
                }

                foreach(string keyword in keywords) {
                    if(text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) {
                        return true;
                    }
                }

                return false;
            }

            private static bool StartsWithAny(string text, params string[] prefixes) {
                if(String.IsNullOrEmpty(text)) {
                    return false;
                }

                foreach(string prefix in prefixes) {
                    if(text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                        return true;
                    }
                }

                return false;
            }

            private static string ScriptModule(string path) {
                if(String.IsNullOrEmpty(path)) {
                    return "";
                }

                const string prefix = "/Script/";
                int start = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if(start < 0) {
                    return "";
                }

                start += prefix.Length;
                int dot = path.IndexOf('.', start);
                int slash = path.IndexOf('/', start);
                int end = path.Length;
                if(dot >= 0) {
                    end = Math.Min(end, dot);
                }
                if(slash >= 0) {
                    end = Math.Min(end, slash);
                }

                return end > start ? path.Substring(start, end - start) : "";
            }

            private static bool IsIgnoredScriptModule(string module) {
                if(String.IsNullOrEmpty(module)) {
                    return false;
                }

                switch(module) {
                    case "AIModule":
                    case "AnimationCore":
                    case "AnimGraphRuntime":
                    case "AssetRegistry":
                    case "AudioExtensions":
                    case "AudioLinkCore":
                    case "AudioLinkEngine":
                    case "AudioMixer":
                    case "AudioModulation":
                    case "BuildPatchServices":
                    case "ChaosSolverEngine":
                    case "CinematicCamera":
                    case "ClothingSystemRuntimeCommon":
                    case "ClothingSystemRuntimeInterface":
                    case "ClothingSystemRuntimeNv":
                    case "Constraints":
                    case "CoreUObject":
                    case "DataflowCore":
                    case "DataflowEngine":
                    case "DataflowSimulation":
                    case "DataRegistry":
                    case "DeveloperSettings":
                    case "Engine":
                    case "EngineSettings":
                    case "EnhancedInput":
                    case "EyeTracker":
                    case "FieldNotification":
                    case "FieldSystemEngine":
                    case "Foliage":
                    case "GameplayAbilities":
                    case "GameplayTags":
                    case "GameplayTasks":
                    case "GeometryCollectionEngine":
                    case "HeadMountedDisplay":
                    case "HttpBlueprint":
                    case "ImageCore":
                    case "ImageWriteQueue":
                    case "InputCore":
                    case "InteractiveToolsFramework":
                    case "IrisCore":
                    case "ISMPool":
                    case "JsonUtilities":
                    case "Landscape":
                    case "LevelSequence":
                    case "MassEntity":
                    case "MaterialShaderQualitySettings":
                    case "MediaAssets":
                    case "MeshDescription":
                    case "MetasoundEngine":
                    case "MetasoundFrontend":
                    case "ModelingComponents":
                    case "ModelViewViewModel":
                    case "MoviePlayer":
                    case "MovieScene":
                    case "MovieSceneCapture":
                    case "MovieSceneTracks":
                    case "MRMesh":
                    case "NetCore":
                    case "Niagara":
                    case "NiagaraCore":
                    case "NiagaraShader":
                    case "NNE":
                    case "OnlineSubsystem":
                    case "OnlineSubsystemUtils":
                    case "Overlay":
                    case "PacketHandler":
                    case "PhysicsCore":
                    case "PropertyPath":
                    case "Renderer":
                    case "Serialization":
                    case "SkeletalMeshDescription":
                    case "Slate":
                    case "SlateCore":
                    case "SlateRHIRenderer":
                    case "SocketSubsystemEOS":
                    case "StaticMeshDescription":
                    case "TimeManagement":
                    case "TypedElementFramework":
                    case "TypedElementRuntime":
                    case "UMG":
                    case "WaveTable":
                        return true;
                    default:
                        return false;
                }
            }

#if UE_DEBUG
            public override void Debug(string gameName) {

                DebugNames();
                DebugObjects();

                void DebugNames() {
                    Log("Debug FNames");
                    string namesPath = $"_{gameName}_Names.log";
                    using(StreamWriter write = new StreamWriter(namesPath)) {
                        foreach(FName fname in FNameSequence()) {
                            string dbgName = $"{fname.index,-7} {fname.name}";
                            write.WriteLine(dbgName);
                        }
                    }
                }

                void DebugObjects() {
                    Log("Debug GObjects");
                    var dict = new Dictionary<string, Tuple<List<IntPtr>, string>>();
                    foreach(IntPtr uobject in UObjectSequence()) {

                        string className = UObjectClassName(uobject);
                        IntPtr uobject2 = uobject;
                        if(className.EndsWith("Property") || className.Equals("Function") || className.Equals("Package") || className.Equals("Enum")) {
                            continue;
                        }

                        string objName = UObjectName(uobject);
                        //if(objName.StartsWith("Default__")) {
                        //    continue;
                        //}

                        objName = className + " " + objName;

                        string superName = UObjectName(SuperStruct(uobject2));
                        if(!String.IsNullOrEmpty(superName)) {
                            objName += " : " + superName;
                        }

                        if(className != "Class" && className != "ScriptStruct") {
                            uobject2 = UObjectClass(uobject);
                        }

                        if(dict.TryGetValue(objName, out var tmp)) {
                            tmp.Item1.Add(uobject);
                        } else {
                            int parentFieldsSize = game.Read<int>(SuperStruct(uobject2) + data.GetOffset("UStruct", "PropertiesSize"));
                            string dbgFields = "";
                            foreach(IntPtr field in FieldSequence(uobject2)) {
                                int offset = game.Read<int>(field + data.GetOffset("Property", "Offset_Internal"));
                                if(offset < parentFieldsSize) {
                                    break;
                                }
                                dbgFields += $"  {offset,-4:X} {PropertyType(field),-32} {FieldName(field)}" + Environment.NewLine;
                            }
                            dict.Add(objName, new Tuple<List<IntPtr>, string>(new List<IntPtr> { uobject }, dbgFields));
                        }
                    }
                    string objectsPath = $"_{gameName}_Objects.log";
                    using(StreamWriter write = new StreamWriter(objectsPath)) {
                        foreach(var kvp in dict) {
                            foreach(var ptr in kvp.Value.Item1) {
                                write.WriteLine(ptr.ToString("X8"));
                            }
                            write.WriteLine(kvp.Key);
                            write.WriteLine(kvp.Value.Item2);
                        }
                    }
                }
            }
#endif

            protected virtual IntPtr SuperStruct(IntPtr uobject) {
                return game.Read<IntPtr>(uobject + data.GetOffset("UStruct", "SuperStruct"));
            }

            public override int GetFieldOffset(string className, string fieldName) {
                string cacheKey = className + "." + fieldName;
                if(fieldOffsetCache.TryGetValue(cacheKey, out int cachedOffset)) {
                    return cachedOffset;
                }

                IntPtr classObject = FindClassObject(className);
                int offset = classObject == default ? default : GetFieldOffset(classObject, fieldName);
                if(offset != default) {
                    fieldOffsetCache[cacheKey] = offset;
                }
                return offset;
            }

            public override int GetFieldOffset(IntPtr uobject, string fieldName) {
                IntPtr classObject = IsClassObject(uobject) ? uobject : UObjectClass(uobject);
                string cacheKey = classObject.ToInt64().ToString("X") + "." + fieldName;
                if(fieldOffsetCache.TryGetValue(cacheKey, out int cachedOffset)) {
                    return cachedOffset;
                }

                foreach(IntPtr ownerClassObject in ClassHierarchy(classObject)) {
                    foreach(IntPtr field in FieldSequence(ownerClassObject)) {
                        if(fieldName.Equals(FieldName(field))) {
                            int offset = game.Read<int>(field + data.GetOffset("Property", "Offset_Internal"));
                            if(offset != default) {
                                fieldOffsetCache[cacheKey] = offset;
                            }
                            return offset;
                        }
                    }
                }

                return default;
            }

            public override IReadOnlyDictionary<string, int> GetFieldOffsets(string className) {
                var result = new Dictionary<string, int>(StringComparer.Ordinal);
                IntPtr classObject = FindClassObject(className);
                if(classObject == default) {
                    return result;
                }

                foreach(IntPtr ownerClassObject in ClassHierarchy(classObject)) {
                    foreach(IntPtr field in FieldSequence(ownerClassObject)) {
                        string fieldName = FieldName(field);
                        int offset = game.Read<int>(field + data.GetOffset("Property", "Offset_Internal"));
                        if(!string.IsNullOrEmpty(fieldName) && offset != default && !result.ContainsKey(fieldName)) {
                            result.Add(fieldName, offset);
                        }
                    }
                }

                return result;
            }

            public override string GetFieldType(string className, string fieldName) {
                IntPtr field = FindField(className, fieldName);
                return field == default ? "" : PropertyType(field);
            }

            public override int GetFieldElementSize(string className, string fieldName) {
                IntPtr field = FindField(className, fieldName);
                return field == default ? 0 : PropertyElementSize(field);
            }

            public override string GetFieldDebugInfo(string className, string fieldName) {
                IntPtr field = FindField(className, fieldName);
                if(field == default) {
                    return "field not found";
                }

                string fieldClass = FieldClassName(field);
                int elementSize = PropertyElementSize(field);
                int arrayDim = game.Read<int>(field + data.GetOffset("Property", "ArrayDim"));
                string result = $"field={field.ToString("X")} class={fieldClass} type={PropertyType(field)} elementSize=0x{elementSize:X} arrayDim={arrayDim}";
                if(fieldClass == "ArrayProperty") {
                    IntPtr inner = ArrayInner(field);
                    result += inner == default
                        ? " inner=0"
                        : $" inner={inner.ToString("X")} innerClass={FieldClassName(inner)} innerName={FieldName(inner)} innerType={PropertyType(inner)} innerSize=0x{PropertyElementSize(inner):X}";
                }
                return result;
            }

            private IntPtr FindField(string className, string fieldName) {
                IntPtr classObject = FindClassObject(className);
                if(classObject == default) {
                    return default;
                }

                foreach(IntPtr ownerClassObject in ClassHierarchy(classObject)) {
                    foreach(IntPtr field in FieldSequence(ownerClassObject)) {
                        if(fieldName.Equals(FieldName(field), StringComparison.Ordinal)) {
                            return field;
                        }
                    }
                }

                return default;
            }

            public override string GetObjectPropertyClassName(string className, string fieldName) {
                IntPtr classObject = FindClassObject(className);
                if(classObject == default) {
                    return "";
                }

                foreach(IntPtr ownerClassObject in ClassHierarchy(classObject)) {
                    foreach(IntPtr field in FieldSequence(ownerClassObject)) {
                        if(!fieldName.Equals(FieldName(field), StringComparison.Ordinal)) {
                            continue;
                        }

                        IntPtr propertyClass = game.Read<IntPtr>(field + data.GetOffset("ObjectPropertyBase", "PropertyClass"));
                        return propertyClass == default ? "" : UObjectName(propertyClass);
                    }
                }

                return "";
            }

            protected abstract string FNameEntryName(int index);

            protected virtual IntPtr UObjectClass(IntPtr uobject) {
                return game.Read<IntPtr>(uobject + data.GetOffset("UObjectBase", "Class"));
            }
            protected virtual string UObjectClassName(IntPtr uobject) {
                return UObjectName(UObjectClass(uobject));
            }
            protected virtual string UObjectName(IntPtr uobject) {
                return GetCachedFNameEntryName(UObjectFName(uobject));
            }
            protected virtual int UObjectFName(IntPtr uobject) {
                //Assume FName Index is always at offset 0
                return game.Read<int>(uobject + data.GetOffset("UObjectBase", "Name"));
            }

            protected string GetCachedFNameEntryName(int index) {
                if(index == 0) {
                    return "";
                }

                if(fnameNameCache.TryGetValue(index, out string cachedName)) {
                    return cachedName;
                }

                string name = FNameEntryName(index);
                fnameNameCache[index] = name;
                return name;
            }

            protected void CacheFNameEntryName(int index, string name) {
                if(index != 0 && name != null) {
                    fnameNameCache[index] = name;
                }
            }

            protected virtual IntPtr FieldClass(IntPtr property) => UObjectClass(property);
            protected virtual string FieldClassName(IntPtr uobject) => FieldName(FieldClass(uobject));
            protected virtual string FieldName(IntPtr property) => UObjectName(property);
            protected virtual int FieldFName(IntPtr property) => UObjectFName(property);

            protected virtual IEnumerable<IntPtr> FieldSequence(IntPtr uobject) {
                int offsetPropertyNext = data.GetOffset("Property", "PropertyLinkNext");
                //IntPtr field = processWrapper.Read<IntPtr>(uobject + data.GetOffset("UStruct", "Children"));
                IntPtr field = game.Read<IntPtr>(uobject + data.GetOffset("UStruct", "PropertyLink"));
                while(field != default) {
                    yield return field;
                    field = game.Read<IntPtr>(field + offsetPropertyNext);
                }
            }

            protected virtual string PropertyType(IntPtr property) {
                string type = FieldClassName(property);
                switch(type) {
                    case "ByteProperty":
                        IntPtr enumPtr = game.Read<IntPtr>(property + data.GetOffset("ByteProperty", "Enum"));
                        return enumPtr == default ? "byte" : "enum " + UObjectName(enumPtr);

                    case "UInt16Property": return "ushort";
                    case "UInt32Property": return "uint";
                    case "UInt64Property": return "ulong";

                    case "Int8Property": return "sbyte";
                    case "Int16Property": return "short";
                    case "IntProperty": return "int";
                    case "Int64Property": return "long";

                    case "FloatProperty": return "float";
                    case "DoubleProperty": return "double";

                    case "BoolProperty": return "bool";

                    case "StrProperty": return "char[]*";
                    case "NameProperty": return "FName";
                    //TODO
                    //case "TextProperty": return "";

                    case "ObjectProperty":
                        return UObjectName(game.Read<IntPtr>(property + data.GetOffset("ObjectPropertyBase", "PropertyClass"))) + "*";
                    case "LazyObjectProperty":
                        return "lazy " + UObjectName(game.Read<IntPtr>(property + data.GetOffset("ObjectPropertyBase", "PropertyClass"))) + "*";
                    case "SoftObjectProperty":
                        return "soft " + UObjectName(game.Read<IntPtr>(property + data.GetOffset("ObjectPropertyBase", "PropertyClass"))) + "*";
                    case "WeakObjectProperty":
                        return "weak " + UObjectName(game.Read<IntPtr>(property + data.GetOffset("ObjectPropertyBase", "PropertyClass"))) + "*";

                    case "ClassProperty":
                        return UObjectName(game.Read<IntPtr>(property + data.GetOffset("ClassProperty", "MetaClass"))) + "*";

                    case "StructProperty":
                        return UObjectName(game.Read<IntPtr>(property + data.GetOffset("StructProperty", "Struct")));

                    case "ArrayProperty":
                        return "TArray<" + PropertyType(ArrayInner(property)) + ">";
                    case "SetProperty":
                        return "TSet<" + PropertyType(game.Read<IntPtr>(property + data.GetOffset("SetProperty", "ElementProp"))) + ">";
                    case "MapProperty":
                        return "TMap<" + PropertyType(game.Read<IntPtr>(property + data.GetOffset("MapProperty", "KeyProp")))
                                + ", " + PropertyType(game.Read<IntPtr>(property + data.GetOffset("MapProperty", "ValueProp"))) + ">";

                    case "InterfaceProperty":
                        return UObjectName(game.Read<IntPtr>(property + data.GetOffset("InterfaceProperty", "InterfaceClass")));

                    case "EnumProperty":
                        return "enum " + UObjectName(game.Read<IntPtr>(property + data.GetOffset("EnumProperty", "Enum")));

                    case "DelegateProperty":
                    case "MulticastDelegateProperty":
                    case "MulticastInlineDelegateProperty":
                    case "MulticastSparseDelegateProperty":
                        return "delegate";

                    default: return type;
                }
            }

            protected virtual IntPtr ArrayInner(IntPtr property) {
                IntPtr inner = game.Read<IntPtr>(property + data.GetOffset("ArrayProperty", "Inner"));
                return inner != default ? inner : game.Read<IntPtr>(property + data.GetOffset("ArrayProperty", "Inner") + 8);
            }

            protected virtual int PropertyElementSize(IntPtr property) {
                int size = game.Read<int>(property + data.GetOffset("Property", "ElementSize"));
                return size > 1 ? size : game.Read<int>(property + data.GetOffset("Property", "ElementSize") + 4);
            }
        }

        private class Unreal4_0Helper : Unreal4HelperBase {

            public Unreal4_0Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : this(game, token, "4", fileVersion, logger) { }

            protected Unreal4_0Helper(ProcessWrapper game, CancellationToken token, string fileName, string fileVersion, Logger logger = null)
                : base(game, token, fileName, fileVersion, logger) { }

            protected override ScanTarget FNamesTarget {
                get => game.Is64Bit ? new ScanTarget(0x7, "48 83 EC 28 48 8B 05 ???????? 48 85 C0 75 ?? B9 08??0000 48 89 5C 24 20 E8")
                                    : new ScanTarget(0x1, "A1 ???????? 85 C0 75 ?? 56 68 08??0000 E8");
            }

            protected override OnScanFoundCallback OnFNamesFound {
                get => (ptr, version) => {
                    namesPtr = game.FromAssemblyAddress(ptr);
                    Log("Names " + namesPtr.ToString("X"));
                };
            }

            protected override ScanTarget UObjectsTarget {
                get => game.Is64Bit ? new ScanTarget(0, "40 53 48 83 EC ?? 48 8D 05 ???????? 48 8B D9 48 89 01 80 3D ???????? 00 74 ?? 48 83 79 10 00 74 ?? 80 3D ???????? 00 75")
                                    : new ScanTarget(0, "56 8B F1 C7 06 ???????? 80 3D ???????? 00 74 ?? 83 7E 0C 00 74 ?? 80 3D ???????? 00 75");
            }

            protected override OnScanFoundCallback OnUObjectsFound {
                get => (ptr, version) => {

                    var tmpScanner = new SignatureScanner(game, ptr, 0x100);
                    var tmpTarget = new ScanTarget().AddSignature(0x1, "B9 ???????? 56 E8 ???????? 5E C3")
                                                    .AddSignature(0x2, "56 B9 ???????? E8 ???????? 5E C3");
                    var tmp = tmpScanner.Scan(tmpTarget);
                    objectsPtr = game.FromAbsoluteAddress(tmp);

                    Log("Objects " + objectsPtr.ToString("X"));
                };
            }

            protected virtual IntPtr FNamesChunk {
                get => game.Read<IntPtr>(namesPtr + data.GetOffset("TStaticIndirectArrayThreadSafeRead<>", "Chunks"));
            }

            protected virtual IntPtr UObjectsObjObjects {
                get => objectsPtr + data.GetOffset("FUObjectArray", "ObjObjects");
            }
            protected virtual IntPtr UObjectsData {
                get => game.Read<IntPtr>(UObjectsObjObjects + data.GetOffset("TArray<>", "AllocatorInstance"));
            }
            protected virtual int UObjectsSize {
                get => game.Read<int>(UObjectsObjObjects + data.GetOffset("TArray<>", "ArrayNum"));
            }

            protected override IEnumerable<FName> FNameSequence() {
                IntPtr chunk;
                int chunkNb = -1;
                IntPtr namesChunks = FNamesChunk;
                while((chunk = game.Read<IntPtr>(namesChunks + (++chunkNb) * game.PointerSize)) != default) {
                    for(int i = 0; i < 16384; i++) {
                        IntPtr fname = game.Read<IntPtr>(chunk + i * game.PointerSize);
                        if(fname == default) {
                            continue;
                        }

                        int index = game.Read<int>(fname + data.GetOffset("FNameEntry", "Index")) >> 1;
                        string name = FNameEntryName(fname);
                        CacheFNameEntryName(index, name);
                        //Console.WriteLine(chunkNb + " " + (chunk + i * memory.PointerSize).ToString("X8") + " " + fname.ToString("X8") + " " + index + " " + name);
                        yield return new FName(index, name);
                    }
                }
            }

            protected override string FNameEntryName(int index) {
                if(index == 0) {
                    return "";
                }
                const int maxChunkSize = 16384;
                IntPtr chunk = game.Read<IntPtr>(FNamesChunk + index / maxChunkSize * game.PointerSize);
                IntPtr fname = game.Read<IntPtr>(chunk + index % maxChunkSize * game.PointerSize);
                return FNameEntryName(fname);
            }

            protected virtual string FNameEntryName(IntPtr fNameEntry) {
                bool isWide = (game.Read<int>(fNameEntry + data.GetOffset("FNameEntry", "Index")) & 1) == 1;
                return game.ReadString(fNameEntry + data.GetOffset("FNameEntry", "Name"), isWide ? EStringType.UTF16 : EStringType.UTF8);
            }

            protected override IEnumerable<IntPtr> UObjectSequence() {
                int size = UObjectsSize;
                IntPtr objects = UObjectsData;
                for(int i = 0; i < size; i++) {
                    IntPtr uobject = game.Read<IntPtr>(objects + i * game.PointerSize);
                    if(uobject == default) {
                        continue;
                    }
                    yield return uobject;
                }
            }
        }

        private class Unreal4_8Helper : Unreal4_0Helper {

            public Unreal4_8Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : this(game, token, "4", fileVersion, logger) { }

            protected Unreal4_8Helper(ProcessWrapper game, CancellationToken token, string fileName, string fileVersion, Logger logger = null)
                : base(game, token, fileName, fileVersion, logger) { }

            protected override ScanTarget UObjectsTarget {
                get => game.Is64Bit ? new ScanTarget(0x4A, "40 53 48 83 EC ?? 48 8D 05 ???????? 48 8B D9 48 89 01 80 3D ???????? 00 74 ?? 48 83 79 10 00 74 ?? 80 3D ???????? 00 75")
                                    : new ScanTarget(0x40, "56 8B F1 C7 06 ???????? 80 3D ???????? 00 74 ?? 83 7E 0C 00 74 ?? 80 3D ???????? 00 75");
            }

            protected override OnScanFoundCallback OnUObjectsFound {
                get => (ptr, version) => {
                    IntPtr getArrayFunc = game.FromRelativeAddress(ptr);
                    var tmpScanner = new SignatureScanner(game, getArrayFunc, 0x100);
                    var tmp = tmpScanner.Scan(game.Is64Bit ? new ScanTarget(0x3, "48 8D 05 ???????? 48 83 C4 ?? 5B C3")
                                                           : new ScanTarget(0x4, "83 C4 ?? B8 ???????? C3"));
                    objectsPtr = game.FromAssemblyAddress(tmp);
                    Log("Objects " + objectsPtr.ToString("X"));
                };
            }

            protected override IntPtr UObjectsData {
                get => UObjectsObjObjects + data.GetOffset("TStaticIndirectArrayThreadSafeRead<>", "Chunks");
            }
            protected override int UObjectsSize {
                get => throw new NotImplementedException();
            }

            protected override IEnumerable<IntPtr> UObjectSequence() {
                int chunkNb = -1;
                IntPtr chunk;
                IntPtr objChunks = UObjectsData;
                while((chunk = game.Read<IntPtr>(objChunks + (++chunkNb) * game.PointerSize)) != default) {
                    for(int i = 0; i < 16384; i++) {
                        IntPtr uobject = game.Read<IntPtr>(chunk + i * game.PointerSize);
                        if(uobject == default) {
                            continue;
                        }
                        yield return uobject;
                    }
                }
            }
        }

        private class Unreal4_11Helper : Unreal4_8Helper {

            public Unreal4_11Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : this(game, token, "4", fileVersion, logger) { }

            protected Unreal4_11Helper(ProcessWrapper game, CancellationToken token, string fileName, string fileVersion, Logger logger = null)
                : base(game, token, fileName, fileVersion, logger) { }

            protected override ScanTarget UObjectsTarget {
                get => game.Is64Bit ? new ScanTarget(0x4F, "40 53 48 83 EC ?? 48 8D 05 ???????? 48 8B D9 48 89 01 80 3D ???????? 00 74 ?? 48 83 79 10 00 74 ?? 80 3D ???????? 00 75")
                                    : new ScanTarget(0x3F, "56 8B F1 C7 06 ???????? 80 3D ???????? 00 74 ?? 83 7E 0C 00 74 ?? 80 3D ???????? 00 75");
            }

            protected override OnScanFoundCallback OnUObjectsFound {
                get => (ptr, version) => {
                    objectsPtr = game.FromAssemblyAddress(ptr);
                    Log("Objects " + objectsPtr.ToString("X"));
                };
            }

            protected override IntPtr UObjectsData {
                get => game.Read<IntPtr>(UObjectsObjObjects + data.GetOffset("FFixedUObjectArray", "Objects"));
            }
            protected override int UObjectsSize {
                get => game.Read<int>(UObjectsObjObjects + data.GetOffset("FFixedUObjectArray", "NumElements"));
            }

            protected override IEnumerable<IntPtr> UObjectSequence() {
                int fuobjectSize = data.GetSelfAlignedSize("FUObjectItem");
                int size = UObjectsSize;
                IntPtr objects = UObjectsData;
                for(int i = 0; i < size; i++) {
                    IntPtr uobject = game.Read<IntPtr>(objects + i * fuobjectSize);
                    if(uobject == default) {
                        continue;
                    }
                    yield return uobject;
                }
            }
        }

        private class Unreal4_20Helper : Unreal4_11Helper {

            public Unreal4_20Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : this(game, token, "4", fileVersion, logger) { }

            protected Unreal4_20Helper(ProcessWrapper game, CancellationToken token, string fileName, string fileVersion, Logger logger = null)
                : base(game, token, fileName, fileVersion, logger) { }

            protected override IntPtr UObjectsData {
                get => game.Read<IntPtr>(UObjectsObjObjects + data.GetOffset("FChunkedFixedUObjectArray", "Objects"));
            }
            protected override int UObjectsSize {
                get => game.Read<int>(UObjectsObjObjects + data.GetOffset("FChunkedFixedUObjectArray", "NumElements"));
            }

            protected override IEnumerable<IntPtr> UObjectSequence() {
                int fuobjectSize = data.GetSelfAlignedSize("FUObjectItem");
                int chunkNb = -1;
                IntPtr chunk;
                IntPtr objChunks = UObjectsData;
                while((chunk = game.Read<IntPtr>(objChunks + (++chunkNb) * game.PointerSize)) != default) {
                    for(int i = 0; i < 65536; i++) {
                        IntPtr uobject = game.Read<IntPtr>(chunk + i * fuobjectSize);
                        if(uobject == default) {
                            continue;
                        }
                        yield return uobject;
                    }
                }
            }
        }

        private class Unreal4_22Helper : Unreal4_20Helper {

            public Unreal4_22Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : this(game, token, "4", fileVersion, logger) { }

            protected Unreal4_22Helper(ProcessWrapper game, CancellationToken token, string fileName, string fileVersion, Logger logger = null)
                : base(game, token, fileName, fileVersion, logger) { }

            protected override ScanTarget UObjectsTarget {
                get => game.Is64Bit ? new ScanTarget(0x9, "48 8B D1 45 33 C0 48 8D 0D ?? ?? ?? ?? E9 ?? ?? ?? ??")
                                    : new ScanTarget(0x7, "6A 00 FF 74 24 08 B9 ?? ?? ?? ?? E8 ?? ?? ?? ?? C3");
            }
        }

        private class Unreal4_23Helper : Unreal4_22Helper {

            public Unreal4_23Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : this(game, token, "4", fileVersion, logger) { }

            protected Unreal4_23Helper(ProcessWrapper game, CancellationToken token, string fileName, string fileVersion, Logger logger = null)
                : base(game, token, fileName, fileVersion, logger) { }

            private int fNameEntryHeaderOffset = -1;
            private int fNameEntryStride = 2;

            protected override ScanTarget FNamesTarget {
                get => game.Is64Bit ? new ScanTarget(0x5, "74 09 48 8D 15 ???????? EB 16 48 8D 0D ???????? E8 ???????? 48 8B D0 C6 05 ???????? 01")
                                    : new ScanTarget(0x3, "74 07 B8 ???????? EB 11 B9 ???????? E8 ???????? C6 05 ???????? 01");
            }

            protected override IntPtr FNamesChunk {
                get => namesPtr + data.GetOffset("FNamePool", "entries") + data.GetOffset("FNameEntryAllocator", "Blocks");
            }

            protected override IEnumerable<FName> FNameSequence() {
                IntPtr chunk;
                int chunkNb = -1;
                IntPtr namesChunks = FNamesChunk;
                EnsureFNameEntryLayout();
                while((chunk = game.Read<IntPtr>(namesChunks + (++chunkNb) * game.PointerSize)) != default) {
                    IntPtr cursor = chunk;
                    int block = chunkNb << 16;
                    int offset;
                    while((offset = (int)(((long)cursor - (long)chunk) / fNameEntryStride)) < 0xFFFF) {
                        int length = FNameEntryHeaderLength(cursor, out _);
                        if(length == default) {
                            break;
                        }
                        int index = block | offset;
                        string name = FNameEntryName(cursor);
                        CacheFNameEntryName(index, name);
                        //Console.WriteLine(chunkNb + " " + cursor.ToString("X8") + " " + index + " " + name);
                        yield return new FName(index, name);
                        cursor += fNameEntryHeaderOffset + 2 + length;
                        cursor = AlignNameEntry(cursor);
                    }
                }
            }

            protected override string FNameEntryName(int index) {
                if(index == 0) {
                    return "";
                }
                EnsureFNameEntryLayout();
                IntPtr cursor = game.Read<IntPtr>(FNamesChunk + game.PointerSize * (index >> 16)) + (index & 0xFFFF) * fNameEntryStride;
                return FNameEntryName(cursor);
            }

            protected override string FNameEntryName(IntPtr fNameEntry) {
                int length = FNameEntryHeaderLength(fNameEntry, out bool isWide);
                return game.ReadString(fNameEntry + fNameEntryHeaderOffset + data.GetOffset("FNameEntry", "Name"), length, isWide ? EStringType.UTF16 : EStringType.UTF8);
            }

            protected virtual int FNameEntryHeaderLength(IntPtr fNameEntry, out bool isWide) {
                EnsureFNameEntryLayout();
                ushort header = game.Read<ushort>(fNameEntry + fNameEntryHeaderOffset);
                int length = header >> 6;
                isWide = (header & 1) == 1;
                if(isWide) {
                    length *= 2;
                }
                return length;
            }

            private void EnsureFNameEntryLayout() {
                if(fNameEntryHeaderOffset >= 0) {
                    return;
                }

                fNameEntryHeaderOffset = 0;
                fNameEntryStride = 2;

                IntPtr firstChunk = game.Read<IntPtr>(FNamesChunk);
                if(firstChunk == default) {
                    return;
                }

                byte[] bytes = game.Read(firstChunk, 16);
                if(bytes == null || bytes.Length < 6) {
                    return;
                }

                for(int i = 0; i <= bytes.Length - 4; i++) {
                    if(bytes[i] == (byte)'N' && bytes[i + 1] == (byte)'o' && bytes[i + 2] == (byte)'n' && bytes[i + 3] == (byte)'e') {
                        fNameEntryHeaderOffset = Math.Max(0, i - 2);
                        fNameEntryStride = i <= 2 ? 2 : 4;
                        return;
                    }
                }
            }

            private IntPtr AlignNameEntry(IntPtr cursor) {
                int remainder = (int)((long)cursor % fNameEntryStride);
                return remainder == 0 ? cursor : cursor + (fNameEntryStride - remainder);
            }
        }

        private class Unreal4_25Helper : Unreal4_23Helper {
            public Unreal4_25Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : this(game, token, "4", fileVersion, logger) { }

            protected Unreal4_25Helper(ProcessWrapper game, CancellationToken token, string fileName, string fileVersion, Logger logger = null)
                : base(game, token, fileName, fileVersion, logger) { }

            protected override IntPtr FieldClass(IntPtr property) {
                return game.Read<IntPtr>(property + data.GetOffset("FField", "Class"));
            }
            protected override string FieldClassName(IntPtr property) {
                return GetCachedFNameEntryName(game.Read<int>(FieldClass(property) + data.GetOffset("FFieldClass", "Name")));
            }
            protected override string FieldName(IntPtr property) {
                return GetCachedFNameEntryName(FieldFName(property));
            }
            protected override int FieldFName(IntPtr property) {
                //Assume FName Index is always at offset 0
                return game.Read<int>(property + data.GetOffset("FField", "Name"));
            }

            protected override IEnumerable<IntPtr> FieldSequence(IntPtr uobject) {
                int offsetFieldNext = data.GetOffset("FField", "Next");
                IntPtr field = game.Read<IntPtr>(uobject + data.GetOffset("UStruct", "ChildProperties"));
                while(field != default) {
                    yield return field;
                    field = game.Read<IntPtr>(field + offsetFieldNext);
                }
            }
        }

        private class Unreal5_0Helper : Unreal4_25Helper {
            private UObjectArrayLayout uobjectArrayLayout;

            public Unreal5_0Helper(ProcessWrapper game, CancellationToken token, string fileVersion, Logger logger = null)
                : base(game, token, "5", fileVersion, logger) { }

            protected override ScanTarget FNamesTarget {
                get {
                    ScanTarget target = new ScanTarget()
                        .AddSignature(0x3, "48 8D 0D ???????? E8 ???????? C6 05 ???????? 01")
                        .AddSignature(0x3, "48 8D 0D ???????? E8 ???????? 48 8B D0 C6 05 ???????? 01")
                        .AddSignature(0x3, "48 8D 0D ???????? E8 ???????? 4C 8B C0 C6 05")
                        .AddSignature(0x3, "48 8D 15 ???????? EB ?? 48 8D 0D ???????? 48 8B")
                        .AddSignature(0x3, "48 8D 05 ???????? EB ?? 48 8D 0D ???????? E8 ???????? C6 05")
                        .AddSignature(0x5, "74 09 4C 8D 05 ???????? EB ?? 48 8D 0D ???????? E8")
                        .AddSignature(0x7, "8B D9 74 ?? 48 8D 15 ???????? EB")
                        .AddSignature(0x3, "48 8D 35 ???????? EB");
                    target.IsGoodMatch = IsGoodFNamesMatch;
                    return target;
                }
            }

            protected override ScanTarget UObjectsTarget {
                get {
                    ScanTarget target = new ScanTarget()
                        .AddSignature(0x9, "48 8B D1 45 33 C0 48 8D 0D ???????? E9 ????????")
                        .AddSignature(0x7, "48 83 EC 28 48 8D 0D ???????? E8 ???????? 48 8D 0D ???????? 48 83 C4 28 E9")
                        .AddSignature(0x3, "48 8D 0D ???????? E8 ???????? E8 ???????? C6 05 ???????? 01")
                        .AddSignature(0x3, "4C 8D 15 ???????? 45 33 ?? 4C 89 ?? B9 FF FF FF FF 41 8B")
                        .AddSignature(0x7, "CC 4C 8B C9 4C 8D 15 ???????? 4C 89 11 45 84 C0")
                        .AddSignature(0x3, "48 8B 05 ???????? 48 8B 0C C8 48 85 C9")
                        .AddSignature(0x3, "4C 8B 0D ???????? 4C 89 0D")
                        .AddSignature(0x3, "4C 8B 15 ???????? 4D 85 D2")
                        .AddSignature(0x3, "48 8B 0D ???????? 48 89 02");
                    target.IsGoodMatch = IsGoodUObjectsMatch;
                    return target;
                }
            }

            protected override OnScanFoundCallback OnUObjectsFound {
                get => (ptr, version) => {
                    IntPtr candidate = game.FromAssemblyAddress(ptr);
                    objectsPtr = candidate;
                    if(TryFindUObjectArrayLayout(game, candidate, out UObjectArrayLayout layout)) {
                        uobjectArrayLayout = layout;
                        objectsPtr = layout.Root;
                        Log("Objects " + objectsPtr.ToString("X") + " " + layout.Description);
                    } else {
                        Log("Objects " + objectsPtr.ToString("X") + " (layout unresolved)");
                    }
                };
            }

            protected override IEnumerable<IntPtr> UObjectSequence() {
                UObjectArrayLayout layout = uobjectArrayLayout;
                if(layout == null && TryFindUObjectArrayLayout(game, objectsPtr, out layout)) {
                    uobjectArrayLayout = layout;
                }

                if(layout == null) {
                    foreach(IntPtr uobject in base.UObjectSequence()) {
                        yield return uobject;
                    }
                    yield break;
                }

                int fuobjectSize = data.GetSelfAlignedSize("FUObjectItem");
                int remaining = layout.NumElements > 0 ? layout.NumElements : Int32.MaxValue;
                int maxChunks = layout.NumChunks > 0 ? layout.NumChunks : 256;

                for(int chunkNb = 0; chunkNb < maxChunks && remaining > 0; chunkNb++) {
                    IntPtr chunk = game.Read<IntPtr>(layout.Chunks + chunkNb * game.PointerSize);
                    if(chunk == default) {
                        if(layout.NumChunks <= 0) {
                            yield break;
                        }
                        continue;
                    }

                    int entriesInChunk = Math.Min(65536, remaining);
                    for(int i = 0; i < entriesInChunk; i++) {
                        IntPtr uobject = game.Read<IntPtr>(chunk + i * fuobjectSize);
                        if(uobject != default) {
                            yield return uobject;
                        }
                    }

                    if(layout.NumElements > 0) {
                        remaining -= entriesInChunk;
                    }
                }
            }

            public override IntPtr ResolveWeakObject(IntPtr weakObjectPtr) {
                if(weakObjectPtr == default) return default;
                int objectIndex = game.Read<int>(weakObjectPtr);
                int objectSerial = game.Read<int>(weakObjectPtr + 4);
                if(objectIndex < 0) return default;
                UObjectArrayLayout layout = uobjectArrayLayout;
                if(layout == null && TryFindUObjectArrayLayout(game, objectsPtr, out layout)) uobjectArrayLayout = layout;
                if(layout == null || objectIndex >= layout.NumElements) return default;
                IntPtr chunk = game.Read<IntPtr>(layout.Chunks + (objectIndex >> 16) * game.PointerSize);
                if(chunk == default) return default;
                int fuobjectSize = data.GetSelfAlignedSize("FUObjectItem");
                IntPtr item = chunk + (objectIndex & 0xFFFF) * fuobjectSize;
                int serial = game.Read<int>(item + data.GetOffset("FUObjectItem", "SerialNumber"));
                return objectSerial != 0 && serial != objectSerial ? default : game.Read<IntPtr>(item);
            }

            protected override void LogUObjectArrayDiagnostics() {
                Log("UObject array diagnostics begin");
                LogUObjectArrayProbe("scan-root", objectsPtr);

                IntPtr deref = game.Read<IntPtr>(objectsPtr);
                if(LooksLikeDataPtr(deref)) {
                    LogUObjectArrayProbe("scan-root-deref", deref);
                }

                if(uobjectArrayLayout != null) {
                    Log("UObject array selected: " + uobjectArrayLayout.Description);
                } else {
                    Log("UObject array selected: <none>");
                }
                Log("UObject array diagnostics end");
            }

            private bool IsGoodFNamesMatch(ProcessWrapper wrapper, IntPtr ptr, string version) {
                IntPtr fNamePool = wrapper.FromAssemblyAddress(ptr);
                int[] chunkOffsets = { 0x10, 0x0, 0x8, 0x20, 0x40 };

                foreach(int offset in chunkOffsets) {
                    IntPtr chunk = wrapper.Read<IntPtr>(fNamePool + offset);
                    if(!LooksLikeDataPtr(chunk)) {
                        continue;
                    }

                    byte[] bytes = wrapper.Read(chunk, 16);
                    if(bytes == null) {
                        continue;
                    }

                    for(int i = 0; i <= bytes.Length - 4; i++) {
                        if(bytes[i] == (byte)'N' && bytes[i + 1] == (byte)'o' && bytes[i + 2] == (byte)'n' && bytes[i + 3] == (byte)'e') {
                            return true;
                        }
                    }
                }

                return false;
            }

            private bool IsGoodUObjectsMatch(ProcessWrapper wrapper, IntPtr ptr, string version) {
                IntPtr candidate = wrapper.FromAssemblyAddress(ptr);
                if(TryFindUObjectArrayLayout(wrapper, candidate, out UObjectArrayLayout layout)) {
                    uobjectArrayLayout = layout;
                    return true;
                }

                IntPtr deref = wrapper.Read<IntPtr>(candidate);
                return LooksLikeDataPtr(deref) && TryFindUObjectArrayLayout(wrapper, deref, out layout);
            }

            private bool TryFindUObjectArrayLayout(ProcessWrapper wrapper, IntPtr root, out UObjectArrayLayout layout) {
                layout = null;
                if(!LooksLikeDataPtr(root)) {
                    return false;
                }

                int[] objObjectsOffsets = { data.GetOffset("FUObjectArray", "ObjObjects"), 0x0, 0x20, 0x30, 0x40, 0x50 };
                foreach(int objObjectsOffset in objObjectsOffsets) {
                    IntPtr objObjects = root + objObjectsOffset;
                    if(TryReadChunkedLayout(wrapper, root, objObjects, "root+" + objObjectsOffset.ToString("X"), out layout)) {
                        return true;
                    }
                }

                IntPtr possibleObjObjects = wrapper.Read<IntPtr>(root);
                if(LooksLikeDataPtr(possibleObjObjects)
                && TryReadChunkedLayout(wrapper, possibleObjObjects, possibleObjObjects, "deref-root", out layout)) {
                    return true;
                }

                return false;
            }

            private bool TryReadChunkedLayout(ProcessWrapper wrapper, IntPtr root, IntPtr objObjects, string description, out UObjectArrayLayout layout) {
                layout = null;
                IntPtr chunks = wrapper.Read<IntPtr>(objObjects + data.GetOffset("FChunkedFixedUObjectArray", "Objects"));
                int maxElements = wrapper.Read<int>(objObjects + data.GetOffset("FChunkedFixedUObjectArray", "MaxElements"));
                int numElements = wrapper.Read<int>(objObjects + data.GetOffset("FChunkedFixedUObjectArray", "NumElements"));
                int maxChunks = wrapper.Read<int>(objObjects + data.GetOffset("FChunkedFixedUObjectArray", "MaxChunks"));
                int numChunks = wrapper.Read<int>(objObjects + data.GetOffset("FChunkedFixedUObjectArray", "NumChunks"));

                if(numElements <= 0 || maxElements < numElements || maxElements > 0x4000000) {
                    return false;
                }

                if(numChunks <= 0 || maxChunks < numChunks || maxChunks > 0x10000) {
                    return false;
                }

                if(!LooksLikeDataPtr(chunks)) {
                    return false;
                }

                IntPtr firstChunk = wrapper.Read<IntPtr>(chunks);
                if(!LooksLikeDataPtr(firstChunk) || !HasPlausibleUObjectItem(wrapper, firstChunk)) {
                    return false;
                }

                layout = new UObjectArrayLayout {
                    Root = root,
                    ObjObjects = objObjects,
                    Chunks = chunks,
                    MaxElements = maxElements,
                    NumElements = numElements,
                    MaxChunks = maxChunks,
                    NumChunks = numChunks,
                    FirstChunk = firstChunk,
                    Description = description
                        + " objObjects=" + objObjects.ToString("X")
                        + " chunks=" + chunks.ToString("X")
                        + " firstChunk=" + firstChunk.ToString("X")
                        + " num=" + numElements
                        + "/" + maxElements
                        + " chunksNum=" + numChunks
                        + "/" + maxChunks
                };
                return true;
            }

            private bool HasPlausibleUObjectItem(ProcessWrapper wrapper, IntPtr chunk) {
                int fuobjectSize = data.GetSelfAlignedSize("FUObjectItem");
                for(int i = 0; i < 128; i++) {
                    IntPtr uobject = wrapper.Read<IntPtr>(chunk + i * fuobjectSize);
                    if(!LooksLikeDataPtr(uobject)) {
                        continue;
                    }

                    IntPtr klass = wrapper.Read<IntPtr>(uobject + data.GetOffset("UObjectBase", "Class"));
                    int name = wrapper.Read<int>(uobject + data.GetOffset("UObjectBase", "Name"));
                    if(LooksLikeDataPtr(klass) && name >= 0 && name < 0x4000000) {
                        return true;
                    }
                }

                return false;
            }

            private void LogUObjectArrayProbe(string label, IntPtr root) {
                if(!LooksLikeDataPtr(root)) {
                    Log("UObject probe " + label + ": root=" + root.ToString("X") + " invalid");
                    return;
                }

                int[] objObjectsOffsets = { data.GetOffset("FUObjectArray", "ObjObjects"), 0x0, 0x20, 0x30, 0x40, 0x50 };
                foreach(int objObjectsOffset in objObjectsOffsets) {
                    IntPtr objObjects = root + objObjectsOffset;
                    IntPtr chunks = game.Read<IntPtr>(objObjects + data.GetOffset("FChunkedFixedUObjectArray", "Objects"));
                    int maxElements = game.Read<int>(objObjects + data.GetOffset("FChunkedFixedUObjectArray", "MaxElements"));
                    int numElements = game.Read<int>(objObjects + data.GetOffset("FChunkedFixedUObjectArray", "NumElements"));
                    int maxChunks = game.Read<int>(objObjects + data.GetOffset("FChunkedFixedUObjectArray", "MaxChunks"));
                    int numChunks = game.Read<int>(objObjects + data.GetOffset("FChunkedFixedUObjectArray", "NumChunks"));
                    IntPtr firstChunk = LooksLikeDataPtr(chunks) ? game.Read<IntPtr>(chunks) : default;
                    bool plausible = LooksLikeDataPtr(firstChunk) && HasPlausibleUObjectItem(game, firstChunk);

                    Log("UObject probe " + label
                        + " root+" + objObjectsOffset.ToString("X")
                        + " objObjects=" + objObjects.ToString("X")
                        + " chunks=" + chunks.ToString("X")
                        + " firstChunk=" + firstChunk.ToString("X")
                        + " num=" + numElements
                        + "/" + maxElements
                        + " chunksNum=" + numChunks
                        + "/" + maxChunks
                        + " plausible=" + plausible);
                }
            }

            private static bool LooksLikeDataPtr(IntPtr ptr) {
                long value = (long)ptr;
                return value >= 0x10000 && value <= 0x00007FFFFFFFFFFF;
            }

            private class UObjectArrayLayout {
                public IntPtr Root;
                public IntPtr ObjObjects;
                public IntPtr Chunks;
                public IntPtr FirstChunk;
                public int MaxElements;
                public int NumElements;
                public int MaxChunks;
                public int NumChunks;
                public string Description;
            }
        }

#if DEBUG
        public class FNameLookup {
            private readonly HashSet<string> missingNames;
            private readonly Dictionary<int, string> indexLookup;
            private readonly Dictionary<string, int> nameLookup;

            public FNameLookup(params string[] names) {
                missingNames = new HashSet<string>(names);
                indexLookup = new Dictionary<int, string>(names.Length);
                nameLookup = new Dictionary<string, int>(names.Length);
            }

            public void AddEntry(int index, string name) {
                if(!missingNames.Remove(name)) {
                    nameLookup.Add(name, index);
                    indexLookup.Add(index, name);
                }
            }

            public int Count => indexLookup.Count;
            public int MissingCount => nameLookup.Count - indexLookup.Count;

            public bool TryGetValue(int index, out string name) => indexLookup.TryGetValue(index, out name);
            public bool TryGetValue(string name, out int index) => nameLookup.TryGetValue(name, out index);

            public string this[int index] => indexLookup[index];
            public int this[string name] => nameLookup[name];
        }
#endif
    }

    public interface IUnrealHelper {
        Dictionary<string, IntPtr> GetUObjects(params string[] names);
        IntPtr GetUObject(string name);
        IntPtr GetUObject(int fname);
        IntPtr FindLiveUObject(string className);
        IntPtr FindLiveUObject(string className, Predicate<IntPtr> predicate);
        IReadOnlyList<IntPtr> FindLiveUObjects(string className, int maxCount = 256);
        string GetUObjectName(IntPtr uobject);
        string GetUObjectPath(IntPtr uobject);
        string GetUObjectClassName(IntPtr uobject);
        IntPtr ResolveWeakObject(IntPtr weakObjectPtr);

        Dictionary<string, int> GetFNames(params string[] names);
        int GetFName(string name);

        int GetFieldOffset(string className, string fieldName);
        int GetFieldOffset(IntPtr uobject, string fieldName);
        IReadOnlyDictionary<string, int> GetFieldOffsets(string className);
        string GetFieldType(string className, string fieldName);
        int GetFieldElementSize(string className, string fieldName);
        string GetFieldDebugInfo(string className, string fieldName);
        string GetObjectPropertyClassName(string className, string fieldName);
        void LogDiagnostics();
#if UE_DEBUG
        void Debug(string gameName);
#endif
    }
}
