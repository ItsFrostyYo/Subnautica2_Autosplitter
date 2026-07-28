using LiveSplit.Subnautica2.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LiveSplit.Subnautica2
{
    public class Subnautica2SplitSetting : UserControl
    {
        private sealed class ComboSearchState
        {
            public DataTable Table;
            public DataView View;
            public bool Updating;
            public bool OpeningSearchResults;
        }

        private static readonly ConditionalWeakTable<ComboBox, ComboSearchState> ComboSearchStates =
            new ConditionalWeakTable<ComboBox, ComboSearchState>();

        public Func<bool> IsLoadingGetter { get; set; }
        public bool IsLoading => IsLoadingGetter?.Invoke() ?? false;
        public bool IsSubCondition { get; set; } = false;
        public virtual ComboBox ComboBox { get; }
        public virtual ComboBox ComboBox2 { get; }
        public virtual Button BtnEdit { get; }
        public virtual Button BtnRemove { get; }
        public virtual SplitName SplitName { get; }
        public virtual Subnautica2Split Split { get; }

        public static void ConfigureSearchableCombo(ComboBox combo)
        {
            if (combo == null)
                return;

            combo.DropDownStyle = ComboBoxStyle.DropDown;
            combo.AutoCompleteMode = AutoCompleteMode.None;
            combo.AutoCompleteSource = AutoCompleteSource.None;
            combo.Width = 343;

            ComboSearchStates.GetValue(combo, key => new ComboSearchState());
            combo.TextUpdate -= ShowComboSearchResults;
            combo.TextUpdate += ShowComboSearchResults;
            combo.DropDown -= RestoreFullComboList;
            combo.DropDown += RestoreFullComboList;
            RefreshComboSearchIndex(combo);
        }

        public static void RefreshComboSearchIndex(ComboBox combo)
        {
            if (combo == null || !ComboSearchStates.TryGetValue(combo, out ComboSearchState state))
                return;

            object selectedValue = combo.SelectedValue;
            string selectedText = combo.Text;
            var table = new DataTable { CaseSensitive = false };
            table.Columns.Add("Display", typeof(string));
            table.Columns.Add("Value", typeof(object));

            foreach (object item in combo.Items)
            {
                string display = combo.GetItemText(item)?.Trim() ?? string.Empty;
                object value = item.GetType().GetProperty(combo.ValueMember)?.GetValue(item) ?? item;
                table.Rows.Add(display, value);
            }

            state.Updating = true;
            try
            {
                state.Table = table;
                state.View = table.DefaultView;
                combo.DisplayMember = "Display";
                combo.ValueMember = "Value";
                combo.DataSource = state.View;

                if (selectedValue != null)
                    combo.SelectedValue = selectedValue;
                else if (!string.IsNullOrEmpty(selectedText))
                    combo.Text = selectedText;
            }
            finally
            {
                state.Updating = false;
            }
        }

        public static bool IsComboSearchUpdating(ComboBox combo) =>
            combo != null
            && ComboSearchStates.TryGetValue(combo, out ComboSearchState state)
            && state.Updating;

        private static void ShowComboSearchResults(object sender, EventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null
                || !ComboSearchStates.TryGetValue(combo, out ComboSearchState state)
                || state.View == null
                || state.Updating)
                return;

            string query = combo.Text ?? string.Empty;
            state.Updating = true;
            try
            {
                state.View.RowFilter = query.Length == 0
                    ? string.Empty
                    : "Display LIKE '%" + EscapeLikeValue(query.Trim()) + "%'";

                if (query.Length > 0 && combo.Items.Count > 0)
                {
                    state.OpeningSearchResults = true;
                    combo.DroppedDown = true;
                    state.OpeningSearchResults = false;
                }

                // Opening a bound ComboBox dropdown selects its first row. Clear
                // that implicit selection after opening so typing remains exactly
                // what the user entered and no result is auto-completed.
                combo.SelectedIndex = -1;
                combo.Text = query;
                combo.SelectionStart = query.Length;
                combo.SelectionLength = 0;
            }
            finally
            {
                state.OpeningSearchResults = false;
                state.Updating = false;
            }
        }

        private static string EscapeLikeValue(string value)
        {
            return value.Replace("'", "''")
                        .Replace("[", "[[]")
                        .Replace("%", "[%]")
                        .Replace("*", "[*]");
        }

        private static void RestoreFullComboList(object sender, EventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null
                || !ComboSearchStates.TryGetValue(combo, out ComboSearchState state)
                || state.View == null
                || state.OpeningSearchResults
                || string.IsNullOrEmpty(state.View.RowFilter))
                return;

            object selectedValue = combo.SelectedValue;
            string text = combo.Text;
            state.Updating = true;
            try
            {
                state.View.RowFilter = string.Empty;
                if (selectedValue != null)
                    combo.SelectedValue = selectedValue;
                else
                    combo.Text = text;
            }
            finally
            {
                state.Updating = false;
            }
        }

        public static SplitName GetSplitName(string text)
        {
            foreach (SplitName split in Enum.GetValues(typeof(SplitName)))
            {
                string name = split.ToString();
                MemberInfo info = typeof(SplitName).GetMember(name)[0];
                DescriptionAttribute description = (DescriptionAttribute)info.GetCustomAttributes(typeof(DescriptionAttribute), false)[0];

                if (name.Equals(text, StringComparison.OrdinalIgnoreCase) || description.Description.Equals(text, StringComparison.OrdinalIgnoreCase))
                {
                    return split;
                }
            }
            return SplitName.None;
        }

        public static InventoryItem GetInventoryItem(string text) => GetEnumValue(text, InventoryItem.None);

        public static Craftable GetCraftable(string text) => GetEnumValue(text, Craftable.None);

        public static Buildable GetBuildable(string text) => GetEnumValue(text, Buildable.None);

        public static EncyEntry GetEncyEntry(string text) => GetEnumValue(text, EncyEntry.None);

        public static StoryGoal GetStoryGoal(string text) => GetEnumValue(text, StoryGoal.None);

        public static Unlockable GetUnlockable(string text) => GetEnumValue(text, Unlockable.None);

        public static Biome GetBiome(string text) => GetEnumValue(text, Biome.None);

        private static TEnum GetEnumValue<TEnum>(string text, TEnum none)
        {
            foreach (TEnum value in Enum.GetValues(typeof(TEnum)))
            {
                string name = value.ToString();
                string displayName = Localization.GetDisplayName(name);

                if (name.Equals(text, StringComparison.OrdinalIgnoreCase) || displayName.Equals(text, StringComparison.OrdinalIgnoreCase))
                    return value;
            }

            return none;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
                DoDragDrop(this, DragDropEffects.Move);
        }
    }    
    
    public class Subnautica2Split
    {
        public SplitName SplitName { get; set; }
        public bool OnlySplitOnce { get; set; }
        public bool IsSubCondition { get; set; }
        public List<Subnautica2Split> Conditions { get; set; } = new List<Subnautica2Split>();
        public virtual string GetDescription() => "Split";
        public virtual Subnautica2Split DeepCopy()
        {
            var copy = (Subnautica2Split)this.MemberwiseClone();
            copy.Conditions = Conditions?.Select(c => c.DeepCopy()).ToList() ?? new List<Subnautica2Split>();

            return copy;
        }
    }

    public enum SplitName
    {
        [Description("None"), ToolTip("None")]
        None,
        [Description("Inventory"), ToolTip("Splits when you Pickup/Drop a Certain Item")]
        Inventory,
        [Description("Blueprint"), ToolTip("Splits when you Have/Unlock a Certain Blueprint")]
        Blueprint,
        [Description("Encyclopedia"), ToolTip("Splits when you Have/Unlock a Certain Databank Entry in the Encyclopedia")]
        Encyclopedia,
        [Description("Story Goal"), ToolTip("Splits when you Have/Unlock a Certain Story Goal")]
        StoryGoal,
        [Description("Biome"), ToolTip("Splits when you Transition from a Certain Biome to another Certain Biome, or Condition a Split to a Specific Biome")]
        Biome,
        [Description("Craft"), ToolTip("Splits when you Start Crafting an Item inside a Fabricator, Vehicle Fabricator, Modification Station or Processor Station")]
        Craft,
        [Description("Build"), ToolTip("Splits when you Complete Building a Constructable using a Builder Tool")]
        Build,
        [Description("(Adaptation) Interact with Angel Comb"), ToolTip("Splits when the Animation to Interact with an Angel Comb Begins (Delayed Slightly Compared to Interaction)")]
        AngelCombAdaptation,
        [Description("(Adaptation) Claim Abandoned Biobed"), ToolTip("Splits on Any Biobed Adaptations (Endurance, Dexterity) for Inventory or Toolbar Expansion")]
        BiobedAdaptation,
        [Description("(Intro) Unlock Door"), ToolTip("Splits when the Locked Door is Unlocked During the Intro")]
        IntroUnlockDoor,
        [Description("(Intro) Analyze Button Press"), ToolTip("Splits when the Analyzer Button is Pressed during the Intro")]
        IntroAnalyzingButtonPress,
        [Description("(Intro) Release Lifepod"), ToolTip("Splits when the Intro Lifepod is Release after Pressing the Levers")]
        IntroReleaseLifepod,
        [Description("(Intro) Lifepod Ascend"), ToolTip("Splits when the Intro Lifepod Begins Ascending")]
        IntroLifepodAscend,
        [Description("Repair Turbine"), ToolTip("Splits when the Power Plant Turbine is Fully Repaired")]
        RepairTurbine,
        [Description("Enter Tadpole"), ToolTip("Splits when you Enter a Tadpole")]
        EnterTadpole,
        [Description("Second Base [NME]"), ToolTip("Splits when you Build a Hatch in the Observatory Biome after Unlocking HumanOutpost_Detected Story Goal (Only works for NME Base Location)")]
        SecondBase,
        [Description("Enter Tadpole after Second Base [NME]"), ToolTip("Splits when you Enter a Tadpole in the Observatory Biome after Unlocking HumanOutpost_Detected Story Goal (Only works for NME Base Location)")]
        EnterTadpoleAfterSecondBase,
        [Description("(Misc) Sonic Resonator Blast"), ToolTip("Splits when a Sonic Resonator Blast Completes after Firing")]
        SonicResonatorBlastShot,
        [Description("Scan Rosetta Stone"), ToolTip("Splits when you Scan the Rosetta Stone and Unlock Story Goal Rosetta_TranslationUnlocked")]
        ScanRosettaStone,
        [Description("(End) Obesrvatory Button Press"), ToolTip("Splits when the Final Observatory Button is Pressed")]
        FinalObservatoryButtonPress,
        [Description("Build Processor"), ToolTip("Splits when a Processor Station is attached after being built")]
        BuildProcessor,
    }
    public class ToolTipAttribute : Attribute
    {
        public string ToolTip { get; set; }
        public ToolTipAttribute(string text)
        {
            ToolTip = text;
        }
    }
}
