using LiveSplit.Subnautica2;
using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Voxif.IO;

namespace Voxif.AutoSplitter {
    public abstract class Component : LogicComponent {

        protected enum EGameTime {
            None,
            Loading,
            GameTime
        }

        protected Subnautica2Settings settings;
        protected TimerModel timer;

        protected Logger logger;

        protected virtual SettingsInfo? StartSettings => new SettingsInfo(1, null);
        protected virtual SettingsInfo? ResetSettings => new SettingsInfo(1, null);
        protected virtual OptionsInfo? OptionsSettings => null;
        protected virtual EGameTime GameTimeType => EGameTime.None;
        protected virtual bool IsGameTimeDefault => true;

        public Component(LiveSplitState state) {
            timer = new TimerModel { CurrentState = state };
            timer.CurrentState.OnStart += OnStart;
            timer.CurrentState.OnSplit += OnSplit;
            timer.CurrentState.OnReset += OnReset;

            if(GameTimeType != EGameTime.None) {
                timer.InitializeGameTime();

                if(IsGameTimeDefault && state.CurrentTimingMethod == TimingMethod.RealTime) {
                    string gameName = LiveSplit.Subnautica2.Factory.ExAssembly.Description().Substring(17);
                    string timingName = GameTimeType == EGameTime.GameTime ? "In-Game Time" : "Time without Loads";
                    DialogResult result = MessageBox.Show(
                        String.Concat(gameName, " uses " + timingName + " as the main timing method.", Environment.NewLine,
                                      "LiveSplit is currently comparing against Real Time.", Environment.NewLine,
                                      "Would you like to set the timing method to Game Time? (Recommended)"),
                        gameName + " Timing Method",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if(result == DialogResult.Yes) {
                        state.CurrentTimingMethod = TimingMethod.GameTime;
                    }
                }
            }
        }

        public override string ComponentName => LiveSplit.Subnautica2.Factory.ExAssembly.FullComponentName();
        public override Control GetSettingsControl(LayoutMode mode) => settings;
        public override XmlNode GetSettings(XmlDocument document) => settings.UpdateSettings(document);
        public override void SetSettings(XmlNode settings) => this.settings.SetSettings(settings);
        public override void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode) {         
            if(!Update()) {
                return;
            }

            if (timer.CurrentState.CurrentSplitIndex >= 0)
            {
                timer.CurrentState.IsGameTimePaused = Loading();

                if (settings.Reset && Reset())
                {
                    timer.Reset();
                    logger?.Log("Reset");
                }
                else if (Split())
                {
                    timer.Split();
                }
            }

            if(timer.CurrentState.CurrentSplitIndex < 0) {
                if((settings.IntroStart || settings.CreativeStart) && Start()) {
                    timer.Start();
                    logger?.Log("Start");
                }
            }
        }

        public abstract bool Update();
        public virtual bool Start() => false;
        public virtual bool Split() => false;
        public virtual bool Reset() => false;
        public virtual bool Loading() => false;
        public virtual TimeSpan? GameTime() => null;

        private void OnStart(object sender, EventArgs e) {
            if(GameTimeType == EGameTime.Loading) {
                timer.CurrentState.IsGameTimePaused = Loading();
                timer.CurrentState.SetGameTime(TimeSpan.Zero);
            } else if(GameTimeType == EGameTime.GameTime) {
                timer.CurrentState.IsGameTimePaused = true;
                timer.CurrentState.SetGameTime(GameTime());
            }
            OnStart();
        }
        private void OnSplit(object sender, EventArgs e) => OnSplit();
        private void OnReset(object sender, TimerPhase e) => OnReset();

        public virtual void OnStart()
        {
            if (!timer.CurrentState.IsGameTimeInitialized)
                timer.InitializeGameTime();
        }

        public virtual void OnSplit() { }
        public virtual void OnReset() { }
        
        public override void Dispose() {
            logger?.Log("Dispose");
            timer.CurrentState.OnStart -= OnStart;
            timer.CurrentState.OnSplit -= OnSplit;
            timer.CurrentState.OnReset -= OnReset;
            logger?.StopLogger();
        }

        public static string[] GetEnumDescriptions<E>() where E : Enum {
            return GetEnumAttributes<E, DescriptionAttribute>().Select(a => a.Description).ToArray();
        }
        public static Type[] GetEnumTypes<E>() where E : Enum {
            return GetEnumAttributes<E, TypeAttribute>().Select(a => a.Type).ToArray();
        }
        public static IEnumerable<A> GetEnumAttributes<E, A>() where E : Enum where A : Attribute {
            return typeof(E).GetMembers().SelectMany(m => m.GetCustomAttributes(typeof(A), false)).Cast<A>();
        }

        public static Control[] CreateControlsFromEnum<E>() where E : Enum {
            string[] names = Enum.GetNames(typeof(E));
            string[] descs = GetEnumDescriptions<E>();
            Type[] types = GetEnumTypes<E>();
            Control[] controls = new Control[types.Length];
            for(int i = 0; i < types.Length; i++) {
                Control control = (Control)Activator.CreateInstance(Type.GetType(types[i].ToString()), new object[] { });
                control.Name = names[i];
                control.Text = descs[i];
                controls[i] = control;
            }
            return controls;
        }

        public class RemainingHashSet<T> : HashSet<T> {
            protected Logger logger;

            public RemainingHashSet(Logger logger = null) {
                this.logger = logger;
            }

            public bool Split(T split) {
                logger?.Log("Try to split: " + split);
                return Remove(split);
            }
        }

        public class RemainingDictionary : Dictionary<string, HashSet<string>> {
            protected Logger logger;

            public RemainingDictionary(Logger logger = null) {
                this.logger = logger;
            }

            public void Setup(HashSet<string> splits) {
                Clear();

                foreach(string split in splits) {
                    int typeSeparator = split.IndexOf('_');

                    if(typeSeparator != -1) {
                        string type = split.Substring(0, typeSeparator);
                        if(!ContainsKey(type)) {
                            Add(type, new HashSet<string>());
                        }
                        string setting = split.Substring(typeSeparator + 1);
                        this[type].Add(setting);
                    } else {
                        Add(split, null);
                    }
                }
            }

            public bool Split(string type, object setting) {
                logger?.Log("Try to split: " + type + ", " + setting);
                if(this[type].Remove(setting.ToString())) {
                    if(this[type].Count == 0) {
                        Remove(type);
                    }
                    return true;
                }
                return false;
            }

            public bool Split(string type) {
                logger?.Log("Try to split type: " + type);
                return Remove(type);
            }
        }
    }

    public struct SettingsInfo {
        public int def;
        public string[] values;

        public SettingsInfo(int def, string[] values) {
            this.def = def;
            this.values = values;
        }
    }

    public struct OptionsInfo {
        public string[] def;
        public Control[] values;

        public OptionsInfo(string[] def, Control[] values) {
            this.def = def;
            this.values = values;
        }
    }

    public class TypeAttribute : Attribute {
        public Type Type { get; }
        public TypeAttribute(Type type) {
            Type = type;
        }
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    public class ComponentNameAttribute : Attribute {
        public string Value { get; private set; }
        public ComponentNameAttribute(string value) {
            Value = value;
        }
    }
}