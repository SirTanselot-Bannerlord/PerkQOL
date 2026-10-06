using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.Library;

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
        private static Action<object, float> _baseTick;
        private static bool _baseTickFailed;
        private static bool _inBaseTick;

        private static MethodBase TargetMethod()
        {
            Type screen = AccessTools.TypeByName("SandBox.GauntletUI.GauntletCharacterDeveloperScreen");
            return screen == null ? null : AccessTools.Method(screen, "OnFrameTick");
        }

        private static bool Prefix(object __instance, float dt)
        {
            // War Sails uses a subclass of this screen. Looking up "the base tick" on that runtime type finds this
            // patched method, and Invoke runs the patch again. That recursion is what hung the game on the search box.
            if (_inBaseTick)
            {
                return false;
            }

            object layer = AccessTools.Field(__instance.GetType(), "_gauntletLayer")?.GetValue(__instance);
            if (layer == null || !IsFocusedOnInput(layer) || IsHotKeyReleased(layer, "Exit"))
            {
                return true;
            }

            _inBaseTick = true;
            try
            {
                CallScreenBase(__instance, dt);
            }
            finally
            {
                _inBaseTick = false;
            }

            AccessTools.Method("TaleWorlds.Engine.LoadingWindow:DisableGlobalLoadingWindow")?.Invoke(null, null);
            return false;
        }

        /// <summary>Non-virtual call of ScreenBase.OnFrameTick. A normal Invoke would dispatch back into this patch.</summary>
        private static void CallScreenBase(object instance, float dt)
        {
            if (_baseTickFailed)
            {
                return;
            }

            try
            {
                if (_baseTick == null)
                {
                    Type screen = AccessTools.TypeByName("SandBox.GauntletUI.GauntletCharacterDeveloperScreen");
                    MethodInfo derived = AccessTools.DeclaredMethod(screen, "OnFrameTick", new[] { typeof(float) });
                    MethodInfo baseMethod = null;
                    for (Type type = derived?.DeclaringType?.BaseType; type != null && baseMethod == null; type = type.BaseType)
                    {
                        baseMethod = AccessTools.DeclaredMethod(type, "OnFrameTick", new[] { typeof(float) });
                    }

                    if (baseMethod == null)
                    {
                        _baseTickFailed = true;
                        return;
                    }

                    DynamicMethod caller = new DynamicMethod(
                        "PerkQOL_CallScreenBaseTick",
                        typeof(void),
                        new[] { typeof(object), typeof(float) },
                        baseMethod.DeclaringType,
                        skipVisibility: true);
                    ILGenerator il = caller.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, baseMethod.DeclaringType);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Call, baseMethod);
                    il.Emit(OpCodes.Ret);
                    _baseTick = (Action<object, float>)caller.CreateDelegate(typeof(Action<object, float>));
                }

                _baseTick(instance, dt);
            }
            catch (Exception e)
            {
                if (_baseTick == null)
                {
                    _baseTickFailed = true;
                }

                Debug.Print("PerkQOL: character screen base tick failed (" + e.Message + ")");
            }
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
