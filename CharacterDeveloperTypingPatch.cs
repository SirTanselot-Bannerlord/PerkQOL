using System.Reflection;
using HarmonyLib;

namespace PerkQOL
{
    /// <summary>
    /// Q and E switch character and C closes the sheet. The screen reads those panel hotkeys every frame, including
    /// while the perk search box is focused, so a typed letter also runs the hotkey. Inventory already returns before
    /// its hotkeys in that case; do the same here, after the base tick.
    /// </summary>
    [HarmonyPatch]
    internal static class CharacterDeveloperTypingPatch
    {
        private static MethodBase TargetMethod()
        {
            System.Type screen = AccessTools.TypeByName("SandBox.GauntletUI.GauntletCharacterDeveloperScreen");
            return screen == null ? null : AccessTools.Method(screen, "OnFrameTick");
        }

        private static bool Prefix(object __instance, float dt)
        {
            object layer = AccessTools.Field(__instance.GetType(), "_gauntletLayer")?.GetValue(__instance);
            if (layer == null || !IsFocusedOnInput(layer))
            {
                return true;
            }

            AccessTools.DeclaredMethod(__instance.GetType().BaseType, "OnFrameTick")?.Invoke(__instance, new object[] { dt });
            AccessTools.Method("TaleWorlds.Engine.LoadingWindow:DisableGlobalLoadingWindow")?.Invoke(null, null);
            return false;
        }

        private static bool IsFocusedOnInput(object layer)
        {
            MethodInfo method = AccessTools.Method(layer.GetType(), "IsFocusedOnInput");
            return method != null && method.Invoke(layer, null) is bool focused && focused;
        }
    }
}
