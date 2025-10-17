using HarmonyLib;

namespace Parity
{
    [HarmonyPatch(typeof(AutoSaveController), nameof(AutoSaveController.Save))]
    internal class HarmonyPatches
    {
        static void Prefix()
        {
            Parity.InvokeSavingEvent();
        }
    }
}
