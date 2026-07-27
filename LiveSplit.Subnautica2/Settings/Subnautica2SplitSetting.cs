using LiveSplit.Subnautica2.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LiveSplit.Subnautica2
{
    public class Subnautica2SplitSetting : UserControl
    {
        public Func<bool> IsLoadingGetter { get; set; }
        public bool IsLoading => IsLoadingGetter?.Invoke() ?? false;
        public bool IsSubCondition { get; set; } = false;
        public virtual ComboBox ComboBox { get; }
        public virtual ComboBox ComboBox2 { get; }
        public virtual Button BtnEdit { get; }
        public virtual Button BtnRemove { get; }
        public virtual SplitName SplitName { get; }
        public virtual Subnautica2Split Split { get; }

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
        [Description("Angel Comb Adaptation"), ToolTip("Splits when the Animation to Interact with an Angel Comb Begins (Delayed Slightly Compared to Interaction)")]
        AngelCombAdaptation,
        [Description("Biobed Adaptation"), ToolTip("Splits on Any Biobed Adaptations (Endurance, Dexterity) for Inventory or Toolbar Expansion")]
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
        [Description("Split on 2nd Base"), ToolTip("Splits when you Build a Room in the Observatory Biome after Unlocking HumanOutpost_Detected Story Goal (Only works for NME Base Location)")]
        SecondBase,
        [Description("Split on Enter Tadpole after 2nd Base"), ToolTip("Splits when you Enter a Tadpole in the Observatory Biome after Unlocking HumanOutpost_Detected Story Goal (Only works for NME Base Location)")]
        EnterTadpoleAfterSecondBase,
        [Description("Sonic Resonator Blast"), ToolTip("Splits when a Sonic Resonator Blast Completes after Firing")]
        SonicResonatorBlastShot,
        [Description("(End) Obesrvatory Button"), ToolTip("Splits when the Final Observatory Button is Pressed")]
        FinalObservatoryButtonPress,
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
