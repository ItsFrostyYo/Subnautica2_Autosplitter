using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Voxif.Helpers.Unreal;
using Voxif.Memory;

namespace LiveSplit.Subnautica2.UE5Events
{
    /// <summary>
    /// Subnautica 2's central UE5 event registry.
    ///
    /// Add event definitions in ConfigureEvents(). The rest of the autosplitter
    /// can then read them by name through Triggered(), Delta(), TryGet(), or the
    /// indexer without knowing anything about the native ProcessEvent hook.
    /// </summary>
    public sealed class Subnautica2Ue5EventRegistry : IDisposable
    {
        private static readonly TimeSpan RegistrationRetryInterval = TimeSpan.FromSeconds(5);

        private sealed class EventDefinition
        {
            public readonly string Name;
            public readonly string ClassName;
            public readonly string ObjectName;
            public readonly string FunctionName;

            public Ue5FunctionFlag Flag;
            public DateTime NextRegistrationAttempt = DateTime.MinValue;

            public EventDefinition(
                string name,
                string className,
                string objectName,
                string functionName)
            {
                Name = name;
                ClassName = className;
                ObjectName = objectName;
                FunctionName = functionName;
            }
        }

        private readonly ProcessWrapper game;
        private readonly IUnrealHelper unreal;
        private readonly Action<string> log;
        private Ue5FunctionEvents reader;
        private Task<IReadOnlyDictionary<string, IReadOnlyList<int>>> nameResolutionTask;
        private EventDefinition[] nameResolutionDefinitions;
        private readonly Dictionary<string, EventDefinition> definitions =
            new Dictionary<string, EventDefinition>(StringComparer.Ordinal);

        private bool disposed;

        public Subnautica2Ue5EventRegistry(
            ProcessWrapper game,
            IUnrealHelper unreal,
            Action<string> log = null)
        {
            this.game = game ?? throw new ArgumentNullException(nameof(game));
            this.unreal = unreal ?? throw new ArgumentNullException(nameof(unreal));
            this.log = log;
            ConfigureEvents();
        }

