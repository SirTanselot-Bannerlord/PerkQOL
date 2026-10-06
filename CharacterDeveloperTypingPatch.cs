using System.Reflection;
using HarmonyLib;

namespace PerkQOL
{
    /// <summary>
    /// Q and E switch character, and C is the character-window shortcut, so it closes the sheet. The screen reads those
    /// every frame, including while the perk search box is focused, so a typed letter also runs the hotkey. Escape is
    /// the Exit hotkey and is not a letter, so it still closes. Other panel hotkeys wait until the box loses focus.
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
            if (layer == null || !IsFocusedOnInput(layer) || IsHotKeyReleased(layer, "Exit"))
            {
                return true;
            }

            AccessTools.DeclaredMethod(__instance.GetType().BaseType, "OnFrameTick")?.Invoke(__instance, new object[] { dt });
            AccessTools.Method("TaleWorlds.Engine.LoadingWindow:DisableGlobalLoadingWindow")?.Invoke(null, null);
            return false;
        }

        private static bool IsHotKeyReleased(object layer, string hotKey)
        {
            object input = AccessTools.Property(layer.GetType(), "Input")?.GetValue(layer);
            MethodInfo method = input == null ? null : AccessTools.Method(input.GetType(), "IsHotKeyReleased", new[] { typeof(string) });
            return method != null && method.Invoke(input, new object[] { hotKey }) is bool released && released;
        }

        private static bool IsFocusedOnInput(object layer)
        {
            MethodInfo method = AccessTools.Method(layer.GetType(), "IsFocusedOnInput");
            return method != null && method.Invoke(layer, null) is bool focused && focused;
        }
    }
}
