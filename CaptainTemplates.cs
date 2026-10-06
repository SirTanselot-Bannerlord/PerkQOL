using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace PerkQOL
{
    /// <summary>One named set of captain-flag ticks, stored beside the MCM settings.</summary>
    internal sealed class CaptainTemplate
    {
        internal string Name;
        internal int Flags;
        internal bool Generic;
    }

    /// <summary>
    /// Named captain flag sets. The file is one template per line: name, flag bits, generic 0/1.
    /// </summary>
    internal static class CaptainTemplates
    {
        internal const int MaxCount = 16;
        internal const int MaxNameLength = 22;

        private static readonly List<CaptainTemplate> Items = new List<CaptainTemplate>();
        private static bool _loaded;

        internal static IReadOnlyList<CaptainTemplate> All
        {
            get
            {
                EnsureLoaded();
                return Items;
            }
        }

        internal static CaptainTemplate Get(int index)
        {
            EnsureLoaded();
            if (index < 0 || index >= Items.Count)
            {
                return null;
            }

            return Items[index];
        }

        internal static bool TryAdd(string rawName, TroopUsageFlags flags, bool generic, out TextObject error)
        {
            if (!TryNormalize(rawName, out string name, out error))
            {
                return false;
            }

            EnsureLoaded();
            if (Find(name) >= 0)
            {
                error = new TextObject("{=PQOL_CT_Exists}That name is already saved. Update replaces it.");
                return false;
            }

            if (Items.Count >= MaxCount)
            {
                error = new TextObject("{=PQOL_CT_Full}The list holds 16 templates. Delete one first.");
                return false;
            }

            Items.Add(new CaptainTemplate { Name = name, Flags = (int)flags, Generic = generic });
            return TryWrite(out error);
        }

        internal static bool TryUpdate(string rawName, TroopUsageFlags flags, bool generic, out TextObject error)
        {
            if (!TryNormalize(rawName, out string name, out error))
            {
                return false;
            }

            EnsureLoaded();
            int index = Find(name);
            if (index < 0)
            {
                error = new TextObject("{=PQOL_CT_Missing}No saved template with that name.");
                return false;
            }

            Items[index].Flags = (int)flags;
            Items[index].Generic = generic;
            return TryWrite(out error);
        }

        internal static bool TryDelete(string rawName, out TextObject error)
        {
            if (!TryNormalize(rawName, out string name, out error))
            {
                return false;
            }

            EnsureLoaded();
            int index = Find(name);
            if (index < 0)
            {
                error = new TextObject("{=PQOL_CT_Missing}No saved template with that name.");
                return false;
            }

            Items.RemoveAt(index);
            return TryWrite(out error);
        }

        private static int Find(string name)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (string.Equals(Items[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryNormalize(string rawName, out string name, out TextObject error)
        {
            name = (rawName ?? "").Trim().Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            while (name.Contains("  "))
            {
                name = name.Replace("  ", " ");
            }

            name = name.Replace("{", "").Replace("}", "");
            if (name.Length == 0)
            {
                error = new TextObject("{=PQOL_CT_NeedName}Type a template name first.");
                return false;
            }

            if (name.Length > MaxNameLength)
            {
                name = name.Substring(0, MaxNameLength).Trim();
            }

            if (name.Length == 0 || IsReserved(name))
            {
                error = new TextObject("{=PQOL_CT_Reserved}That name is used by a preset.");
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsReserved(string name)
        {
            foreach (TextObject preset in PerkHighlighter.PresetNames)
            {
                if (string.Equals(preset.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    return;
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    if (Items.Count >= MaxCount || string.IsNullOrWhiteSpace(line) || line[0] == '#')
                    {
                        continue;
                    }

                    string[] parts = line.Split('\t');
                    if (parts.Length != 3 || !int.TryParse(parts[1], out int flags) || (parts[2] != "0" && parts[2] != "1"))
                    {
                        continue;
                    }

                    if (!TryNormalize(parts[0], out string name, out _) || Find(name) >= 0)
                    {
                        continue;
                    }

                    Items.Add(new CaptainTemplate { Name = name, Flags = flags, Generic = parts[2] == "1" });
                }
            }
            catch (Exception e)
            {
                Debug.Print("PerkQOL: could not read captain templates (" + e.Message + ")");
            }
        }

        private static bool TryWrite(out TextObject error)
        {
            try
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var lines = new List<string> { "# PerkQOL captain templates. name, flag bits, generic 0/1." };
                foreach (CaptainTemplate template in Items)
                {
                    lines.Add(template.Name + "\t" + template.Flags + "\t" + (template.Generic ? "1" : "0"));
                }

                string temp = path + ".tmp";
                File.WriteAllLines(temp, lines);
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }

                error = null;
                return true;
            }
            catch (Exception e)
            {
                Debug.Print("PerkQOL: could not write captain templates (" + e.Message + ")");
                error = new TextObject("{=PQOL_CT_WriteFailed}Could not save templates.");
                return false;
            }
        }

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "Configs", "ModSettings", "Global", "PerkQOL", "captain_templates.txt");
    }

    /// <summary>
    /// Vanilla already counts untagged (mask None) captain perks in every formation preview, because the empty mask
    /// passes HasAllFlags. When the setting is off, drop perks that matched only through that empty mask.
    /// </summary>
    [HarmonyPatch(typeof(PerkHelper), nameof(PerkHelper.GetCaptainPerksForTroopUsages))]
    internal static class CaptainPreviewGenericPatch
    {
        private static void Postfix(TroopUsageFlags troopUsageFlags, ref IEnumerable<PerkObject> __result)
        {
            if (Settings.Current.CaptainPresetsIncludeGeneric || __result == null)
            {
                return;
            }

            __result = __result.Where(perk => HasTaggedMatch(perk, troopUsageFlags)).ToList();
        }

        private static bool HasTaggedMatch(PerkObject perk, TroopUsageFlags formation) =>
            IsTaggedMatch(perk.PrimaryTroopUsageMask, formation) || IsTaggedMatch(perk.SecondaryTroopUsageMask, formation);

        private static bool IsTaggedMatch(TroopUsageFlags mask, TroopUsageFlags formation) =>
            mask != TroopUsageFlags.Undefined && mask != TroopUsageFlags.None && formation.HasAllFlags(mask);
    }

    /// <summary>
    /// The captain dropdown sits on the bottom row, so extra template rows would run under the HUD.
    /// Keep vanilla's root-level list, then shift this one dropdown up by its own height.
    /// </summary>
    internal static class CaptainDropdownOpensUp
    {
        private static PropertyInfo _id;
        private static PropertyInfo _listPanel;
        private static PropertyInfo _button;
        private static PropertyInfo _marginTop;
        private static PropertyInfo _isVisible;
        private static FieldInfo _openFrameCounter;
        private static bool _positioning;

        internal static void TryPatch(Harmony harmony)
        {
            Type type = AccessTools.TypeByName("TaleWorlds.GauntletUI.BaseTypes.DropdownWidget");
            MethodInfo method = type?.GetMethod("UpdateListPanelPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(typeof(CaptainDropdownOpensUp), nameof(Postfix)));
        }

        private static void Postfix(object __instance)
        {
            if (_positioning || __instance == null)
            {
                return;
            }

            _positioning = true;
            try
            {
                if (__instance == null)
                {
                    return;
                }

                _id ??= __instance.GetType().GetProperty("Id");
                if (!string.Equals(_id?.GetValue(__instance) as string, "CaptainTroopDropdown", StringComparison.Ordinal))
                {
                    return;
                }

                _listPanel ??= __instance.GetType().GetProperty("ListPanel");
                _button ??= __instance.GetType().GetProperty("Button");
                object list = _listPanel?.GetValue(__instance);
                object button = _button?.GetValue(__instance);
                if (list == null || button == null)
                {
                    return;
                }

                _isVisible ??= list.GetType().GetProperty("IsVisible");
                if (!(_isVisible?.GetValue(list) is bool visible) || !visible)
                {
                    return;
                }

                _marginTop ??= list.GetType().GetProperty("MarginTop");
                float marginTop = _marginTop?.GetValue(list) is float top ? top : 0f;
                float buttonBottom = ComponentY(GetMember(__instance, "GlobalPosition")) + ComponentY(GetMember(button, "Size"));
                float listHeight = ComponentY(GetMember(list, "Size"));
                if (marginTop <= 0f || buttonBottom <= 0.01f || listHeight <= 1f)
                {
                    return;
                }

                float inverse = marginTop / buttonBottom;
                _marginTop.SetValue(list, (ComponentY(GetMember(__instance, "GlobalPosition")) - listHeight) * inverse - 2f * inverse);

                // The list jumps up once its height is known. Vanilla closes a dropdown that moves; hold this one open.
                _openFrameCounter ??= FindField(__instance.GetType(), "_openFrameCounter");
                _openFrameCounter?.SetValue(__instance, 0);
            }
            catch (Exception e)
            {
                Debug.Print("PerkQOL: captain dropdown position failed (" + e.Message + ")");
            }
            finally
            {
                _positioning = false;
            }
        }

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }

        private static object GetMember(object instance, string name)
        {
            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name);
            if (property != null)
            {
                return property.GetValue(instance);
            }

            return type.GetField(name)?.GetValue(instance);
        }

        private static float ComponentY(object vector)
        {
            if (vector == null)
            {
                return 0f;
            }

            object y = vector.GetType().GetField("Y")?.GetValue(vector) ?? vector.GetType().GetProperty("Y")?.GetValue(vector);
            return y is float value ? value : 0f;
        }
    }

    /// <summary>
    /// The options dropdown opens downward from its button. This one is on the bottom row, so that list would run under
    /// the HUD. Keep the open list above the button, and keep the drift-close from treating that move as the screen scrolling.
    /// </summary>
    internal static class CaptainAnimatedDropdownOpensUp
    {
        private static PropertyInfo _id;
        private static PropertyInfo _clip;
        private static PropertyInfo _button;
        private static PropertyInfo _scaledHeight;
        private static PropertyInfo _scaledY;
        private static PropertyInfo _globalPosition;
        private static PropertyInfo _areaRect;
        private static PropertyInfo _topLeft;
        private static FieldInfo _openPosition;
        private static FieldInfo _isOpen;

        internal static void TryPatch(Harmony harmony)
        {
            Type type = AccessTools.TypeByName("TaleWorlds.GauntletUI.AnimatedDropdownWidget");
            if (type == null)
            {
                return;
            }

            MethodInfo late = AccessTools.Method(type, "OnLateUpdate");
            MethodInfo position = AccessTools.Method(type, "UpdateListPanelPosition");
            if (late != null)
            {
                harmony.Patch(late, prefix: new HarmonyMethod(typeof(CaptainAnimatedDropdownOpensUp), nameof(PinOpenPosition)));
            }

            if (position != null)
            {
                harmony.Patch(position, postfix: new HarmonyMethod(typeof(CaptainAnimatedDropdownOpensUp), nameof(OpenUpward)));
            }
        }

        private static bool IsCaptainDropdown(object instance)
        {
            _id ??= instance.GetType().GetProperty("Id");
            return string.Equals(_id?.GetValue(instance) as string, "CaptainTroopDropdown", StringComparison.Ordinal);
        }

        private static void PinOpenPosition(object __instance)
        {
            try
            {
                if (__instance == null || !IsCaptainDropdown(__instance))
                {
                    return;
                }

                _isOpen ??= __instance.GetType().GetField("_isOpen", BindingFlags.Instance | BindingFlags.NonPublic);
                if (!(_isOpen?.GetValue(__instance) is bool open) || !open)
                {
                    return;
                }

                object clip = Clip(__instance);
                if (clip == null)
                {
                    return;
                }

                _openPosition ??= __instance.GetType().GetField("_dropdownOpenPosition", BindingFlags.Instance | BindingFlags.NonPublic);
                _areaRect ??= clip.GetType().GetProperty("AreaRect");
                object rect = _areaRect?.GetValue(clip);
                if (rect == null || _openPosition == null)
                {
                    return;
                }

                _topLeft ??= rect.GetType().GetProperty("TopLeft");
                object topLeft = _topLeft?.GetValue(rect);
                if (topLeft != null)
                {
                    _openPosition.SetValue(__instance, topLeft);
                }
            }
            catch (Exception e)
            {
                Debug.Print("PerkQOL: captain dropdown anchor failed (" + e.Message + ")");
            }
        }

        private static void OpenUpward(object __instance)
        {
            try
            {
                if (__instance == null || !IsCaptainDropdown(__instance))
                {
                    return;
                }

                object clip = Clip(__instance);
                object button = Button(__instance);
                if (clip == null || button == null)
                {
                    return;
                }

                _scaledHeight ??= clip.GetType().GetProperty("ScaledSuggestedHeight");
                _scaledY ??= clip.GetType().GetProperty("ScaledPositionYOffset");
                _globalPosition ??= button.GetType().GetProperty("GlobalPosition");
                if (_scaledHeight == null || _scaledY == null || _globalPosition == null)
                {
                    return;
                }

                float height = _scaledHeight.GetValue(clip) is float value ? value : 0f;
                _scaledY.SetValue(clip, Y(_globalPosition.GetValue(button)) - height);
            }
            catch (Exception e)
            {
                Debug.Print("PerkQOL: captain dropdown position failed (" + e.Message + ")");
            }
        }

        private static object Clip(object instance)
        {
            _clip ??= instance.GetType().GetProperty("DropdownClipWidget");
            return _clip?.GetValue(instance);
        }

        private static object Button(object instance)
        {
            _button ??= instance.GetType().GetProperty("Button");
            return _button?.GetValue(instance);
        }

        private static float Y(object vector)
        {
            if (vector == null)
            {
                return 0f;
            }

            object y = vector.GetType().GetField("Y")?.GetValue(vector) ?? vector.GetType().GetProperty("Y")?.GetValue(vector);
            return y is float value ? value : 0f;
        }
    }
}