        private void ConfigureEvents()
        {
            #region Add Events Here


            // Start/Reset Events
            Add("MainMenuConstruct", "WBP_MainLobbyScreen_C", null, "Construct");
            Add("CreativeStart", "BP_CreativeModePlayerStart_C", null, "OnStartConditionsApplied");
            Add("DeepStartCinematicTagRemoved", "BPC_DeepStartIntro_C", null, "Removed_D54A696449B06FC2AE42B1971D15CB2F");
            // Start on Input/Interaction (Unused Currently)
            Add("InteractWithStorage", "BP_Character_01_C", "BP_Character_01_C", "OnInteractWithOtherInventory");
            Add("FirstMovement", "GA_Walk_C", "GA_Walk_C", "OnStarted_CD32F07B44EEF144D8A18C86FCFC3E47");
            Add("InteractWithFabricator", "WBP_FabricatorScreen_C", "WBP_FabricatorScreen_C", "RecipeListEntriesRefreshed");
            Add("FirstJump", "BP_Character_01_C", "BP_Character_01_C", "OnJumped");
            Add("OpenPDA", "WBP_Inventory_C", "WBP_Inventory_C", "ExecuteUbergraph_WBP_Inventory");
            Add("InteractWithNoA", "WBP_ComputerTextInterface_C", "WBP_ComputerTextInterface_C", "UpdateDialogueOptions");
            Add("FirstSwim", "GA_Swim_C", "GA_Swim_C", "OnStarted_E7B4EFF4450EE32D27781F951D040059");
            Add("InteractWithBiomodStation", "WBP_CharacterCustomizationScreen_C", "WBP_CharacterCustomizationScreen_C", "ValidItemsChanged");
            
            // Prefabricated Split Events
            // Adaptations
            Add("AdaptationRippleAfterInteraction", "BP_AngelCombCore_Ripple_NotifyState_C", null, "Received_NotifyBegin");
            Add("BiobedAdaptationInventory", "SN2PlayerUpgradesPlayerStateComponent", null, "OnEventTrackerIncreaseInventoryEvent");
            Add("BiobedAdaptationToolbar", "SN2PlayerUpgradesPlayerStateComponent", null, "OnEventTrackerIncreaseToolbarEvent");
            // Intro 
            Add("IntroUnlockDoor", "BP_SlidingDoor_C", "BP_SlidingDoor_C_UAID_C87F54AE2B72FF0402", "BndEvt__BP_SlidingDoor_LockComponent_K2Node_ComponentBoundEvent_0_LockDelegate__DelegateSignature");
            Add("IntroAnalyzingButtonPress", "BP_ScanningButton_C", null, "BroadcastButtonPressed");
            Add("IntroFirstLeverPress", "BP_LifepodBay_Lever_C", "BP_LifepodBay_Lever_C_UAID_14AC60D60A5A096C02", "BroadcastButtonPressed");
            Add("IntroReleaseLifepod", "BP_LifepodBay_Chunk_Hatch_C", null, "RightLever");
            // Misc
            Add("EnterTadpole", "BP_Tadpole_C", null, "OnPilotEntered_BP");
            Add("SonicResonatorBlastShot", "GA_SonicResonator_Blast_C", null, "OnCompleted_B65B54F241049DF1F76DA59AAF9E5B09");
            Add("RepairTurbine", "BP_PowerPlant_FixedTurbineBlade_C", null, "OnUnlocked_8DED1B5341E414D6BCD2C6B91609A9C5");
            Add("BuildHatch", "BP_BaseHatch_C", "BP_BaseHatch_C", "BndEvt__BP_BaseHatch_UWEAttachable_K2Node_ComponentBoundEvent_0_OnAttached__DelegateSignature");
            Add("BuildProcessor", "BP_ProcessorStation_C", "BP_ProcessorStation_C", "OnAttached");
            // Databank progression. These are notifications only; the memory reader resolves the concrete entry from SN2DatabankViewModel.
            Add("DatabankStoryGoalUnlocked", "SN2DatabankViewModel", null, "OnStoryGoalUnlocked");
            Add("DatabankScanCompleted", "UWEScannedActorsComponent", null, "OnScanCompletedEventFired");
            // End
            Add("FinalObservatoryButtonPress", "BP_Hologram_AxumFinale_Button_C", null, "ToggledOn");

            // Load Removal Events
            Add("IntroLifepodAscend", "BP_NarrativeSignal_C", null, "OnUnlocked_62920D1448BD71509596E5B554437304");
            Add("NoATerminalScreenClosed", "WBP_ComputerTextInterface_C", null, "BP_OnDeactivated");
            Add("IntroCutsceneLoadRemovalEnd", "BP_LifepodManager_C", null, "OnSequenceEnd");
  
            // [BP_BlightNode_C] [BP_BlightNode_C_UAID_C87F54AE2B72E68402] [OnBroken]
            // [BP_BlightNode_C] [BP_BlightNode_C_UAID_C87F54AE2B72E68402] [OnSequencePlay]
            //
            //
            //
            //
            //
            //
            //
            //
            //
            //
            //
            //
            //

            #endregion Add Events Here
        }

