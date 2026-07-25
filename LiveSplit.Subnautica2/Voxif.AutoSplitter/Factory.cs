using LiveSplit.Subnautica2;
using LiveSplit.Model;
using LiveSplit.UI.Components;
using System;
using System.Reflection;

namespace LiveSplit.Subnautica2 {
    public class Factory : IComponentFactory {
        public string ComponentName => "Subnautica 2 Autosplitter";

        public string Description => "Autosplitter for Subnautica 2";

        public ComponentCategory Category => ComponentCategory.Control;

        public string UpdateName => ComponentName;

        public string XMLURL => UpdateURL + "Components/Subnautica2.Updates.xml";

        public string UpdateURL => "https://raw.githubusercontent.com/Sprinter31/LiveSplit.Subnautica2/master/";

        public Version Version => ExAssembly.GetName().Version;

        public IComponent Create(LiveSplitState state) => new Subnautica2Component(state);

        public static Assembly ExAssembly = Assembly.GetExecutingAssembly();
    }
}