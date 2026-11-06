using System;
using RestraintHub;

namespace Harness
{
    /// <summary>Whether it is on, as one extra key in the game's save slot. RestraintHub calls these and catches exceptions.</summary>
    internal static class Patches
    {
        public const string SaveKeyLocked = "Harness.Locked";

        internal static void Save(SaveSlot slot)
        {
            var c = HarnessController.Instance;
            if (c == null) return;
            slot.Save(SaveKeyLocked, c.Machine.Worn);
        }

        internal static void Load(SaveSlot slot)
        {
            var c = HarnessController.Instance;
            if (c == null) return;
            bool locked = slot.Load(SaveKeyLocked, false);
            c.OnSaveLoaded(locked);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] load: locked=" + locked);
        }
    }
}