        private void Add(
            string name,
            string className,
            string objectName,
            string functionName)
        {
            if (String.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An event name is required.", nameof(name));
            if (functionName == null)
                throw new ArgumentNullException(nameof(functionName));
            if (definitions.ContainsKey(name))
                throw new InvalidOperationException("A UE5 event named '" + name + "' already exists.");

            definitions.Add(
                name,
                new EventDefinition(name, className, objectName, functionName));
        }

        public int Count => definitions.Count;

        public Ue5FunctionFlag this[string name]
        {
            get
            {
                if (!TryGet(name, out Ue5FunctionFlag flag))
                    throw new KeyNotFoundException(
                        "UE5 event '" + name + "' is not registered yet or does not exist.");

                return flag;
            }
        }

        public bool TryGet(string name, out Ue5FunctionFlag flag)
        {
            flag = null;

            return name != null &&
                definitions.TryGetValue(name, out EventDefinition definition) &&
                (flag = definition.Flag) != null;
        }

        /// <summary>
        /// True only on an update where the named function was observed.
        /// Returns false while the event is still waiting to register.
        /// </summary>
        public bool Triggered(string name)
        {
            return TryGet(name, out Ue5FunctionFlag flag) && flag.Triggered;
        }

        /// <summary>
        /// Number of matching calls observed since the previous update.
        /// Returns zero while the event is still waiting to register.
        /// </summary>
        public ulong Delta(string name)
        {
            return TryGet(name, out Ue5FunctionFlag flag) ? flag.Delta : 0UL;
        }

        public void Update()
        {
            ThrowIfDisposed();

            // With no configured events, do absolutely nothing. In particular,
            // do not scan, allocate memory, or install a ProcessEvent hook.
            if (definitions.Count == 0)
                return;

            EnsureReader();
            TryRegisterPendingEvents();
            reader.Update();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            definitions.Clear();
            reader?.Dispose();
            reader = null;
        }

        private void EnsureReader()
        {
            if (reader == null)
                reader = new Ue5FunctionEvents(game, unreal, log);
        }

        private void TryRegisterPendingEvents()
        {
            if (nameResolutionTask != null)
            {
                if (!nameResolutionTask.IsCompleted)
                    return;

                Task<IReadOnlyDictionary<string, IReadOnlyList<int>>> completedTask = nameResolutionTask;
                EventDefinition[] completedDefinitions = nameResolutionDefinitions;
                nameResolutionTask = null;
                nameResolutionDefinitions = null;

                IReadOnlyDictionary<string, IReadOnlyList<int>> resolved;
                try
                {
                    resolved = completedTask.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    log?.Invoke("[UE5Events] Name resolution failed; retrying later: " + ex.Message);
                    return;
                }

                RegisterResolvedEvents(completedDefinitions, resolved);
                return;
            }

            DateTime now = DateTime.UtcNow;
            EventDefinition[] pending = definitions.Values
                .Where(definition => definition.Flag == null && now >= definition.NextRegistrationAttempt)
                .ToArray();

            if (pending.Length == 0)
                return;

            foreach (EventDefinition definition in pending)
                definition.NextRegistrationAttempt = now.Add(RegistrationRetryInterval);

            string[] patterns = pending
                .SelectMany(definition => new[]
                    {
                        definition.ClassName,
                        definition.ObjectName,
                        definition.FunctionName,
                    })
                .Where(pattern => pattern != null)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            nameResolutionDefinitions = pending;
            Ue5FunctionEvents activeReader = reader;
            nameResolutionTask = Task.Run(() => activeReader.ResolveFNamePatterns(patterns));
        }

        private void RegisterResolvedEvents(
            IEnumerable<EventDefinition> pending,
            IReadOnlyDictionary<string, IReadOnlyList<int>> resolved)
        {
            foreach (EventDefinition definition in pending)
            {
                if (definition.Flag != null)
                    continue;

                try
                {
                    definition.Flag = reader.FunctionFlag(
                        definition.Name,
                        definition.ClassName,
                        definition.ObjectName,
                        definition.FunctionName,
                        resolved);

                    log?.Invoke("[UE5Events] '" + definition.Name + "' registered");
                }
                catch (Exception ex)
                {
                    log?.Invoke(
                        "[UE5Events] '" + definition.Name +
                        "' is waiting to register; retrying later: " + ex.Message);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(Subnautica2Ue5EventRegistry));
        }
    }
}
