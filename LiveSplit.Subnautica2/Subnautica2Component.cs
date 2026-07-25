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
        private readonly Subnautica2Memory memory;
        private readonly LiveSplitState _state;
        private readonly TimerModel timerModel;
        public readonly HashSet<Subnautica2Split> alreadySplit = new HashSet<Subnautica2Split>();

        public Subnautica2Component(LiveSplitState state) : base(state)
        {
            logger = EnableDebugConsole
                ? (Logger)new ConsoleLogger()
                : new FileLogger("_" + Factory.ExAssembly.GetName().Name.Substring(10) + ".log");
            logger.StartLogger();

            Localization.Load();
            _state = state;
            settings = new Subnautica2Settings(state);
            memory = new Subnautica2Memory(state, this, logger, settings);
            timerModel = new TimerModel() { CurrentState = state };
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

            TryResetOnMainMenu();

            return true;
        }

        public override bool Start()
        {
            if (memory.startedTimerBefore || !memory.pointersInitialized)
                return false;

            

            return false;
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

        private void TryResetOnMainMenu()
        {
            return; // TODO: Add check for main menu and remove this return statement
            if (!settings.Reset)
                return;
            if (_state.CurrentPhase == TimerPhase.NotRunning)
                return;

            Form ui = _state.Form;
            Action doReset = () =>
            {
                bool GoldSegment = false;
                for (int index = 0; index < _state.Run.Count; index++)
                {
                    if (LiveSplitStateHelper.CheckBestSegment(_state, index, _state.CurrentTimingMethod))
                    {
                        GoldSegment = true;
                        break;
                    }
                }

                bool save = true;
                if (settings.AskForGoldSave && GoldSegment)
                {
                    DialogResult r = MessageBox.Show(
                        ui,
                        "Save splits before resetting?",
                        "Reset",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);

                    if (r == DialogResult.Cancel)
                        return;

                    save = (r == DialogResult.Yes);
                }

                timerModel.Reset(save);
            };

            if (ui.InvokeRequired)
                ui.BeginInvoke(doReset);
            else
                doReset();
        }

        public override void OnReset()
        {
            alreadySplit.Clear();
        }
    }
}
