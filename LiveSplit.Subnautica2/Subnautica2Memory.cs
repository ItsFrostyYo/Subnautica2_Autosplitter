using LiveSplit.ComponentUtil;
using LiveSplit.Model;
using LiveSplit.Subnautica2.Enums;
using LiveSplit.Subnautica2.UE5Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Voxif.AutoSplitter;
using Voxif.Helpers.Unity;
using Voxif.Helpers.Unreal;
using Voxif.IO;
using Voxif.Memory;

namespace LiveSplit.Subnautica2
{
    public class Subnautica2Memory : Memory
    {
        protected override string[] ProcessNames => new string[] { "Subnautica2-Win64-Shipping" };

        public Subnautica2Split CurrentSplitToCheck { get; set; }

        public bool startedTimerBefore = false;

        public Subnautica2Ue5EventRegistry Events { get; private set; }

        public bool Ue5EventTriggered(string eventName) =>
            Events != null && Events.Triggered(eventName);

        public ulong Ue5EventDelta(string eventName) =>
            Events == null ? 0UL : Events.Delta(eventName);

        private const int maxInventoryTimeWithoutChangingMs = 1000;
        public bool pointersInitialized;
        public GameVersion gameVersion;
        private static readonly bool EnableEnumDiscoveryLogs = false;
        private static readonly bool EnableBiomeDiscoveryLogs = false;
        private static readonly bool EnableBiomeProbeLogs = false;
        private static readonly TimeSpan MemoryUpdateInterval = TimeSpan.FromMilliseconds(50);
        private static readonly string[] PlayerCharacterClassNames = new[] { "SN2PlayerCharacter", "BP_SN2PlayerCharacter_C" };
        private static readonly string[] WorldHudClassNames = new[] { "SN2WorldHUD", "BP_SN2WorldHUD_C" };
        private static readonly Lazy<Dictionary<string, EncyEntry>> EncyEntryAliases = new Lazy<Dictionary<string, EncyEntry>>(BuildEncyEntryAliases);
        private static readonly TimeSpan EncyclopediaUpdateInterval = TimeSpan.FromMilliseconds(1000);
        private static readonly TimeSpan CurrentBiomeLogInterval = TimeSpan.FromSeconds(2);
        private const int EnumDiscoveryMaxObjects = 1024;
        private static readonly IReadOnlyDictionary<SplitName, string[]> PrefabricatedEventNames =
            new Dictionary<SplitName, string[]>
            {
                { SplitName.AngelCombAdaptation, new[] { "AdaptationRippleAfterInteraction" } },
                { SplitName.BiobedAdaptation, new[] { "BiobedAdaptationInventory", "BiobedAdaptationToolbar" } },
                { SplitName.IntroUnlockDoor, new[] { "IntroUnlockDoor" } },
                { SplitName.IntroAnalyzingButtonPress, new[] { "IntroAnalyzingButtonPress" } },
                { SplitName.IntroReleaseLifepod, new[] { "IntroReleaseLifepod" } },
                { SplitName.IntroLifepodAscend, new[] { "IntroLifepodAscend" } },
                { SplitName.RepairTurbine, new[] { "RepairTurbine" } },
                { SplitName.EnterTadpole, new[] { "EnterTadpole" } },
                { SplitName.SonicResonatorBlastShot, new[] { "SonicResonatorBlastShot" } },
                { SplitName.FinalObservatoryButtonPress, new[] { "FinalObservatoryButtonPress" } },
            };

        public readonly Dictionary<SplitName, Func<bool>> splitConditions;
        public readonly Dictionary<SplitName, Func<bool>> subConditions;

        private readonly Subnautica2Settings settings;

        #region Pointer stuff
        private UnrealNestedPointerFactory pointerFactory;
        private IUnrealHelper unrealHelper;
        private UnrealObjectPointer playerCharacterPointer;
        private Pointer<IntPtr> playerInventoryComponent;
        private Pointer<IntPtr> playerEquipmentComponent;
        private Pointer<IntPtr> playerToolbarComponent;
        private DateTime nextInventoryProbeAttempt = DateTime.MinValue;
        private DateTime nextBlueprintProbeAttempt = DateTime.MinValue;
        private DateTime nextDatabankProbeAttempt = DateTime.MinValue;
        private DateTime nextBiomeProbeAttempt = DateTime.MinValue;
        private DateTime nextEnumDiscoveryLog = DateTime.MinValue;
        private DateTime nextBiomeDiscoveryLog = DateTime.MinValue;
        private DateTime nextEncyclopediaUpdateAttempt = DateTime.MinValue;
        private DateTime nextCurrentBiomeLog = DateTime.MinValue;
        private DateTime nextMemoryUpdate = DateTime.MinValue;
        private IntPtr playerVolumeTracker = IntPtr.Zero;
        private IntPtr worldZoneTracker = IntPtr.Zero;
        private Task<IntPtr> worldZoneTrackerRefreshTask;
        private DateTime nextWorldZoneTrackerAttempt = DateTime.MinValue;
        private bool biomeBaselineInitialized;
        private string currentBiomeKey = string.Empty;
        private string currentBiomeKeyOld = string.Empty;
        private string lastBiomeProbeState = string.Empty;
        private bool mainMenuEnteredThisUpdate;
        private bool creativeStartThisUpdate;
        private bool survivalStartThisUpdate;
        private bool gameplayInitializationPending;
        private string gameplayInitializationReason = string.Empty;
        private bool secondBaseReached;
        private bool secondBaseArmed;
        private bool enterTadpoleAfterSecondBaseArmed;
        private int deepStartCinematicRemovalCount;
        private bool loadRemovalActive;
        private bool playerDiscoveryAllowed = true;

        private readonly Dictionary<InventoryItem, InvChangeInfo> curPickUpCounts = new Dictionary<InventoryItem, InvChangeInfo>();
        private readonly Dictionary<InventoryItem, InvChangeInfo> curDropCounts = new Dictionary<InventoryItem, InvChangeInfo>();
        private readonly HashSet<string> loggedInventoryItemTypeAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> loggedCraftingRecipeAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> loggedDatabankEntryAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> loggedBiomeAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<IntPtr, InventoryItem> inventoryItemTypeCache = new Dictionary<IntPtr, InventoryItem>();
        private readonly Dictionary<IntPtr, Unlockable> blueprintRecipeCache = new Dictionary<IntPtr, Unlockable>();
        private readonly Dictionary<IntPtr, Craftable> craftableRecipeCache = new Dictionary<IntPtr, Craftable>();
        private readonly Dictionary<IntPtr, Buildable> buildableRecipeCache = new Dictionary<IntPtr, Buildable>();
        private readonly HashSet<IntPtr> loggedBuilderGhostIdentity = new HashSet<IntPtr>();
        private readonly Dictionary<IntPtr, Biome> biomeObjectCache = new Dictionary<IntPtr, Biome>();
        private Dictionary<InventoryItem, int> currentInventoryChanges = new Dictionary<InventoryItem, int>();
        private readonly HashSet<Craftable> currentCraftEvents = new HashSet<Craftable>();
        private readonly HashSet<Buildable> currentBuildEvents = new HashSet<Buildable>();
        private HashSet<ActiveCraftKey> activeCrafts = new HashSet<ActiveCraftKey>();
        private readonly HashSet<IntPtr> knownCrafters = new HashSet<IntPtr>();
        private readonly List<IntPtr> processorStations = new List<IntPtr>();
        private readonly Dictionary<IntPtr, ProcessorCraftState> processorStates = new Dictionary<IntPtr, ProcessorCraftState>();
        private Task<CraftingTargets> craftingTargetsRefreshTask;
        private DateTime nextCraftingTargetsRefreshAttempt = DateTime.MinValue;
        private bool refreshCraftingTargetsAfterCurrent;
        private IntPtr currentPlayerCharacterAddress;
        private IntPtr controlledPawnAddress;
        private IntPtr localPlayer;
        private int localPlayerControllerOffset;
        private int controllerPawnOffset;
        private DateTime nextLocalPlayerResolveAttempt = DateTime.MinValue;
        private IntPtr builderTool;
        private IntPtr builderConstructionComponent;
        private IntPtr builderReplicatedGhost;
        private IntPtr builderReplicatedComponent;
        private float builderReplicatedLastProgress;
        private bool builderReplicatedSawIncrease;
        private DateTime builderReplicatedCompletedAt = DateTime.MinValue;
        private IntPtr builderCompletedLocalComponent;
        private IntPtr builderCompletedReplicatedComponent;
        private DateTime nextProcessorCraftingUpdate = DateTime.MinValue;
        private Buildable pendingBuilderBuildable = Buildable.None;
        private IntPtr pendingBuilderSource;
        private IntPtr builderLastDiscoveryGhost;
        private bool craftBaselineInitialized;
        private List<IntPtr> inventoryStorageObjects = new List<IntPtr>();
        private Task<List<IntPtr>> inventoryStorageRefreshTask;
        private Task<PlayerCharacterMatch> playerCharacterRefreshTask;
        private int inventoryStorageRefreshInventoryId = int.MinValue;
        private string playerCharacterRefreshReason = string.Empty;
        private const int InventoryProbeRetryMilliseconds = 250;
        private string playerCharacterClassName = string.Empty;
        private DateTime nextInventoryStorageRefreshAttempt = DateTime.MinValue;
        private bool inventoryBaselineInitialized;
        private int lastPlayerInventoryId = int.MinValue;
        private readonly List<IntPtr> recipeListViewModels = new List<IntPtr>();
        private Task<List<IntPtr>> recipeListViewModelRefreshTask;
        private bool recipeListViewModelsInvalidated;
        private bool recipeListViewModelsChanged;
        private readonly List<IntPtr> databankViewModels = new List<IntPtr>();
        private Task<List<IntPtr>> databankViewModelRefreshTask;
        private bool databankViewModelsInvalidated;
        private bool databankViewModelsChanged;
        private bool encyclopediaBaselineInitialized;
        private bool blueprintBaselineInitialized;
        private bool storyGoalBaselineInitialized;
        private DateTime nextStoryGoalUpdateAttempt = DateTime.MinValue;
        private HashSet<StoryGoal> currentStoryGoals = new HashSet<StoryGoal>();
        private HashSet<StoryGoal> previousStoryGoals = new HashSet<StoryGoal>();
        private List<string> encyclopediaPrimaryEntryKeys = new List<string>();
        private List<string> encyclopediaPrimaryEntryKeysOld = new List<string>();
        private List<string> encyclopediaEntryKeys = new List<string>();
        private List<string> encyclopediaEntryKeysOld = new List<string>();
        private readonly Dictionary<IntPtr, DatabankEntryInfo> databankEntryInfoCache = new Dictionary<IntPtr, DatabankEntryInfo>();
        private readonly object databankEntryInfoCacheLock = new object();
        private readonly Dictionary<string, int> unrealFieldOffsetCache = new Dictionary<string, int>(StringComparer.Ordinal);
        private int activeCraftStride;
        private string lastEncyclopediaReadState = string.Empty;
        private Task<EncyclopediaReadResult> encyclopediaReadTask;
        private bool encyclopediaReadTaskResetsBaseline;
        private int encyclopediaReadGeneration;
        private int encyclopediaReadTaskGeneration;

        public Dictionary<InventoryItem, int> PlayerInventory = new Dictionary<InventoryItem, int>();
        public Dictionary<InventoryItem, int> PlayerInventoryOld = new Dictionary<InventoryItem, int>();
        public Dictionary<InventoryItem, int> PlayerEquipment = new Dictionary<InventoryItem, int>();
        public Dictionary<InventoryItem, int> PlayerEquipmentOld = new Dictionary<InventoryItem, int>();
        public List<Unlockable> KnownBlueprints = new List<Unlockable>();
        public List<Unlockable> KnownBlueprintsOld = new List<Unlockable>();
        public List<EncyEntry> Encyclopedia = new List<EncyEntry>();
        public List<EncyEntry> EncyclopediaOld = new List<EncyEntry>();
        public Biome CurrentBiome = Biome.None;
        public Biome CurrentBiomeOld = Biome.None;

        #endregion

        UnrealHelperTask unrealTask;

        private struct PlayerCharacterMatch
        {
            public IntPtr Pointer;
            public string ClassName;
            public CraftingTargets CraftingTargets;
            public List<IntPtr> InventoryStorages;
            public int InventoryId;
            public List<IntPtr> RecipeViewModels;

            public PlayerCharacterMatch(
                IntPtr pointer,
                string className,
                CraftingTargets craftingTargets = null,
                List<IntPtr> inventoryStorages = null,
                int inventoryId = -1,
                List<IntPtr> recipeViewModels = null)
            {
                Pointer = pointer;
                ClassName = className;
                CraftingTargets = craftingTargets;
                InventoryStorages = inventoryStorages ?? new List<IntPtr>();
                InventoryId = inventoryId;
                RecipeViewModels = recipeViewModels ?? new List<IntPtr>();
            }
        }

        private struct ActiveCraftKey : IEquatable<ActiveCraftKey>
        {
            public readonly IntPtr Crafter;
            public readonly IntPtr Recipe;
            public readonly ulong TimerHandle;

            public ActiveCraftKey(IntPtr crafter, IntPtr recipe, ulong timerHandle)
            {
                Crafter = crafter;
                Recipe = recipe;
                TimerHandle = timerHandle;
            }

            public bool Equals(ActiveCraftKey other) => Crafter == other.Crafter && Recipe == other.Recipe && TimerHandle == other.TimerHandle;
            public override bool Equals(object obj) => obj is ActiveCraftKey other && Equals(other);
            public override int GetHashCode() => Crafter.GetHashCode() ^ Recipe.GetHashCode() ^ TimerHandle.GetHashCode();
        }

        private struct ProcessorCraftState
        {
            public readonly IntPtr Recipe;
            public readonly byte State;
            public readonly float Progress;

            public ProcessorCraftState(IntPtr recipe, byte state, float progress)
            {
                Recipe = recipe;
                State = state;
                Progress = progress;
            }
        }

        private sealed class CraftingTargets
        {
            public readonly List<IntPtr> Crafters;
            public readonly List<IntPtr> ProcessorStations;

            public CraftingTargets(
                List<IntPtr> crafters,
                List<IntPtr> processorStations)
            {
                Crafters = crafters;
                ProcessorStations = processorStations;
            }
        }

        private sealed class DatabankEntryInfo
        {
            public readonly EncyEntry Entry;
            public readonly string PrimaryKey;
            public readonly List<string> Keys;

            public DatabankEntryInfo(EncyEntry entry, string primaryKey, List<string> keys)
            {
                Entry = entry;
                PrimaryKey = primaryKey;
                Keys = keys;
            }
        }

        private sealed class EncyclopediaReadResult
        {
            public readonly List<EncyEntry> Entries;
            public readonly List<string> PrimaryKeys;
            public readonly List<string> Keys;
            public readonly bool ShouldInvalidateDatabankViewModels;
            public readonly string InvalidateReason;

            public EncyclopediaReadResult(List<EncyEntry> entries, List<string> primaryKeys, List<string> keys, bool shouldInvalidateDatabankViewModels, string invalidateReason)
            {
                Entries = entries;
                PrimaryKeys = primaryKeys;
                Keys = keys;
                ShouldInvalidateDatabankViewModels = shouldInvalidateDatabankViewModels;
                InvalidateReason = invalidateReason;
            }
        }

        private struct BiomeReadResult
        {
            public readonly Biome Biome;
            public readonly string Key;
            public readonly string ObjectName;
            public readonly string ObjectPath;
            public readonly IntPtr CurrentVolume;
            public readonly IntPtr OuterVolume;
            public readonly int CurrentVolumeType;
            public readonly List<string> Tags;
            public readonly List<IntPtr> Volumes;
            public readonly string Source;

            public BiomeReadResult(Biome biome, string key, string objectName, string objectPath, IntPtr currentVolume, IntPtr outerVolume, int currentVolumeType, List<string> tags, List<IntPtr> volumes, string source = "VolumeTracker")
            {
                Biome = biome;
                Key = key;
                ObjectName = objectName;
                ObjectPath = objectPath;
                CurrentVolume = currentVolume;
                OuterVolume = outerVolume;
                CurrentVolumeType = currentVolumeType;
                Tags = tags ?? new List<string>();
                Volumes = volumes ?? new List<IntPtr>();
                Source = source ?? string.Empty;
            }
        }

        private struct Vector3D
        {
            public readonly double X;
            public readonly double Y;
            public readonly double Z;

            public Vector3D(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public static Vector3D One => new Vector3D(1, 1, 1);

            public static Vector3D operator +(Vector3D left, Vector3D right)
            {
                return new Vector3D(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
            }

            public static Vector3D operator -(Vector3D left, Vector3D right)
            {
                return new Vector3D(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
            }
        }

        private const int UWEInventoryComponent_InventoryId = 0x100;
        private const int UWEInventoryStorage_StorageContainers = 0x2C8;
        private const int UWEInventoryStorage_ItemsContainer = 0x2D8;
        private const int FUWEInventoryStorageContainer_Stride = 0x38;
        private const int FUWEInventoryStorageContainer_InventoryId = 0x00;
        private const int UWEInventoryContainer_Items = 0x110;
        private const int FUWEInventoryItem_Stride = 0x40;
        private const int FUWEInventoryItem_InventoryId = 0x0C;
        private const int FUWEInventoryItem_ItemType = 0x20;
        private const int FUWEInventoryItem_Count = 0x28;
        private const int UWEEquipmentComponent_EquippedItems = 0x120;
        private const int UWEToolbarComponent_ToolbarItems = 0xE8;
        private const int FUWEToolbarItem_Stride = 0x30;
        private const int FUWEToolbarItem_Actor = 0x08;
        private const int FUWEToolbarItem_IsEquipped = 0x10;
        private const int FUWEToolbarItem_StackSize = 0x14;
        private const int FUWEToolbarItem_ItemType = 0x18;
        private const int AUWEBaseItem_ItemType = 0x358;
        private const int SN2PlayerCharacter_VolumeTracker = 0xA68;
        private const int UVolumeTrackerComponent_VolumeQueryResult = 0x2E0;
        private const int FVolumeQueryResult_Volumes = 0x00;
        private const int FVolumeQueryResult_OuterVolume = 0x18;
        private const int FVolumeQueryResult_CurrentVolume = 0x28;
        private const int FVolumeQueryResult_CurrentVolumeType = 0x38;
        private const int FVolumeQueryResult_CurrentTags = 0x40;
        private const int UWEBiomeTrackerSubsystem_Volumes = 0x48;
        private const int AActor_RootComponent = 0x1C0;
        private const int AActor_Tags = 0x1E8;
        private const int USceneComponent_AttachParent = 0xD0;
        private const int USceneComponent_AttachChildren = 0xE8;
        private const int USceneComponent_RelativeLocation = 0x148;
        private const int USceneComponent_RelativeScale3D = 0x178;
        private const int AUWEWaterBiomeRegionActor_BiomeVolumeComponent = 0x3C8;
        private const int ABP_UWEOceanBiomeRegionActor_VolumeData = 0x480;
        private const int UShapeVolumeComponent_VolumeData = 0x558;
        private const int UBrushVolumeComponent_VolumeData = 0x548;
        private const int UInstancedMeshVolumeComponent_VolumeData = 0x9C8;
        private const int USplineMeshVolumeComponent_VolumeData = 0x760;
        private const int UStaticMeshVolumeComponent_VolumeData = 0x630;
        private const int UUWEVolumeActorComponent_VolumeData = 0xD8;
        private const int FTrackedVolumeData_TagsToAdd = 0x08;
        private const int FTrackedVolumeData_TagsToRemove = 0x28;
        private const int FTrackedVolumeData_VolumeTags = 0x48;
        private const int FTrackedVolumeData_GASLooseTags = 0x68;
        private const int FTrackedVolumeData_EnvironmentType = 0x88;
        private const int AUWEWorldZone_RegionAsset = 0x370;
        private const int AUWEWorldZone_ShapeComponent = 0x378;
        private const int UUWEWorldRegionDataAsset_RegionTag = 0x80;
        private const int UUWEWorldZoneTrackerSubsystem_Zones = 0x40;
        private const int UBoxComponent_BoxExtent = 0x550;
        private const int USphereComponent_SphereRadius = 0x550;
        private const int SN2WorldHUD_DatabankViewModel = 0x408;
        private const int SN2WorldHUD_FabricatorRecipesListViewModel = 0x438;
        private const int SN2WorldHUD_PDARecipesListViewModel = 0x440;
        private const int SN2WorldHUD_BuilderRecipesListViewModel = 0x448;
        private const int SN2RecipesListViewModel_UnlockedRecipes = 0xA8;
        private const int SN2PlayerCharacter_CraftingComponent = 0x828;
        private const int UWECraftingComponent_CurrentCrafterComponent = 0xD0;
        private const int UWECrafterComponent_ActiveCrafts = 0x1A8;
        private const int FUWEActiveCraft_Stride = 0x68;
        private const int FUWEActiveCraft_CraftingTimerHandle = 0x08;
        private const int FUWEActiveCraft_Recipe = 0x28;
        private const int FUWEActiveCraft_InProgress = 0x60;
        private const int ABPProcessorStation_CurrentRecipe = 0x310;
        private const int ABPProcessorStation_ProcessorState = 0x31C;
        private const int ABPProcessorStation_ProcessingProgress = 0x320;
        private const int ASN2BuilderTool_CurrentTarget = 0x518;
        private const int ASN2BuilderTool_Ghost = 0x520;
        private const int ASN2BuilderGhost_ConstructableComponent = 0x2C8;
        private const int ASN2BuilderGhost_GhostParams = 0x2D0;
        private const int FSN2BuilderGhostParams_SourceActors = 0x10;
        private const int FSN2BuilderGhostParams_CustomGhostClass = 0x20;
        private const int USN2ConstructableComponent_ConstructableParams = 0x130;
        private const int FSN2ConstructableParams_ActorClassToConstruct = 0x28;
        private static readonly TimeSpan CraftingTargetsRefreshInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan CraftingTargetsMissingRetryInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan CraftingTargetsFailureRetryInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ProcessorCraftingUpdateInterval = TimeSpan.FromMilliseconds(100);
        private const int SN2DatabankViewModel_Entries = 0x68;
        private const int SN2DatabankViewModel_Root = 0x78;
        private const int SN2DatabankViewModel_StoryGoalContainer = 0x80;
        private const int SN2DatabankViewModel_DatabankEntries = 0x88;
        private const int SN2DatabankCategoryViewModel_SubCategories = 0x78;
        private const int SN2DatabankCategoryViewModel_Entries = 0x88;
        private const int SN2DatabankEntryViewModel_Entry = 0x68;
        private const int WBP_TabDatabank_ViewModel = 0x518;
        private const int WBP_DatabankCategory_ViewModel = 0x4E0;
        private const int WBP_DatabankEntry_SN2DatabankEntryViewModel = 0x1580;
        private const int WBP_DatabankEntryDetail_ViewModel = 0x3C8;
        private const int WBP_DatabankEntryWrapper_EntryViewModel = 0x350;
        private const int UWEDatabankEntry_UnlockingRequirements = 0xA0;
        private const int UWEStoryGoalContainer_UnlockRecords = 0xC8;
        private const int UWEStoryGoalContainer_StoryGoalsEntries = 0x1F8;
        private const int UWEStoryGoalContainer_CachedStoryGoals = 0x208;
        private const int SN2GameState_StoryGoalContainerComponent = 0x438;
        private const int SN2PlayerState_StoryGoalContainerComponent = 0x410;
        private const int FStoryGoalUnlockRecord_Stride = 0x18;
        private const int FStoryGoalUnlockRecord_StoryGoal = 0x00;
        private const int FUWEStoryGoalEntry_Stride = 0x1C;
        private const int FUWEStoryGoalEntry_StoryGoal = 0x0C;
        private const int FPrimaryAssetId_PrimaryAssetName = 0x08;
        private const int UWERequiredStoryGoalRule_RequiredStoryGoalRef = 0x30;
        private const int UWEStoryGoalRuleComposite_Rules = 0x30;
        private const int UWEStoryGoalRuleNegate_RuleToNegate = 0x30;
        private const int UWEStoryGoalRuleCount_MinimumCount = 0x40;

        public Subnautica2Memory(LiveSplitState state, Subnautica2Component component, Logger logger, Subnautica2Settings settings) : base(logger)
        {            
            OnHook += () =>
            {
                GetGameVersion();
                unrealTask = new UnrealHelperTask(game, logger);
                unrealTask.Run(Version.Parse("5.6.0"), InitPointers);
            };

            OnExit += () => {
                Events?.Dispose();
                Events = null;

                if (unrealTask != null)
                {
                    pointersInitialized = false;
                    unrealHelper = null;
                    pointerFactory = null;
                    playerCharacterPointer = null;
                    playerInventoryComponent = null;
                    playerEquipmentComponent = null;
                    playerToolbarComponent = null;
                    nextInventoryProbeAttempt = DateTime.MinValue;
                    nextBlueprintProbeAttempt = DateTime.MinValue;
                    nextDatabankProbeAttempt = DateTime.MinValue;
                    nextBiomeProbeAttempt = DateTime.MinValue;
                    nextEnumDiscoveryLog = DateTime.MinValue;
                    nextBiomeDiscoveryLog = DateTime.MinValue;
                    nextEncyclopediaUpdateAttempt = DateTime.MinValue;
                    nextCurrentBiomeLog = DateTime.MinValue;
                    nextMemoryUpdate = DateTime.MinValue;
                    playerVolumeTracker = IntPtr.Zero;
                    worldZoneTracker = IntPtr.Zero;
                    worldZoneTrackerRefreshTask = null;
                    nextWorldZoneTrackerAttempt = DateTime.MinValue;
                    biomeBaselineInitialized = false;
                    currentBiomeKey = string.Empty;
                    currentBiomeKeyOld = string.Empty;
                    lastBiomeProbeState = string.Empty;
                    mainMenuEnteredThisUpdate = false;
                    creativeStartThisUpdate = false;
                    survivalStartThisUpdate = false;
                    gameplayInitializationPending = false;
                    gameplayInitializationReason = string.Empty;
                    secondBaseReached = false;
                    secondBaseArmed = false;
                    enterTadpoleAfterSecondBaseArmed = false;
                    deepStartCinematicRemovalCount = 0;
                    loadRemovalActive = false;
                    inventoryStorageObjects.Clear();
                    inventoryStorageRefreshTask = null;
                    playerCharacterRefreshTask = null;
                    inventoryStorageRefreshInventoryId = int.MinValue;
                    playerCharacterRefreshReason = string.Empty;
                    playerCharacterClassName = string.Empty;
                    nextInventoryStorageRefreshAttempt = DateTime.MinValue;
                    inventoryBaselineInitialized = false;
                    lastPlayerInventoryId = int.MinValue;
                    currentInventoryChanges.Clear();
                    currentCraftEvents.Clear();
                    currentBuildEvents.Clear();
                    activeCrafts.Clear();
                    knownCrafters.Clear();
                    processorStations.Clear();
                    processorStates.Clear();
                    craftingTargetsRefreshTask = null;
                    nextCraftingTargetsRefreshAttempt = DateTime.MinValue;
                    refreshCraftingTargetsAfterCurrent = false;
                    currentPlayerCharacterAddress = IntPtr.Zero;
                    controlledPawnAddress = IntPtr.Zero;
                    localPlayer = IntPtr.Zero;
                    localPlayerControllerOffset = 0;
                    controllerPawnOffset = 0;
                    nextLocalPlayerResolveAttempt = DateTime.MinValue;
                    builderTool = IntPtr.Zero;
                    builderConstructionComponent = IntPtr.Zero;
                    builderReplicatedGhost = IntPtr.Zero;
                    builderReplicatedComponent = IntPtr.Zero;
                    builderReplicatedLastProgress = 0f;
                    builderReplicatedSawIncrease = false;
                    builderReplicatedCompletedAt = DateTime.MinValue;
                    builderCompletedLocalComponent = IntPtr.Zero;
                    builderCompletedReplicatedComponent = IntPtr.Zero;
                    nextProcessorCraftingUpdate = DateTime.MinValue;
                    pendingBuilderBuildable = Buildable.None;
                    pendingBuilderSource = IntPtr.Zero;
                    builderLastDiscoveryGhost = IntPtr.Zero;
                    craftBaselineInitialized = false;
                    curPickUpCounts.Clear();
                    curDropCounts.Clear();
                    loggedInventoryItemTypeAssets.Clear();
                    loggedCraftingRecipeAssets.Clear();
                    loggedDatabankEntryAssets.Clear();
                    loggedBiomeAssets.Clear();
                    inventoryItemTypeCache.Clear();
                    blueprintRecipeCache.Clear();
                    craftableRecipeCache.Clear();
                    buildableRecipeCache.Clear();
                    loggedBuilderGhostIdentity.Clear();
                    biomeObjectCache.Clear();
                    recipeListViewModels.Clear();
                    recipeListViewModelRefreshTask = null;
                    recipeListViewModelsInvalidated = false;
                    recipeListViewModelsChanged = false;
                    databankViewModels.Clear();
                    databankViewModelRefreshTask = null;
                    databankViewModelsInvalidated = false;
                    databankViewModelsChanged = false;
                    encyclopediaBaselineInitialized = false;
                    encyclopediaPrimaryEntryKeys.Clear();
                    encyclopediaPrimaryEntryKeysOld.Clear();
                    encyclopediaEntryKeys.Clear();
                    encyclopediaEntryKeysOld.Clear();
                    lock (databankEntryInfoCacheLock)
                        databankEntryInfoCache.Clear();
                    unrealFieldOffsetCache.Clear();
                    activeCraftStride = 0;
                    lastEncyclopediaReadState = string.Empty;
                    encyclopediaReadTask = null;
                    encyclopediaReadTaskResetsBaseline = false;
                    encyclopediaReadGeneration++;
                    encyclopediaReadTaskGeneration = encyclopediaReadGeneration;
                    KnownBlueprints.Clear();
                    KnownBlueprintsOld.Clear();
                    Encyclopedia.Clear();
                    EncyclopediaOld.Clear();
                    CurrentBiome = Biome.None;
                    CurrentBiomeOld = Biome.None;
                    unrealTask.Dispose();
                    unrealTask = null;
                }
            };

            this.settings = settings;

            subConditions = new Dictionary<SplitName, Func<bool>>
            {
                { SplitName.Inventory,            () => {
                                                        var inv = (ItemSplit)CurrentSplitToCheck;
                                                        return !inv.IsCount && HasPlayerItem(inv.Item)
                                                            || inv.IsCount && GetPlayerItemCount(inv.Item) == inv.Count;
                                                        } },
                { SplitName.Blueprint,            () => HasBlueprint(((BlueprintSplit)CurrentSplitToCheck).Blueprint) },
                { SplitName.Craft,                () => currentCraftEvents.Contains(((CraftSplit)CurrentSplitToCheck).Craftable) },
                { SplitName.Build,                () => currentBuildEvents.Contains(((BuildSplit)CurrentSplitToCheck).Buildable) },
                { SplitName.Encyclopedia,         () => HasEncyclopediaEntry((EncySplit)CurrentSplitToCheck) },
                { SplitName.StoryGoal,            () => HasStoryGoal(((StoryGoalSplit)CurrentSplitToCheck).Goal) },
                { SplitName.Biome,                () => IsInBiome(((BiomeSplit)CurrentSplitToCheck).Biomes.Biome1) },
            };

            foreach (SplitName splitName in PrefabricatedEventNames.Keys)
                    subConditions.Add(
                               splitName,
                                () => PrefabricatedEventTriggered(splitName));

                    subConditions.Add(
                                SplitName.SecondBase,
                                () => secondBaseArmed
                                && IsInBiome(Biome.Observatory)
                                && currentBuildEvents.Contains(Buildable.Room));

                    subConditions.Add(
                                SplitName.EnterTadpoleAfterSecondBase,
                                () => enterTadpoleAfterSecondBaseArmed
                                && IsInBiome(Biome.Observatory)
                                && Ue5EventTriggered("EnterTadpole"));
                    subConditions.Add(
                                SplitName.ScanRosettaStone,
                                () => HasStoryGoal(StoryGoal.Rosetta_TranslationUnlocked));            
            
            splitConditions = new Dictionary<SplitName, Func<bool>>
            {
                { SplitName.Inventory,            () => {
                                                        var inv = (ItemSplit)CurrentSplitToCheck;
                                                        var item = inv.Item;

                                                        int currentPickUpChange = curPickUpCounts.TryGetValue(item, out InvChangeInfo infP) ? infP.Count : 0;
                                                        int currentDropChange = curDropCounts.TryGetValue(item, out InvChangeInfo infD) ? infD.Count : 0;

                                                        int change = inv.PickUp ? currentPickUpChange : -currentDropChange;
                                                        bool changedInRightDirection = change > 0;

                                                        if (!inv.IsCount)
                                                        {
                                                            int currentChange = currentInventoryChanges.TryGetValue(item, out int delta) ? delta : 0;
                                                            return inv.PickUp ? currentChange > 0 : currentChange < 0;
                                                        }

                                                        if (inv.AlreadySplitInvChanging)
                                                            return false;

                                                        bool split = change >= inv.Count && changedInRightDirection;
                                                        inv.AlreadySplitInvChanging = split;
                                                        return split;
                                                        } },
                { SplitName.Blueprint,            () => {
                                                        var blueprint = ((BlueprintSplit)CurrentSplitToCheck).Blueprint;
                                                        return HasNewBlueprint(blueprint);
                                                        } },
                { SplitName.Craft,                () => currentCraftEvents.Contains(((CraftSplit)CurrentSplitToCheck).Craftable) },
                { SplitName.Build,                () => currentBuildEvents.Contains(((BuildSplit)CurrentSplitToCheck).Buildable) },
                { SplitName.Encyclopedia,         () => HasNewEncyclopediaEntry((EncySplit)CurrentSplitToCheck) },
                { SplitName.StoryGoal,            () => HasNewStoryGoal(((StoryGoalSplit)CurrentSplitToCheck).Goal) },
                { SplitName.Biome,                () => HasEnteredBiome((BiomeSplit)CurrentSplitToCheck) },
            };

            foreach (SplitName splitName in PrefabricatedEventNames.Keys)
                    splitConditions.Add(
                               splitName,
                               () => PrefabricatedEventTriggered(splitName));

                    splitConditions.Add(
                               SplitName.SecondBase,
                               SecondBaseTriggered);

                    splitConditions.Add(
                               SplitName.EnterTadpoleAfterSecondBase,
                               EnterTadpoleAfterSecondBaseTriggered);   
                    splitConditions.Add(
                               SplitName.ScanRosettaStone,
                               () => HasNewStoryGoal(StoryGoal.Rosetta_TranslationUnlocked));                  
        }

        public override bool Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextMemoryUpdate)
                return false;

            nextMemoryUpdate = now.Add(MemoryUpdateInterval);

            if (!base.Update())
                return false;

            if (!pointersInitialized || game == null)
                return false;

            try
            {
                Events?.Update();
            }
            catch (Exception ex)
            {
                logger.Log("[UE5Events] Disabled after failure: " + ex.Message);
                Events?.Dispose();
                Events = null;
            }

            AttachPlayerFromLocalPlayer();
            UpdateEventState();

            // Return the start signal to LiveSplit on the same update. Even
            // asynchronous discovery is begun on the following tick so no
            // gameplay reader can delay the timer start.
            if (!creativeStartThisUpdate && !survivalStartThisUpdate)
            {
                ApplyPendingGameplayInitialization();
                UpdateMemoryWatchers();
            }

            return true;
        }

