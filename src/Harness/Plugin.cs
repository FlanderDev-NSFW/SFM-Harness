using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Harness.Logic;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using RestraintHub;

namespace Harness
{
    /// <summary>A wearable harness: belt, thigh hoops and two pistons driven by crossed cords. Model authored in the 3D model repository.</summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Hub.Guid, "^2.1.0")] // RestraintHub 2.1: the panel (U), PutOn / TakeOff, mission settings, arousal
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "FlanderDev.SFM.Harness";
        public const string PluginName = "Harness";
        public const string PluginVersion = "2.1.0";

        internal static ManualLogSource Logger;
        internal static Wearable Entry;
        public static ConfigEntry<float> AttemptSeconds, SuccessChance, HoopAlongThigh, ScaleTrim, GuideDrop, Size, BeltHeight, PelvisSize, FrontSeat;
        public static ConfigEntry<DriveMode> Mode;
        public static ConfigEntry<float> ArouseFront, ArouseRear, ArouseMinPush;

        public override void Load()
        {
            Logger = Log;
            AttemptSeconds = Config.Bind("Unlock", "AttemptSeconds", 4.5f, "Seconds of holding F per attempt to get free.");
            SuccessChance = Config.Bind("Unlock", "SuccessChance", 1f, "Chance that an attempt frees her.");
            HoopAlongThigh = Config.Bind("Fit", "HoopAlongThigh", 0.6f, "Where the hoop sits, as a share of the way from hip to knee.");
            ScaleTrim = Config.Bind("Fit", "ScaleTrim", 1f, "Multiplies the model's size.");

            BeltHeight = Config.Bind("Fit", "BeltHeight", 0.55f, "Where the belt sits, from hip joints (0) to chest (1).");
            PelvisSize = Config.Bind("Fit", "PelvisSize", 0.6f, "Pistons and guides, against the rest of the harness.");
            Mode = Config.Bind("Drive", "Mode", DriveMode.Gamma, "Gamma: the crossed cords, both pistons pull when her legs part. Zeta: both cords run to her right thigh, one behind and one in front, so the pistons take turns in step with her stride.");
            ArouseFront = Config.Bind("Arousal", "FrontPerPush", 0.024f, "How much one full push in of the front piston raises her ecstasy gauge (0 - 1 of the gauge; partial counts too).");
            ArouseRear = Config.Bind("Arousal", "RearPerPush", 0.024f, "The same for the rear piston.");
            ArouseMinPush = Config.Bind("Arousal", "MinPush", 0.1f, "Pushes shorter than this share of the full stroke add nothing (0 - 1).");
            FrontSeat = Config.Bind("Fit", "FrontSeat", 0f, "Moves the front guide out (+) or in (-), in mm, from where the fit puts it.");
            Size = Config.Bind("Fit", "Size", 0.78f, "Overall size of the hardware and straps, against her body.");
            GuideDrop = Config.Bind("Fit", "GuideDrop", 0.03f, "How far the centre of each guide band sits below its anchor in her pelvis, in model metres.");

            ClassInjector.RegisterTypeInIl2Cpp<HarnessController>();
            AddComponent<HarnessController>();

            Entry = new Wearable
            {
                Id = "Harness",
                Label = () => "Harness",
                OnSave = Patches.Save,
                OnLoad = Patches.Load,
                OnNewGame = () => HarnessController.Instance?.OnNewGame(),
                Holds = BodyParts.None,
                IsWorn = () => HarnessController.Instance != null && HarnessController.Instance.Machine.Worn,
                Blocks = () => RestraintHub.Blocks.None,
                Attempt = () => HarnessController.Instance == null ? 0f : HarnessController.Instance.Machine.Attempt,
                PutOn = () => HarnessController.Instance == null ? "not ready" : HarnessController.Instance.PutOn(),
                TakeOff = () => HarnessController.Instance?.TakeOff(),
            };
            Hub.Register(Entry);
            Logger.LogInfo("[SFM-ACCEPT] load: " + PluginName + " " + PluginVersion + " ready");
        }
    }
}
