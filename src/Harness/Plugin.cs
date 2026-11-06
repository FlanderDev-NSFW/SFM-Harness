using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Harness.Logic;

namespace Harness
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    // Hard dependency on another mod (so it loads first and its types are usable):
    // [BepInDependency("com.TetoraFetora.SFMCosplayLoaderMod", BepInDependency.DependencyFlags.HardDependency)]
    // Optional integration (reach it by reflection; it may be absent or a different version):
    // [BepInDependency("SFM_custom_mission_v2", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "FlanderDev.SFM.Harness";
        public const string PluginName = "Harness";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Logger;

        public override void Load()
        {
            Logger = Log;

            // Custom MonoBehaviours must be registered with IL2CPP before they can be added.
            ClassInjector.RegisterTypeInIl2Cpp<ModBehaviour>();
            AddComponent<ModBehaviour>();

            Logger.LogInfo(GreetingPolicy.Greeting(PluginName, PluginVersion));
            // Acceptance markers let ./tools/dev.ps1 wait-log detect that a condition really happened in game.
            Logger.LogInfo("[SFM-ACCEPT] load: plugin loaded and behaviour attached");
        }
    }

    /// <summary>
    /// Injected MonoBehaviour. Rules (see .github/instructions/il2cpp-interop.instructions.md):
    /// the IntPtr constructor is mandatory; keep Il2Cpp-visible members to simple types; mark managed-only
    /// helpers [HideFromIl2Cpp]; never throw out of Update.
    /// </summary>
    public sealed class ModBehaviour : MonoBehaviour
    {
        private int _frames;

        public ModBehaviour(IntPtr pointer) : base(pointer) { }

        private void Update()
        {
            _frames++;
            try
            {
                // Legacy Input.GetKeyDown works in this game (CollarAsset's hotkeys use it).
                if (Input.GetKeyDown(KeyCode.F8))
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] hotkey: F8 pressed after " + _frames + " frames");
            }
            catch (Exception error)
            {
                Plugin.Logger.LogWarning("Update failed: " + error.Message);
            }
        }
    }
}