        private void UpdateEventState()
        {
            mainMenuEnteredThisUpdate = Ue5EventTriggered("MainMenuConstruct");
            creativeStartThisUpdate = Ue5EventTriggered("CreativeStart");
            survivalStartThisUpdate = false;

            if (mainMenuEnteredThisUpdate)
            {
                startedTimerBefore = false;
                deepStartCinematicRemovalCount = 0;
                loadRemovalActive = false;
                playerDiscoveryAllowed = false;
                gameplayInitializationPending = false;
                gameplayInitializationReason = string.Empty;
                secondBaseReached = false;
                secondBaseArmed = false;
                enterTadpoleAfterSecondBaseArmed = false;
                ClearPlayerSessionState();
                logger.Log("Main menu entered");
            }

            if (Ue5EventTriggered("IntroLifepodAscend"))
            {
                loadRemovalActive = true;
                logger.Log("Intro load removal started");
            }

            bool introSequenceEnded = Ue5EventTriggered("IntroCutsceneLoadRemovalEnd");
            if (introSequenceEnded)
            {
                loadRemovalActive = false;
                logger.Log("Intro load removal ended");
            }

            bool deepStartCinematicRemoved = Ue5EventTriggered("DeepStartCinematicTagRemoved");
            if (deepStartCinematicRemoved)
            {
                deepStartCinematicRemovalCount++;
                logger.Log($"Deep Start cinematic tag removed (count={deepStartCinematicRemovalCount})");
                if (deepStartCinematicRemovalCount >= 2 && !survivalStartThisUpdate)
                {
                    survivalStartThisUpdate = true;
                    logger.Log("Survival start: second cinematic removal returned player control");
                }
            }

            if (creativeStartThisUpdate || survivalStartThisUpdate)
                QueueGameplayInitialization(creativeStartThisUpdate ? "Creative Start" : "Survival Start");
        }

        private void QueueGameplayInitialization(string reason)
        {
            playerDiscoveryAllowed = true;
            gameplayInitializationPending = true;
            gameplayInitializationReason = reason;
            logger.Log($"Gameplay initialization queued after {reason}");
        }

        private void ApplyPendingGameplayInitialization()
        {
            if (!gameplayInitializationPending)
                return;

            gameplayInitializationPending = false;
            string reason = gameplayInitializationReason;
            gameplayInitializationReason = string.Empty;
            nextLocalPlayerResolveAttempt = DateTime.MinValue;
            AttachPlayerFromLocalPlayer();

            nextInventoryProbeAttempt = DateTime.MinValue;
            nextInventoryStorageRefreshAttempt = DateTime.MinValue;
            nextCraftingTargetsRefreshAttempt = DateTime.MinValue;
            nextBiomeProbeAttempt = DateTime.MinValue;
            nextBlueprintProbeAttempt = DateTime.MinValue;
            nextDatabankProbeAttempt = DateTime.MinValue;
            nextEncyclopediaUpdateAttempt = DateTime.MinValue;

            logger.Log($"Gameplay initialization started after {reason}: player, inventory, craft, build, biome, blueprint, and encyclopedia");
        }

        private void AttachPlayerFromLocalPlayer()
        {
            if (unrealHelper == null)
                return;

            if (localPlayer == IntPtr.Zero)
            {
                if (DateTime.UtcNow < nextLocalPlayerResolveAttempt)
                    return;

                nextLocalPlayerResolveAttempt = DateTime.UtcNow.AddSeconds(5);
                localPlayer = unrealHelper.FindLiveUObject("SN2LocalPlayer");
                if (localPlayer == IntPtr.Zero)
                    return;
            }

            try
            {
                IntPtr controller = game.Read<IntPtr>(localPlayer + localPlayerControllerOffset);
                IntPtr player = controller == IntPtr.Zero
                    ? IntPtr.Zero
                    : game.Read<IntPtr>(controller + controllerPawnOffset);
                controlledPawnAddress = player;
                if (player != IntPtr.Zero && player != currentPlayerCharacterAddress)
                {
                    string className = unrealHelper.GetUObjectClassName(player);
                    if (IsPlayableCharacterClass(className))
                        AttachPlayerCharacter(player, "SN2LocalPlayer.PlayerController.Pawn");
                }
            }
            catch
            {
                localPlayer = IntPtr.Zero;
            }
        }

        private void AttachPlayerCharacter(IntPtr player, string source)
        {
            string className = unrealHelper.GetUObjectClassName(player);
            if (!IsPlayableCharacterClass(className))
                return;

            bool playerChanged = currentPlayerCharacterAddress != player;
            currentPlayerCharacterAddress = player;
            playerDiscoveryAllowed = true;

            if (playerChanged)
            {
                ResetProgressionSessionState();
                InvalidateCraftingTargetsForPlayerChange();
            }

            var playerCharacter = new PlayerCharacterMatch(player, className);
            if (playerCharacterPointer == null
                || playerInventoryComponent == null
                || playerEquipmentComponent == null
                || playerCharacterPointer.ClassName != className)
            {
                InitializeInventoryPointers(playerCharacter);
            }
            else
            {
                playerCharacterPointer.SetBase(player);
                playerInventoryComponent.ForceUpdate(true);
                playerEquipmentComponent.ForceUpdate(true);
                playerToolbarComponent?.ForceUpdate(true);
            }

            nextInventoryProbeAttempt = DateTime.MinValue;
            nextInventoryStorageRefreshAttempt = DateTime.MinValue;
            nextCraftingTargetsRefreshAttempt = DateTime.MinValue;
            nextBiomeProbeAttempt = DateTime.MinValue;
            logger.Log($"Player attached directly: source={source} class={className} player={player.ToString("X")}");
        }

        private static bool IsPlayableCharacterClass(string className) =>
            !string.IsNullOrWhiteSpace(className)
            && (className.IndexOf("SN2PlayerCharacter", StringComparison.OrdinalIgnoreCase) >= 0
                || className.StartsWith("BP_Character_", StringComparison.OrdinalIgnoreCase));

        private void ClearPlayerSessionState()
        {
            currentPlayerCharacterAddress = IntPtr.Zero;
            controlledPawnAddress = IntPtr.Zero;
            playerCharacterPointer = null;
            playerInventoryComponent = null;
            playerEquipmentComponent = null;
            playerToolbarComponent = null;
            playerCharacterClassName = string.Empty;
            playerCharacterRefreshTask = null;
            playerCharacterRefreshReason = string.Empty;
            inventoryStorageObjects.Clear();
            inventoryStorageRefreshTask = null;
            inventoryStorageRefreshInventoryId = int.MinValue;
            inventoryBaselineInitialized = false;
            lastPlayerInventoryId = int.MinValue;
            currentInventoryChanges.Clear();
            craftingTargetsRefreshTask = null;
            refreshCraftingTargetsAfterCurrent = false;
            ResetProgressionSessionState();
            InvalidateCraftingTargetsForPlayerChange();
        }

        private void ResetProgressionSessionState()
        {
            KnownBlueprints.Clear();
            KnownBlueprintsOld.Clear();
            blueprintBaselineInitialized = false;
            recipeListViewModels.Clear();
            recipeListViewModelRefreshTask = null;
            recipeListViewModelsInvalidated = true;
            recipeListViewModelsChanged = true;
            nextBlueprintProbeAttempt = DateTime.MinValue;

            Encyclopedia.Clear();
            EncyclopediaOld.Clear();
            encyclopediaPrimaryEntryKeys.Clear();
            encyclopediaPrimaryEntryKeysOld.Clear();
            encyclopediaEntryKeys.Clear();
            encyclopediaEntryKeysOld.Clear();
            encyclopediaBaselineInitialized = false;
            databankViewModels.Clear();
            databankViewModelRefreshTask = null;
            databankViewModelsInvalidated = true;
            databankViewModelsChanged = true;
            encyclopediaReadGeneration++;
            encyclopediaReadTask = null;
            encyclopediaReadTaskResetsBaseline = false;
            nextDatabankProbeAttempt = DateTime.MinValue;
            nextEncyclopediaUpdateAttempt = DateTime.MinValue;
            lastEncyclopediaReadState = string.Empty;
            currentStoryGoals.Clear();
            previousStoryGoals.Clear();
            storyGoalBaselineInitialized = false;
            nextStoryGoalUpdateAttempt = DateTime.MinValue;
        }

        private bool PrefabricatedEventTriggered(SplitName splitName)
        {
            return PrefabricatedEventNames.TryGetValue(splitName, out string[] eventNames)
                && eventNames.Any(Ue5EventTriggered);
        }

        private bool SecondBaseTriggered()
{
    if (!secondBaseArmed
        || !IsInBiome(Biome.Observatory)
        || !currentBuildEvents.Contains(Buildable.Room))
    {
        return false;
    }

    secondBaseArmed = false;
    return true;
}

private bool EnterTadpoleAfterSecondBaseTriggered()
{
    if (!enterTadpoleAfterSecondBaseArmed
        || !IsInBiome(Biome.Observatory)
        || !Ue5EventTriggered("EnterTadpole"))
    {
        return false;
    }

    enterTadpoleAfterSecondBaseArmed = false;
    return true;
}

private void UpdateSecondBaseArming()
{
    // HumanOutpost_Detected is the prerequisite. The player does not
    // need to be inside Observatory at the moment it becomes available.
    if (secondBaseReached
        || !HasStoryGoal(StoryGoal.HumanOutpost_Detected))
    {
        return;
    }

    secondBaseReached = true;

    if (Needs(SplitName.SecondBase))
    {
        secondBaseArmed = true;
        logger.Log(
            "Split on 2nd Base armed by HumanOutpost_Detected; " +
            "waiting for a Room build inside Observatory");
    }

    if (Needs(SplitName.EnterTadpoleAfterSecondBase))
    {
        enterTadpoleAfterSecondBaseArmed = true;
        logger.Log(
            "Enter Tadpole after 2nd Base armed by HumanOutpost_Detected; " +
            "waiting for Tadpole entry inside Observatory");
    }
}

        public bool CreativeStartTriggered() => creativeStartThisUpdate;
        public bool SurvivalStartTriggered() => survivalStartThisUpdate;
        public bool MainMenuEntered() => mainMenuEnteredThisUpdate;

