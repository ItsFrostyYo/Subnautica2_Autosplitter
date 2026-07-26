using LiveSplit.Model;
using LiveSplit.Options;
using LiveSplit.Subnautica2;
using LiveSplit.UI.Components;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Voxif.AutoSplitter;
using Voxif.IO;

namespace LiveSplit.Subnautica2
{
    public class Subnautica2Component : Voxif.AutoSplitter.Component
    {
        private const bool EnableDebugConsole = true;
        protected override EGameTime GameTimeType => EGameTime.Loading;
        private readonly Subnautica2Memory memory;
        private readonly LiveSplitState _state;
        public readonly HashSet<Subnautica2Split> alreadySplit = new HashSet<Subnautica2Split>();

        public Subnautica2Component(LiveSplitState state) : base(state)
        {
            string logPath = "_" + Factory.ExAssembly.GetName().Name.Substring(10) + ".log";
            logger = EnableDebugConsole
                ? (Logger)new CompositeLogger(new ConsoleLogger(), new FileLogger(logPath))
                : new FileLogger(logPath);
            logger.StartLogger();

            Localization.Load();
            _state = state;
            settings = new Subnautica2Settings(state);
            memory = new Subnautica2Memory(state, this, logger, settings);
        }

        public override bool Update()
        {
            bool ok;

            try
            {
                ok = memory.Update();
            }
            catch (Win32Exception ex)
            {
                logger.Log($"Win32Exception in memory.Update: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                logger.Log($"Unexpected exception in memory.Update: {ex}");
                return false;
            }

            if (!ok || !memory.pointersInitialized)
                return false;


            return true;
        }

        public override void Dispose()
        {
            // The base component only detaches timer callbacks. Dispose the
            // memory reader first so its UE5 event hook is restored while the
            // game process and logger are still available.
            memory?.Dispose();
            base.Dispose();
        }

        public override bool Start()
        {
            if (memory.startedTimerBefore || !memory.pointersInitialized)
                return false;

            bool survivalStart = settings.IntroStart && memory.SurvivalStartTriggered();
            bool creativeStart = settings.CreativeStart && memory.CreativeStartTriggered();
            if (!survivalStart && !creativeStart)
                return false;

            memory.startedTimerBefore = true;
            logger.Log(survivalStart ? "Survival Start triggered" : "Creative Start triggered");
            return true;
        }

        public override bool Reset() => memory.MainMenuEntered();

        protected override bool PerformReset()
        {
            bool hasGoldSegment = settings.AskForGoldSave
                && Enumerable.Range(0, _state.Run.Count)
                    .Any(index => LiveSplitStateHelper.CheckBestSegment(
                        _state, index, _state.CurrentTimingMethod));

            if (!hasGoldSegment)
            {
                timer.Reset();
                return true;
            }

            DialogResult result = MessageBox.Show(
                _state.Form,
                "Save splits before resetting?",
                "Subnautica 2 Auto Reset",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (result == DialogResult.Cancel)
                return false;

            timer.Reset(result == DialogResult.Yes);
            return true;
        }

        public override bool Split()
        {
            if (!memory.pointersInitialized)
                return false;

            var splits = settings.Splits;
            
            for (int i = 0; i < splits.Count; i++)
            {
                if ((Subnautica2Settings.OrderedAutoSplits && i != alreadySplit.Count) || (Subnautica2Settings.OrderedLiveSplit && i != _state.CurrentSplitIndex))
                    continue;

                var split = splits[i];

                IEnumerable<Subnautica2Split> conditionsSplits = GetAllConditions(split);
                bool allConditionsMet = true;

                foreach (var conditionSplit in conditionsSplits)
                {
                    memory.CurrentSplitToCheck = conditionSplit;
                    if (memory.subConditions.TryGetValue(conditionSplit.SplitName, out var subCondition) && !subCondition())
                    {
                        allConditionsMet = false;
                        break;
                    }
                }

                memory.CurrentSplitToCheck = split;
                if (allConditionsMet 
                    && memory.splitConditions.TryGetValue(split.SplitName, out var condition) 
                    && condition()
                    && !(split.OnlySplitOnce && !Subnautica2Settings.OrderedAutoSplits && !Subnautica2Settings.OrderedLiveSplit && alreadySplit.Contains(split)))
                {
                    alreadySplit.Add(split);
                    logger.Log($"{split.GetDescription()} triggered");
                    return true;
                }
            }
            return false;
        }

        public static IEnumerable<Subnautica2Split> GetAllConditions(Subnautica2Split split)
        {
            if (split?.Conditions == null)
                yield break;

            foreach (var c in split.Conditions.Where(c => c.IsSubCondition))
            {
                yield return c;

                foreach (var nested in GetAllConditions(c))
                    yield return nested;
            }
        }

        public override bool Loading() => memory.ShouldPause();

        public override void OnReset()
        {
            alreadySplit.Clear();
        }
    }
}
