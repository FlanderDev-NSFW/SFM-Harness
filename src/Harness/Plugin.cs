using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using RestraintHub;

namespace Harness
{
    /// <summary>A wearable harness: belt, thigh hoops and two pistons driven by crossed cords. Model authored in the 3D model repository.</summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Hub.Guid, "^1.1.0")]
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "FlanderDev.SFM.Harness";
        public const string PluginName = "Harness";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Logger;
        internal static Restraint Entry;
        public static ConfigEntry<float> AttemptSeconds, SuccessChance, HoopAlongThigh, ScaleTrim;

        public override void Load()
        {
            Logger = Log;
            AttemptSeconds = Config.Bind("Unlock", "AttemptSeconds", 4.5f, "Seconds of holding F per attempt to get free.");
            SuccessChance = Config.Bind("Unlock", "SuccessChance", 1f, "Chance that an attempt frees her.");
            HoopAlongThigh = Config.Bind("Fit", "HoopAlongThigh", 0.6f, "Where the hoop sits, as a share of the way from hip to knee.");
            ScaleTrim = Config.Bind("Fit", "ScaleTrim", 1f, "Multiplies the model's size.");

            ClassInjector.RegisterTypeInIl2Cpp<HarnessController>();
            AddComponent<HarnessController>();

            Entry = new Restraint
            {
                Id = "Harness",
                RingValue = 9700,
                Label = () => "Harness",
                ShortcutSaveKey = "Harness.Shortcuts",
                OnSave = Patches.Save,
                OnLoad = Patches.Load,
                OnNewGame = () => HarnessController.Instance?.OnNewGame(),
                Holds = BodyParts.None,
                IsWorn = () => HarnessController.Instance != null && HarnessController.Instance.Machine.Worn,
                Blocks = () => RestraintHub.Blocks.None,
                OnChosen = () => HarnessController.Instance?.OnRingChosen(),
                Attempt = () => HarnessController.Instance == null ? 0f : HarnessController.Instance.Machine.Attempt,
                PutOn = () => HarnessController.Instance == null ? "not ready" : HarnessController.Instance.PutOn(),
                TakeOff = () => HarnessController.Instance?.TakeOff(),
            };
            Hub.Register(Entry);
            Logger.LogInfo("[SFM-ACCEPT] load: " + PluginName + " " + PluginVersion + " ready");
        }
    }
}