        #region Memory stuff
        private void GetGameVersion()
        {
            System.Diagnostics.ProcessModule firstModule = game.Process.Modules.Cast<System.Diagnostics.ProcessModule>().FirstOrDefault();
            if (firstModule == null) return;
            int moduleLen = firstModule.ModuleMemorySize;
            gameVersion = GameVersion.v121347;
            if (moduleLen != 230486016)
            {
                MessageBox.Show($"Module length {moduleLen} does not match supported game version 121347.",
                                "Subnautica2 Autosplitter",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
            }
        }

        private void InitPointers(IUnrealHelper unrealHelper)
        {
            logger.Log("Unreal helper initialized");

            this.unrealHelper = unrealHelper;
            pointerFactory = new UnrealNestedPointerFactory(game, unrealHelper);
            localPlayerControllerOffset = GetUnrealFieldOffset("LocalPlayer", 0x30, "PlayerController");
            controllerPawnOffset = GetUnrealFieldOffset("Controller", 0x2F0, "Pawn");
            localPlayer = unrealHelper.FindLiveUObject("SN2LocalPlayer");
            logger.Log(
                $"Local-player chain initialized: localPlayer={localPlayer.ToString("X")} " +
                $"controllerOffset=0x{localPlayerControllerOffset:X} pawnOffset=0x{controllerPawnOffset:X}");

            Events?.Dispose();
            Events = null;
            try
            {
                Events = new Subnautica2Ue5EventRegistry(
                    game,
                    unrealHelper,
                    message => logger.Log(message));

                logger.Log(
                    Events.Count == 0
                        ? "[UE5Events] No events configured; event reader is inactive"
                        : "[UE5Events] Registry initialized with " + Events.Count + " configured event(s)");
            }
            catch (Exception ex)
            {
                Events?.Dispose();
                Events = null;
                logger.Log("[UE5Events] Registry disabled: " + ex.Message);
            }

            if (EnableEnumDiscoveryLogs)
                logger.Log("[EnumDiscovery] enabled; logging InventoryItem, Unlockable, Craftable, and EncyEntry candidates as asset names show up");
            if (EnableBiomeDiscoveryLogs)
                logger.Log("[EnumDiscovery] biome discovery enabled; logging Biome candidates from volume trackers, biome assets, and live biome volumes");
            else if (EnableBiomeProbeLogs)
                logger.Log("Biome probe logging enabled; current Biome reads will log without full enum discovery dumps");

            logger.Log("Pointers initialized");
            logger.Log(
                $"Autosplit settings active: splits={settings?.Splits?.Count ?? 0}, " +
                $"orderedLiveSplit={Subnautica2Settings.OrderedLiveSplit}, " +
                $"orderedAutoSplits={Subnautica2Settings.OrderedAutoSplits}");
            pointersInitialized = true;
        }

        private void UpdateMemoryWatchers()
{
    if (EnableEnumDiscoveryLogs)
        UpdateEnumDiscoveryLogs();

    // Craft and Build are immediately usable after a start. Process
    // their player-owned pointers before scheduling any fallback
    // UObject discovery for biome/inventory/databank systems.
    if (Needs(SplitName.Craft, SplitName.Build, SplitName.SecondBase))
        UpdateCrafting();

    if (Needs(
            SplitName.Biome,
            SplitName.SecondBase,
            SplitName.EnterTadpoleAfterSecondBase)
        || EnableBiomeDiscoveryLogs
        || EnableBiomeProbeLogs)
    {
        EnsureBiomeProbe();
        UpdateBiome();
    }

    if (Needs(
            SplitName.Inventory,
            SplitName.Craft,
            SplitName.Build,
            SplitName.SecondBase)
        || EnableEnumDiscoveryLogs)
    {
        EnsureInventoryProbe();
    }

    if (Needs(SplitName.Inventory) || EnableEnumDiscoveryLogs)
        UpdateInventory();

    if (Needs(SplitName.Blueprint) || EnableEnumDiscoveryLogs)
        UpdateBlueprints();

    if (Needs(SplitName.Encyclopedia) || EnableEnumDiscoveryLogs)
        UpdateEncyclopedia();

    if (Needs(
            SplitName.StoryGoal,
            SplitName.SecondBase,
            SplitName.ScanRosettaStone,
            SplitName.EnterTadpoleAfterSecondBase))
    {
        UpdateStoryGoals();
    }

    UpdateSecondBaseArming();
}

        private void UpdateCrafting()
        {
            currentCraftEvents.Clear();
            currentBuildEvents.Clear();

            if (Needs(SplitName.Build, SplitName.SecondBase))
            {
                EnsureBuilderTargets();
                UpdateBuilderConstruction();
            }

            if (Needs(SplitName.Craft))
            {
                EnsureCraftingTargets();
                UpdateProcessorCrafting();
                UpdateNormalCrafting();

                }
            }


        private void UpdateNormalCrafting()
        {

            IntPtr player = GetPlayerCharacterAddress();
            if (player == IntPtr.Zero || unrealHelper == null)
                return;

            var inProgressCrafts = new HashSet<ActiveCraftKey>();
            try
            {
                int craftingComponentOffset = GetUnrealFieldOffset("SN2PlayerCharacter", SN2PlayerCharacter_CraftingComponent, "CraftingComponent");
                IntPtr craftingComponent = game.Read<IntPtr>(player + craftingComponentOffset);
                if (craftingComponent == IntPtr.Zero)
                    return;

                int currentCrafterOffset = GetUnrealFieldOffset("UWECraftingComponent", UWECraftingComponent_CurrentCrafterComponent, "CurrentCrafterComponent");
                IntPtr crafter = game.Read<IntPtr>(craftingComponent + currentCrafterOffset);
                if (crafter != IntPtr.Zero)
                    knownCrafters.Add(crafter);

                // CurrentCrafterComponent may clear when the player releases
                // the Habitat Builder. Keep reading crafters already seen so
                // a paused construction is not mistaken for a completion.
                foreach (IntPtr knownCrafter in knownCrafters)
                {
                    try
                    {
                        if (!ReadActiveCrafts(knownCrafter, inProgressCrafts))
                            PreserveActiveCrafterState(knownCrafter, inProgressCrafts);
                    }
                    catch
                    {
                        // A transient failed read must not look like every craft on
                        // this component completed at once. Preserve its last state.
                        PreserveActiveCrafterState(knownCrafter, inProgressCrafts);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Log($"Craft read failed: {ex.Message}");
                return;
            }

            if (!craftBaselineInitialized)
            {
                activeCrafts = inProgressCrafts;
                craftBaselineInitialized = true;
                logger.Log($"Craft baseline initialized: activeCrafts={activeCrafts.Count}");
                return;
            }

            foreach (ActiveCraftKey craft in inProgressCrafts)
            {
                if (activeCrafts.Contains(craft))
                    continue;

                if (TryReadCraftable(craft.Recipe, out Craftable craftable, out string objectName, out string objectPath))
                {
                    currentCraftEvents.Add(craftable);
                    logger.Log($"Craft started: {craftable} recipe={objectName} path={objectPath}");
                }
                else
                {
                    logger.Log($"Craft started with unmapped recipe: object={craft.Recipe.ToString("X")} name={objectName} path={objectPath}");
                }
            }

            activeCrafts = inProgressCrafts;
        }

        private void EnsureCraftingTargets()
        {
            if (craftingTargetsRefreshTask != null && craftingTargetsRefreshTask.IsCompleted)
            {
                TimeSpan nextRefreshInterval = CraftingTargetsRefreshInterval;
                try
                {
                    CraftingTargets targets = craftingTargetsRefreshTask.Result;
                    ApplyCraftingTargets(targets, "refreshed");
                    if (targets == null || targets.Crafters.Count == 0)
                    {
                        nextRefreshInterval = CraftingTargetsMissingRetryInterval;
                    }
                }
                catch (Exception ex)
                {
                    logger.Log($"Craft/build target refresh failed: {ex.GetBaseException().Message}");
                    nextRefreshInterval = CraftingTargetsFailureRetryInterval;
                }
                finally
                {
                    craftingTargetsRefreshTask = null;
                    if (refreshCraftingTargetsAfterCurrent)
                    {
                        refreshCraftingTargetsAfterCurrent = false;
                        nextCraftingTargetsRefreshAttempt = DateTime.MinValue;
                    }
                    else
                    {
                        nextCraftingTargetsRefreshAttempt = DateTime.UtcNow.Add(nextRefreshInterval);
                    }
                }
            }

            if (currentPlayerCharacterAddress == IntPtr.Zero
                || craftingTargetsRefreshTask != null
                || DateTime.UtcNow < nextCraftingTargetsRefreshAttempt
                || unrealHelper == null)
                return;

            IUnrealHelper helper = unrealHelper;
            nextCraftingTargetsRefreshAttempt = DateTime.UtcNow.Add(CraftingTargetsRefreshInterval);
            craftingTargetsRefreshTask = Task.Run(() => FindCraftingTargets(helper));
        }

        private CraftingTargets FindCraftingTargets(IUnrealHelper helper)
        {
            const int maxPerClass = 128;
            IReadOnlyDictionary<string, IReadOnlyList<IntPtr>> objects =
                helper.FindLiveUObjects(maxPerClass, "UWECrafterComponent", "BP_ProcessorStation_C");

            return CreateCraftingTargets(helper, objects);
        }

        private CraftingTargets CreateCraftingTargets(
            IUnrealHelper helper,
            IReadOnlyDictionary<string, IReadOnlyList<IntPtr>> objects)
        {
            List<IntPtr> crafters = objects["UWECrafterComponent"]
                .Where(pointer => IsPersistentLevelObject(helper, pointer))
                .ToList();
            List<IntPtr> processors = objects["BP_ProcessorStation_C"]
                .Where(pointer => IsPersistentLevelObject(helper, pointer))
                .ToList();

            return new CraftingTargets(crafters, processors);
        }

        private void EnsureBuilderTargets()
        {
            if (builderTool != IntPtr.Zero || playerToolbarComponent == null || unrealHelper == null)
                return;

            try
            {
                IntPtr toolbar = playerToolbarComponent.New;
                if (toolbar == IntPtr.Zero)
                    return;

                IntPtr itemsArray = toolbar + UWEToolbarComponent_ToolbarItems;
                IntPtr data = game.Read<IntPtr>(itemsArray);
                int count = game.Read<int>(itemsArray + game.PointerSize);
                int capacity = game.Read<int>(itemsArray + game.PointerSize + 4);
                if (!IsPlausibleArray(data, count, capacity, 64))
                    return;

                for (int index = 0; index < count; index++)
                {
                    IntPtr actor = game.Read<IntPtr>(data + index * FUWEToolbarItem_Stride + FUWEToolbarItem_Actor);
                    if (actor == IntPtr.Zero)
                        continue;

                    string className = unrealHelper.GetUObjectClassName(actor);
                    bool isBuilder = className.Equals("BP_Builder_C", StringComparison.OrdinalIgnoreCase)
                        || className.IndexOf("BuilderTool", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isBuilder || className.IndexOf("Ghost", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    builderTool = actor;
                    logger.Log($"Build tracking ready from player toolbar: tool={builderTool.ToString("X")} class={className}");
                    return;
                }
            }
            catch
            {
                // Toolbar contents are transient during spawn/equip. Retry the
                // cheap player-owned read on the next update.
            }
        }

        private void ApplyCraftingTargets(CraftingTargets targets, string reason)
        {
            if (targets == null)
                return;

            int craftersBefore = knownCrafters.Count;
            foreach (IntPtr crafter in targets.Crafters)
                if (crafter != IntPtr.Zero) knownCrafters.Add(crafter);

            processorStations.Clear();
            processorStations.AddRange(targets.ProcessorStations.Where(pointer => pointer != IntPtr.Zero));
            processorStates.Keys
                .Where(pointer => !processorStations.Contains(pointer))
                .ToList()
                .ForEach(pointer => processorStates.Remove(pointer));

            logger.Log(
                $"Craft targets {reason}: crafters={knownCrafters.Count} (+{knownCrafters.Count - craftersBefore}), " +
                $"processors={processorStations.Count}");
        }

        private static bool IsPersistentLevelObject(IUnrealHelper helper, IntPtr pointer)
        {
            try
            {
                return helper.GetUObjectPath(pointer).IndexOf(".PersistentLevel.", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private void UpdateProcessorCrafting()
        {
            if (unrealHelper == null || processorStations.Count == 0)
                return;

            DateTime now = DateTime.UtcNow;
            if (now < nextProcessorCraftingUpdate)
                return;
            nextProcessorCraftingUpdate = now.Add(ProcessorCraftingUpdateInterval);

            int recipeOffset = GetUnrealFieldOffset("BP_ProcessorStation_C", ABPProcessorStation_CurrentRecipe, "CurrentRecipe");
            int stateOffset = GetUnrealFieldOffset("BP_ProcessorStation_C", ABPProcessorStation_ProcessorState, "ProcessorState");
            int progressOffset = GetUnrealFieldOffset("BP_ProcessorStation_C", ABPProcessorStation_ProcessingProgress, "ProcessingProgress");

            foreach (IntPtr station in processorStations.ToList())
            {
                try
                {
                    IntPtr recipe = game.Read<IntPtr>(station + recipeOffset);
                    byte state = game.Read<byte>(station + stateOffset);
                    float progress = game.Read<float>(station + progressOffset);
                    if (float.IsNaN(progress) || float.IsInfinity(progress))
                        continue;

                    var current = new ProcessorCraftState(recipe, state, progress);
                    if (!processorStates.TryGetValue(station, out ProcessorCraftState previous))
                    {
                        processorStates[station] = current;
                        continue;
                    }

                    bool started = recipe != IntPtr.Zero && progress > 0.0001f
                        && (previous.Progress <= 0.0001f || previous.Recipe != recipe);
                    processorStates[station] = current;
                    if (!started)
                        continue;

                    if (TryReadCraftable(recipe, out Craftable craftable, out string objectName, out string objectPath))
                    {
                        currentCraftEvents.Add(craftable);
                        logger.Log($"Processor craft started: {craftable} recipe={objectName} path={objectPath} state={state} progress={progress:F3}");
                    }
                    else
                    {
                        logger.Log($"Processor craft started with unmapped recipe: object={recipe.ToString("X")} name={objectName} path={objectPath} state={state} progress={progress:F3}");
                    }
                }
                catch (Exception ex)
                {
                    logger.Log($"Processor craft read failed for station {station.ToString("X")}: {ex.Message}");
                }
            }
        }

        private void UpdateBuilderConstruction()
        {
            if (unrealHelper == null || builderTool == IntPtr.Zero)
                return;

            try
            {
                int ghostOffset = GetUnrealFieldOffset("SN2BuilderTool", ASN2BuilderTool_Ghost, "Ghost");
                IntPtr ghost = unrealHelper.ResolveWeakObject(builderTool + ghostOffset);
                ResolveBuilderTransientTargets(ghost);
                IntPtr component = FindBuilderConstructableComponent(ghost);

                if (component != IntPtr.Zero && component != builderConstructionComponent)
                {
                    ResetBuilderConstructionState();
                    builderConstructionComponent = component;
                    if (component != builderCompletedLocalComponent)
                    {
                        builderCompletedLocalComponent = IntPtr.Zero;
                        CaptureBuilderConstructionIdentity(component, ghost, false);
                    }
                }
                else if (component != IntPtr.Zero
                    && component != builderCompletedLocalComponent
                    && pendingBuilderBuildable == Buildable.None)
                {
                    // Structural base ghosts can receive their serialized BrushType late in
                    // the construction. Re-read unresolved identity before checking the
                    // authoritative replicated completion on this frame.
                    CaptureBuilderConstructionIdentity(component, ghost, false);
                }

                UpdateReplicatedBuilderCompletion();

            }
            catch (Exception ex)
            {
                logger.Log($"Builder construction read failed: {ex.Message}");
            }
        }

        private void UpdateReplicatedBuilderCompletion()
        {
            if (builderReplicatedCompletedAt != DateTime.MinValue)
            {
                // The authoritative completion can arrive before structural
                // ghost serialization finishes. Re-read the exact constructed
                // class/ghost identity during the short completion window.
                CaptureReplicatedBuilderIdentity(builderReplicatedComponent, builderReplicatedGhost);
                if (pendingBuilderBuildable != Buildable.None
                    || DateTime.UtcNow - builderReplicatedCompletedAt >= TimeSpan.FromSeconds(1))
                {
                    FinishReplicatedBuilderCompletion();
                }
                return;
            }

            int targetOffset = GetUnrealFieldOffset("SN2BuilderTool", ASN2BuilderTool_CurrentTarget, "CurrentTarget");
            IntPtr currentTarget = unrealHelper.ResolveWeakObject(builderTool + targetOffset);
            if (currentTarget != IntPtr.Zero
                && unrealHelper.GetUObjectClassName(currentTarget).Equals(
                    "SN2ReplicatedBuilderGhost", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr replicatedComponent = game.Read<IntPtr>(
                    currentTarget + ASN2BuilderGhost_ConstructableComponent);
                if (replicatedComponent != IntPtr.Zero
                    && unrealHelper.GetUObjectClassName(replicatedComponent).IndexOf(
                        "ConstructableComponent", StringComparison.OrdinalIgnoreCase) >= 0
                    && replicatedComponent != builderCompletedReplicatedComponent
                    && (currentTarget != builderReplicatedGhost
                        || replicatedComponent != builderReplicatedComponent))
                {
                    builderCompletedReplicatedComponent = IntPtr.Zero;
                    builderReplicatedGhost = currentTarget;
                    builderReplicatedComponent = replicatedComponent;
                    builderReplicatedLastProgress = game.Read<float>(replicatedComponent + 0xF4);
                    builderReplicatedSawIncrease = false;
                    builderReplicatedCompletedAt = DateTime.MinValue;
                    CaptureReplicatedBuilderIdentity(replicatedComponent, currentTarget);
                    logger.Log(
                        $"Builder replicated construction tracked: mapped={pendingBuilderBuildable} " +
                        $"ghost={currentTarget.ToString("X")} component={replicatedComponent.ToString("X")}");
                }
            }

            if (builderReplicatedComponent == IntPtr.Zero)
                return;

            string componentClass = unrealHelper.GetUObjectClassName(builderReplicatedComponent);
            if (componentClass.IndexOf("ConstructableComponent", StringComparison.OrdinalIgnoreCase) < 0)
            {
                ResetReplicatedBuilderState();
                return;
            }

            float progress = game.Read<float>(builderReplicatedComponent + 0xF4);
            if (float.IsNaN(progress) || float.IsInfinity(progress)
                || progress < -0.001f || progress > 1.001f)
                return;

            if (progress > builderReplicatedLastProgress + 0.002f)
                builderReplicatedSawIncrease = true;

            if (builderReplicatedSawIncrease
                && builderReplicatedLastProgress < 0.999f
                && progress >= 0.999f)
            {
                CaptureReplicatedBuilderIdentity(builderReplicatedComponent, builderReplicatedGhost);
                builderReplicatedCompletedAt = DateTime.UtcNow;
                if (pendingBuilderBuildable != Buildable.None)
                    FinishReplicatedBuilderCompletion();
                else
                    logger.Log(
                        $"Builder completion awaiting exact build identity: " +
                        $"ghost={builderReplicatedGhost.ToString("X")} component={builderReplicatedComponent.ToString("X")}");
                return;
            }

            builderReplicatedLastProgress = progress;
        }

        private void FinishReplicatedBuilderCompletion()
        {
            IntPtr completedLocalComponent = builderConstructionComponent;
            IntPtr completedReplicatedComponent = builderReplicatedComponent;
            ReportBuilderCompletion("replicated constructable full");
            ResetBuilderConstructionState();
            ResetReplicatedBuilderState();
            builderCompletedLocalComponent = completedLocalComponent;
            builderCompletedReplicatedComponent = completedReplicatedComponent;
        }

        private void ResetReplicatedBuilderState()
        {
            builderReplicatedGhost = IntPtr.Zero;
            builderReplicatedComponent = IntPtr.Zero;
            builderReplicatedLastProgress = 0f;
            builderReplicatedSawIncrease = false;
            builderReplicatedCompletedAt = DateTime.MinValue;
        }

        private void CaptureReplicatedBuilderIdentity(IntPtr component, IntPtr ghost)
        {
            Buildable knownBuildable = pendingBuilderBuildable;
            IntPtr knownSource = pendingBuilderSource;
            CaptureBuilderConstructionIdentity(component, ghost, false);
            if (pendingBuilderBuildable == Buildable.None && knownBuildable != Buildable.None)
            {
                pendingBuilderBuildable = knownBuildable;
                pendingBuilderSource = knownSource;
            }
        }

        private void CaptureBuilderConstructionIdentity(
            IntPtr component,
            IntPtr ghost = default,
            bool logWhenUnmapped = true)
        {
            Buildable previousBuildable = pendingBuilderBuildable;
            pendingBuilderSource = ResolveBuilderConstructedClass(component);

            string sourceName;
            string sourcePath;
            if (TryReadBuildable(pendingBuilderSource, out Buildable buildable, out sourceName, out sourcePath))
                pendingBuilderBuildable = buildable;
            else if (TryResolveBuildableFromGhostParams(
                ghost, logWhenUnmapped, out IntPtr ghostSource, out buildable, out sourceName, out sourcePath))
            {
                pendingBuilderSource = ghostSource;
                pendingBuilderBuildable = buildable;
            }
            else
            {
                int targetOffset = GetUnrealFieldOffset("SN2BuilderTool", ASN2BuilderTool_CurrentTarget, "CurrentTarget");
                IntPtr target = unrealHelper.ResolveWeakObject(builderTool + targetOffset);
                pendingBuilderSource = target;
                pendingBuilderBuildable = TryReadBuildable(target, out buildable, out sourceName, out sourcePath)
                    ? buildable
                    : Buildable.None;
            }

            if (pendingBuilderBuildable != Buildable.None && pendingBuilderBuildable != previousBuildable
                || pendingBuilderBuildable == Buildable.None && logWhenUnmapped)
            {
                logger.Log(
                    $"Builder construction found: mapped={pendingBuilderBuildable} source={sourceName} path={sourcePath} " +
                    $"component={component.ToString("X")}");
            }
        }

        private bool TryResolveBuildableFromGhostParams(
            IntPtr ghost,
            bool logWhenUnmapped,
            out IntPtr source,
            out Buildable buildable,
            out string sourceName,
            out string sourcePath)
        {
            source = IntPtr.Zero;
            buildable = Buildable.None;
            sourceName = string.Empty;
            sourcePath = string.Empty;
            if (ghost == IntPtr.Zero)
                return false;

            IntPtr ghostParams = ghost + ASN2BuilderGhost_GhostParams;
            if (TryResolveBuildableFromGhostEdit(
                ghostParams, out buildable, out sourceName, out sourcePath))
            {
                source = ghost;
                return true;
            }

            IntPtr customGhostAddress = ghostParams + FSN2BuilderGhostParams_CustomGhostClass;
            IntPtr customGhost = unrealHelper.ResolveWeakObject(customGhostAddress);
            if (TryReadBuildable(customGhost, out buildable, out sourceName, out sourcePath))
            {
                source = customGhost;
                return true;
            }
            string customGhostPath = ReadSoftObjectAssetPath(customGhostAddress);
            if (TryMapBuildable(string.Empty, customGhostPath, out buildable))
            {
                source = ghost;
                sourceName = LastPathSegment(customGhostPath);
                sourcePath = customGhostPath;
                return true;
            }

            IntPtr overrideMeshAddress = ghostParams + 0x240;
            IntPtr overrideMesh = unrealHelper.ResolveWeakObject(overrideMeshAddress);
            if (TryReadBuildable(overrideMesh, out buildable, out sourceName, out sourcePath))
            {
                source = overrideMesh;
                return true;
            }
            string overrideMeshPath = ReadSoftObjectAssetPath(overrideMeshAddress);
            if (TryMapBuildable(string.Empty, overrideMeshPath, out buildable))
            {
                source = ghost;
                sourceName = LastPathSegment(overrideMeshPath);
                sourcePath = overrideMeshPath;
                return true;
            }

            IntPtr sourceActorsArray = ghostParams + FSN2BuilderGhostParams_SourceActors;
            IntPtr data = game.Read<IntPtr>(sourceActorsArray);
            int count = game.Read<int>(sourceActorsArray + 8);
            int capacity = game.Read<int>(sourceActorsArray + 12);
            if (data == IntPtr.Zero || count <= 0 || count > capacity || capacity > 64)
            {
                if (logWhenUnmapped)
                    LogBuilderGhostIdentity(ghost, customGhostAddress, Array.Empty<IntPtr>());
                return false;
            }

            var candidates = new List<IntPtr>();
            for (int i = 0; i < count; i++)
            {
                IntPtr candidateAddress = data + i * 0x28;
                IntPtr candidate = unrealHelper.ResolveWeakObject(candidateAddress);
                if (candidate != IntPtr.Zero)
                    candidates.Add(candidate);
                if (TryReadBuildable(candidate, out buildable, out sourceName, out sourcePath))
                {
                    source = candidate;
                    return true;
                }
                string candidatePath = ReadSoftObjectAssetPath(candidateAddress);
                if (TryMapBuildable(string.Empty, candidatePath, out buildable))
                {
                    source = ghost;
                    sourceName = LastPathSegment(candidatePath);
                    sourcePath = candidatePath;
                    return true;
                }
            }

            if (logWhenUnmapped)
                LogBuilderGhostIdentity(ghost, customGhostAddress, candidates);
            return false;
        }

        private bool TryResolveBuildableFromGhostEdit(
            IntPtr ghostParams,
            out Buildable buildable,
            out string sourceName,
            out string sourcePath)
        {
            buildable = Buildable.None;
            sourceName = string.Empty;
            sourcePath = string.Empty;
            IntPtr binaryArray = ghostParams + 0x198 + 0x18;
            IntPtr binaryData = game.Read<IntPtr>(binaryArray);
            int binaryCount = game.Read<int>(binaryArray + 8);
            int binaryCapacity = game.Read<int>(binaryArray + 12);
            if (binaryData == IntPtr.Zero || binaryCount <= 0
                || binaryCount > binaryCapacity || binaryCount > 65536)
                return false;

            byte[] bytes = game.Read(binaryData, binaryCount);
            string text = new string(bytes
                .Select(value => value >= 32 && value <= 126 ? (char)value : '.')
                .ToArray());
            const string marker = "SculpturalBase.BrushType.";
            int markerIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                return false;

            int valueStart = markerIndex + marker.Length;
            int valueEnd = valueStart;
            while (valueEnd < text.Length
                && (char.IsLetterOrDigit(text[valueEnd]) || text[valueEnd] == '_'))
                valueEnd++;
            string brushType = text.Substring(valueStart, valueEnd - valueStart);
            if (!TryMapBuildable(brushType, brushType, out buildable))
                return false;

            sourceName = brushType;
            sourcePath = marker + brushType;
            return true;
        }

        private string ReadSoftObjectAssetPath(IntPtr softObjectAddress)
        {
            try
            {
                int assetPathNameIndex = game.Read<int>(softObjectAddress + 0x10);
                if (assetPathNameIndex <= 0)
                    return string.Empty;
                string value = unrealHelper.GetFNameEntryName(assetPathNameIndex);
                return IsReadableUObjectText(value) ? value : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void LogBuilderGhostIdentity(
            IntPtr ghost,
            IntPtr customGhostAddress,
            IReadOnlyList<IntPtr> sourceActors)
        {
            if (!loggedBuilderGhostIdentity.Add(ghost))
                return;

            string custom = ReadSoftObjectAssetPath(customGhostAddress);
            if (string.IsNullOrEmpty(custom))
                custom = "none";
            string sources = sourceActors.Count == 0
                ? "none"
                : string.Join(", ", sourceActors.Select(unrealHelper.GetUObjectPath));
            IntPtr liveEditAction = unrealHelper.ResolveWeakObject(
                ghost + ASN2BuilderGhost_GhostParams + 0x1C0);
            string edit = liveEditAction == IntPtr.Zero
                ? "none"
                : unrealHelper.GetUObjectPath(liveEditAction);
            string mesh = ReadSoftObjectAssetPath(
                ghost + ASN2BuilderGhost_GhostParams + 0x240);
            if (string.IsNullOrEmpty(mesh))
                mesh = "none";
            IntPtr binaryArray = ghost + ASN2BuilderGhost_GhostParams + 0x198 + 0x18;
            IntPtr binaryData = game.Read<IntPtr>(binaryArray);
            int binaryCount = game.Read<int>(binaryArray + 8);
            string binaryText = "none";
            if (binaryData != IntPtr.Zero && binaryCount > 0 && binaryCount <= 4096)
            {
                byte[] bytes = game.Read(binaryData, Math.Min(binaryCount, 256));
                binaryText = new string(bytes
                    .Select(value => value >= 32 && value <= 126 ? (char)value : '.')
                    .ToArray());
            }
            logger.Log(
                $"Builder ghost identity candidates: ghost={ghost.ToString("X")} " +
                $"custom={custom} sources=[{sources}] liveEdit={edit} overrideMesh={mesh} " +
                $"editBytes={binaryText}");
        }

        private IntPtr FindBuilderConstructableComponent(IntPtr ghost)
        {
            if (ghost == IntPtr.Zero)
                return IntPtr.Zero;

            int componentOffset = GetUnrealFieldOffset(
                "SN2BuilderGhost",
                ASN2BuilderGhost_ConstructableComponent,
                "ConstructableComponent");
            IntPtr component = game.Read<IntPtr>(ghost + componentOffset);
            if (component == IntPtr.Zero)
                return IntPtr.Zero;

            string className = unrealHelper.GetUObjectClassName(component);
            return className.IndexOf("ConstructableComponent", StringComparison.OrdinalIgnoreCase) >= 0
                ? component
                : IntPtr.Zero;
        }

        private void ResolveBuilderTransientTargets(IntPtr ghost)
        {
            if (ghost != IntPtr.Zero && ghost != builderLastDiscoveryGhost)
            {
                builderLastDiscoveryGhost = ghost;
            }
            else if (ghost == IntPtr.Zero)
            {
                builderLastDiscoveryGhost = IntPtr.Zero;
            }

        }

        private IntPtr ResolveBuilderConstructedClass(IntPtr component)
        {
            int parametersOffset = GetUnrealFieldOffset(
                "SN2ConstructableComponent",
                USN2ConstructableComponent_ConstructableParams,
                "ConstructableParams");
            int actorClassOffset = GetUnrealFieldOffset(
                "SN2ConstructableParams",
                FSN2ConstructableParams_ActorClassToConstruct,
                "ActorClassToConstruct");
            return unrealHelper.ResolveWeakObject(component + parametersOffset + actorClassOffset);
        }

        private void ReportBuilderCompletion(string signal)
        {
            if (pendingBuilderBuildable != Buildable.None)
            {
                currentBuildEvents.Add(pendingBuilderBuildable);
                logger.Log(
                    $"Build completed: {pendingBuilderBuildable} source={pendingBuilderSource.ToString("X")} " +
                    $"signal={signal}");
                return;
            }

            string sourceName = pendingBuilderSource == IntPtr.Zero
                ? string.Empty
                : unrealHelper.GetUObjectName(pendingBuilderSource);
            string sourcePath = pendingBuilderSource == IntPtr.Zero
                ? string.Empty
                : unrealHelper.GetUObjectPath(pendingBuilderSource);
            logger.Log(
                $"Build completed with unmapped source: source={pendingBuilderSource.ToString("X")} name={sourceName} " +
                $"path={sourcePath} signal={signal}");
        }

        private void ResetBuilderConstructionState()
        {
            builderConstructionComponent = IntPtr.Zero;
            pendingBuilderBuildable = Buildable.None;
            pendingBuilderSource = IntPtr.Zero;
        }

        private void PreserveActiveCrafterState(IntPtr crafter, HashSet<ActiveCraftKey> crafts)
        {
            foreach (ActiveCraftKey craft in activeCrafts)
                if (craft.Crafter == crafter) crafts.Add(craft);
        }

        private bool ReadActiveCrafts(IntPtr crafter, HashSet<ActiveCraftKey> crafts)
        {
            int activeCraftsOffset = GetUnrealFieldOffset("UWECrafterComponent", UWECrafterComponent_ActiveCrafts, "ActiveCrafts");
            if (activeCraftStride <= 0)
            {
                activeCraftStride = unrealHelper.GetFieldElementSize("UWECrafterComponent", "ActiveCrafts");
                if (activeCraftStride <= 0 || activeCraftStride > 0x400)
                    activeCraftStride = FUWEActiveCraft_Stride;
            }

            int timerHandleOffset = GetUnrealFieldOffset("UWEActiveCraft", FUWEActiveCraft_CraftingTimerHandle, "CraftingTimerHandle");
            int recipeOffset = GetUnrealFieldOffset("UWEActiveCraft", FUWEActiveCraft_Recipe, "Recipe");
            int inProgressOffset = GetUnrealFieldOffset("UWEActiveCraft", FUWEActiveCraft_InProgress, "bInProgress");
            IntPtr arrayAddress = crafter + activeCraftsOffset;
            IntPtr dataPtr = game.Read<IntPtr>(arrayAddress);
            int num = game.Read<int>(arrayAddress + game.PointerSize);
            int max = game.Read<int>(arrayAddress + game.PointerSize + 4);
            if (!IsPlausibleArray(dataPtr, num, max, 128))
                return false;

            for (int i = 0; i < num; i++)
            {
                IntPtr activeCraft = dataPtr + i * activeCraftStride;
                IntPtr recipe = unrealHelper.ResolveWeakObject(activeCraft + recipeOffset);
                if (recipe == IntPtr.Zero)
                    continue;

                if (TryReadBuildable(recipe, out Buildable buildable, out _, out _) && buildable != Buildable.None)
                {
                    // Habitat Builder recipes are intentionally ignored here.
                    // Build completion is tracked only by UpdateBuilderConstruction.
                    continue;
                }
                else if (game.Read<bool>(activeCraft + inProgressOffset))
                {
                    ulong timerHandle = game.Read<ulong>(activeCraft + timerHandleOffset);
                    crafts.Add(new ActiveCraftKey(crafter, recipe, timerHandle));
                }
            }

            return true;
        }

        private bool TryReadCraftable(IntPtr recipe, out Craftable craftable, out string objectName, out string objectPath)
        {
            objectName = string.Empty;
            objectPath = string.Empty;
            if (recipe == IntPtr.Zero || unrealHelper == null)
            {
                craftable = Craftable.None;
                return false;
            }

            if (craftableRecipeCache.TryGetValue(recipe, out craftable))
            {
                objectName = unrealHelper.GetUObjectName(recipe);
                objectPath = unrealHelper.GetUObjectPath(recipe);
                return craftable != Craftable.None;
            }

            try
            {
                objectName = unrealHelper.GetUObjectName(recipe);
                objectPath = unrealHelper.GetUObjectPath(recipe);
                if (IsReadableUObjectText(objectName) && IsReadableUObjectText(objectPath)
                    && TryMapCraftable(objectName, objectPath, out craftable))
                {
                    craftableRecipeCache[recipe] = craftable;
                    return true;
                }
            }
            catch
            {
            }

            craftable = Craftable.None;
            craftableRecipeCache[recipe] = craftable;
            return false;
        }

        private bool TryReadBuildable(IntPtr recipe, out Buildable buildable, out string objectName, out string objectPath)
        {
            objectName = string.Empty;
            objectPath = string.Empty;
            if (recipe == IntPtr.Zero || unrealHelper == null)
            {
                buildable = Buildable.None;
                return false;
            }

            if (buildableRecipeCache.TryGetValue(recipe, out buildable))
            {
                objectName = unrealHelper.GetUObjectName(recipe);
                objectPath = unrealHelper.GetUObjectPath(recipe);
                return buildable != Buildable.None;
            }

            try
            {
                objectName = unrealHelper.GetUObjectName(recipe);
                objectPath = unrealHelper.GetUObjectPath(recipe);
                if (IsReadableUObjectText(objectName) && IsReadableUObjectText(objectPath)
                    && TryMapBuildable(objectName, objectPath, out buildable))
                {
                    buildableRecipeCache[recipe] = buildable;
                    return true;
                }
            }
            catch
            {
            }

            buildable = Buildable.None;
            buildableRecipeCache[recipe] = buildable;
            return false;
        }

        private void EnsureInventoryProbe()
        {
            if (!playerDiscoveryAllowed)
                return;

            if (playerInventoryComponent != null && playerEquipmentComponent != null)
                return;

            // SN2LocalPlayer persists across map travel. When it exists, wait
            // for its controller pawn instead of scanning the entire UObject
            // array for a player that has not spawned yet.
            if (localPlayer != IntPtr.Zero)
                return;

            UpdatePlayerCharacterRefresh();
            if (playerInventoryComponent != null && playerEquipmentComponent != null)
                return;

            if (playerCharacterRefreshTask != null)
                return;

            if (DateTime.Now < nextInventoryProbeAttempt)
                return;

            RefreshInventoryProbe("initialization");
        }

        private void UpdateEnumDiscoveryLogs()
        {
            if (unrealHelper == null || DateTime.Now < nextEnumDiscoveryLog)
                return;

            nextEnumDiscoveryLog = DateTime.Now.AddSeconds(3);
            LogLiveInventoryItemTypes();
            LogLiveCraftingRecipes();
            LogLiveDatabankEntries();
        }

        private void LogLiveInventoryItemTypes()
        {
            try
            {
                foreach (IntPtr itemType in unrealHelper.FindLiveUObjects("UWEItemType", EnumDiscoveryMaxObjects))
                    TryLogInventoryItemType(itemType);
            }
            catch (Exception ex)
            {
                logger.Log($"[EnumDiscovery] inventory item scan failed: {ex.Message}");
            }
        }

        private void LogLiveCraftingRecipes()
        {
            try
            {
                foreach (IntPtr recipe in unrealHelper.FindLiveUObjects("UWECraftingRecipe", EnumDiscoveryMaxObjects))
                    TryLogCraftingRecipe(recipe, Unlockable.None);
            }
            catch (Exception ex)
            {
                logger.Log($"[EnumDiscovery] crafting recipe scan failed: {ex.Message}");
            }
        }

        private void LogLiveDatabankEntries()
        {
            try
            {
                foreach (IntPtr entry in unrealHelper.FindLiveUObjects("UWEDatabankEntry", EnumDiscoveryMaxObjects))
                    TryLogDatabankEntry(entry, EncyEntry.None);
            }
            catch (Exception ex)
            {
                logger.Log($"[EnumDiscovery] databank entry scan failed: {ex.Message}");
            }
        }

        private void UpdateBiomeDiscoveryLogs()
        {
            if (!EnableBiomeDiscoveryLogs || unrealHelper == null || DateTime.Now < nextBiomeDiscoveryLog)
                return;

            nextBiomeDiscoveryLog = DateTime.Now.AddSeconds(3);
            LogWorldRegionAssets();
            LogLiveWorldZones();
            LogWorldZoneTrackers();
            LogLiveBiomeObjects("UWEWorldBiomeDataAsset");
            LogLiveBiomeObjects("UWEWaterBiomeRegionActor");
            LogLiveBiomeObjects("BP_UWEWaterBiomeRegionActor_C");
            LogLiveBiomeObjects("BP_UWEOceanBiomeRegionActor_C");
            LogLiveBiomeTrackers();
        }

        private void LogLiveBiomeObjects(string className)
        {
            try
            {
                foreach (IntPtr biomeObject in unrealHelper.FindLiveUObjects(className, EnumDiscoveryMaxObjects))
                    TryLogBiomeObject(biomeObject, className);
            }
            catch (Exception ex)
            {
                logger.Log($"[EnumDiscovery] biome scan failed for {className}: {ex.Message}");
            }
        }

        private void LogWorldRegionAssets()
        {
            try
            {
                foreach (IntPtr regionAsset in unrealHelper.FindLiveUObjects("UWEWorldRegionDataAsset", EnumDiscoveryMaxObjects))
                    TryLogWorldRegionAsset(regionAsset, "UWEWorldRegionDataAsset");
            }
            catch (Exception ex)
            {
                logger.Log($"[EnumDiscovery] world region asset scan failed: {ex.Message}");
            }
        }

        private void LogLiveWorldZones()
        {
            try
            {
                foreach (IntPtr zone in unrealHelper.FindLiveUObjects("UWEWorldZone", EnumDiscoveryMaxObjects))
                    TryLogWorldZone(zone, "UWEWorldZone");
            }
            catch (Exception ex)
            {
                logger.Log($"[EnumDiscovery] world zone scan failed: {ex.Message}");
            }
        }

        private void LogWorldZoneTrackers()
        {
            try
            {
                int zonesOffset = GetUnrealFieldOffset("UWEWorldZoneTrackerSubsystem", UUWEWorldZoneTrackerSubsystem_Zones, "Zones");
                foreach (IntPtr tracker in unrealHelper.FindLiveUObjects("UWEWorldZoneTrackerSubsystem", 16))
                {
                    List<IntPtr> zones = ReadPointerArray(tracker + zonesOffset, 4096);
                    logger.Log($"[EnumDiscovery][BiomeRegionTracker] tracker={tracker.ToString("X")} zones={zones.Count}");

                    foreach (IntPtr zone in zones)
                        TryLogWorldZone(zone, "UWEWorldZoneTrackerSubsystem.Zones");
                }
            }
            catch (Exception ex)
            {
                logger.Log($"[EnumDiscovery] world zone tracker scan failed: {ex.Message}");
            }
        }

        private void LogLiveBiomeTrackers()
        {
            try
            {
                int volumesOffset = GetUnrealFieldOffset("UWEBiomeTrackerSubsystem", UWEBiomeTrackerSubsystem_Volumes, "Volumes");
                foreach (IntPtr tracker in unrealHelper.FindLiveUObjects("UWEBiomeTrackerSubsystem", 16))
                {
                    List<IntPtr> volumes = ReadPointerArray(tracker + volumesOffset, 4096);
                    logger.Log($"[EnumDiscovery][BiomeTracker] tracker={tracker.ToString("X")} volumes={volumes.Count}");

                    foreach (IntPtr volume in volumes)
                        TryLogBiomeObject(volume, "UWEBiomeTrackerSubsystem.Volumes");
                }
            }
            catch (Exception ex)
            {
                logger.Log($"[EnumDiscovery] biome tracker scan failed: {ex.Message}");
            }
        }

        private void EnsureBiomeProbe()
        {
            if (currentPlayerCharacterAddress == IntPtr.Zero || unrealHelper == null)
                return;

            if (playerVolumeTracker == IntPtr.Zero && DateTime.Now >= nextBiomeProbeAttempt)
            {
                nextBiomeProbeAttempt = DateTime.Now.AddSeconds(1);
                InitBiomeProbe(unrealHelper);
            }

            if (worldZoneTrackerRefreshTask != null && worldZoneTrackerRefreshTask.IsCompleted)
            {
                try
                {
                    worldZoneTracker = worldZoneTrackerRefreshTask.Result;
                    if (worldZoneTracker != IntPtr.Zero)
                        logger.Log($"Biome world-zone fallback initialized: tracker={worldZoneTracker.ToString("X")}");
                    else
                        nextWorldZoneTrackerAttempt = DateTime.UtcNow.AddSeconds(2);
                }
                catch (Exception ex)
                {
                    logger.Log($"Biome world-zone fallback failed: {ex.GetBaseException().Message}");
                }
                finally
                {
                    worldZoneTrackerRefreshTask = null;
                }
            }

            if (worldZoneTracker == IntPtr.Zero
                && worldZoneTrackerRefreshTask == null
                && DateTime.UtcNow >= nextWorldZoneTrackerAttempt)
            {
                IUnrealHelper helper = unrealHelper;
                nextWorldZoneTrackerAttempt = DateTime.UtcNow.AddSeconds(2);
                worldZoneTrackerRefreshTask = Task.Run(() => FindWorldZoneTracker(helper));
            }
        }

        private void InitBiomeProbe(IUnrealHelper helper)
        {
            try
            {
                IntPtr player = currentPlayerCharacterAddress;
                if (player == IntPtr.Zero)
                    throw new InvalidOperationException("player character not found");

                string className = helper.GetUObjectClassName(player);
                var playerCharacter = new PlayerCharacterMatch(player, className);
                SetPlayerCharacterPointer(playerCharacter);

                int volumeTrackerOffset = GetUnrealFieldOffset("SN2PlayerCharacter", SN2PlayerCharacter_VolumeTracker, "VolumeTracker");
                playerVolumeTracker = game.Read<IntPtr>(player + volumeTrackerOffset);

                if (playerVolumeTracker == IntPtr.Zero && worldZoneTracker == IntPtr.Zero)
                    throw new InvalidOperationException("player volume tracker not found");

                logger.Log($"Biome probe initialized: class={playerCharacterClassName} player={player.ToString("X")} volumeTracker={playerVolumeTracker.ToString("X")} worldZoneTracker={worldZoneTracker.ToString("X")}");
            }
            catch (Exception ex)
            {
                logger.Log($"Biome probe not initialized: {ex.Message}");
                playerVolumeTracker = IntPtr.Zero;
                worldZoneTracker = IntPtr.Zero;
                biomeBaselineInitialized = false;
                nextBiomeProbeAttempt = DateTime.Now.AddSeconds(2);
            }
        }

        private IntPtr FindWorldZoneTracker(IUnrealHelper helper)
        {
            if (helper == null)
                return IntPtr.Zero;

            try
            {
                int zonesOffset = GetUnrealFieldOffset("UWEWorldZoneTrackerSubsystem", UUWEWorldZoneTrackerSubsystem_Zones, "Zones");
                foreach (IntPtr tracker in helper.FindLiveUObjects("UWEWorldZoneTrackerSubsystem", 16))
                {
                    if (tracker == IntPtr.Zero)
                        continue;

                    if (ReadPointerArray(tracker + zonesOffset, 4096).Count > 0)
                        return tracker;
                }

                return helper.FindLiveUObject("UWEWorldZoneTrackerSubsystem");
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private void SetPlayerCharacterPointer(PlayerCharacterMatch playerCharacter)
        {
            string className = string.IsNullOrEmpty(playerCharacter.ClassName) ? PlayerCharacterClassNames[0] : playerCharacter.ClassName;
            playerCharacterClassName = className;

            if (playerCharacterPointer == null || playerCharacterPointer.ClassName != className && playerInventoryComponent == null && playerEquipmentComponent == null)
                playerCharacterPointer = new UnrealObjectPointer(game, unrealHelper, className, playerCharacter.Pointer);
            else
                playerCharacterPointer.SetBase(playerCharacter.Pointer);
        }

        private void UpdateBiome()
        {
            if (EnableBiomeDiscoveryLogs)
                UpdateBiomeDiscoveryLogs();

            if (playerVolumeTracker == IntPtr.Zero && worldZoneTracker == IntPtr.Zero)
                return;

            try
            {
                BiomeReadResult readResult = default(BiomeReadResult);
                bool read = false;

                // The player's VolumeTracker is the game's authoritative current
                // biome state. World-zone geometry is only a fallback when that
                // tracker has no usable biome yet.
                if (playerVolumeTracker != IntPtr.Zero)
                {
                    readResult = ReadBiomeFromVolumeTracker(playerVolumeTracker);
                    read = readResult.Biome != Biome.None;
                }

                if (!read)
                    read = TryReadBiomeFromWorldZones(out readResult);

                if (!read)
                    return;

                LogBiomeProbeState(readResult);
                bool firstBiomeRead = !biomeBaselineInitialized;
                if (firstBiomeRead)
                {
                    CurrentBiome = readResult.Biome;
                    CurrentBiomeOld = readResult.Biome;
                    currentBiomeKey = readResult.Key;
                    currentBiomeKeyOld = readResult.Key;
                    biomeBaselineInitialized = true;

                    LogCurrentPlayerBiome(readResult, true);
                    logger.Log($"Biome baseline initialized: {FormatBiome(readResult.Biome, readResult.Key)}");
                    return;
                }

                CurrentBiomeOld = CurrentBiome;
                currentBiomeKeyOld = currentBiomeKey;
                CurrentBiome = readResult.Biome;
                currentBiomeKey = readResult.Key;

                bool changed = !string.Equals(currentBiomeKey, currentBiomeKeyOld, StringComparison.OrdinalIgnoreCase) || CurrentBiome != CurrentBiomeOld;
                if (changed)
                    logger.Log($"Biome change: {FormatBiome(CurrentBiomeOld, currentBiomeKeyOld)} -> {FormatBiome(CurrentBiome, currentBiomeKey)}");

                LogCurrentPlayerBiome(readResult, changed);
            }
            catch (Exception ex)
            {
                logger.Log($"Biome read failed: {ex.Message}");
                playerVolumeTracker = IntPtr.Zero;
                worldZoneTracker = IntPtr.Zero;
                biomeBaselineInitialized = false;
                nextBiomeProbeAttempt = DateTime.Now.AddSeconds(2);
            }
        }

        private bool ShouldHoldLastKnownBiome(BiomeReadResult readResult)
        {
            if (CurrentBiome == Biome.None || readResult.Biome != Biome.None)
                return false;

            if (readResult.Source.StartsWith("WorldZone", StringComparison.OrdinalIgnoreCase))
                return false;

            string key = NormalizeEnumCandidate(readResult.Key);
            if (string.IsNullOrEmpty(key) || key.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                return true;

            return key.StartsWith("Movement_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("Volume_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("EnvironmentType_", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryReadBiomeFromWorldZones(out BiomeReadResult readResult)
        {
            readResult = default(BiomeReadResult);

            if (worldZoneTracker == IntPtr.Zero)
                return false;

            IntPtr locationPawn = controlledPawnAddress != IntPtr.Zero
                ? controlledPawnAddress
                : GetPlayerCharacterAddress();
            IntPtr playerRoot = ReadActorRootComponent(locationPawn);
            if (!TryReadSceneComponentWorldLocation(playerRoot, out Vector3D playerLocation))
                return false;

            int zonesOffset = GetUnrealFieldOffset("UWEWorldZoneTrackerSubsystem", UUWEWorldZoneTrackerSubsystem_Zones, "Zones");
            List<IntPtr> zones = ReadPointerArray(worldZoneTracker + zonesOffset, 4096);
            if (zones.Count == 0)
            {
                worldZoneTracker = FindWorldZoneTracker(unrealHelper);
                zones = worldZoneTracker == IntPtr.Zero ? new List<IntPtr>() : ReadPointerArray(worldZoneTracker + zonesOffset, 4096);
            }

            double bestSize = double.MaxValue;
            BiomeReadResult bestResult = default(BiomeReadResult);
            double bestHorizontalSize = double.MaxValue;
            BiomeReadResult bestHorizontalResult = default(BiomeReadResult);

            foreach (IntPtr zone in zones)
            {
                if (TryReadWorldZoneBiome(zone, playerLocation, false, out BiomeReadResult zoneResult, out double zoneSize))
                {
                    if (zoneSize < bestSize)
                    {
                        bestSize = zoneSize;
                        bestResult = zoneResult;
                    }
                }

                if (TryReadWorldZoneBiome(zone, playerLocation, true, out BiomeReadResult horizontalZoneResult, out double horizontalZoneSize))
                {
                    if (horizontalZoneSize < bestHorizontalSize)
                    {
                        bestHorizontalSize = horizontalZoneSize;
                        bestHorizontalResult = horizontalZoneResult;
                    }
                }
            }

            if (bestSize != double.MaxValue)
            {
                readResult = bestResult;
                return true;
            }

            if (bestHorizontalSize == double.MaxValue)
                return false;

            readResult = bestHorizontalResult;
            return true;
        }

        private IntPtr GetPlayerCharacterAddress()
        {
            try
            {
                return playerCharacterPointer == null ? IntPtr.Zero : playerCharacterPointer.New;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private bool TryReadWorldZoneBiome(IntPtr zone, Vector3D playerLocation, bool horizontalOnly, out BiomeReadResult readResult, out double zoneSize)
        {
            readResult = default(BiomeReadResult);
            zoneSize = double.MaxValue;

            if (zone == IntPtr.Zero || !TryReadWorldZoneBounds(zone, playerLocation, horizontalOnly, out zoneSize))
                return false;

            if (!TryReadUObjectText(zone, out string zoneName, out string zonePath))
                return false;

            IntPtr regionAsset = ReadWorldZoneRegionAsset(zone);
            string regionTag = ReadWorldRegionTag(regionAsset);
            string regionName = string.Empty;
            string regionPath = string.Empty;

            if (regionAsset != IntPtr.Zero)
                TryReadUObjectText(regionAsset, out regionName, out regionPath);

            string objectName = string.IsNullOrWhiteSpace(regionName) ? zoneName : regionName;
            string objectPath = string.IsNullOrWhiteSpace(regionPath) ? zonePath : regionPath;
            TryMapWorldRegion(objectName, objectPath, regionTag, out Biome biome);

            string key = biome == Biome.None
                ? BestEnumCandidate(WorldRegionNameCandidates(objectName, objectPath, regionTag))
                : biome.ToString();

            var tags = new List<string>();
            AddUniqueTag(tags, regionTag);

            readResult = new BiomeReadResult(biome, key, objectName, objectPath, zone, regionAsset, -1, tags, new List<IntPtr>(), horizontalOnly ? "WorldZone2D" : "WorldZone");
            return true;
        }

        private bool TryReadWorldZoneBounds(IntPtr zone, Vector3D point, bool horizontalOnly, out double zoneSize)
        {
            zoneSize = double.MaxValue;

            string className = GetUObjectClassName(zone);
            if (string.IsNullOrWhiteSpace(className))
                return false;

            IntPtr shapeComponent = ReadWorldZoneShapeComponent(zone);
            if (shapeComponent == IntPtr.Zero)
                return false;

            if (!TryReadSceneComponentWorldLocation(shapeComponent, out Vector3D center))
                return false;

            Vector3D scale = ReadSceneComponentScale(shapeComponent);

            if (IsUObjectClass(className, "UWEBoxWorldZone"))
            {
                Vector3D extent = ReadVector3D(shapeComponent + UBoxComponent_BoxExtent);
                extent = new Vector3D(Math.Abs(extent.X * scale.X), Math.Abs(extent.Y * scale.Y), Math.Abs(extent.Z * scale.Z));
                if (!IsPlausibleExtent(extent))
                    return false;

                Vector3D delta = point - center;
                const double tolerance = 1.0;
                if (Math.Abs(delta.X) > extent.X + tolerance || Math.Abs(delta.Y) > extent.Y + tolerance)
                    return false;

                if (!horizontalOnly && Math.Abs(delta.Z) > extent.Z + tolerance)
                    return false;

                zoneSize = horizontalOnly
                    ? Math.Max(1.0, extent.X) * Math.Max(1.0, extent.Y)
                    : Math.Max(1.0, extent.X) * Math.Max(1.0, extent.Y) * Math.Max(1.0, extent.Z);
                return true;
            }

            if (IsUObjectClass(className, "UWESphereWorldZone"))
            {
                float radius = game.Read<float>(shapeComponent + USphereComponent_SphereRadius);
                double scaledRadius = radius * MaxAbs(scale.X, scale.Y, scale.Z);
                if (!IsFinite(scaledRadius) || scaledRadius <= 0 || scaledRadius > 10000000)
                    return false;

                Vector3D delta = point - center;
                double distanceSquared = horizontalOnly
                    ? delta.X * delta.X + delta.Y * delta.Y
                    : delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;
                if (distanceSquared > scaledRadius * scaledRadius)
                    return false;

                zoneSize = horizontalOnly
                    ? scaledRadius * scaledRadius
                    : scaledRadius * scaledRadius * scaledRadius;
                return true;
            }

            return false;
        }

        private IntPtr ReadWorldZoneShapeComponent(IntPtr zone)
        {
            try
            {
                IntPtr component = game.Read<IntPtr>(zone + AUWEWorldZone_ShapeComponent);
                return component == IntPtr.Zero ? ReadActorRootComponent(zone) : component;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private bool TryReadSceneComponentWorldLocation(IntPtr sceneComponent, out Vector3D location, int depth = 0)
        {
            location = default(Vector3D);

            if (sceneComponent == IntPtr.Zero || depth > 8)
                return false;

            try
            {
                Vector3D relativeLocation = ReadVector3D(sceneComponent + USceneComponent_RelativeLocation);
                if (!IsPlausibleWorldVector(relativeLocation))
                    return false;

                IntPtr parent = game.Read<IntPtr>(sceneComponent + USceneComponent_AttachParent);
                if (parent != IntPtr.Zero
                    && parent != sceneComponent
                    && TryReadSceneComponentWorldLocation(parent, out Vector3D parentLocation, depth + 1))
                {
                    location = parentLocation + relativeLocation;
                    return IsPlausibleWorldVector(location);
                }

                location = relativeLocation;
                return true;
            }
            catch
            {
                location = default(Vector3D);
                return false;
            }
        }

        private Vector3D ReadSceneComponentScale(IntPtr sceneComponent)
        {
            try
            {
                Vector3D scale = ReadVector3D(sceneComponent + USceneComponent_RelativeScale3D);
                if (IsFinite(scale.X) && IsFinite(scale.Y) && IsFinite(scale.Z)
                    && Math.Abs(scale.X) > 0.0001 && Math.Abs(scale.Y) > 0.0001 && Math.Abs(scale.Z) > 0.0001
                    && Math.Abs(scale.X) < 1000 && Math.Abs(scale.Y) < 1000 && Math.Abs(scale.Z) < 1000)
                    return scale;
            }
            catch
            {
            }

            return Vector3D.One;
        }

        private Vector3D ReadVector3D(IntPtr address)
        {
            return new Vector3D(
                game.Read<double>(address),
                game.Read<double>(address + 0x08),
                game.Read<double>(address + 0x10));
        }

        private static bool IsPlausibleWorldVector(Vector3D vector)
        {
            return IsFinite(vector.X) && IsFinite(vector.Y) && IsFinite(vector.Z)
                && Math.Abs(vector.X) < 10000000
                && Math.Abs(vector.Y) < 10000000
                && Math.Abs(vector.Z) < 10000000;
        }

        private static bool IsPlausibleExtent(Vector3D vector)
        {
            return IsFinite(vector.X) && IsFinite(vector.Y) && IsFinite(vector.Z)
                && vector.X > 0 && vector.Y > 0 && vector.Z > 0
                && vector.X < 10000000 && vector.Y < 10000000 && vector.Z < 10000000;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double MaxAbs(double x, double y, double z)
        {
            return Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
        }

        private BiomeReadResult ReadBiomeFromVolumeTracker(IntPtr volumeTracker)
        {
            int queryResultOffset = GetUnrealFieldOffset("VolumeTrackerComponent", UVolumeTrackerComponent_VolumeQueryResult, "VolumeQueryResult");
            IntPtr queryResult = volumeTracker + queryResultOffset;

            IntPtr currentVolume = ReadScriptInterfaceObject(queryResult + FVolumeQueryResult_CurrentVolume);
            IntPtr outerVolume = ReadScriptInterfaceObject(queryResult + FVolumeQueryResult_OuterVolume);
            int currentVolumeType = game.Read<byte>(queryResult + FVolumeQueryResult_CurrentVolumeType);
            List<string> tags = ReadGameplayTagContainer(queryResult + FVolumeQueryResult_CurrentTags, 64);
            List<IntPtr> volumes = ReadScriptInterfaceArray(queryResult + FVolumeQueryResult_Volumes, 128);

            if (TryReadBiomeObject(currentVolume, out Biome biome, out string key, out string objectName, out string objectPath, tags))
                return new BiomeReadResult(biome, key, objectName, objectPath, currentVolume, outerVolume, currentVolumeType, tags, volumes);

            foreach (IntPtr volume in volumes)
                if (TryReadBiomeObject(volume, out biome, out key, out objectName, out objectPath, tags))
                    return new BiomeReadResult(biome, key, objectName, objectPath, currentVolume, outerVolume, currentVolumeType, tags, volumes);

            foreach (string tag in tags)
            {
                foreach (string candidate in ExpandedBiomeNameCandidates(tag))
                {
                    if (TryParseNamedEnum(candidate, out biome) && biome != Biome.None)
                    {
                        key = NormalizeEnumCandidate(candidate);
                        return new BiomeReadResult(biome, key, string.Empty, string.Empty, currentVolume, outerVolume, currentVolumeType, tags, volumes);
                    }
                }
            }

            key = BestEnumCandidate(tags.SelectMany(ExpandedBiomeNameCandidates));
            if (key == "Unknown" && currentVolume != IntPtr.Zero)
            {
                if (TryReadUObjectText(currentVolume, out objectName, out objectPath))
                    key = BestEnumCandidate(BiomeNameCandidates(objectName, objectPath, tags));
            }
            else
            {
                objectName = objectPath = string.Empty;
            }

            return new BiomeReadResult(Biome.None, key, objectName, objectPath, currentVolume, outerVolume, currentVolumeType, tags, volumes);
        }

        private IntPtr ReadScriptInterfaceObject(IntPtr interfaceAddress)
        {
            try
            {
                return game.Read<IntPtr>(interfaceAddress);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private List<IntPtr> ReadScriptInterfaceArray(IntPtr arrayAddress, int maxElements)
        {
            var result = new List<IntPtr>();

            try
            {
                IntPtr dataPtr = game.Read<IntPtr>(arrayAddress);
                int num = game.Read<int>(arrayAddress + game.PointerSize);
                int max = game.Read<int>(arrayAddress + game.PointerSize + 4);

                if (!IsPlausibleArray(dataPtr, num, max, maxElements))
                    return result;

                var seen = new HashSet<IntPtr>();
                for (int i = 0; i < num; i++)
                {
                    IntPtr ptr = game.Read<IntPtr>(dataPtr + i * 0x10);
                    if (ptr != IntPtr.Zero && seen.Add(ptr))
                        result.Add(ptr);
                }
            }
            catch
            {
            }

            return result;
        }

        private List<string> ReadGameplayTagContainer(IntPtr containerAddress, int maxElements)
        {
            var result = new List<string>();

            foreach (string tag in ReadGameplayTagArray(containerAddress, maxElements))
                AddUniqueTag(result, tag);

            foreach (string tag in ReadGameplayTagArray(containerAddress + 0x10, maxElements))
                AddUniqueTag(result, tag);

            return result;
        }

        private IEnumerable<string> ReadGameplayTagArray(IntPtr arrayAddress, int maxElements)
        {
            try
            {
                IntPtr dataPtr = game.Read<IntPtr>(arrayAddress);
                int num = game.Read<int>(arrayAddress + game.PointerSize);
                int max = game.Read<int>(arrayAddress + game.PointerSize + 4);

                if (!IsPlausibleArray(dataPtr, num, max, maxElements))
                    yield break;

                for (int i = 0; i < num; i++)
                {
                    string tag = ReadFNameString(dataPtr + i * 0x08);
                    if (!string.IsNullOrWhiteSpace(tag))
                        yield return tag;
                }
            }
            finally
            {
            }
        }

        private static void AddUniqueTag(List<string> tags, string tag)
        {
            if (!string.IsNullOrWhiteSpace(tag) && !tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                tags.Add(tag);
        }

        private bool TryReadBiomeObject(IntPtr biomeObject, out Biome biome, out string key, out string objectName, out string objectPath, IEnumerable<string> extraTags = null)
        {
            biome = Biome.None;
            key = string.Empty;
            objectName = string.Empty;
            objectPath = string.Empty;

            if (biomeObject == IntPtr.Zero || unrealHelper == null)
                return false;

            foreach (IntPtr relatedObject in GetBiomeRelatedObjects(biomeObject))
            {
                if (!TryReadUObjectText(relatedObject, out string relatedName, out string relatedPath))
                    continue;

                if (string.IsNullOrEmpty(objectName))
                {
                    objectName = relatedName;
                    objectPath = relatedPath;
                }

                var tags = new List<string>();
                if (extraTags != null)
                    foreach (string tag in extraTags)
                        AddUniqueTag(tags, tag);

                foreach (string tag in ReadTrackedVolumeTags(relatedObject))
                    AddUniqueTag(tags, tag);

                bool mapped = TryMapBiome(relatedName, relatedPath, tags, out Biome relatedBiome);
                string candidate = mapped ? relatedBiome.ToString() : BestEnumCandidate(BiomeNameCandidates(relatedName, relatedPath, tags));
                if (string.IsNullOrWhiteSpace(key) || key == "Unknown")
                    key = candidate;

                LogBiomeObject(relatedObject, relatedName, relatedPath, tags, mapped ? relatedBiome : Biome.None, "read");

                if (mapped)
                {
                    biome = relatedBiome;
                    key = relatedBiome.ToString();
                    objectName = relatedName;
                    objectPath = relatedPath;
                    return true;
                }
            }

            return false;
        }

        private bool TryReadUObjectText(IntPtr uobject, out string objectName, out string objectPath)
        {
            objectName = string.Empty;
            objectPath = string.Empty;

            if (uobject == IntPtr.Zero || unrealHelper == null)
                return false;

            try
            {
                objectName = unrealHelper.GetUObjectName(uobject);
                objectPath = unrealHelper.GetUObjectPath(uobject);
                return IsReadableUObjectText(objectName) && IsReadableUObjectText(objectPath);
            }
            catch
            {
                objectName = string.Empty;
                objectPath = string.Empty;
                return false;
            }
        }

        private IEnumerable<IntPtr> GetBiomeRelatedObjects(IntPtr biomeObject)
        {
            var seen = new HashSet<IntPtr>();

            foreach (IntPtr relatedObject in GetBiomeRelatedObjectsCore(biomeObject))
                if (relatedObject != IntPtr.Zero && seen.Add(relatedObject))
                    yield return relatedObject;
        }

        private IEnumerable<IntPtr> GetBiomeRelatedObjectsCore(IntPtr biomeObject)
        {
            yield return biomeObject;

            IntPtr outer = ReadUObjectOuter(biomeObject);
            if (outer != IntPtr.Zero)
                yield return outer;

            IntPtr biomeComponent = ReadBiomeVolumeComponent(biomeObject);
            if (biomeComponent != IntPtr.Zero)
                yield return biomeComponent;

            IntPtr outerBiomeComponent = ReadBiomeVolumeComponent(outer);
            if (outerBiomeComponent != IntPtr.Zero)
                yield return outerBiomeComponent;

            foreach (IntPtr child in ReadSceneComponentChildren(ReadActorRootComponent(biomeObject)))
                yield return child;

            foreach (IntPtr child in ReadSceneComponentChildren(ReadActorRootComponent(outer)))
                yield return child;
        }

        private IntPtr ReadUObjectOuter(IntPtr uobject)
        {
            if (uobject == IntPtr.Zero)
                return IntPtr.Zero;

            try
            {
                return game.Read<IntPtr>(uobject + 0x20);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private IntPtr ReadBiomeVolumeComponent(IntPtr actor)
        {
            if (actor == IntPtr.Zero)
                return IntPtr.Zero;

            try
            {
                return game.Read<IntPtr>(actor + AUWEWaterBiomeRegionActor_BiomeVolumeComponent);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private IntPtr ReadActorRootComponent(IntPtr actor)
        {
            if (actor == IntPtr.Zero)
                return IntPtr.Zero;

            try
            {
                return game.Read<IntPtr>(actor + AActor_RootComponent);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private IEnumerable<IntPtr> ReadSceneComponentChildren(IntPtr sceneComponent)
        {
            if (sceneComponent == IntPtr.Zero)
                yield break;

            foreach (IntPtr child in ReadPointerArray(sceneComponent + USceneComponent_AttachChildren, 128))
                yield return child;
        }

        private IEnumerable<string> ReadTrackedVolumeTags(IntPtr volumeObject)
        {
            string className = GetUObjectClassName(volumeObject);
            if (string.IsNullOrWhiteSpace(className))
                yield break;

            foreach (string tag in ReadFNameArray(volumeObject + AActor_Tags, 64))
                yield return tag;

            int volumeDataOffset = GetTrackedVolumeDataOffset(className);
            if (volumeDataOffset < 0)
                yield break;

            foreach (string tag in ReadTrackedVolumeDataTags(volumeObject + volumeDataOffset))
                yield return tag;
        }

        private IEnumerable<string> ReadTrackedVolumeDataTags(IntPtr volumeDataAddress)
        {
            foreach (string tag in ReadGameplayTagContainer(volumeDataAddress + FTrackedVolumeData_TagsToAdd, 64))
                yield return tag;

            foreach (string tag in ReadGameplayTagContainer(volumeDataAddress + FTrackedVolumeData_TagsToRemove, 64))
                yield return tag;

            foreach (string tag in ReadGameplayTagContainer(volumeDataAddress + FTrackedVolumeData_VolumeTags, 64))
                yield return tag;

            foreach (string tag in ReadGameplayTagContainer(volumeDataAddress + FTrackedVolumeData_GASLooseTags, 64))
                yield return tag;

            string environmentType = ReadFNameString(volumeDataAddress + FTrackedVolumeData_EnvironmentType);
            if (!string.IsNullOrWhiteSpace(environmentType))
                yield return environmentType;
        }

        private int GetTrackedVolumeDataOffset(string className)
        {
            if (IsUObjectClass(className, "BP_UWEOceanBiomeRegionActor"))
                return GetUnrealFieldOffset(className, ABP_UWEOceanBiomeRegionActor_VolumeData, "VolumeData", "Volume_Data");

            if (IsUObjectClass(className, "BrushVolumeComponent"))
                return GetUnrealFieldOffset(className, UBrushVolumeComponent_VolumeData, "VolumeData");

            if (IsUObjectClass(className, "InstancedMeshVolumeComponent"))
                return GetUnrealFieldOffset(className, UInstancedMeshVolumeComponent_VolumeData, "VolumeData");

            if (IsUObjectClass(className, "SplineMeshVolumeComponent"))
                return GetUnrealFieldOffset(className, USplineMeshVolumeComponent_VolumeData, "VolumeData");

            if (IsUObjectClass(className, "StaticMeshVolumeComponent"))
                return GetUnrealFieldOffset(className, UStaticMeshVolumeComponent_VolumeData, "VolumeData");

            if (IsUObjectClass(className, "UWEVolumeActorComponent"))
                return GetUnrealFieldOffset(className, UUWEVolumeActorComponent_VolumeData, "VolumeData");

            if (IsUObjectClass(className, "ShapeVolumeComponent")
                || IsUObjectClass(className, "BoxVolumeComponent")
                || IsUObjectClass(className, "CapsuleVolumeComponent")
                || IsUObjectClass(className, "SphereVolumeComponent"))
                return GetUnrealFieldOffset(className, UShapeVolumeComponent_VolumeData, "VolumeData");

            return GetUnrealFieldOffset(className, -1, "VolumeData", "Volume_Data", "BaseOverlapVolumeData");
        }

        private IEnumerable<string> ReadFNameArray(IntPtr arrayAddress, int maxElements)
        {
            try
            {
                IntPtr dataPtr = game.Read<IntPtr>(arrayAddress);
                int num = game.Read<int>(arrayAddress + game.PointerSize);
                int max = game.Read<int>(arrayAddress + game.PointerSize + 4);

                if (!IsPlausibleArray(dataPtr, num, max, maxElements))
                    yield break;

                for (int i = 0; i < num; i++)
                {
                    string name = ReadFNameString(dataPtr + i * 0x08);
                    if (!string.IsNullOrWhiteSpace(name))
                        yield return name;
                }
            }
            finally
            {
            }
        }

        private void TryLogWorldZone(IntPtr zone, string source)
        {
            if (!EnableBiomeDiscoveryLogs || zone == IntPtr.Zero)
                return;

            try
            {
                if (!TryReadUObjectText(zone, out string zoneName, out string zonePath))
                    return;

                IntPtr regionAsset = ReadWorldZoneRegionAsset(zone);
                string regionTag = ReadWorldRegionTag(regionAsset);
                string regionName = string.Empty;
                string regionPath = string.Empty;

                if (regionAsset != IntPtr.Zero)
                {
                    TryLogWorldRegionAsset(regionAsset, $"{source}.RegionAsset");
                    TryReadUObjectText(regionAsset, out regionName, out regionPath);
                }

                string candidate = BestEnumCandidate(WorldRegionNameCandidates(
                    string.IsNullOrWhiteSpace(regionName) ? zoneName : regionName,
                    string.IsNullOrWhiteSpace(regionPath) ? zonePath : regionPath,
                    regionTag));
                TryMapWorldRegion(regionName, regionPath, regionTag, out Biome biome);
                LogWorldRegionObject(source, zone, zoneName, zonePath, regionTag, candidate, biome, regionAsset, regionName, regionPath);
            }
            catch
            {
            }
        }

        private bool TryLogWorldRegionAsset(IntPtr regionAsset, string source)
        {
            if (!EnableBiomeDiscoveryLogs || regionAsset == IntPtr.Zero)
                return false;

            try
            {
                if (!TryReadUObjectText(regionAsset, out string objectName, out string objectPath))
                    return false;

                string regionTag = ReadWorldRegionTag(regionAsset);
                string candidate = BestEnumCandidate(WorldRegionNameCandidates(objectName, objectPath, regionTag));
                TryMapWorldRegion(objectName, objectPath, regionTag, out Biome biome);
                LogWorldRegionObject(source, regionAsset, objectName, objectPath, regionTag, candidate, biome, IntPtr.Zero, string.Empty, string.Empty);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private IntPtr ReadWorldZoneRegionAsset(IntPtr zone)
        {
            try
            {
                return zone == IntPtr.Zero ? IntPtr.Zero : game.Read<IntPtr>(zone + AUWEWorldZone_RegionAsset);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private string ReadWorldRegionTag(IntPtr regionAsset)
        {
            if (regionAsset == IntPtr.Zero)
                return string.Empty;

            return ReadFNameString(regionAsset + UUWEWorldRegionDataAsset_RegionTag);
        }

        private void LogWorldRegionObject(string source, IntPtr obj, string objectName, string objectPath, string regionTag, string candidate, Biome mappedBiome, IntPtr regionAsset, string regionName, string regionPath)
        {
            if (!EnableBiomeDiscoveryLogs)
                return;

            string key = $"BiomeRegion:{source}:{obj.ToString("X")}:{objectPath}:{objectName}:{regionTag}:{regionAsset.ToString("X")}";
            if (!loggedBiomeAssets.Add(key))
                return;

            string mapped = mappedBiome == Biome.None ? "<new>" : mappedBiome.ToString();
            string regionAssetText = regionAsset == IntPtr.Zero ? string.Empty : $" regionAsset={regionAsset.ToString("X")}";
            string regionNameText = string.IsNullOrWhiteSpace(regionName) ? string.Empty : $" regionName={regionName}";
            string regionPathText = string.IsNullOrWhiteSpace(regionPath) ? string.Empty : $" regionPath={regionPath}";
            string regionTagText = string.IsNullOrWhiteSpace(regionTag) ? string.Empty : $" regionTag={regionTag}";
            logger.Log($"[EnumDiscovery][BiomeRegion] source={source} candidate={candidate} mapped={mapped} object={obj.ToString("X")} class={GetUObjectClassName(obj)} name={objectName} path={objectPath}{regionTagText}{regionAssetText}{regionNameText}{regionPathText}");
        }

        private bool TryMapWorldRegion(string objectName, string objectPath, string regionTag, out Biome biome)
        {
            foreach (string candidate in WorldRegionNameCandidates(objectName, objectPath, regionTag))
            {
                if (TryParseNamedEnum(candidate, out biome) && biome != Biome.None)
                    return true;
            }

            biome = Biome.None;
            return false;
        }

        private IEnumerable<string> WorldRegionNameCandidates(string objectName, string objectPath, string regionTag)
        {
            foreach (string value in new[] { objectName, LastPathSegment(objectPath), ParentPathSegment(objectPath) })
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                foreach (string candidate in ExpandedBiomeNameCandidates(StripBiomeName(value)))
                    yield return candidate;

                foreach (string candidate in ExpandedBiomeNameCandidates(value))
                    yield return candidate;
            }

            if (!string.IsNullOrWhiteSpace(regionTag))
            {
                foreach (string candidate in ExpandedBiomeNameCandidates(LastQualifiedSegment(regionTag)))
                    yield return candidate;

                foreach (string candidate in ExpandedBiomeNameCandidates(regionTag))
                    yield return candidate;
            }
        }

        private void TryLogBiomeObject(IntPtr biomeObject, string source)
        {
            if (!EnableBiomeDiscoveryLogs || biomeObject == IntPtr.Zero)
                return;

            try
            {
                foreach (IntPtr relatedObject in GetBiomeRelatedObjects(biomeObject))
                {
                    if (!TryReadUObjectText(relatedObject, out string objectName, out string objectPath))
                        continue;

                    var tags = ReadTrackedVolumeTags(relatedObject).ToList();
                    TryMapBiome(objectName, objectPath, tags, out Biome biome);
                    LogBiomeObject(relatedObject, objectName, objectPath, tags, biome, source);
                }
            }
            catch
            {
            }
        }

        private void LogBiomeObject(IntPtr biomeObject, string objectName, string objectPath, IEnumerable<string> tags, Biome mappedBiome, string source)
        {
            if (!EnableBiomeDiscoveryLogs)
                return;

            var tagList = tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
            string key = $"{source}:{biomeObject.ToString("X")}:{objectPath}:{objectName}:{string.Join("|", tagList)}";
            if (string.IsNullOrWhiteSpace(key) || !loggedBiomeAssets.Add(key))
                return;

            string candidate = BestEnumCandidate(BiomeNameCandidates(objectName, objectPath, tagList));
            string mapped = mappedBiome == Biome.None ? "<new>" : mappedBiome.ToString();
            string tagText = tagList.Count == 0 ? string.Empty : $" tags=[{string.Join(", ", tagList)}]";
            logger.Log($"[EnumDiscovery][Biome] source={source} candidate={candidate} mapped={mapped} object={biomeObject.ToString("X")} class={GetUObjectClassName(biomeObject)} name={objectName} path={objectPath}{tagText}");
        }

        private void LogBiomeProbeState(BiomeReadResult readResult)
        {
            if (!EnableBiomeProbeLogs)
                return;

            bool isWorldZoneRead = readResult.Source.StartsWith("WorldZone", StringComparison.OrdinalIgnoreCase);
            if (!isWorldZoneRead)
            {
                foreach (IntPtr volume in readResult.Volumes)
                    TryLogBiomeObject(volume, "VolumeQueryResult.Volumes");

                TryLogBiomeObject(readResult.CurrentVolume, "VolumeQueryResult.CurrentVolume");
                TryLogBiomeObject(readResult.OuterVolume, "VolumeQueryResult.OuterVolume");
            }
            else
            {
                TryLogWorldZone(readResult.CurrentVolume, "read");
            }

            string state = $"{readResult.Source}:{playerVolumeTracker.ToString("X")}:{worldZoneTracker.ToString("X")}:{readResult.CurrentVolume.ToString("X")}:{readResult.OuterVolume.ToString("X")}:{readResult.CurrentVolumeType}:{readResult.Key}:{string.Join("|", readResult.Tags)}:{readResult.Volumes.Count}";
            if (state == lastBiomeProbeState)
                return;

            lastBiomeProbeState = state;
            string tags = readResult.Tags.Count == 0 ? string.Empty : $" tags=[{string.Join(", ", readResult.Tags)}]";
            if (isWorldZoneRead)
                logger.Log($"Biome probe: source={readResult.Source} worldZoneTracker={worldZoneTracker.ToString("X")} zone={readResult.CurrentVolume.ToString("X")} regionAsset={readResult.OuterVolume.ToString("X")} mapped={FormatBiome(readResult.Biome, readResult.Key)} name={readResult.ObjectName} path={readResult.ObjectPath}{tags}");
            else
                logger.Log($"Biome probe: source=VolumeTracker tracker={playerVolumeTracker.ToString("X")} currentVolume={readResult.CurrentVolume.ToString("X")} outerVolume={readResult.OuterVolume.ToString("X")} currentType={readResult.CurrentVolumeType} volumes={readResult.Volumes.Count} mapped={FormatBiome(readResult.Biome, readResult.Key)} name={readResult.ObjectName} path={readResult.ObjectPath}{tags}");
        }

        private void LogCurrentPlayerBiome(BiomeReadResult readResult, bool force = false)
        {
            if (!EnableBiomeProbeLogs)
                return;

            if (!force && DateTime.Now < nextCurrentBiomeLog)
                return;

            nextCurrentBiomeLog = DateTime.Now.Add(CurrentBiomeLogInterval);

            string source = string.IsNullOrWhiteSpace(readResult.Source) ? "Unknown" : readResult.Source;
            string name = string.IsNullOrWhiteSpace(readResult.ObjectName) ? string.Empty : $" name={readResult.ObjectName}";
            string path = string.IsNullOrWhiteSpace(readResult.ObjectPath) ? string.Empty : $" path={readResult.ObjectPath}";
            string tags = readResult.Tags.Count == 0 ? string.Empty : $" tags=[{string.Join(", ", readResult.Tags)}]";

            logger.Log($"Current player biome: {FormatBiome(readResult.Biome, readResult.Key)} source={source}{name}{path}{tags}");
        }

        private bool TryMapBiome(string objectName, string objectPath, IEnumerable<string> tags, out Biome biome)
        {
            foreach (string candidate in BiomeNameCandidates(objectName, objectPath, tags))
            {
                if (TryParseNamedEnum(candidate, out biome) && biome != Biome.None)
                    return true;
            }

            biome = Biome.None;
            return false;
        }

        private IEnumerable<string> BiomeNameCandidates(string objectName, string objectPath, IEnumerable<string> tags = null)
        {
            foreach (string candidate in ParentQualifiedBiomeNameCandidates(objectPath))
                yield return candidate;

            foreach (string value in new[] { objectName, LastPathSegment(objectPath), ParentPathSegment(objectPath) })
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                foreach (string candidate in ExpandedBiomeNameCandidates(value))
                    yield return candidate;
            }

            if (tags == null)
                yield break;

            foreach (string tag in PrioritizeBiomeTags(tags))
                foreach (string candidate in ExpandedBiomeNameCandidates(tag))
                    yield return candidate;
        }

        private static IEnumerable<string> PrioritizeBiomeTags(IEnumerable<string> tags)
        {
            var tagList = tags?
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            foreach (string tag in tagList.Where(tag => tag.StartsWith("Volume.", StringComparison.OrdinalIgnoreCase)))
                yield return tag;

            foreach (string tag in tagList.Where(tag => tag.StartsWith("EnvironmentType.", StringComparison.OrdinalIgnoreCase)))
                yield return tag;

            foreach (string tag in tagList.Where(tag =>
                !tag.StartsWith("Volume.", StringComparison.OrdinalIgnoreCase)
                && !tag.StartsWith("EnvironmentType.", StringComparison.OrdinalIgnoreCase)))
                yield return tag;
        }

        private IEnumerable<string> ParentQualifiedBiomeNameCandidates(string objectPath)
        {
            if (string.IsNullOrWhiteSpace(objectPath))
                yield break;

            string[] parts = objectPath.Split(new[] { '/', '\\', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                yield break;

            string parent = NormalizeEnumCandidate(StripBiomeName(parts[parts.Length - 2]));
            string name = NormalizeEnumCandidate(StripBiomeName(parts[parts.Length - 1]));
            if (!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(name))
                yield return $"{parent}_{name}";

            if (!string.IsNullOrWhiteSpace(name))
                yield return name;
        }

        private static IEnumerable<string> ExpandedBiomeNameCandidates(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                yield break;

            yield return value;

            string stripped = StripBiomeName(value);
            yield return stripped;
            yield return NormalizeEnumCandidate(stripped);
            yield return stripped.Replace("_", "").Replace("-", "").Replace(".", "");

            string[] parts = stripped.Split(new[] { '_', '-', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                string last = parts[parts.Length - 1];
                yield return last;
                yield return NormalizeEnumCandidate(last);
            }
        }

        private static string ParentPathSegment(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string[] parts = path.Split(new[] { '/', '\\', '.' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[parts.Length - 2] : string.Empty;
        }

        private static string LastQualifiedSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            string[] parts = value.Split(new[] { '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? value : parts[parts.Length - 1];
        }

        private static string StripBiomeName(string value)
        {
            string result = value;
            bool changed;
            do
            {
                changed = false;
                foreach (string prefix in new[] { "Biome_", "BIO_", "Region_", "RGN_", "WorldBiome_", "WorldRegion_", "UWEWorldBiome_", "UWEWorldRegion_", "UWEWorldBiomeDataAsset_", "UWEWorldRegionDataAsset_", "BP_UWEOceanBiomeRegionActor_", "BP_UWEWaterBiomeRegionActor_", "BP_", "ABP_", "DA_", "DAT_", "Data_" })
                {
                    if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        result = result.Substring(prefix.Length);
                        changed = true;
                        break;
                    }
                }

                foreach (string suffix in new[] { "_C", "_Biome", "Biome", "_Region", "Region", "_Volume", "Volume", "_Actor", "Actor", "_DataAsset", "DataAsset", "_Data", "_DA" })
                {
                    if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        result = result.Substring(0, result.Length - suffix.Length);
                        changed = true;
                        break;
                    }
                }
            }
            while (changed);

            return result;
        }

        private bool IsInBiome(Biome biome) =>
            biome == Biome.Any
                ? biomeBaselineInitialized && CurrentBiome != Biome.None
                : biome != Biome.None && CurrentBiome == biome;

        private bool HasEnteredBiome(BiomeSplit split)
        {
            if (split == null
                || !biomeBaselineInitialized
                || split.Biomes.Biome1 == Biome.None
                || split.Biomes.Biome2 == Biome.None
                || CurrentBiomeOld == CurrentBiome)
                return false;

            bool fromMatches = split.Biomes.Biome1 == Biome.Any || CurrentBiomeOld == split.Biomes.Biome1;
            bool toMatches = split.Biomes.Biome2 == Biome.Any || CurrentBiome == split.Biomes.Biome2;
            return fromMatches && toMatches;
        }

        private static string FormatBiome(Biome biome, string key)
        {
            if (biome != Biome.None)
                return biome.ToString();

            return string.IsNullOrWhiteSpace(key) ? Biome.None.ToString() : $"{Biome.None}({key})";
        }

        private void InitInventoryProbe(IUnrealHelper unrealHelper)
        {
            try
            {
                PlayerCharacterMatch playerCharacter = FindPlayerCharacter(unrealHelper);
                if (playerCharacter.Pointer == IntPtr.Zero)
                    throw new InvalidOperationException("player character not found");

                InitializeInventoryPointers(playerCharacter);
                UpdateInventoryStorageRefresh();

                IntPtr inventoryComponent = playerInventoryComponent.New;
                IntPtr toolbarComponent = playerToolbarComponent == null ? IntPtr.Zero : playerToolbarComponent.New;
                int inventoryId = inventoryComponent == IntPtr.Zero ? -1 : game.Read<int>(inventoryComponent + UWEInventoryComponent_InventoryId);
                logger.Log($"Inventory probe initialized: class={playerCharacterClassName} player={playerCharacterPointer.New.ToString("X")} inventoryComponent={inventoryComponent.ToString("X")} toolbarComponent={toolbarComponent.ToString("X")} inventoryId={inventoryId}");
            }
            catch (Exception ex)
            {
                logger.Log($"Inventory probe not initialized: {ex.Message}");
                playerCharacterPointer = null;
                playerInventoryComponent = null;
                playerEquipmentComponent = null;
                playerToolbarComponent = null;
                playerCharacterClassName = string.Empty;
                nextInventoryProbeAttempt = DateTime.Now.AddMilliseconds(InventoryProbeRetryMilliseconds);
            }
        }

        private void InitializeInventoryPointers(PlayerCharacterMatch playerCharacter)
        {
            string className = string.IsNullOrEmpty(playerCharacter.ClassName) ? PlayerCharacterClassNames[0] : playerCharacter.ClassName;
            playerCharacterClassName = className;
            playerCharacterPointer = new UnrealObjectPointer(game, unrealHelper, className, playerCharacter.Pointer);

            playerInventoryComponent = pointerFactory.Make<IntPtr>(playerCharacterPointer, "SN2PlayerCharacter", "InventoryComponent");
            playerEquipmentComponent = pointerFactory.Make<IntPtr>(playerCharacterPointer, "SN2PlayerCharacter", "EquippedItemsComponent");
            try
            {
                playerToolbarComponent = pointerFactory.Make<IntPtr>(playerCharacterPointer, "SN2PlayerCharacter", "ToolbarComponent");
            }
            catch (Exception ex)
            {
                playerToolbarComponent = null;
                logger.Log($"Toolbar probe not initialized: {ex.Message}");
            }
        }

        private PlayerCharacterMatch FindPlayerCharacter(IUnrealHelper helper)
        {
            int inventoryComponentOffset = GetUnrealFieldOffset("SN2PlayerCharacter", default, "InventoryComponent");
            string[] classNames = PlayerCharacterClassNames
                .Concat(WorldHudClassNames)
                .Concat(new[]
                {
                    "UWEInventoryStorage",
                    "UWECrafterComponent",
                    "BP_ProcessorStation_C",
                    "SN2RecipesListViewModel"
                })
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            IReadOnlyDictionary<string, IReadOnlyList<IntPtr>> objects =
                helper.FindLiveUObjects(1024, classNames);

            IntPtr matchedPlayer = IntPtr.Zero;
            string matchedClassName = string.Empty;
            foreach (string className in PlayerCharacterClassNames)
            {
                foreach (IntPtr playerCharacter in objects[className])
                {
                    if (inventoryComponentOffset == default || PlayerCharacterHasValidInventory(playerCharacter, inventoryComponentOffset))
                    {
                        matchedPlayer = playerCharacter;
                        matchedClassName = className;
                        break;
                    }
                }

                if (matchedPlayer != IntPtr.Zero)
                    break;
            }

            if (matchedPlayer == IntPtr.Zero)
                return new PlayerCharacterMatch(IntPtr.Zero, string.Empty);

            IntPtr inventoryComponent = game.Read<IntPtr>(matchedPlayer + inventoryComponentOffset);
            int inventoryId = inventoryComponent == IntPtr.Zero
                ? -1
                : game.Read<int>(inventoryComponent + UWEInventoryComponent_InventoryId);
            List<IntPtr> storages = inventoryId <= 0
                ? new List<IntPtr>()
                : objects["UWEInventoryStorage"]
                    .Where(storage => StorageContainsInventoryId(storage, inventoryId))
                    .Take(1)
                    .ToList();

            var recipeViewModels = new List<IntPtr>();
            foreach (IntPtr viewModel in objects["SN2RecipesListViewModel"])
                AddRecipeListViewModel(recipeViewModels, viewModel);
            foreach (string worldHudClassName in WorldHudClassNames)
                foreach (IntPtr worldHud in objects[worldHudClassName])
                {
                    AddRecipeListViewModel(recipeViewModels, game.Read<IntPtr>(worldHud + SN2WorldHUD_FabricatorRecipesListViewModel));
                    AddRecipeListViewModel(recipeViewModels, game.Read<IntPtr>(worldHud + SN2WorldHUD_PDARecipesListViewModel));
                    AddRecipeListViewModel(recipeViewModels, game.Read<IntPtr>(worldHud + SN2WorldHUD_BuilderRecipesListViewModel));
                }

            return new PlayerCharacterMatch(
                matchedPlayer,
                matchedClassName,
                CreateCraftingTargets(helper, objects),
                storages,
                inventoryId,
                recipeViewModels);
        }

        private PlayerCharacterMatch FindPlayerCharacterFast(IUnrealHelper helper)
        {
            int inventoryComponentOffset = GetUnrealFieldOffset("SN2PlayerCharacter", default, "InventoryComponent");
            IntPtr player = helper.FindLiveUObject(
                "SN2PlayerCharacter",
                candidate => inventoryComponentOffset == default
                    || PlayerCharacterHasValidInventory(candidate, inventoryComponentOffset));

            if (player == IntPtr.Zero)
                return new PlayerCharacterMatch(IntPtr.Zero, string.Empty);

            return new PlayerCharacterMatch(player, helper.GetUObjectClassName(player));
        }

        private void RefreshInventoryProbe(string reason)
        {
            inventoryStorageObjects.Clear();
            inventoryStorageRefreshTask = null;
            inventoryStorageRefreshInventoryId = int.MinValue;
            inventoryBaselineInitialized = false;
            lastPlayerInventoryId = int.MinValue;
            currentInventoryChanges.Clear();
            curPickUpCounts.Clear();
            curDropCounts.Clear();
            InvalidateRecipeListViewModels(reason);

            UpdatePlayerCharacterRefresh();
            if (playerCharacterRefreshTask != null)
                return;

            if (DateTime.Now < nextInventoryProbeAttempt)
                return;

            try
            {
                var helper = unrealHelper;
                playerCharacterRefreshReason = reason;
                nextInventoryProbeAttempt = DateTime.Now.AddMilliseconds(InventoryProbeRetryMilliseconds);
                playerCharacterRefreshTask = Task.Run(() => FindPlayerCharacterFast(helper));
            }
            catch (Exception ex)
            {
                logger.Log($"Inventory probe refresh request failed after {reason}: {ex.Message}");
                playerCharacterRefreshTask = null;
                playerCharacterRefreshReason = string.Empty;
                ScheduleInventoryProbeRetry();
            }
        }

        private void UpdatePlayerCharacterRefresh()
        {
            if (playerCharacterRefreshTask == null || !playerCharacterRefreshTask.IsCompleted)
                return;

            string reason = playerCharacterRefreshReason;
            Task<PlayerCharacterMatch> task = playerCharacterRefreshTask;
            playerCharacterRefreshTask = null;
            playerCharacterRefreshReason = string.Empty;

            if (!playerDiscoveryAllowed)
                return;

            try
            {
                PlayerCharacterMatch playerCharacter = task.Result;
                IntPtr player = playerCharacter.Pointer;
                if (player == IntPtr.Zero)
                {
                    int retryMilliseconds = ScheduleInventoryProbeRetry();
                    logger.Log($"Inventory probe refresh deferred after {reason}: player not found; retrying in {retryMilliseconds}ms");
                    return;
                }

                bool playerChanged = currentPlayerCharacterAddress != player;
                currentPlayerCharacterAddress = player;
                if (playerChanged)
                {
                    ResetProgressionSessionState();
                    InvalidateCraftingTargetsForPlayerChange();
                }

                if (playerCharacterPointer == null || playerInventoryComponent == null || playerEquipmentComponent == null || playerCharacterPointer.ClassName != playerCharacter.ClassName)
                {
                    InitializeInventoryPointers(playerCharacter);
                }
                else
                {
                    playerCharacterPointer.SetBase(player);
                    playerInventoryComponent.ForceUpdate(true);
                    playerEquipmentComponent.ForceUpdate(true);
                    playerToolbarComponent?.ForceUpdate(true);
                }

                IntPtr inventoryComponent = playerInventoryComponent == null ? IntPtr.Zero : playerInventoryComponent.New;
                IntPtr toolbarComponent = playerToolbarComponent == null ? IntPtr.Zero : playerToolbarComponent.New;
                int inventoryId = inventoryComponent == IntPtr.Zero ? -1 : game.Read<int>(inventoryComponent + UWEInventoryComponent_InventoryId);
                logger.Log($"Inventory probe refreshed after {reason}: class={playerCharacterClassName} player={player.ToString("X")} inventoryComponent={inventoryComponent.ToString("X")} toolbarComponent={toolbarComponent.ToString("X")} inventoryId={inventoryId}");
                nextInventoryProbeAttempt = DateTime.MinValue;
                nextInventoryStorageRefreshAttempt = DateTime.MinValue;

                ApplyCraftingTargets(playerCharacter.CraftingTargets, "discovered with player");
                nextCraftingTargetsRefreshAttempt = playerCharacter.CraftingTargets == null
                    ? DateTime.MinValue
                    : DateTime.UtcNow.Add(CraftingTargetsRefreshInterval);
                refreshCraftingTargetsAfterCurrent = false;
                if (playerCharacter.InventoryStorages.Count > 0)
                {
                    inventoryStorageObjects = playerCharacter.InventoryStorages;
                    inventoryStorageRefreshInventoryId = playerCharacter.InventoryId;
                    inventoryStorageRefreshTask = null;
                    logger.Log(
                        $"Inventory storage discovered with player: inventoryId={playerCharacter.InventoryId} " +
                        $"storageActors={inventoryStorageObjects.Count}");
                }
                if (playerCharacter.RecipeViewModels.Count > 0)
                {
                    ReplaceRecipeListViewModels(playerCharacter.RecipeViewModels);
                    recipeListViewModelsInvalidated = false;
                    recipeListViewModelRefreshTask = null;
                }
            }
            catch (Exception ex)
            {
                logger.Log($"Inventory probe refresh failed after {reason}: {ex.Message}");
                ScheduleInventoryProbeRetry();
            }
        }

        private int ScheduleInventoryProbeRetry()
        {
            nextInventoryProbeAttempt = DateTime.Now.AddMilliseconds(InventoryProbeRetryMilliseconds);
            return InventoryProbeRetryMilliseconds;
        }

        private void InvalidateCraftingTargetsForPlayerChange()
        {
            knownCrafters.Clear();
            processorStations.Clear();
            processorStates.Clear();
            builderTool = IntPtr.Zero;
            builderLastDiscoveryGhost = IntPtr.Zero;
            playerVolumeTracker = IntPtr.Zero;
            worldZoneTracker = IntPtr.Zero;
            worldZoneTrackerRefreshTask = null;
            nextWorldZoneTrackerAttempt = DateTime.MinValue;
            biomeBaselineInitialized = false;
            CurrentBiome = Biome.None;
            CurrentBiomeOld = Biome.None;
            currentBiomeKey = string.Empty;
            currentBiomeKeyOld = string.Empty;
            nextBiomeProbeAttempt = DateTime.MinValue;
            ResetReplicatedBuilderState();
            ResetBuilderConstructionState();
            activeCrafts.Clear();
            craftBaselineInitialized = false;

            if (craftingTargetsRefreshTask == null)
                nextCraftingTargetsRefreshAttempt = DateTime.MinValue;
            else
                refreshCraftingTargetsAfterCurrent = true;

            logger.Log("Craft and Build targets invalidated independently for newly loaded player");
        }

        private bool PlayerCharacterHasValidInventory(IntPtr playerCharacter, int inventoryComponentOffset)
        {
            try
            {
                IntPtr inventoryComponent = game.Read<IntPtr>(playerCharacter + inventoryComponentOffset);
                return inventoryComponent != IntPtr.Zero
                    && game.Read<int>(inventoryComponent + UWEInventoryComponent_InventoryId) > 0;
            }
            catch
            {
                return false;
            }
        }

        private void InvalidateRecipeListViewModels(string reason)
        {
            if (recipeListViewModels.Count == 0 && recipeListViewModelsInvalidated)
                return;

            recipeListViewModels.Clear();
            recipeListViewModelsInvalidated = true;
            recipeListViewModelsChanged = true;
            if (recipeListViewModelRefreshTask != null && recipeListViewModelRefreshTask.IsCompleted)
                recipeListViewModelRefreshTask = null;
            nextBlueprintProbeAttempt = DateTime.MinValue;
            logger.Log($"Recipe view models invalidated after {reason}");
        }

        private void UpdateInventory()
        {
            UpdateInventoryStorageRefresh();
            if (inventoryStorageObjects.Count == 0)
                return;

            var newInventory = ReadInventoryCounts();
            var toolbarActors = new HashSet<IntPtr>();
            var equippedToolbarItemTypes = new HashSet<IntPtr>();
            MergeCounts(newInventory, ReadToolbarCounts(toolbarActors, equippedToolbarItemTypes));
            var newEquipment = ReadEquipmentCounts(toolbarActors, equippedToolbarItemTypes);

            if (!inventoryBaselineInitialized)
            {
                PlayerInventory = newInventory;
                PlayerInventoryOld = new Dictionary<InventoryItem, int>(newInventory);
                PlayerEquipment = newEquipment;
                PlayerEquipmentOld = new Dictionary<InventoryItem, int>(newEquipment);
                currentInventoryChanges.Clear();
                curPickUpCounts.Clear();
                curDropCounts.Clear();
                inventoryBaselineInitialized = true;
                logger.Log($"Inventory baseline initialized: inventoryItems={PlayerInventory.Count} equipmentItems={PlayerEquipment.Count}");
                return;
            }

            PlayerInventoryOld = PlayerInventory;
            PlayerInventory = newInventory;
            PlayerEquipmentOld = PlayerEquipment;
            PlayerEquipment = newEquipment;

            Dictionary<InventoryItem, int> changedItems =
                PlayerInventory.Keys
                    .Union(PlayerInventoryOld.Keys)
                    .Union(PlayerEquipment.Keys)
                    .Union(PlayerEquipmentOld.Keys)
                    .Select(key => new
                    {
                        Key = key,
                        Delta = GetPlayerItemCount(key) - GetPlayerItemCountOld(key)
                    })
                    .Where(x => x.Delta != 0)
                    .ToDictionary(x => x.Key, x => x.Delta);

            currentInventoryChanges = changedItems;

            foreach (var key in curPickUpCounts.Keys.ToList())
                if (curPickUpCounts[key].ElapsedTime.ElapsedMilliseconds > maxInventoryTimeWithoutChangingMs)
                {
                    var correspondingSplit = settings.Splits
                        .OfType<ItemSplit>()
                        .FirstOrDefault(s => s.Item == key);

                    if (correspondingSplit != null)
                        correspondingSplit.AlreadySplitInvChanging = false;

                    curPickUpCounts.Remove(key);
                }

            foreach (var key in curDropCounts.Keys.ToList())
                if (curDropCounts[key].ElapsedTime.ElapsedMilliseconds > maxInventoryTimeWithoutChangingMs)
                {
                    var correspondingSplit = settings.Splits
                        .OfType<ItemSplit>()
                        .FirstOrDefault(s => s.Item == key);

                    if (correspondingSplit != null)
                        correspondingSplit.AlreadySplitInvChanging = false;

                    curDropCounts.Remove(key);
                }

            foreach (var changedItem in changedItems)
            {
                if (changedItem.Value > 0)
                    HandleChange(curPickUpCounts, changedItem.Key, changedItem.Value);
                else
                    HandleChange(curDropCounts, changedItem.Key, changedItem.Value);

                logger.Log($"Inventory change: {changedItem.Key} delta={changedItem.Value} old={GetPlayerItemCountOld(changedItem.Key)} new={GetPlayerItemCount(changedItem.Key)}");
            }

            void HandleChange(Dictionary<InventoryItem, InvChangeInfo> dict, InventoryItem key, int amount)
            {
                if (dict.TryGetValue(key, out var info))
                {
                    info.Count += amount;
                    info.ElapsedTime.Restart();
                }
                else
                {
                    dict[key] = new InvChangeInfo(amount, Stopwatch.StartNew());
                }
            }
        }

        private bool HasPlayerItem(InventoryItem item) => GetPlayerItemCount(item) > 0;

        private void RequestCraftingTargetRefresh()
        {
            if (craftingTargetsRefreshTask == null)
                nextCraftingTargetsRefreshAttempt = DateTime.MinValue;
            else
                refreshCraftingTargetsAfterCurrent = true;
        }

        private int GetPlayerItemCount(InventoryItem item) => GetDictionaryCount(PlayerInventory, item) + GetDictionaryCount(PlayerEquipment, item);

        private int GetPlayerItemCountOld(InventoryItem item) => GetDictionaryCount(PlayerInventoryOld, item) + GetDictionaryCount(PlayerEquipmentOld, item);

        private static int GetDictionaryCount(Dictionary<InventoryItem, int> dict, InventoryItem item)
        {
            return dict != null && dict.TryGetValue(item, out int count) ? count : 0;
        }

        private static void MergeCounts(Dictionary<InventoryItem, int> target, Dictionary<InventoryItem, int> source)
        {
            foreach (KeyValuePair<InventoryItem, int> count in source)
                target[count.Key] = GetDictionaryCount(target, count.Key) + count.Value;
        }

        private Dictionary<InventoryItem, int> ReadInventoryCounts()
        {
            var result = new Dictionary<InventoryItem, int>();
            if (playerInventoryComponent == null || unrealHelper == null)
                return result;

            try
            {
                IntPtr inventoryComponent = playerInventoryComponent.New;
                if (inventoryComponent == IntPtr.Zero)
                    return result;

                int inventoryId = game.Read<int>(inventoryComponent + UWEInventoryComponent_InventoryId);
                if (inventoryId < 0)
                    return result;

                if (inventoryId != lastPlayerInventoryId)
                {
                    logger.Log($"Player inventory id changed: {lastPlayerInventoryId} -> {inventoryId}");
                    lastPlayerInventoryId = inventoryId;
                    if (inventoryStorageRefreshInventoryId != inventoryId)
                    {
                        inventoryStorageObjects.Clear();
                        inventoryStorageRefreshTask = null;
                        inventoryStorageRefreshInventoryId = int.MinValue;
                    }
                    inventoryBaselineInitialized = false;
                }

                foreach (IntPtr storage in inventoryStorageObjects)
                    ReadStorageItems(storage, inventoryId, result);
            }
            catch (Exception ex)
            {
                logger.Log($"Inventory read failed: {ex.Message}");
            }

            return result;
        }

        private Dictionary<InventoryItem, int> ReadToolbarCounts(HashSet<IntPtr> toolbarActors, HashSet<IntPtr> equippedToolbarItemTypes)
        {
            var result = new Dictionary<InventoryItem, int>();
            if (playerToolbarComponent == null || unrealHelper == null)
                return result;

            try
            {
                IntPtr toolbarComponent = playerToolbarComponent.New;
                if (toolbarComponent == IntPtr.Zero)
                    return result;

                IntPtr itemsArray = toolbarComponent + UWEToolbarComponent_ToolbarItems;
                IntPtr dataPtr = game.Read<IntPtr>(itemsArray);
                int num = game.Read<int>(itemsArray + game.PointerSize);
                int max = game.Read<int>(itemsArray + game.PointerSize + 4);

                if (!IsPlausibleArray(dataPtr, num, max, 64))
                    return result;

                for (int i = 0; i < num; i++)
                {
                    IntPtr item = dataPtr + i * FUWEToolbarItem_Stride;
                    IntPtr actor = game.Read<IntPtr>(item + FUWEToolbarItem_Actor);
                    IntPtr itemType = game.Read<IntPtr>(item + FUWEToolbarItem_ItemType);
                    if (itemType == IntPtr.Zero && actor != IntPtr.Zero)
                        itemType = game.Read<IntPtr>(actor + AUWEBaseItem_ItemType);

                    if (actor != IntPtr.Zero && itemType != IntPtr.Zero)
                        toolbarActors.Add(actor);

                    if (itemType != IntPtr.Zero && game.Read<byte>(item + FUWEToolbarItem_IsEquipped) != 0)
                        equippedToolbarItemTypes.Add(itemType);

                    int count = game.Read<int>(item + FUWEToolbarItem_StackSize);
                    AddItemTypeCount(result, itemType, count <= 0 ? 1 : count);
                }
            }
            catch (Exception ex)
            {
                logger.Log($"Toolbar read failed: {ex.Message}");
            }

            return result;
        }

        private Dictionary<InventoryItem, int> ReadEquipmentCounts(HashSet<IntPtr> toolbarActors, HashSet<IntPtr> equippedToolbarItemTypes)
        {
            var result = new Dictionary<InventoryItem, int>();
            if (playerEquipmentComponent == null || unrealHelper == null)
                return result;

            try
            {
                IntPtr equipmentComponent = playerEquipmentComponent.New;
                if (equipmentComponent == IntPtr.Zero)
                    return result;

                IntPtr itemsArray = equipmentComponent + UWEEquipmentComponent_EquippedItems;
                IntPtr dataPtr = game.Read<IntPtr>(itemsArray);
                int num = game.Read<int>(itemsArray + game.PointerSize);
                int max = game.Read<int>(itemsArray + game.PointerSize + 4);

                if (!IsPlausibleArray(dataPtr, num, max, 64))
                    return result;

                for (int i = 0; i < num; i++)
                {
                    IntPtr item = game.Read<IntPtr>(dataPtr + i * game.PointerSize);
                    if (item == IntPtr.Zero)
                        continue;
                    if (toolbarActors.Contains(item))
                        continue;

                    IntPtr itemType = game.Read<IntPtr>(item + AUWEBaseItem_ItemType);
                    if (equippedToolbarItemTypes.Contains(itemType))
                        continue;

                    AddItemTypeCount(result, itemType, 1);
                }
            }
            catch (Exception ex)
            {
                logger.Log($"Equipment read failed: {ex.Message}");
            }

            return result;
        }

        private void ReadStorageItems(IntPtr storage, int inventoryId, Dictionary<InventoryItem, int> result)
        {
            try
            {
                IntPtr itemsArray = storage + UWEInventoryStorage_ItemsContainer + UWEInventoryContainer_Items;
                IntPtr dataPtr = game.Read<IntPtr>(itemsArray);
                int num = game.Read<int>(itemsArray + game.PointerSize);
                int max = game.Read<int>(itemsArray + game.PointerSize + 4);

                if (!IsPlausibleArray(dataPtr, num, max, 2048))
                    return;

                for (int i = 0; i < num; i++)
                {
                    IntPtr item = dataPtr + i * FUWEInventoryItem_Stride;
                    int itemInventoryId = game.Read<int>(item + FUWEInventoryItem_InventoryId);
                    if (itemInventoryId != inventoryId)
                        continue;

                    IntPtr itemType = game.Read<IntPtr>(item + FUWEInventoryItem_ItemType);
                    int count = game.Read<int>(item + FUWEInventoryItem_Count);
                    AddItemTypeCount(result, itemType, count);
                }
            }
            catch
            {
            }
        }

        private void AddItemTypeCount(Dictionary<InventoryItem, int> result, IntPtr itemType, int count)
        {
            if (itemType == IntPtr.Zero || count <= 0)
                return;

            if (inventoryItemTypeCache.TryGetValue(itemType, out InventoryItem cachedItem))
            {
                if (cachedItem != InventoryItem.None)
                    result[cachedItem] = GetDictionaryCount(result, cachedItem) + count;

                return;
            }

            string objectName;
            string objectPath;
            try
            {
                objectName = unrealHelper.GetUObjectName(itemType);
                objectPath = unrealHelper.GetUObjectPath(itemType);
            }
            catch
            {
                return;
            }

            if (!IsReadableUObjectText(objectName) || !IsReadableUObjectText(objectPath))
                return;

            bool mapped = TryMapInventoryItem(objectName, objectPath, out InventoryItem inventoryItem);
            LogInventoryItemType(objectName, objectPath, mapped ? inventoryItem : InventoryItem.None);
            inventoryItemTypeCache[itemType] = mapped ? inventoryItem : InventoryItem.None;
            if (!mapped)
                return;

            result[inventoryItem] = GetDictionaryCount(result, inventoryItem) + count;
        }

        private void TryLogInventoryItemType(IntPtr itemType)
        {
            if (!EnableEnumDiscoveryLogs || itemType == IntPtr.Zero)
                return;

            try
            {
                string objectName = unrealHelper.GetUObjectName(itemType);
                string objectPath = unrealHelper.GetUObjectPath(itemType);

                if (!IsReadableUObjectText(objectName) || !IsReadableUObjectText(objectPath))
                    return;

                bool mapped = TryMapInventoryItem(objectName, objectPath, out InventoryItem inventoryItem);
                LogInventoryItemType(objectName, objectPath, mapped ? inventoryItem : InventoryItem.None);
            }
            catch
            {
            }
        }

        private void LogInventoryItemType(string objectName, string objectPath, InventoryItem mappedItem)
        {
            if (!EnableEnumDiscoveryLogs)
                return;

            string key = string.IsNullOrEmpty(objectPath) ? objectName : objectPath;
            if (string.IsNullOrWhiteSpace(key) || !loggedInventoryItemTypeAssets.Add(key))
                return;

            string candidate = BestEnumCandidate(InventoryItemNameCandidates(objectName, objectPath));
            string mapped = mappedItem == InventoryItem.None ? "<new>" : mappedItem.ToString();
            logger.Log($"[EnumDiscovery][InventoryItem] candidate={candidate} mapped={mapped} name={objectName} path={objectPath}");
        }

        private static bool IsReadableUObjectText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return true;

            if (value.Length > 512)
                return false;

            foreach (char c in value)
            {
                if (char.IsControl(c) || c > 0x7E)
                    return false;
            }

            return true;
        }

        private static bool TryParseNamedEnum<TEnum>(string candidate, out TEnum value) where TEnum : struct
        {
            if (IsNumericEnumCandidate(candidate))
            {
                value = default(TEnum);
                return false;
            }

            return Enum.TryParse(candidate, ignoreCase: true, out value);
        }

        private static bool IsNumericEnumCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return true;

            candidate = candidate.Trim();
            int start = candidate[0] == '+' || candidate[0] == '-' ? 1 : 0;
            if (start == candidate.Length)
                return false;

            for (int i = start; i < candidate.Length; i++)
            {
                if (!char.IsDigit(candidate[i]))
                    return false;
            }

            return true;
        }

        private bool TryMapInventoryItem(string objectName, string objectPath, out InventoryItem item)
        {
            foreach (string candidate in InventoryItemNameCandidates(objectName, objectPath))
            {
                if (TryParseNamedEnum(candidate, out item) && item != InventoryItem.None)
                    return true;
            }

            item = InventoryItem.None;
            return false;
        }

        private IEnumerable<string> InventoryItemNameCandidates(string objectName, string objectPath)
        {
            foreach (string value in new[] { objectName, LastPathSegment(objectPath) })
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                yield return value;
                yield return StripInventoryItemName(value);
            }
        }

        private static string LastPathSegment(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            int dot = path.LastIndexOf('.');
            return dot >= 0 && dot + 1 < path.Length ? path.Substring(dot + 1) : path;
        }

        private static string StripInventoryItemName(string value)
        {
            string result = value;
            foreach (string prefix in new[] { "ITM_", "Item_", "ItemType_", "DA_", "DAT_", "Data_", "UWEItemType_", "BP_" })
            {
                if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(prefix.Length);
                    break;
                }
            }

            // Current builds expose the six biomod strength tiers as distinct
            // UWEItemType assets. Treat those numbered assets as the same
            // logical inventory item used by the settings UI.
            int numberedItemType = result.LastIndexOf("_ItemType_", StringComparison.OrdinalIgnoreCase);
            if (numberedItemType >= 0
                && int.TryParse(result.Substring(numberedItemType + "_ItemType_".Length), out int tier)
                && tier > 0)
            {
                result = result.Substring(0, numberedItemType);
            }

            foreach (string suffix in new[] { "_C", "_ItemType", "ItemType", "_Data", "_DA" })
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length);
                    break;
                }
            }

            return result;
        }

        private static string BestEnumCandidate(IEnumerable<string> candidates)
        {
            foreach (string candidate in candidates)
            {
                string normalized = NormalizeEnumCandidate(candidate);
                if (!string.IsNullOrEmpty(normalized))
                    return normalized;
            }

            return "Unknown";
        }

        private static string NormalizeEnumCandidate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var chars = new List<char>(value.Length);
            foreach (char c in value.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    chars.Add(c);
                else if (chars.Count > 0 && chars[chars.Count - 1] != '_')
                    chars.Add('_');
            }

            string result = new string(chars.ToArray()).Trim('_');
            if (result.Length == 0)
                return string.Empty;

            if (char.IsDigit(result[0]))
                result = "_" + result;

            return result;
        }

        private void UpdateInventoryStorageRefresh()
        {
            if (unrealHelper == null || playerInventoryComponent == null)
                return;

            UpdatePlayerCharacterRefresh();
            if (playerCharacterRefreshTask != null)
                return;

            int inventoryId;
            try
            {
                inventoryId = GetCurrentPlayerInventoryId();
            }
            catch
            {
                return;
            }

            if (inventoryId <= 0)
            {
                if (currentPlayerCharacterAddress == IntPtr.Zero)
                    RefreshInventoryProbe($"invalid inventoryId={inventoryId}");
                else
                    nextInventoryStorageRefreshAttempt = DateTime.Now.AddMilliseconds(InventoryProbeRetryMilliseconds);
                return;
            }

            if (inventoryStorageObjects.Count > 0 && inventoryStorageRefreshInventoryId == inventoryId)
                return;

            if (inventoryStorageObjects.Count > 0 && inventoryStorageRefreshInventoryId != inventoryId)
                inventoryStorageObjects.Clear();

            if (inventoryStorageRefreshTask != null && inventoryStorageRefreshInventoryId != inventoryId)
            {
                inventoryStorageRefreshTask = null;
                inventoryStorageRefreshInventoryId = int.MinValue;
            }

            if (inventoryStorageRefreshTask != null)
            {
                if (!inventoryStorageRefreshTask.IsCompleted)
                    return;

                try
                {
                    inventoryStorageObjects = inventoryStorageRefreshTask.Result ?? new List<IntPtr>();
                    logger.Log($"Inventory storage scan finished: inventoryId={inventoryStorageRefreshInventoryId} storageActors={inventoryStorageObjects.Count}");
                }
                catch (Exception ex)
                {
                    logger.Log($"Inventory storage refresh failed: {ex.Message}");
                }
                finally
                {
                    int completedInventoryId = inventoryStorageRefreshInventoryId;
                    inventoryStorageRefreshTask = null;
                    if (inventoryStorageObjects.Count == 0)
                    {
                        logger.Log($"Inventory storage not available yet: inventoryId={completedInventoryId}; retrying");
                        nextInventoryStorageRefreshAttempt = DateTime.Now.AddMilliseconds(InventoryProbeRetryMilliseconds);
                    }
                }

                return;
            }

            if (DateTime.Now < nextInventoryStorageRefreshAttempt)
                return;

            try
            {
                var helper = unrealHelper;
                int targetInventoryId = inventoryId;
                inventoryStorageRefreshInventoryId = targetInventoryId;
                inventoryStorageRefreshTask = Task.Run(() =>
                {
                    IntPtr storage = helper.FindLiveUObject("UWEInventoryStorage", candidate => StorageContainsInventoryId(candidate, targetInventoryId));
                    return storage == IntPtr.Zero ? new List<IntPtr>() : new List<IntPtr> { storage };
                });
            }
            catch (Exception ex)
            {
                logger.Log($"Inventory storage refresh failed: {ex.Message}");
                nextInventoryStorageRefreshAttempt = DateTime.Now.AddSeconds(5);
            }
        }

        private int GetCurrentPlayerInventoryId()
        {
            IntPtr inventoryComponent = playerInventoryComponent.New;
            return inventoryComponent == IntPtr.Zero ? -1 : game.Read<int>(inventoryComponent + UWEInventoryComponent_InventoryId);
        }

        private bool StorageContainsInventoryId(IntPtr storage, int inventoryId)
        {
            try
            {
                IntPtr containersArray = storage + UWEInventoryStorage_StorageContainers;
                IntPtr dataPtr = game.Read<IntPtr>(containersArray);
                int num = game.Read<int>(containersArray + game.PointerSize);
                int max = game.Read<int>(containersArray + game.PointerSize + 4);

                if (IsPlausibleArray(dataPtr, num, max, 256))
                {
                    for (int i = 0; i < num; i++)
                    {
                        IntPtr container = dataPtr + i * FUWEInventoryStorageContainer_Stride;
                        if (game.Read<int>(container + FUWEInventoryStorageContainer_InventoryId) == inventoryId)
                            return true;
                    }
                }

                IntPtr itemsArray = storage + UWEInventoryStorage_ItemsContainer + UWEInventoryContainer_Items;
                dataPtr = game.Read<IntPtr>(itemsArray);
                num = game.Read<int>(itemsArray + game.PointerSize);
                max = game.Read<int>(itemsArray + game.PointerSize + 4);

                if (IsPlausibleArray(dataPtr, num, max, 2048))
                {
                    for (int i = 0; i < num; i++)
                    {
                        IntPtr item = dataPtr + i * FUWEInventoryItem_Stride;
                        if (game.Read<int>(item + FUWEInventoryItem_InventoryId) == inventoryId)
                            return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsPlausibleArray(IntPtr dataPtr, int num, int max, int maxAllowed)
        {
            if (num < 0 || max < num || max > maxAllowed)
                return false;

            return num == 0 || dataPtr != IntPtr.Zero;
        }

        private int GetUnrealFieldOffset(string className, int fallback, params string[] fieldNames)
        {
            if (TryGetUnrealFieldOffset(className, out int offset, fieldNames))
                return offset;

            return fallback;
        }

        private bool TryGetUnrealFieldOffset(string className, out int offset, params string[] fieldNames)
        {
            offset = default;
            if (unrealHelper == null || string.IsNullOrWhiteSpace(className) || fieldNames == null || fieldNames.Length == 0)
                return false;

            string cacheKey = className + "." + string.Join("|", fieldNames);
            if (unrealFieldOffsetCache.TryGetValue(cacheKey, out int cachedOffset))
            {
                offset = cachedOffset;
                return cachedOffset >= 0;
            }

            foreach (string fieldName in fieldNames)
            {
                if (string.IsNullOrWhiteSpace(fieldName))
                    continue;

                try
                {
                    int resolvedOffset = unrealHelper.GetFieldOffset(className, fieldName);
                    if (resolvedOffset != default)
                    {
                        unrealFieldOffsetCache[cacheKey] = resolvedOffset;
                        offset = resolvedOffset;
                        return true;
                    }
                }
                catch
                {
                }
            }

            unrealFieldOffsetCache[cacheKey] = -1;
            return false;
        }

        private void UpdateBlueprints()
        {
            if (currentPlayerCharacterAddress == IntPtr.Zero)
                return;

            List<Unlockable> previousBlueprints = KnownBlueprints;
            List<Unlockable> currentBlueprints = ReadBlueprints();
            bool establishingBaseline = !blueprintBaselineInitialized || recipeListViewModelsChanged;

            // The HUD publishes its recipe-list view models before their unlocked sets are populated.
            // Every playable session has starter recipes, so an empty first read is not a valid baseline.
            if (establishingBaseline && currentBlueprints.Count == 0)
                return;

            if (establishingBaseline)
            {
                KnownBlueprintsOld = currentBlueprints;
                if (recipeListViewModels.Count > 0)
                {
                    recipeListViewModelsChanged = false;
                    blueprintBaselineInitialized = true;
                    logger.Log($"Blueprint baseline initialized: unlocked={currentBlueprints.Count}");
                }
            }
            else
            {
                KnownBlueprintsOld = previousBlueprints;
            }

            KnownBlueprints = currentBlueprints;

            if (!establishingBaseline)
                foreach (Unlockable blueprint in currentBlueprints.Except(previousBlueprints))
                    logger.Log($"Blueprint unlocked: {blueprint}");
        }

        private bool HasBlueprint(Unlockable blueprint) =>
            blueprintBaselineInitialized && KnownBlueprints.Contains(blueprint);

        private bool HasNewBlueprint(Unlockable blueprint) =>
            blueprintBaselineInitialized
            && KnownBlueprints.Contains(blueprint)
            && !KnownBlueprintsOld.Contains(blueprint);

        private List<Unlockable> ReadBlueprints()
        {
            var result = new List<Unlockable>();
            if (unrealHelper == null)
                return result;

            EnsureRecipeListViewModels();

            if (recipeListViewModels.Count == 0 && recipeListViewModelRefreshTask != null)
                return KnownBlueprints.ToList();

            foreach (IntPtr viewModel in recipeListViewModels.ToList())
            {
                try
                {
                    foreach (IntPtr recipe in ReadPointerSet(viewModel + SN2RecipesListViewModel_UnlockedRecipes, 512))
                        if (TryReadBlueprint(recipe, out Unlockable blueprint) && !result.Contains(blueprint))
                            result.Add(blueprint);
                }
                catch
                {
                }
            }

            if (result.Count == 0 && recipeListViewModels.Count > 0 && KnownBlueprints.Count > 0)
                InvalidateRecipeListViewModels("empty blueprint read");

            return result;
        }

        private void EnsureRecipeListViewModels()
        {
            if (currentPlayerCharacterAddress == IntPtr.Zero)
                return;

            if ((recipeListViewModels.Count == 0 || recipeListViewModelsInvalidated)
                && TryGetCurrentWorldHud(out IntPtr worldHud))
            {
                var directViewModels = new List<IntPtr>();
                AddRecipeListViewModel(directViewModels, game.Read<IntPtr>(worldHud + SN2WorldHUD_FabricatorRecipesListViewModel));
                AddRecipeListViewModel(directViewModels, game.Read<IntPtr>(worldHud + SN2WorldHUD_PDARecipesListViewModel));
                AddRecipeListViewModel(directViewModels, game.Read<IntPtr>(worldHud + SN2WorldHUD_BuilderRecipesListViewModel));
                if (directViewModels.Count > 0)
                {
                    ReplaceRecipeListViewModels(directViewModels);
                    recipeListViewModelsInvalidated = false;
                    recipeListViewModelRefreshTask = null;
                    return;
                }
            }

            if (recipeListViewModelRefreshTask != null)
            {
                if (!recipeListViewModelRefreshTask.IsCompleted)
                    return;

                try
                {
                    List<IntPtr> viewModels = recipeListViewModelRefreshTask.Result ?? new List<IntPtr>();
                    ReplaceRecipeListViewModels(viewModels);
                    recipeListViewModelsInvalidated = viewModels.Count == 0;
                }
                catch (Exception ex)
                {
                    logger.Log($"Recipe view model refresh failed: {ex.Message}");
                    recipeListViewModelsInvalidated = true;
                }
                finally
                {
                    recipeListViewModelRefreshTask = null;
                }

                return;
            }

            if (recipeListViewModels.Count > 0 && !recipeListViewModelsInvalidated)
                return;

            if (DateTime.Now < nextBlueprintProbeAttempt)
                return;

            try
            {
                var helper = unrealHelper;
                nextBlueprintProbeAttempt = DateTime.Now.AddSeconds(recipeListViewModels.Count == 0 ? 2 : 10);
                recipeListViewModelRefreshTask = Task.Run(() => FindRecipeListViewModels(helper));
            }
            catch
            {
                nextBlueprintProbeAttempt = DateTime.Now.AddSeconds(10);
            }
        }

        private List<IntPtr> FindRecipeListViewModels(IUnrealHelper helper)
        {
            var viewModels = new List<IntPtr>();

            foreach (string worldHudClassName in WorldHudClassNames)
                AddRecipeListViewModelsFromWorldHud(helper, viewModels, worldHudClassName);

            if (viewModels.Count > 0)
                return viewModels;

            foreach (IntPtr viewModel in helper.FindLiveUObjects("SN2RecipesListViewModel", 8))
                AddRecipeListViewModel(viewModels, viewModel);

            return viewModels;
        }

        private void AddRecipeListViewModelsFromWorldHud(IUnrealHelper helper, List<IntPtr> viewModels, string worldHudClassName)
        {
            try
            {
                foreach (IntPtr worldHud in helper.FindLiveUObjects(worldHudClassName, 4))
                {
                    if (!WorldHudHasRecipeViewModels(worldHud))
                        continue;

                    AddRecipeListViewModel(viewModels, game.Read<IntPtr>(worldHud + SN2WorldHUD_FabricatorRecipesListViewModel));
                    AddRecipeListViewModel(viewModels, game.Read<IntPtr>(worldHud + SN2WorldHUD_PDARecipesListViewModel));
                    AddRecipeListViewModel(viewModels, game.Read<IntPtr>(worldHud + SN2WorldHUD_BuilderRecipesListViewModel));
                }
            }
            catch
            {
            }
        }

        private bool WorldHudHasRecipeViewModels(IntPtr worldHud)
        {
            try
            {
                return game.Read<IntPtr>(worldHud + SN2WorldHUD_FabricatorRecipesListViewModel) != IntPtr.Zero
                    || game.Read<IntPtr>(worldHud + SN2WorldHUD_PDARecipesListViewModel) != IntPtr.Zero
                    || game.Read<IntPtr>(worldHud + SN2WorldHUD_BuilderRecipesListViewModel) != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private void AddRecipeListViewModel(List<IntPtr> viewModels, IntPtr viewModel)
        {
            if (viewModel != IntPtr.Zero && !viewModels.Contains(viewModel))
                viewModels.Add(viewModel);
        }

        private void ReplaceRecipeListViewModels(List<IntPtr> viewModels)
        {
            if (viewModels.Count == 0 || SamePointers(recipeListViewModels, viewModels))
                return;

            if (recipeListViewModels.Count > 0)
                recipeListViewModelsChanged = true;

            recipeListViewModels.Clear();
            recipeListViewModels.AddRange(viewModels);
            logger.Log($"Recipe view models refreshed: viewModels={recipeListViewModels.Count}");
        }

        private static bool SamePointers(List<IntPtr> left, List<IntPtr> right)
        {
            return left.Count == right.Count && new HashSet<IntPtr>(left).SetEquals(right);
        }

        private List<IntPtr> ReadPointerSet(IntPtr setAddress, int maxElements)
        {
            var result = new List<IntPtr>();

            try
            {
                IntPtr dataPtr = game.Read<IntPtr>(setAddress);
                int num = game.Read<int>(setAddress + game.PointerSize);
                int max = game.Read<int>(setAddress + game.PointerSize + 4);

                if (!IsPlausibleArray(dataPtr, num, max, maxElements))
                    return result;

                var seen = new HashSet<IntPtr>();
                for (int i = 0; i < num; i++)
                {
                    IntPtr ptr = game.Read<IntPtr>(dataPtr + i * 0x10);
                    if (ptr != IntPtr.Zero && seen.Add(ptr))
                        result.Add(ptr);
                }
            }
            catch
            {
            }

            return result;
        }

        private List<IntPtr> ReadPointerArray(IntPtr arrayAddress, int maxElements)
        {
            var result = new List<IntPtr>();

            try
            {
                IntPtr dataPtr = game.Read<IntPtr>(arrayAddress);
                int num = game.Read<int>(arrayAddress + game.PointerSize);
                int max = game.Read<int>(arrayAddress + game.PointerSize + 4);

                if (!IsPlausibleArray(dataPtr, num, max, maxElements))
                    return result;

                var seen = new HashSet<IntPtr>();
                for (int i = 0; i < num; i++)
                {
                    IntPtr ptr = game.Read<IntPtr>(dataPtr + i * game.PointerSize);
                    if (ptr != IntPtr.Zero && seen.Add(ptr))
                        result.Add(ptr);
                }
            }
            catch
            {
            }

            return result;
        }

        private bool TryReadBlueprint(IntPtr recipe, out Unlockable blueprint)
        {
            blueprint = Unlockable.None;
            if (recipe == IntPtr.Zero || unrealHelper == null)
                return false;

            if (blueprintRecipeCache.TryGetValue(recipe, out blueprint))
                return blueprint != Unlockable.None;

            try
            {
                string objectName = unrealHelper.GetUObjectName(recipe);
                string objectPath = unrealHelper.GetUObjectPath(recipe);

                if (!IsReadableUObjectText(objectName) || !IsReadableUObjectText(objectPath))
                    return false;

                foreach (string candidate in BlueprintNameCandidates(objectName, objectPath))
                {
                    if (TryParseNamedEnum(candidate, out blueprint) && blueprint != Unlockable.None)
                    {
                        LogCraftingRecipe(objectName, objectPath, blueprint);
                        blueprintRecipeCache[recipe] = blueprint;
                        return true;
                    }
                }

                LogCraftingRecipe(objectName, objectPath, Unlockable.None);
                blueprintRecipeCache[recipe] = Unlockable.None;
            }
            catch
            {
            }

            blueprint = Unlockable.None;
            return false;
        }

        private void TryLogCraftingRecipe(IntPtr recipe, Unlockable knownBlueprint)
        {
            if (!EnableEnumDiscoveryLogs || recipe == IntPtr.Zero)
                return;

            try
            {
                string objectName = unrealHelper.GetUObjectName(recipe);
                string objectPath = unrealHelper.GetUObjectPath(recipe);

                if (!IsReadableUObjectText(objectName) || !IsReadableUObjectText(objectPath))
                    return;

                Unlockable mappedBlueprint = knownBlueprint;
                if (mappedBlueprint == Unlockable.None)
                    TryMapBlueprint(objectName, objectPath, out mappedBlueprint);

                LogCraftingRecipe(objectName, objectPath, mappedBlueprint);
            }
            catch
            {
            }
        }

        private void LogCraftingRecipe(string objectName, string objectPath, Unlockable mappedBlueprint)
        {
            if (!EnableEnumDiscoveryLogs)
                return;

            string key = string.IsNullOrEmpty(objectPath) ? objectName : objectPath;
            if (string.IsNullOrWhiteSpace(key) || !loggedCraftingRecipeAssets.Add(key))
                return;

            string candidate = BestEnumCandidate(BlueprintNameCandidates(objectName, objectPath));
            string blueprintMapped = mappedBlueprint == Unlockable.None ? "<new>" : mappedBlueprint.ToString();
            string craftableMapped = TryMapCraftable(objectName, objectPath, out Craftable craftable) && craftable != Craftable.None
                ? craftable.ToString()
                : "<new>";

            logger.Log($"[EnumDiscovery][Unlockable] candidate={candidate} mapped={blueprintMapped} name={objectName} path={objectPath}");
            logger.Log($"[EnumDiscovery][Craftable] candidate={candidate} mapped={craftableMapped} name={objectName} path={objectPath}");
        }

        private bool TryMapBlueprint(string objectName, string objectPath, out Unlockable blueprint)
        {
            foreach (string candidate in BlueprintNameCandidates(objectName, objectPath))
            {
                if (TryParseNamedEnum(candidate, out blueprint) && blueprint != Unlockable.None)
                    return true;
            }

            blueprint = Unlockable.None;
            return false;
        }

        private bool TryMapCraftable(string objectName, string objectPath, out Craftable craftable)
        {
            foreach (string candidate in BlueprintNameCandidates(objectName, objectPath))
            {
                if (TryParseNamedEnum(candidate, out craftable) && craftable != Craftable.None)
                    return true;

                if (candidate.IndexOf("CelestineToStrontiumIngot", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    craftable = Craftable.CelestineToStrontium;
                    return true;
                }
            }

            craftable = Craftable.None;
            return false;
        }

        private bool TryMapBuildable(string objectName, string objectPath, out Buildable buildable)
        {
            foreach (string candidate in BlueprintNameCandidates(objectName, objectPath))
            {
                if (TryParseNamedEnum(candidate, out buildable) && buildable != Buildable.None)
                    return true;

                if (candidate.Equals("MoonPoolDock", StringComparison.OrdinalIgnoreCase))
                {
                    buildable = Buildable.VehicleDock;
                    return true;
                }

                if (candidate.Equals("VehicleFabricator", StringComparison.OrdinalIgnoreCase))
                {
                    buildable = Buildable.VehicleBay;
                    return true;
                }

                if (candidate.Equals("ProcessorStation", StringComparison.OrdinalIgnoreCase))
                {
                    buildable = Buildable.Processor;
                    return true;
                }

                if (candidate.StartsWith("Base", StringComparison.OrdinalIgnoreCase)
                    && TryParseNamedEnum(candidate.Substring(4), out buildable)
                    && buildable != Buildable.None)
                    return true;
            }

            buildable = Buildable.None;
            return false;
        }

        private IEnumerable<string> BlueprintNameCandidates(string objectName, string objectPath)
        {
            foreach (string value in new[] { objectName, LastPathSegment(objectPath) })
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                foreach (string candidate in ExpandedBlueprintNameCandidates(value))
                    yield return candidate;
            }
        }

        private static IEnumerable<string> ExpandedBlueprintNameCandidates(string value)
        {
            yield return value;

            string stripped = StripBlueprintName(value);
            yield return stripped;
            yield return stripped.Replace("_", "").Replace("-", "");

            int underscore = stripped.LastIndexOf('_');
            if (underscore >= 0 && underscore + 1 < stripped.Length)
            {
                string last = stripped.Substring(underscore + 1);
                yield return last;
                yield return last.Replace("_", "").Replace("-", "");
            }
        }

        private static string StripBlueprintName(string value)
        {
            string result = value;
            foreach (string prefix in new[] { "REC_", "Recipe_", "CraftingRecipe_", "Crafting_", "Blueprint_", "BP_", "DA_", "DAT_", "Data_", "ITM_", "Item_", "ItemType_" })
            {
                if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(prefix.Length);
                    break;
                }
            }

            foreach (string suffix in new[] {
                "_ItemBrushData_Snap", "_ItemBrushData", "ItemBrushData",
                "_ConstructData", "ConstructData", "_C", "_Recipe", "Recipe",
                "_Blueprint", "Blueprint", "_Data", "_DA"
            })
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length);
                    break;
                }
            }

            return result;
        }

        private void UpdateEncyclopedia()
        {
            if (unrealHelper == null || currentPlayerCharacterAddress == IntPtr.Zero)
                return;

            EnsureDatabankViewModels();

            if (encyclopediaReadTask != null)
            {
                if (!encyclopediaReadTask.IsCompleted)
                    return;

                try
                {
                    if (encyclopediaReadTaskGeneration == encyclopediaReadGeneration)
                        ApplyEncyclopediaRead(encyclopediaReadTask.Result, encyclopediaReadTaskResetsBaseline);
                    else
                        nextEncyclopediaUpdateAttempt = DateTime.MinValue;
                }
                catch (Exception ex)
                {
                    logger.Log($"Encyclopedia refresh failed: {ex.Message}");
                    nextEncyclopediaUpdateAttempt = DateTime.Now.AddSeconds(2);
                }
                finally
                {
                    encyclopediaReadTask = null;
                    encyclopediaReadTaskResetsBaseline = false;
                }

                return;
            }

            DateTime now = DateTime.Now;
            if (encyclopediaBaselineInitialized && !databankViewModelsChanged && now < nextEncyclopediaUpdateAttempt)
                return;

            if (databankViewModels.Count == 0 && databankViewModelRefreshTask != null)
                return;

            List<IntPtr> viewModels = databankViewModels.ToList();
            bool resetBaseline = !encyclopediaBaselineInitialized || databankViewModelsChanged;
            bool hadPreviousKeys = encyclopediaEntryKeys.Count > 0;
            int generation = encyclopediaReadGeneration;

            encyclopediaReadTaskGeneration = generation;
            encyclopediaReadTaskResetsBaseline = resetBaseline;
            nextEncyclopediaUpdateAttempt = now.Add(EncyclopediaUpdateInterval);
            encyclopediaReadTask = Task.Run(() => ReadEncyclopedia(viewModels, hadPreviousKeys));
        }

        private void ApplyEncyclopediaRead(EncyclopediaReadResult readResult, bool resetBaseline)
        {
            List<EncyEntry> previousEntries = Encyclopedia;
            List<string> previousPrimaryKeys = encyclopediaPrimaryEntryKeys;
            List<string> previousKeys = encyclopediaEntryKeys;
            List<EncyEntry> currentEntries = readResult?.Entries ?? new List<EncyEntry>();
            List<string> currentPrimaryKeys = readResult?.PrimaryKeys ?? new List<string>();
            List<string> currentKeys = readResult?.Keys ?? new List<string>();

            if (readResult != null && readResult.ShouldInvalidateDatabankViewModels)
            {
                InvalidateDatabankViewModels(readResult.InvalidateReason);
                return;
            }

            // Like recipe lists, the Databank view model exists briefly before its entry tree is filled.
            // Do not turn that transient empty state into a session baseline.
            if (resetBaseline && currentPrimaryKeys.Count == 0 && currentEntries.Count == 0 && currentKeys.Count == 0)
            {
                nextEncyclopediaUpdateAttempt = DateTime.Now.AddMilliseconds(250);
                return;
            }

            if (resetBaseline)
            {
                EncyclopediaOld = currentEntries;
                encyclopediaPrimaryEntryKeysOld = currentPrimaryKeys;
                encyclopediaEntryKeysOld = currentKeys;
                encyclopediaBaselineInitialized = true;
                databankViewModelsChanged = false;
            }
            else
            {
                EncyclopediaOld = previousEntries;
                encyclopediaPrimaryEntryKeysOld = previousPrimaryKeys;
                encyclopediaEntryKeysOld = previousKeys;
            }

            Encyclopedia = currentEntries;
            encyclopediaPrimaryEntryKeys = currentPrimaryKeys;
            encyclopediaEntryKeys = currentKeys;

            string readState = $"{encyclopediaPrimaryEntryKeys.Count}:{Encyclopedia.Count}:{encyclopediaEntryKeys.Count}";
            if (readState != lastEncyclopediaReadState)
            {
                lastEncyclopediaReadState = readState;
                logger.Log($"Encyclopedia read: entries={encyclopediaPrimaryEntryKeys.Count} mappedEntries={Encyclopedia.Count} keys={encyclopediaEntryKeys.Count}");
            }

        }

        private bool HasEncyclopediaEntry(EncySplit split)
        {
            if (split == null || !encyclopediaBaselineInitialized)
                return false;

            if (split.Entry == EncyEntry.Any)
                return encyclopediaPrimaryEntryKeys.Count > 0 || Encyclopedia.Count > 0 || encyclopediaEntryKeys.Count > 0;

            if (split.Entry != EncyEntry.None)
                return Encyclopedia.Contains(split.Entry) || ContainsEncyclopediaKey(encyclopediaEntryKeys, split.EntryKey);

            return ContainsEncyclopediaKey(encyclopediaEntryKeys, split.EntryKey);
        }

        private bool HasNewEncyclopediaEntry(EncySplit split)
        {
            if (split == null || !encyclopediaBaselineInitialized)
                return false;

            if (split.Entry == EncyEntry.Any)
            {
                return Encyclopedia.Any(entry => !EncyclopediaOld.Contains(entry))
                    || encyclopediaPrimaryEntryKeys.Any(key => !ContainsEncyclopediaKey(encyclopediaPrimaryEntryKeysOld, key));
            }

            if (split.Entry != EncyEntry.None)
            {
                bool hasEntry = Encyclopedia.Contains(split.Entry) || ContainsEncyclopediaKey(encyclopediaEntryKeys, split.EntryKey);
                bool hadEntry = EncyclopediaOld.Contains(split.Entry) || ContainsEncyclopediaKey(encyclopediaEntryKeysOld, split.EntryKey);
                return hasEntry && !hadEntry;
            }

            string splitKey = split.EntryKey;
            return ContainsEncyclopediaKey(encyclopediaEntryKeys, splitKey)
                && !ContainsEncyclopediaKey(encyclopediaEntryKeysOld, splitKey);
        }

        private static bool ContainsEncyclopediaKey(IEnumerable<string> keys, string key)
        {
            if (keys == null)
                return false;

            var candidates = new HashSet<string>(DatabankEntryKeyCandidates(key), StringComparer.OrdinalIgnoreCase);
            return candidates.Count > 0 && keys.Any(k => candidates.Contains(NormalizeEnumCandidate(k)));
        }

        private static Dictionary<string, EncyEntry> BuildEncyEntryAliases()
        {
            var aliases = new Dictionary<string, EncyEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (EncyEntry entry in Enum.GetValues(typeof(EncyEntry)))
            {
                if (entry == EncyEntry.None || entry == EncyEntry.Any)
                    continue;

                foreach (string key in DatabankEntryKeyCandidates(entry.ToString()))
                    if (!aliases.ContainsKey(key))
                        aliases.Add(key, entry);
            }

            return aliases;
        }

        private static IEnumerable<string> DatabankEntryKeyCandidates(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                yield break;

            foreach (string candidate in new[] {
                value,
                LastPathSegment(value),
                StripDatabankEntryName(value),
                StripDatabankEntryName(LastPathSegment(value)),
            })
            {
                string normalized = NormalizeEnumCandidate(candidate);
                if (!string.IsNullOrEmpty(normalized))
                    yield return normalized;

                string compact = normalized.Replace("_", string.Empty);
                if (!string.IsNullOrEmpty(compact))
                    yield return compact;
            }
        }

        private EncyclopediaReadResult ReadEncyclopedia(List<IntPtr> viewModels, bool hadPreviousKeys)
        {
            var result = new List<EncyEntry>();
            var primaryEntryKeys = new List<string>();
            var entryKeys = new List<string>();

            if (unrealHelper == null)
                return new EncyclopediaReadResult(result, primaryEntryKeys, entryKeys, false, string.Empty);

            foreach (IntPtr viewModel in viewModels)
            {
                try
                {
                    int entriesOffset = GetUnrealFieldOffset("SN2DatabankViewModel", SN2DatabankViewModel_Entries, "Entries");
                    int rootOffset = GetUnrealFieldOffset("SN2DatabankViewModel", SN2DatabankViewModel_Root, "Root");
                    List<IntPtr> visibleEntryViewModels = ReadPointerArray(viewModel + entriesOffset, 1024);
                    AddUniquePointers(visibleEntryViewModels, ReadDatabankCategoryEntryViewModels(game.Read<IntPtr>(viewModel + rootOffset)));

                    foreach (IntPtr entryViewModel in visibleEntryViewModels)
                    {
                        IntPtr databankEntry = ReadDatabankEntryFromViewModel(entryViewModel);
                        AddDatabankEntry(databankEntry, result, primaryEntryKeys, entryKeys);
                    }
                }
                catch
                {
                }
            }

            bool shouldInvalidate = entryKeys.Count == 0 && viewModels.Count > 0 && hadPreviousKeys;

            return new EncyclopediaReadResult(result, primaryEntryKeys, entryKeys, shouldInvalidate, "empty databank read");
        }

        private List<IntPtr> ReadDatabankCategoryEntryViewModels(IntPtr rootCategory)
        {
            var result = new List<IntPtr>();
            AddDatabankCategoryEntryViewModels(rootCategory, result, new HashSet<IntPtr>(), 0);
            return result;
        }

        private void AddDatabankCategoryEntryViewModels(IntPtr category, List<IntPtr> result, HashSet<IntPtr> visited, int depth)
        {
            if (category == IntPtr.Zero || depth > 64 || !visited.Add(category))
                return;

            try
            {
                int entriesOffset = GetUnrealFieldOffset("SN2DatabankCategoryViewModel", SN2DatabankCategoryViewModel_Entries, "Entries");
                AddUniquePointers(result, ReadPointerArray(category + entriesOffset, 1024));

                int subCategoriesOffset = GetUnrealFieldOffset("SN2DatabankCategoryViewModel", SN2DatabankCategoryViewModel_SubCategories, "SubCategories", "Subcategories");
                foreach (IntPtr subCategory in ReadPointerArray(category + subCategoriesOffset, 256))
                    AddDatabankCategoryEntryViewModels(subCategory, result, visited, depth + 1);
            }
            catch
            {
            }
        }

        private static void AddUniquePointers(List<IntPtr> target, IEnumerable<IntPtr> pointers)
        {
            foreach (IntPtr pointer in pointers)
                if (pointer != IntPtr.Zero && !target.Contains(pointer))
                    target.Add(pointer);
        }

        private IntPtr ReadDatabankEntryFromViewModel(IntPtr entryViewModel)
        {
            if (entryViewModel == IntPtr.Zero)
                return IntPtr.Zero;

            try
            {
                int entryOffset = GetUnrealFieldOffset("SN2DatabankEntryViewModel", SN2DatabankEntryViewModel_Entry, "Entry", "DatabankEntry", "DataBankEntry");
                return game.Read<IntPtr>(entryViewModel + entryOffset);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private void AddVisibleDatabankEntryViewModels(List<EncyEntry> entries, List<string> primaryEntryKeys, List<string> entryKeys)
        {
            if (unrealHelper == null)
                return;

            var entryViewModels = new List<IntPtr>();

            try
            {
                foreach (IntPtr entryViewModel in unrealHelper.FindLiveUObjects("SN2DatabankEntryViewModel", 1024))
                    AddUniquePointers(entryViewModels, new[] { entryViewModel });
            }
            catch
            {
            }

            AddWidgetDatabankEntryViewModels(entryViewModels);

            foreach (IntPtr entryViewModel in entryViewModels)
                AddDatabankEntry(ReadDatabankEntryFromViewModel(entryViewModel), entries, primaryEntryKeys, entryKeys);
        }

        private void AddWidgetDatabankEntryViewModels(List<IntPtr> entryViewModels)
        {
            AddEntryViewModelsFromWidgetClass(entryViewModels, "WBP_DatabankEntry_C", WBP_DatabankEntry_SN2DatabankEntryViewModel, "SN2DatabankEntryViewModel", "ViewModel");
            AddEntryViewModelsFromWidgetClass(entryViewModels, "WBP_DatabankEntryDetail_C", WBP_DatabankEntryDetail_ViewModel, "ViewModel", "SN2DatabankEntryViewModel");
            AddEntryViewModelsFromWidgetClass(entryViewModels, "WBP_DatabankEntryWrapper_C", WBP_DatabankEntryWrapper_EntryViewModel, "Entry_View_Model", "EntryViewModel", "ViewModel");

            try
            {
                int categoryViewModelOffset = GetUnrealFieldOffset("WBP_DatabankCategory_C", WBP_DatabankCategory_ViewModel, "ViewModel");
                foreach (IntPtr categoryWidget in unrealHelper.FindLiveUObjects("WBP_DatabankCategory_C", 256))
                    AddUniquePointers(entryViewModels, ReadDatabankCategoryEntryViewModels(game.Read<IntPtr>(categoryWidget + categoryViewModelOffset)));
            }
            catch
            {
            }
        }

        private void AddEntryViewModelsFromWidgetClass(List<IntPtr> entryViewModels, string className, int fallbackOffset, params string[] fieldNames)
        {
            try
            {
                int offset = GetUnrealFieldOffset(className, fallbackOffset, fieldNames);
                foreach (IntPtr widget in unrealHelper.FindLiveUObjects(className, 512))
                    AddUniquePointers(entryViewModels, new[] { game.Read<IntPtr>(widget + offset) });
            }
            catch
            {
            }
        }

        private void AddUnlockedLiveDatabankEntries(List<EncyEntry> entries, List<string> primaryEntryKeys, List<string> entryKeys)
        {
            if (unrealHelper == null)
                return;

            DatabankStoryGoalState unlockedStoryGoals = ReadGlobalDatabankStoryGoals();
            if (unlockedStoryGoals.Count == 0)
                return;

            try
            {
                foreach (IntPtr databankEntry in unrealHelper.FindLiveUObjects("UWEDatabankEntry", 4096))
                    if (IsDatabankEntryUnlocked(databankEntry, unlockedStoryGoals))
                        AddDatabankEntry(databankEntry, entries, primaryEntryKeys, entryKeys);
            }
            catch
            {
            }
        }

        private sealed class DatabankStoryGoalState
        {
            public readonly HashSet<IntPtr> Pointers = new HashSet<IntPtr>();
            public readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public int Count => Pointers.Count + Names.Count;

            public void AddPointer(IntPtr pointer)
            {
                if (pointer != IntPtr.Zero)
                    Pointers.Add(pointer);
            }

            public void AddName(string name)
            {
                foreach (string candidate in StoryGoalNameCandidates(name))
                {
                    string normalized = NormalizeEnumCandidate(candidate);
                    if (!string.IsNullOrEmpty(normalized))
                        Names.Add(normalized);
                }
            }

            public bool ContainsName(string name)
            {
                foreach (string candidate in StoryGoalNameCandidates(name))
                {
                    string normalized = NormalizeEnumCandidate(candidate);
                    if (!string.IsNullOrEmpty(normalized) && Names.Contains(normalized))
                        return true;
                }

                return false;
            }
        }

        private DatabankStoryGoalState ReadDatabankStoryGoals(IntPtr databankViewModel)
        {
            var result = new DatabankStoryGoalState();

            try
            {
                int storyGoalContainerOffset = GetUnrealFieldOffset("SN2DatabankViewModel", SN2DatabankViewModel_StoryGoalContainer, "StoryGoalContainer", "StoryGoalsContainer");
                IntPtr storyGoalContainer = game.Read<IntPtr>(databankViewModel + storyGoalContainerOffset);
                AddStoryGoalsFromContainer(storyGoalContainer, result);
            }
            catch
            {
            }

            if (result.Count == 0)
                AddGlobalStoryGoalContainers(result);

            return result;
        }

        private DatabankStoryGoalState ReadGlobalDatabankStoryGoals()
        {
            var result = new DatabankStoryGoalState();

            if (unrealHelper == null)
                return result;

            try
            {
                AddGlobalStoryGoalContainers(result);
            }
            catch
            {
            }

            try
            {
                foreach (IntPtr subsystem in unrealHelper.FindLiveUObjects("UWEStoryGoalsWorldSubsystem", 4))
                    AddStoryGoalsFromSubsystem(subsystem, result);
            }
            catch
            {
            }

            return result;
        }

        private void UpdateStoryGoals()
        {
            if (currentPlayerCharacterAddress == IntPtr.Zero
                || unrealHelper == null
                || DateTime.UtcNow < nextStoryGoalUpdateAttempt)
                return;

            nextStoryGoalUpdateAttempt = DateTime.UtcNow.AddMilliseconds(250);
            DatabankStoryGoalState state = ReadPlayerStoryGoals();
            if (state.Count == 0)
                return;

            var readGoals = new HashSet<StoryGoal>();
            foreach (string name in state.Names)
                if (TryParseNamedEnum(name, out StoryGoal goal) && goal != StoryGoal.None && goal != StoryGoal.Any)
                    readGoals.Add(goal);

            if (!storyGoalBaselineInitialized)
            {
                currentStoryGoals = readGoals;
                previousStoryGoals = new HashSet<StoryGoal>(readGoals);
                storyGoalBaselineInitialized = true;
                logger.Log($"Story Goal baseline initialized: completed={readGoals.Count}");
                return;
            }

            previousStoryGoals = currentStoryGoals;
            currentStoryGoals = readGoals;
            foreach (StoryGoal goal in currentStoryGoals.Except(previousStoryGoals))
                logger.Log($"Story Goal completed: {goal}");
        }

        private DatabankStoryGoalState ReadPlayerStoryGoals()
        {
            var result = new DatabankStoryGoalState();
            try
            {
                if (!TryGetUnrealFieldOffset("Pawn", out int playerStateOffset, "PlayerState"))
                    return result;

                IntPtr playerState = game.Read<IntPtr>(currentPlayerCharacterAddress + playerStateOffset);
                if (playerState == IntPtr.Zero)
                    return result;

                int containerOffset = GetUnrealFieldOffset(
                    "SN2PlayerState",
                    SN2PlayerState_StoryGoalContainerComponent,
                    "StoryGoalContainerComponent",
                    "StoryGoalContainer");
                AddStoryGoalsFromContainer(game.Read<IntPtr>(playerState + containerOffset), result);
            }
            catch
            {
            }
            return result;
        }

        private bool HasStoryGoal(StoryGoal goal) =>
            storyGoalBaselineInitialized
            && (goal == StoryGoal.Any ? currentStoryGoals.Count > 0 : currentStoryGoals.Contains(goal));

        private bool HasNewStoryGoal(StoryGoal goal) =>
            storyGoalBaselineInitialized
            && (goal == StoryGoal.Any
                ? currentStoryGoals.Any(value => !previousStoryGoals.Contains(value))
                : currentStoryGoals.Contains(goal) && !previousStoryGoals.Contains(goal));

        private void AddGlobalStoryGoalContainers(DatabankStoryGoalState result)
        {
            if (unrealHelper == null)
                return;

            try
            {
                foreach (IntPtr storyGoalContainer in unrealHelper.FindLiveUObjects("UWEStoryGoalContainerComponent", 16))
                    AddStoryGoalsFromContainer(storyGoalContainer, result);
            }
            catch
            {
            }

            AddStoryGoalsFromContainerOwners("SN2PlayerState", SN2PlayerState_StoryGoalContainerComponent, result, "StoryGoalContainerComponent", "StoryGoalContainer");
            AddStoryGoalsFromContainerOwners("BP_SN2PlayerState_C", SN2PlayerState_StoryGoalContainerComponent, result, "StoryGoalContainerComponent", "StoryGoalContainer");
            AddStoryGoalsFromContainerOwners("SN2GameState", SN2GameState_StoryGoalContainerComponent, result, "StoryGoalContainerComponent", "StoryGoalContainer");
            AddStoryGoalsFromContainerOwners("BP_SN2GameState_C", SN2GameState_StoryGoalContainerComponent, result, "StoryGoalContainerComponent", "StoryGoalContainer");
        }

        private void AddStoryGoalsFromContainerOwners(string ownerClassName, int fallbackOffset, DatabankStoryGoalState result, params string[] fieldNames)
        {
            try
            {
                int offset = GetUnrealFieldOffset(ownerClassName, fallbackOffset, fieldNames);
                foreach (IntPtr owner in unrealHelper.FindLiveUObjects(ownerClassName, 8))
                    AddStoryGoalsFromContainer(game.Read<IntPtr>(owner + offset), result);
            }
            catch
            {
            }
        }

        private void AddStoryGoalsFromSubsystem(IntPtr subsystem, DatabankStoryGoalState result)
        {
            if (subsystem == IntPtr.Zero)
                return;

            try
            {
                if (!TryGetUnrealFieldOffset("UWEStoryGoalsWorldSubsystem", out int storyGoalContainerOffset, "StoryGoalContainer", "StoryGoalsContainer", "GoalContainer"))
                    return;

                IntPtr storyGoalContainer = game.Read<IntPtr>(subsystem + storyGoalContainerOffset);
                AddStoryGoalsFromContainer(storyGoalContainer, result);
            }
            catch
            {
            }
        }

        private void AddStoryGoalsFromContainer(IntPtr storyGoalContainer, DatabankStoryGoalState result)
        {
            if (storyGoalContainer == IntPtr.Zero)
                return;

            try
            {
                int cachedStoryGoalsOffset = GetUnrealFieldOffset("UWEStoryGoalContainerComponent", UWEStoryGoalContainer_CachedStoryGoals, "CachedStoryGoals");
                foreach (IntPtr storyGoal in ReadPointerSet(storyGoalContainer + cachedStoryGoalsOffset, 4096))
                {
                    result.AddPointer(storyGoal);

                    if (TryReadUObjectName(storyGoal, out string storyGoalName))
                        result.AddName(storyGoalName);
                }

                int storyGoalEntriesOffset = GetUnrealFieldOffset("UWEStoryGoalContainerComponent", UWEStoryGoalContainer_StoryGoalsEntries, "StoryGoalsEntries", "StoryGoalEntries");
                foreach (string storyGoalName in ReadPrimaryAssetNameArray(storyGoalContainer + storyGoalEntriesOffset, 4096, FUWEStoryGoalEntry_Stride, FUWEStoryGoalEntry_StoryGoal))
                    result.AddName(storyGoalName);

                int unlockRecordsOffset = GetUnrealFieldOffset("UWEStoryGoalContainerComponent", UWEStoryGoalContainer_UnlockRecords, "UnlockRecords", "StoryGoalUnlockRecords");
                foreach (string storyGoalName in ReadPrimaryAssetNameArray(storyGoalContainer + unlockRecordsOffset, 512, FStoryGoalUnlockRecord_Stride, FStoryGoalUnlockRecord_StoryGoal))
                    result.AddName(storyGoalName);
            }
            catch
            {
            }
        }

        private IEnumerable<string> ReadPrimaryAssetNameArray(IntPtr arrayAddress, int maxElements, int elementStride, int primaryAssetIdOffset)
        {
            try
            {
                IntPtr dataPtr = game.Read<IntPtr>(arrayAddress);
                int num = game.Read<int>(arrayAddress + game.PointerSize);
                int max = game.Read<int>(arrayAddress + game.PointerSize + 4);

                if (!IsPlausibleArray(dataPtr, num, max, maxElements))
                    yield break;

                for (int i = 0; i < num; i++)
                {
                    string name = ReadPrimaryAssetIdName(dataPtr + i * elementStride + primaryAssetIdOffset);
                    if (!string.IsNullOrEmpty(name))
                        yield return name;
                }
            }
            finally
            {
            }
        }

        private string ReadPrimaryAssetIdName(IntPtr primaryAssetIdAddress)
        {
            return ReadFNameString(primaryAssetIdAddress + FPrimaryAssetId_PrimaryAssetName);
        }

        private string ReadFNameString(IntPtr fnameAddress)
        {
            try
            {
                int index = game.Read<int>(fnameAddress);
                string name = unrealHelper?.GetFNameEntryName(index) ?? string.Empty;
                return IsReadableUObjectText(name) ? name : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool TryReadUObjectName(IntPtr uobject, out string name)
        {
            name = string.Empty;
            if (uobject == IntPtr.Zero || unrealHelper == null)
                return false;

            try
            {
                name = unrealHelper.GetUObjectName(uobject);
                return !string.IsNullOrEmpty(name) && IsReadableUObjectText(name);
            }
            catch
            {
                name = string.Empty;
                return false;
            }
        }

        private bool IsDatabankEntryUnlocked(IntPtr databankEntry, DatabankStoryGoalState unlockedStoryGoals)
        {
            if (databankEntry == IntPtr.Zero)
                return false;

            try
            {
                int unlockingRequirementsOffset = GetUnrealFieldOffset("UWEDatabankEntry", UWEDatabankEntry_UnlockingRequirements, "UnlockingRequirements", "UnlockingRequirement", "UnlockRule", "UnlockingRule");
                IntPtr unlockingRequirements = game.Read<IntPtr>(databankEntry + unlockingRequirementsOffset);
                return IsStoryGoalRuleSatisfied(unlockingRequirements, unlockedStoryGoals, new HashSet<IntPtr>());
            }
            catch
            {
                return false;
            }
        }

        private bool IsStoryGoalRuleSatisfied(IntPtr rule, DatabankStoryGoalState unlockedStoryGoals, HashSet<IntPtr> visitedRules)
        {
            if (rule == IntPtr.Zero)
                return true;

            if (!visitedRules.Add(rule))
                return false;

            string className;
            try
            {
                className = GetUObjectClassName(rule);
            }
            catch
            {
                return false;
            }

            try
            {
                if (IsUObjectClass(className, "UWERequiredStoryGoalRule"))
                {
                    int requiredStoryGoalOffset = GetUnrealFieldOffset("UWERequiredStoryGoalRule", UWERequiredStoryGoalRule_RequiredStoryGoalRef, "RequiredStoryGoalRef", "RequiredStoryGoal", "StoryGoal");
                    return IsRequiredStoryGoalSatisfied(rule + requiredStoryGoalOffset, unlockedStoryGoals);
                }

                if (IsUObjectClass(className, "UWEStoryGoalRuleAnd"))
                {
                    int rulesOffset = GetUnrealFieldOffset("UWEStoryGoalRuleComposite", UWEStoryGoalRuleComposite_Rules, "Rules", "ChildRules");
                    List<IntPtr> rules = ReadPointerArray(rule + rulesOffset, 128);
                    return rules.Count == 0 || rules.All(childRule => IsStoryGoalRuleSatisfied(childRule, unlockedStoryGoals, visitedRules));
                }

                if (IsUObjectClass(className, "UWEStoryGoalRuleOr"))
                {
                    int rulesOffset = GetUnrealFieldOffset("UWEStoryGoalRuleComposite", UWEStoryGoalRuleComposite_Rules, "Rules", "ChildRules");
                    return ReadPointerArray(rule + rulesOffset, 128)
                        .Any(childRule => IsStoryGoalRuleSatisfied(childRule, unlockedStoryGoals, visitedRules));
                }

                if (IsUObjectClass(className, "UWEStoryGoalRuleCount"))
                {
                    int rulesOffset = GetUnrealFieldOffset("UWEStoryGoalRuleComposite", UWEStoryGoalRuleComposite_Rules, "Rules", "ChildRules");
                    int minimumCountOffset = GetUnrealFieldOffset("UWEStoryGoalRuleCount", UWEStoryGoalRuleCount_MinimumCount, "MinimumCount", "MinCount");
                    List<IntPtr> rules = ReadPointerArray(rule + rulesOffset, 128);
                    int minimumCount = game.Read<int>(rule + minimumCountOffset);
                    int satisfied = rules.Count(childRule => IsStoryGoalRuleSatisfied(childRule, unlockedStoryGoals, visitedRules));
                    return satisfied >= minimumCount;
                }

                if (IsUObjectClass(className, "UWEStoryGoalRuleNegate"))
                {
                    int ruleToNegateOffset = GetUnrealFieldOffset("UWEStoryGoalRuleNegate", UWEStoryGoalRuleNegate_RuleToNegate, "RuleToNegate", "Rule", "ChildRule");
                    IntPtr childRule = game.Read<IntPtr>(rule + ruleToNegateOffset);
                    return !IsStoryGoalRuleSatisfied(childRule, unlockedStoryGoals, visitedRules);
                }

                return false;
            }
            finally
            {
                visitedRules.Remove(rule);
            }
        }

        private bool IsRequiredStoryGoalSatisfied(IntPtr storyGoalRefAddress, DatabankStoryGoalState unlockedStoryGoals)
        {
            try
            {
                IntPtr requiredStoryGoal = game.Read<IntPtr>(storyGoalRefAddress);
                if (unlockedStoryGoals.Pointers.Contains(requiredStoryGoal))
                    return true;

                if (TryReadUObjectName(requiredStoryGoal, out string storyGoalName)
                    && unlockedStoryGoals.ContainsName(storyGoalName))
                    return true;
            }
            catch
            {
            }

            foreach (string storyGoalName in ReadStoryGoalReferenceNames(storyGoalRefAddress))
                if (unlockedStoryGoals.ContainsName(storyGoalName))
                    return true;

            return false;
        }

        private IEnumerable<string> ReadStoryGoalReferenceNames(IntPtr storyGoalRefAddress)
        {
            string primaryAssetName = ReadPrimaryAssetIdName(storyGoalRefAddress);
            if (!string.IsNullOrEmpty(primaryAssetName))
                yield return primaryAssetName;

            string typeName = ReadFNameString(storyGoalRefAddress);
            if (!string.IsNullOrEmpty(typeName))
                yield return typeName;

            string name = ReadFNameString(storyGoalRefAddress + FPrimaryAssetId_PrimaryAssetName);
            if (!string.IsNullOrEmpty(name))
                yield return name;
        }

        private static bool IsUObjectClass(string className, string expectedClassName)
        {
            return className.Equals(expectedClassName, StringComparison.OrdinalIgnoreCase)
                || className.Equals(expectedClassName + "_C", StringComparison.OrdinalIgnoreCase);
        }

        private string GetUObjectClassName(IntPtr uobject)
        {
            if (uobject == IntPtr.Zero || unrealHelper == null)
                return string.Empty;

            IntPtr uclass = game.Read<IntPtr>(uobject + 0x10);
            return uclass == IntPtr.Zero ? string.Empty : unrealHelper.GetUObjectName(uclass);
        }

        private void AddDatabankEntry(IntPtr databankEntry, List<EncyEntry> entries, List<string> primaryEntryKeys, List<string> entryKeys)
        {
            if (!TryReadDatabankEntry(databankEntry, out EncyEntry entry, out string primaryKey, out List<string> keys))
                return;

            if (entry != EncyEntry.None && entry != EncyEntry.Any && !entries.Contains(entry))
                entries.Add(entry);

            if (!string.IsNullOrEmpty(primaryKey) && !ContainsEncyclopediaKey(primaryEntryKeys, primaryKey))
                primaryEntryKeys.Add(primaryKey);

            foreach (string key in keys)
                if (!ContainsEncyclopediaKey(entryKeys, key))
                    entryKeys.Add(key);
        }

        private void EnsureDatabankViewModels()
        {
            if (currentPlayerCharacterAddress == IntPtr.Zero)
                return;

            if ((databankViewModels.Count == 0 || databankViewModelsInvalidated)
                && TryGetCurrentWorldHud(out IntPtr worldHud))
            {
                var directViewModels = new List<IntPtr>();
                AddDatabankViewModel(directViewModels, game.Read<IntPtr>(worldHud + SN2WorldHUD_DatabankViewModel));
                if (directViewModels.Count > 0)
                {
                    ReplaceDatabankViewModels(directViewModels);
                    databankViewModelsInvalidated = false;
                    databankViewModelRefreshTask = null;
                    return;
                }
            }

            if (databankViewModelRefreshTask != null)
            {
                if (!databankViewModelRefreshTask.IsCompleted)
                    return;

                try
                {
                    List<IntPtr> viewModels = databankViewModelRefreshTask.Result ?? new List<IntPtr>();
                    ReplaceDatabankViewModels(viewModels);
                    databankViewModelsInvalidated = viewModels.Count == 0;
                }
                catch (Exception ex)
                {
                    logger.Log($"Databank view model refresh failed: {ex.Message}");
                    databankViewModelsInvalidated = true;
                }
                finally
                {
                    databankViewModelRefreshTask = null;
                }

                return;
            }

            if (databankViewModels.Count > 0 && !databankViewModelsInvalidated)
                return;

            if (DateTime.Now < nextDatabankProbeAttempt)
                return;

            try
            {
                var helper = unrealHelper;
                nextDatabankProbeAttempt = DateTime.Now.AddSeconds(databankViewModels.Count == 0 ? 2 : 10);
                databankViewModelRefreshTask = Task.Run(() => FindDatabankViewModels(helper));
            }
            catch
            {
                nextDatabankProbeAttempt = DateTime.Now.AddSeconds(10);
            }
        }

        private List<IntPtr> FindDatabankViewModels(IUnrealHelper helper)
        {
            var viewModels = new List<IntPtr>();

            foreach (string worldHudClassName in WorldHudClassNames)
                AddDatabankViewModelFromWorldHud(helper, viewModels, worldHudClassName);

            if (viewModels.Count > 0)
                return viewModels;

            try
            {
                foreach (IntPtr viewModel in helper.FindLiveUObjects("SN2DatabankViewModel", 16))
                    AddDatabankViewModel(viewModels, viewModel);
            }
            catch
            {
            }

            try
            {
                int tabViewModelOffset = GetUnrealFieldOffset("WBP_TabDatabank_C", WBP_TabDatabank_ViewModel, "ViewModel");
                foreach (IntPtr databankTab in helper.FindLiveUObjects("WBP_TabDatabank_C", 8))
                    AddDatabankViewModel(viewModels, game.Read<IntPtr>(databankTab + tabViewModelOffset));
            }
            catch
            {
            }

            return viewModels;
        }

        private bool TryGetCurrentWorldHud(out IntPtr worldHud)
        {
            worldHud = IntPtr.Zero;
            if (localPlayer == IntPtr.Zero || localPlayerControllerOffset <= 0)
                return false;

            try
            {
                IntPtr controller = game.Read<IntPtr>(localPlayer + localPlayerControllerOffset);
                if (controller == IntPtr.Zero)
                    return false;

                string controllerClassName = unrealHelper.GetUObjectClassName(controller);
                int hudOffset;
                if ((!string.IsNullOrWhiteSpace(controllerClassName)
                        && TryGetUnrealFieldOffset(controllerClassName, out hudOffset, "MyHUD", "HUD"))
                    || TryGetUnrealFieldOffset("PlayerController", out hudOffset, "MyHUD", "HUD"))
                {
                    worldHud = game.Read<IntPtr>(controller + hudOffset);
                    return worldHud != IntPtr.Zero;
                }
            }
            catch
            {
            }

            return false;
        }

        private void AddDatabankViewModelFromWorldHud(IUnrealHelper helper, List<IntPtr> viewModels, string worldHudClassName)
        {
            try
            {
                int databankViewModelOffset = GetUnrealFieldOffset(worldHudClassName, SN2WorldHUD_DatabankViewModel, "DatabankViewModel", "DataBankViewModel");
                foreach (IntPtr worldHud in helper.FindLiveUObjects(worldHudClassName, 4))
                    if (WorldHudHasDatabankViewModel(worldHud))
                        AddDatabankViewModel(viewModels, game.Read<IntPtr>(worldHud + databankViewModelOffset));
            }
            catch
            {
            }
        }

        private bool WorldHudHasDatabankViewModel(IntPtr worldHud)
        {
            try
            {
                return game.Read<IntPtr>(worldHud + SN2WorldHUD_DatabankViewModel) != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private void AddDatabankViewModel(List<IntPtr> viewModels, IntPtr viewModel)
        {
            if (viewModel != IntPtr.Zero && !viewModels.Contains(viewModel))
                viewModels.Add(viewModel);
        }

        private void ReplaceDatabankViewModels(List<IntPtr> viewModels)
        {
            if (viewModels.Count == 0 || SamePointers(databankViewModels, viewModels))
                return;

            databankViewModelsChanged = true;
            databankViewModels.Clear();
            databankViewModels.AddRange(viewModels);
            lock (databankEntryInfoCacheLock)
                databankEntryInfoCache.Clear();
            encyclopediaReadGeneration++;
            nextEncyclopediaUpdateAttempt = DateTime.MinValue;
            logger.Log($"Databank view models refreshed: viewModels={databankViewModels.Count}");
        }

        private void InvalidateDatabankViewModels(string reason)
        {
            if (!databankViewModelsInvalidated)
                logger.Log($"Databank view models invalidated: {reason}");

            databankViewModelsInvalidated = true;
            databankViewModels.Clear();
            lock (databankEntryInfoCacheLock)
                databankEntryInfoCache.Clear();
            encyclopediaReadGeneration++;
            nextDatabankProbeAttempt = DateTime.Now;
            nextEncyclopediaUpdateAttempt = DateTime.MinValue;
        }

        private bool TryReadDatabankEntry(IntPtr databankEntry, out EncyEntry entry, out string primaryKey, out List<string> keys)
        {
            entry = EncyEntry.None;
            primaryKey = string.Empty;
            keys = new List<string>();

            if (databankEntry == IntPtr.Zero || unrealHelper == null)
                return false;

            DatabankEntryInfo cachedEntry;
            lock (databankEntryInfoCacheLock)
                databankEntryInfoCache.TryGetValue(databankEntry, out cachedEntry);

            if (cachedEntry != null)
            {
                entry = cachedEntry.Entry;
                primaryKey = cachedEntry.PrimaryKey;
                keys = cachedEntry.Keys.ToList();
                return true;
            }

            try
            {
                string objectName = unrealHelper.GetUObjectName(databankEntry);
                string objectPath = unrealHelper.GetUObjectPath(databankEntry);

                if (!IsReadableUObjectText(objectName) || !IsReadableUObjectText(objectPath))
                    return false;

                TryMapDatabankEntry(objectName, objectPath, out entry);
                primaryKey = NormalizeEnumCandidate(objectName);
                if (string.IsNullOrEmpty(primaryKey))
                    primaryKey = NormalizeEnumCandidate(LastPathSegment(objectPath));

                foreach (string candidate in DatabankEntryNameCandidates(objectName, objectPath))
                {
                    string key = NormalizeEnumCandidate(candidate);
                    if (!string.IsNullOrEmpty(key) && !ContainsEncyclopediaKey(keys, key))
                        keys.Add(key);
                }

                if (entry != EncyEntry.None && entry != EncyEntry.Any)
                {
                    string entryKey = NormalizeEnumCandidate(entry.ToString());
                    if (!string.IsNullOrEmpty(entryKey) && !ContainsEncyclopediaKey(keys, entryKey))
                        keys.Add(entryKey);
                }

                LogDatabankEntry(objectName, objectPath, entry);
                bool readable = keys.Count > 0 || entry != EncyEntry.None;
                if (readable)
                {
                    lock (databankEntryInfoCacheLock)
                        databankEntryInfoCache[databankEntry] = new DatabankEntryInfo(entry, primaryKey, keys.ToList());
                }

                return readable;
            }
            catch
            {
            }

            entry = EncyEntry.None;
            primaryKey = string.Empty;
            keys = new List<string>();
            return false;
        }

        private void TryLogDatabankEntry(IntPtr databankEntry, EncyEntry knownEntry)
        {
            if (!EnableEnumDiscoveryLogs || databankEntry == IntPtr.Zero)
                return;

            try
            {
                string objectName = unrealHelper.GetUObjectName(databankEntry);
                string objectPath = unrealHelper.GetUObjectPath(databankEntry);

                if (!IsReadableUObjectText(objectName) || !IsReadableUObjectText(objectPath))
                    return;

                EncyEntry mappedEntry = knownEntry;
                if (mappedEntry == EncyEntry.None)
                    TryMapDatabankEntry(objectName, objectPath, out mappedEntry);

                LogDatabankEntry(objectName, objectPath, mappedEntry);
            }
            catch
            {
            }
        }

        private void LogDatabankEntry(string objectName, string objectPath, EncyEntry mappedEntry)
        {
            if (!EnableEnumDiscoveryLogs)
                return;

            string key = string.IsNullOrEmpty(objectPath) ? objectName : objectPath;
            if (string.IsNullOrWhiteSpace(key) || !loggedDatabankEntryAssets.Add(key))
                return;

            string candidate = BestEnumCandidate(DatabankEntryNameCandidates(objectName, objectPath));
            string mapped = mappedEntry == EncyEntry.None ? "<new>" : mappedEntry.ToString();
            logger.Log($"[EnumDiscovery][EncyEntry] candidate={candidate} mapped={mapped} name={objectName} path={objectPath}");
        }

        private bool TryMapDatabankEntry(string objectName, string objectPath, out EncyEntry entry)
        {
            foreach (string candidate in DatabankEntryNameCandidates(objectName, objectPath))
            {
                if (TryParseNamedEnum(candidate, out entry) && entry != EncyEntry.None && entry != EncyEntry.Any)
                    return true;

                foreach (string key in DatabankEntryKeyCandidates(candidate))
                    if (EncyEntryAliases.Value.TryGetValue(key, out entry) && entry != EncyEntry.None && entry != EncyEntry.Any)
                        return true;
            }

            entry = EncyEntry.None;
            return false;
        }

        private IEnumerable<string> DatabankEntryNameCandidates(string objectName, string objectPath)
        {
            foreach (string candidate in ParentQualifiedDatabankEntryNameCandidates(objectPath))
                yield return candidate;

            foreach (string value in new[] { objectName, objectPath, LastPathSegment(objectPath) })
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                foreach (string candidate in ExpandedDatabankEntryNameCandidates(value))
                    yield return candidate;
            }
        }

        private IEnumerable<string> ParentQualifiedDatabankEntryNameCandidates(string objectPath)
        {
            if (string.IsNullOrWhiteSpace(objectPath))
                yield break;

            string path = objectPath;
            int dot = path.LastIndexOf('.');
            if (dot >= 0)
                path = path.Substring(0, dot);

            string[] parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                yield break;

            string parent = NormalizeEnumCandidate(parts[parts.Length - 2]);
            string name = NormalizeEnumCandidate(parts[parts.Length - 1]);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
                yield break;

            yield return $"{parent}_{name}";
        }

        private static IEnumerable<string> ExpandedDatabankEntryNameCandidates(string value)
        {
            yield return value;

            string stripped = StripDatabankEntryName(value);
            yield return stripped;
            yield return NormalizeEnumCandidate(stripped);
            yield return stripped.Replace("_", "").Replace("-", "");

            int underscore = stripped.LastIndexOf('_');
            if (underscore >= 0 && underscore + 1 < stripped.Length)
            {
                string last = stripped.Substring(underscore + 1);
                yield return last;
                yield return NormalizeEnumCandidate(last);
                yield return last.Replace("_", "").Replace("-", "");
            }
        }

        private static IEnumerable<string> StoryGoalNameCandidates(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                yield break;

            yield return value;

            string stripped = StripStoryGoalName(value);
            yield return stripped;
            yield return stripped.Replace("_", "").Replace("-", "");
        }

        private static string StripStoryGoalName(string value)
        {
            string result = value;
            foreach (string prefix in new[] { "StoryGoal_", "SG_", "DA_", "DAT_", "Data_" })
            {
                if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(prefix.Length);
                    break;
                }
            }

            foreach (string suffix in new[] { "_C", "_StoryGoal", "StoryGoal", "_Data", "_DA" })
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length);
                    break;
                }
            }

            return result;
        }

        private static string StripDatabankEntryName(string value)
        {
            string result = value;
            bool changed;
            do
            {
                changed = false;
                foreach (string prefix in new[] { "DB_", "Databank_", "DatabankEntry_", "DataBankEntry_", "Ency_", "EncyEntry_", "Story_", "StoryGoal_", "SG_", "BP_", "DA_", "DAT_", "Data_" })
                {
                    if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        result = result.Substring(prefix.Length);
                        changed = true;
                        break;
                    }
                }

                foreach (string suffix in new[] { "_C", "_DatabankEntry", "DatabankEntry", "_DataBankEntry", "DataBankEntry", "_Databank", "Databank", "_Entry", "Entry", "_StoryGoal", "StoryGoal", "_Data", "_DA" })
                {
                    if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        result = result.Substring(0, result.Length - suffix.Length);
                        changed = true;
                        break;
                    }
                }
            }
            while (changed);

            return result;
        }

        private bool Needs(params SplitName[] required)
        {
            if (settings?.Splits == null || settings.Splits.Count == 0)
                return false;

            var usedSplitNames = new HashSet<SplitName>();

            foreach (var split in settings.Splits)
            {
                usedSplitNames.Add(split.SplitName);

                foreach (var conditionSplit in Subnautica2Component.GetAllConditions(split))
                    usedSplitNames.Add(conditionSplit.SplitName);
            }
            return required.Any(usedSplitNames.Contains);
        }
        #endregion Memory stuff
        #region World/Player Checks
        public bool ShouldPause() => loadRemovalActive;
        #endregion
    }

    public class InvChangeInfo
    {
        public int Count { get; set; }
        public Stopwatch ElapsedTime { get; }

        public InvChangeInfo(int count, Stopwatch elapsedTime)
        {
            Count = count;
            ElapsedTime = elapsedTime ?? throw new ArgumentNullException(nameof(elapsedTime));
        }
    }
}
