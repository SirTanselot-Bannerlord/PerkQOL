using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using HarmonyLib;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterDeveloper;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace PerkQOL
{
    /// <summary>
    /// Character screen role highlighting: frames every perk with an effect for the selected roles, each role in its own
    /// colour, picked with a tickbox per role. Captain can be narrowed to a troop class. A search bar highlights matching
    /// perks (and related captain perks via troop-usage masks) in white, and a skill with any match gets a red name.
    /// Selection lasts for the session. Registered at load only when the MCM option is on, since Gauntlet caches the
    /// patched prefab.
    /// </summary>
    internal static class PerkHighlighter
    {
        /// <summary>Role groups, plus None (no group) and All (the tickbox that toggles every group).</summary>
        internal enum Mode
        {
            None,
            All,
            PersonalCombat,
            PersonalCampaign,
            PartyLeader,
            ArmyCommander,
            Captain,
            PartyRole,
            Governor,
            ClanLeader
        }

        /// <summary>Troop-class filter for Captain highlights; Any matches every captain effect.</summary>
        internal enum CaptainTroopClass
        {
            Any = 0,
            Infantry = 1,
            Ranged = 2,
            Cavalry = 3,
            HorseArcher = 4
        }

        internal static readonly Mode[] Roles =
        {
            Mode.PersonalCombat, Mode.PersonalCampaign, Mode.PartyLeader, Mode.ArmyCommander, Mode.Captain, Mode.PartyRole, Mode.Governor, Mode.ClanLeader
        };

        private static readonly Dictionary<Mode, TextObject> Names = new Dictionary<Mode, TextObject>
        {
            [Mode.None] = new TextObject("{=PQOL_HL_None}None"),
            [Mode.All] = new TextObject("{=PQOL_HL_All}All roles"),
            [Mode.PersonalCombat] = new TextObject("{=PQOL_HL_PersonalCombat}Personal - Combat"),
            [Mode.PersonalCampaign] = new TextObject("{=PQOL_HL_PersonalCampaign}Personal - Campaign"),
            [Mode.PartyLeader] = new TextObject("{=PQOL_HL_PartyLeader}Party Leader"),
            [Mode.ArmyCommander] = new TextObject("{=PQOL_HL_ArmyCommander}Army Commander"),
            [Mode.Captain] = new TextObject("{=PQOL_HL_Captain}Captain"),
            [Mode.PartyRole] = new TextObject("{=PQOL_HL_PartyRole}Party Role"),
            [Mode.Governor] = new TextObject("{=PQOL_HL_Governor}Governor"),
            [Mode.ClanLeader] = new TextObject("{=PQOL_HL_ClanLeader}Clan Leader"),
        };

        internal static readonly TextObject SearchPlaceholder = new TextObject("{=PQOL_HL_Filter}Filter...");

        private static readonly TextObject[] CaptainFilterNames =
        {
            new TextObject("{=PQOL_CF_Any}Any"),
            new TextObject("{=PQOL_CF_Infantry}Infantry"),
            new TextObject("{=PQOL_CF_Ranged}Archer"),
            new TextObject("{=PQOL_CF_Cavalry}Cavalry"),
            new TextObject("{=PQOL_CF_HorseArcher}H. Archer"),
            new TextObject("{=PQOL_CF_Custom}Custom"),
        };

        /// <summary>Index of the dropdown row that means "the ticks no longer match a preset or a saved template".</summary>
        internal const int CustomPresetIndex = 5;

        /// <summary>First dropdown index used by a saved template. Rows above this are the built-in presets and Custom.</summary>
        internal const int TemplateIndexBase = CustomPresetIndex + 1;

        internal static IEnumerable<TextObject> PresetNames => CaptainFilterNames;

        /// <summary>Same values as <c>PerkFixes.TroopUsageBits</c>; kept local so PerkFixes stays optional.</summary>
        private const TroopUsageFlags LandBit = (TroopUsageFlags)2048;
        private const TroopUsageFlags NavalBit = (TroopUsageFlags)4096;

        private const TroopUsageFlags BaseTroopFlags =
            TroopUsageFlags.OnFoot | TroopUsageFlags.Mounted | TroopUsageFlags.Melee | TroopUsageFlags.Ranged
            | TroopUsageFlags.OneHandedUser | TroopUsageFlags.ShieldUser | TroopUsageFlags.TwoHandedUser
            | TroopUsageFlags.PolearmUser | TroopUsageFlags.BowUser | TroopUsageFlags.ThrownUser
            | TroopUsageFlags.CrossbowUser;

        private static bool? _perkFixesLoaded;
        private static CaptainFlagOption[] _flagOptions;
        private static TroopUsageFlags _selectedCaptainFlags;
        private static bool _selectedCaptainFlagsReady;

        /// <summary>
        /// True when PerkFixes is loaded. Prefer the TroopUsageBits type; fall back to the assembly so the Land/Naval
        /// ticks still appear if that type is not shipped yet.
        /// </summary>
        internal static bool PerkFixesLoaded =>
            _perkFixesLoaded ?? (_perkFixesLoaded = DetectPerkFixes()).Value;

        private static bool DetectPerkFixes()
        {
            if (AccessTools.TypeByName("PerkFixes.TroopUsageBits") != null)
            {
                return true;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, "PerkFixes", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static TroopUsageFlags AllTroopFlags =>
            PerkFixesLoaded ? BaseTroopFlags | LandBit | NavalBit : BaseTroopFlags;

        private static TroopUsageFlags SelectedCaptainFlags
        {
            get
            {
                if (!_selectedCaptainFlagsReady)
                {
                    _selectedCaptainFlags = AllTroopFlags;
                    _selectedCaptainFlagsReady = true;
                }

                return _selectedCaptainFlags;
            }
            set
            {
                _selectedCaptainFlags = value;
                _selectedCaptainFlagsReady = true;
            }
        }

        internal readonly struct CaptainFlagOption
        {
            internal readonly TextObject Name;
            internal readonly TextObject Hint;
            internal readonly TroopUsageFlags Flag;
            internal readonly bool Generic;

            internal CaptainFlagOption(TextObject name, TextObject hint, TroopUsageFlags flag, bool generic = false)
            {
                Name = name;
                Hint = hint;
                Flag = flag;
                Generic = generic;
            }
        }

        /// <summary>
        /// Flags the captain menu can tick. Generic is the untagged captain effects (mask None), not a troop flag.
        /// Land/Naval appear only when PerkFixes is loaded. Bandit culture is left out on purpose.
        /// </summary>
        internal static CaptainFlagOption[] FlagOptions => _flagOptions ?? (_flagOptions = BuildFlagOptions());

        private static CaptainFlagOption[] BuildFlagOptions()
        {
            // Row-major 2-column order: pairs sit side by side (On foot/Mounted, Melee/Ranged, Land/Naval),
            // then melee weapons on the left and ranged on the right, Generic filling the last cell.
            var options = new List<CaptainFlagOption>
            {
                new CaptainFlagOption(
                    new TextObject("{=PQOL_FF_OnFoot}On foot"),
                    new TextObject("{=PQOL_FH_OnFoot}Captain perks for troops fighting on foot."),
                    TroopUsageFlags.OnFoot),
                new CaptainFlagOption(
                    new TextObject("{=PQOL_FF_Mounted}Mounted"),
                    new TextObject("{=PQOL_FH_Mounted}Captain perks for mounted troops."),
                    TroopUsageFlags.Mounted),
                new CaptainFlagOption(
                    new TextObject("{=PQOL_FF_Melee}Melee"),
                    new TextObject("{=PQOL_FH_Melee}Captain perks aimed at melee troops."),
                    TroopUsageFlags.Melee),
                new CaptainFlagOption(
                    new TextObject("{=PQOL_FF_Ranged}Ranged"),
                    new TextObject("{=PQOL_FH_Ranged}Captain perks aimed at ranged troops."),
                    TroopUsageFlags.Ranged),
            };

            if (PerkFixesLoaded)
            {
                options.Add(new CaptainFlagOption(
                    new TextObject("{=PQOL_FF_Land}Land"),
                    new TextObject("{=PQOL_FH_Land}Captain perks that apply in land battles (PerkFixes)."),
                    LandBit));
                options.Add(new CaptainFlagOption(
                    new TextObject("{=PQOL_FF_Naval}Naval"),
                    new TextObject("{=PQOL_FH_Naval}Captain perks that apply in naval battles (PerkFixes)."),
                    NavalBit));
            }

            options.Add(new CaptainFlagOption(
                new TextObject("{=PQOL_FF_OneHanded}1-Handed"),
                new TextObject("{=PQOL_FH_OneHanded}Captain perks for one-handed weapon users."),
                TroopUsageFlags.OneHandedUser));
            options.Add(new CaptainFlagOption(
                new TextObject("{=PQOL_FF_Bow}Bow"),
                new TextObject("{=PQOL_FH_Bow}Captain perks for bow users."),
                TroopUsageFlags.BowUser));
            options.Add(new CaptainFlagOption(
                new TextObject("{=PQOL_FF_TwoHanded}2-Handed"),
                new TextObject("{=PQOL_FH_TwoHanded}Captain perks for two-handed weapon users."),
                TroopUsageFlags.TwoHandedUser));
            options.Add(new CaptainFlagOption(
                new TextObject("{=PQOL_FF_Thrown}Thrown"),
                new TextObject("{=PQOL_FH_Thrown}Captain perks for thrown-weapon users."),
                TroopUsageFlags.ThrownUser));
            options.Add(new CaptainFlagOption(
                new TextObject("{=PQOL_FF_Polearm}Polearm"),
                new TextObject("{=PQOL_FH_Polearm}Captain perks for polearm users."),
                TroopUsageFlags.PolearmUser));
            options.Add(new CaptainFlagOption(
                new TextObject("{=PQOL_FF_Crossbow}Crossbow"),
                new TextObject("{=PQOL_FH_Crossbow}Captain perks for crossbow users."),
                TroopUsageFlags.CrossbowUser));
            options.Add(new CaptainFlagOption(
                new TextObject("{=PQOL_FF_Shield}Shield"),
                new TextObject("{=PQOL_FH_Shield}Captain perks for troops that use shields."),
                TroopUsageFlags.ShieldUser));
            options.Add(new CaptainFlagOption(
                new TextObject("{=PQOL_FF_Generic}Generic"),
                new TextObject("{=PQOL_FH_Generic}Captain perks with no troop flag (untagged)."),
                TroopUsageFlags.None,
                true));
            return options.ToArray();
        }

        private static readonly HashSet<Mode> Selected = new HashSet<Mode>();

        private static readonly List<WeakReference<PerkHighlightPerkMixin>> Perks = new List<WeakReference<PerkHighlightPerkMixin>>();
        private static readonly List<WeakReference<PerkHighlightSkillMixin>> Skills = new List<WeakReference<PerkHighlightSkillMixin>>();

        private static bool _includeUntagged = true;
        private static string _searchText = "";
        private static WeakReference<PerkHighlightScreenMixin> _screen;
        /// <summary>
        /// Which profile row the user last picked. Kept when several templates share the same flags so Duplicate
        /// copies stay selectable instead of always snapping to the first match.
        /// </summary>
        private static int _pinnedProfileIndex = (int)CaptainTroopClass.Any;

        internal static string NameOf(Mode mode) => Names[mode].ToString();

        internal static TroopUsageFlags CurrentFlags => SelectedCaptainFlags;

        internal static bool IncludeUntagged => _includeUntagged;

        internal static List<TextObject> CaptainFilterLabels()
        {
            var labels = new List<TextObject>(CaptainFilterNames);
            foreach (CaptainTemplate template in CaptainTemplates.All)
            {
                labels.Add(new TextObject(template.Name));
            }

            return labels;
        }

        internal static bool IsSelected(Mode role) => Selected.Contains(role);

        internal static bool AllSelected => Selected.Count == Roles.Length;

        /// <summary>Dropdown index: a preset, a saved template, or <see cref="CustomPresetIndex"/> when the ticks match none.</summary>
        internal static int PresetIndex()
        {
            if (PinStillValid(_pinnedProfileIndex))
            {
                return _pinnedProfileIndex;
            }

            _pinnedProfileIndex = DerivePresetIndex();
            return _pinnedProfileIndex;
        }

        internal static void PinProfile(int filterIndex) => _pinnedProfileIndex = filterIndex;

        /// <summary>First matching preset/template for the current ticks, ignoring the pin.</summary>
        private static int DerivePresetIndex()
        {
            if (_includeUntagged && SelectedCaptainFlags == AllTroopFlags)
            {
                return (int)CaptainTroopClass.Any;
            }

            // The four troop presets include Generic only when the setting says so. Any always includes it.
            if (_includeUntagged == Settings.Current.CaptainPresetsIncludeGeneric)
            {
                if (SelectedCaptainFlags == FormationFlagsOf(CaptainTroopClass.Infantry)) return (int)CaptainTroopClass.Infantry;
                if (SelectedCaptainFlags == FormationFlagsOf(CaptainTroopClass.Ranged)) return (int)CaptainTroopClass.Ranged;
                if (SelectedCaptainFlags == FormationFlagsOf(CaptainTroopClass.Cavalry)) return (int)CaptainTroopClass.Cavalry;
                if (SelectedCaptainFlags == FormationFlagsOf(CaptainTroopClass.HorseArcher)) return (int)CaptainTroopClass.HorseArcher;
            }

            IReadOnlyList<CaptainTemplate> templates = CaptainTemplates.All;
            for (int i = 0; i < templates.Count; i++)
            {
                if (templates[i].Generic == _includeUntagged && (TroopUsageFlags)templates[i].Flags == SelectedCaptainFlags)
                {
                    return TemplateIndexBase + i;
                }
            }

            return CustomPresetIndex;
        }

        private static bool PinStillValid(int index)
        {
            if (index < 0)
            {
                return false;
            }

            if (index < CustomPresetIndex)
            {
                return FlagsMatchPreset((CaptainTroopClass)index);
            }

            if (index == CustomPresetIndex)
            {
                return DerivePresetIndex() == CustomPresetIndex;
            }

            CaptainTemplate template = CaptainTemplates.Get(index - TemplateIndexBase);
            return template != null
                && template.Generic == _includeUntagged
                && (TroopUsageFlags)template.Flags == SelectedCaptainFlags;
        }

        private static bool FlagsMatchPreset(CaptainTroopClass troopClass)
        {
            if (troopClass == CaptainTroopClass.Any)
            {
                return _includeUntagged && SelectedCaptainFlags == AllTroopFlags;
            }

            return _includeUntagged == Settings.Current.CaptainPresetsIncludeGeneric
                && SelectedCaptainFlags == FormationFlagsOf(troopClass);
        }

        internal static string SelectedTemplateName()
        {
            return CaptainTemplates.Get(PresetIndex() - TemplateIndexBase)?.Name;
        }

        /// <summary>
        /// The open screen was showing a troop preset. Keep it that preset under the new Generic rule.
        /// A saved template that merely shares the flag bits is left alone.
        /// </summary>
        internal static void OnGenericPresetSettingChanged(bool includeGeneric)
        {
            if (_includeUntagged == includeGeneric || SelectedCaptainFlags == AllTroopFlags)
            {
                return;
            }

            if (SelectedCaptainFlags != FormationFlagsOf(CaptainTroopClass.Infantry)
                && SelectedCaptainFlags != FormationFlagsOf(CaptainTroopClass.Ranged)
                && SelectedCaptainFlags != FormationFlagsOf(CaptainTroopClass.Cavalry)
                && SelectedCaptainFlags != FormationFlagsOf(CaptainTroopClass.HorseArcher))
            {
                return;
            }

            _includeUntagged = includeGeneric;
            RefreshAll();
            NotifyScreen();
        }

        internal static bool IsFlagOn(CaptainFlagOption option) =>
            option.Generic ? _includeUntagged : SelectedCaptainFlags.HasAllFlags(option.Flag);

        internal static void BindScreen(PerkHighlightScreenMixin screen) =>
            _screen = new WeakReference<PerkHighlightScreenMixin>(screen);

        internal static void ApplyPreset(CaptainTroopClass troopClass)
        {
            TroopUsageFlags flags = troopClass == CaptainTroopClass.Any ? AllTroopFlags : FormationFlagsOf(troopClass);
            bool generic = troopClass == CaptainTroopClass.Any || Settings.Current.CaptainPresetsIncludeGeneric;
            _pinnedProfileIndex = (int)troopClass;
            if (flags == SelectedCaptainFlags && generic == _includeUntagged)
            {
                NotifyScreen();
                return;
            }

            SelectedCaptainFlags = flags;
            _includeUntagged = generic;
            RefreshAll();
            NotifyScreen();
        }

        /// <returns>False when that saved row no longer exists.</returns>
        internal static bool ApplyTemplate(int templateIndex)
        {
            CaptainTemplate template = CaptainTemplates.Get(templateIndex);
            if (template == null)
            {
                return false;
            }

            _pinnedProfileIndex = TemplateIndexBase + templateIndex;
            TroopUsageFlags flags = (TroopUsageFlags)template.Flags;
            if (flags == SelectedCaptainFlags && template.Generic == _includeUntagged)
            {
                NotifyScreen();
                return true;
            }

            SelectedCaptainFlags = flags;
            _includeUntagged = template.Generic;
            RefreshAll();
            NotifyScreen();
            return true;
        }

        internal static void SetFlag(CaptainFlagOption option, bool selected)
        {
            if (option.Generic)
            {
                if (_includeUntagged == selected)
                {
                    return;
                }

                _includeUntagged = selected;
            }
            else
            {
                TroopUsageFlags next = selected ? SelectedCaptainFlags | option.Flag : SelectedCaptainFlags & ~option.Flag;
                if (next == SelectedCaptainFlags)
                {
                    return;
                }

                SelectedCaptainFlags = next;
            }

            RefreshAll();
            NotifyScreen();
        }

        private static void NotifyScreen()
        {
            if (_screen != null && _screen.TryGetTarget(out PerkHighlightScreenMixin screen))
            {
                screen.SyncCaptainFlags();
            }
        }

        internal static string SearchText
        {
            get => _searchText;
            set
            {
                string next = value ?? "";
                if (next != _searchText)
                {
                    _searchText = next;
                    RefreshAll();
                }
            }
        }

        internal static void Select(Mode role, bool selected)
        {
            if (selected ? Selected.Add(role) : Selected.Remove(role))
            {
                RefreshAll();
            }
        }

        internal static void SelectAll(bool selected)
        {
            Selected.Clear();
            if (selected)
            {
                Selected.UnionWith(Roles);
            }

            RefreshAll();
        }

        private static void RefreshAll()
        {
            Perks.RemoveAll(r => !r.TryGetTarget(out _));
            foreach (WeakReference<PerkHighlightPerkMixin> reference in Perks)
            {
                if (reference.TryGetTarget(out PerkHighlightPerkMixin mixin))
                {
                    mixin.Refresh();
                }
            }

            Skills.RemoveAll(r => !r.TryGetTarget(out _));
            foreach (WeakReference<PerkHighlightSkillMixin> reference in Skills)
            {
                if (reference.TryGetTarget(out PerkHighlightSkillMixin mixin))
                {
                    mixin.Refresh();
                }
            }
        }

        internal static void Track(PerkHighlightPerkMixin mixin) => Perks.Add(new WeakReference<PerkHighlightPerkMixin>(mixin));

        internal static void Track(PerkHighlightSkillMixin mixin) => Skills.Add(new WeakReference<PerkHighlightSkillMixin>(mixin));

        internal static void RefreshSkill(SkillObject skill)
        {
            if (skill == null)
            {
                return;
            }

            Skills.RemoveAll(r => !r.TryGetTarget(out _));
            foreach (WeakReference<PerkHighlightSkillMixin> reference in Skills)
            {
                if (reference.TryGetTarget(out PerkHighlightSkillMixin mixin) && mixin.Skill == skill)
                {
                    mixin.Refresh();
                }
            }
        }

        /// <summary>Must match the frame's ColorFactor; pre-dividing keeps pale tints from clamping to white.</summary>
        private const float FrameColorFactor = 1.6f;

        internal static Color LabelColorOf(Mode group)
        {
            switch (group)
            {
                case Mode.None:
                case Mode.All: return Color.ConvertStringToColor("#CFC2AAFF");
                // Command roles run teal -> violet as their scope grows.
                case Mode.Captain: return Color.ConvertStringToColor("#5FE0C0FF");
                case Mode.PartyLeader: return Color.ConvertStringToColor("#7CC4FFFF");
                case Mode.ClanLeader: return Color.ConvertStringToColor("#7A80FFFF");
                case Mode.ArmyCommander: return Color.ConvertStringToColor("#C890FFFF");
                case Mode.PartyRole: return Color.ConvertStringToColor("#FFE070FF");
                case Mode.Governor: return Color.ConvertStringToColor("#FF9CD0FF");
                case Mode.PersonalCombat: return Color.ConvertStringToColor("#FF6A3DFF");
                default: return Color.ConvertStringToColor("#FFF6E8FF");
            }
        }

        private static Color FrameColorOf(Mode group)
        {
            Color c = LabelColorOf(group);
            return new Color(c.Red / FrameColorFactor, c.Green / FrameColorFactor, c.Blue / FrameColorFactor, c.Alpha);
        }

        /// <summary>
        /// Frame colours for the left (primary role) and right (secondary role) half. When only one of the perk's roles
        /// is selected, its colour fills the whole frame. False when neither is selected.
        /// </summary>
        internal static bool TryGetRoleColors(PerkObject perk, out Color left, out Color right)
        {
            left = right = default;
            if (perk == null)
            {
                return false;
            }

            Mode primary = GroupOf(perk, perk.PrimaryRole, 1);
            Mode secondary = GroupOf(perk, perk.SecondaryRole, 2);
            bool primaryOn = EffectMatchesRole(primary, perk.PrimaryTroopUsageMask);
            bool secondaryOn = EffectMatchesRole(secondary, perk.SecondaryTroopUsageMask);
            if (!primaryOn && !secondaryOn)
            {
                return false;
            }

            left = FrameColorOf(primaryOn ? primary : secondary);
            right = FrameColorOf(secondaryOn ? secondary : primary);
            return true;
        }

        private static bool EffectMatchesRole(Mode group, TroopUsageFlags mask)
        {
            if (group == Mode.None || !Selected.Contains(group))
            {
                return false;
            }

            if (group != Mode.Captain)
            {
                return true;
            }

            // None is "everyone in the formation". Any always counts it. The four troop presets count it when the
            // setting is on. The flag menu's Generic tick is that same bucket. A real mask matches when every one of
            // its flags is ticked.
            if (mask == TroopUsageFlags.None)
            {
                return _includeUntagged;
            }

            return MaskMatchesFormation(mask, SelectedCaptainFlags);
        }

        internal static bool MatchesSearch(PerkObject perk)
        {
            if (perk == null)
            {
                return false;
            }

            string query = (_searchText ?? "").Trim();
            if (query.Length == 0 || string.Equals(query, SearchPlaceholder.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (TextContains(perk.Name?.ToString(), query)
                || TextContains(perk.PrimaryDescription?.ToString(), query)
                || TextContains(perk.SecondaryDescription?.ToString(), query))
            {
                return true;
            }

            if (TryParseFormationKeyword(query, out TroopUsageFlags formation)
                && (MaskMatchesFormation(perk.PrimaryTroopUsageMask, formation)
                    || MaskMatchesFormation(perk.SecondaryTroopUsageMask, formation)
                    || (Settings.Current.CaptainPresetsIncludeGeneric && HasUntaggedCaptainEffect(perk))))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// A typed filter narrows the ticked roles: a perk lights only when it matches both. With no roles ticked,
        /// the text is the whole filter and uses the white frame. An empty box leaves the role ticks alone.
        /// </summary>
        internal static bool PerkIsHighlighted(PerkObject perk, out bool useRoleColors)
        {
            bool searchActive = HasActiveSearch();
            bool role = TryGetRoleColors(perk, out _, out _);
            bool search = searchActive && MatchesSearch(perk);
            if (searchActive && Selected.Count > 0)
            {
                useRoleColors = true;
                return role && search;
            }

            useRoleColors = role && !searchActive;
            return searchActive ? search : role;
        }

        internal static bool SkillHasMatch(SkillObject skill) =>
            TryGetSkillMatchStats(skill, out _, out _);

        /// <summary>
        /// Temporary A/B/C skill-name highlight styles for comparison (left / middle / right grid columns).
        /// </summary>
        internal enum SkillMatchVisual
        {
            /// <summary>Cream skill name; only ", X, Y" is tinted red.</summary>
            SuffixTint = 0,
            /// <summary>Full name tinted; darker when the skill card is selected.</summary>
            SelectionAware = 1,
            /// <summary>Gold name with a dark drop-shadow outline.</summary>
            GoldOutline = 2,
        }

        // Column 1 / 2 / 3 of the character skill grid (plus naval extras in the same columns).
        private static readonly HashSet<string> SuffixTintSkillIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OneHanded", "Bow", "Riding", "Scouting", "Charm", "Steward", "Mariner",
        };

        private static readonly HashSet<string> SelectionAwareSkillIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TwoHanded", "Crossbow", "Athletics", "Tactics", "Leadership", "Medicine", "Boatswain",
        };

        private static readonly HashSet<string> GoldOutlineSkillIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Polearm", "Throwing", "Crafting", "Roguery", "Trade", "Engineering", "Shipmaster",
        };

        internal static SkillMatchVisual MatchVisualOf(SkillObject skill)
        {
            string id = skill?.StringId ?? "";
            if (SuffixTintSkillIds.Contains(id))
            {
                return SkillMatchVisual.SuffixTint;
            }

            if (SelectionAwareSkillIds.Contains(id))
            {
                return SkillMatchVisual.SelectionAware;
            }

            if (GoldOutlineSkillIds.Contains(id))
            {
                return SkillMatchVisual.GoldOutline;
            }

            int hash = 0;
            foreach (char c in id)
            {
                hash = (hash * 33) ^ c;
            }

            return (SkillMatchVisual)(Math.Abs(hash) % 3);
        }

        /// <summary>
        /// Matching perks for a skill under the current role/search filter: count and highest required skill level.
        /// </summary>
        internal static bool TryGetSkillMatchStats(SkillObject skill, out int matchCount, out int maxRequirement)
        {
            matchCount = 0;
            maxRequirement = 0;
            if (skill == null || (Selected.Count == 0 && !HasActiveSearch()))
            {
                return false;
            }

            foreach (PerkObject perk in PerkObject.All)
            {
                if (perk.Skill != skill || !PerkIsHighlighted(perk, out _))
                {
                    continue;
                }

                matchCount++;
                int required = (int)perk.RequiredSkillValue;
                if (required > maxRequirement)
                {
                    maxRequirement = required;
                }
            }

            return matchCount > 0;
        }

        private static bool HasActiveSearch()
        {
            string query = (_searchText ?? "").Trim();
            return query.Length > 0 && !string.Equals(query, SearchPlaceholder.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool TextContains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack)
            && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Same rule as vanilla captain rating: the formation's flags must contain every bit of the perk's usage mask.
        /// </summary>
        private static bool HasUntaggedCaptainEffect(PerkObject perk) =>
            (perk.PrimaryTroopUsageMask == TroopUsageFlags.None && GroupOf(perk, perk.PrimaryRole, 1) == Mode.Captain)
            || (perk.SecondaryTroopUsageMask == TroopUsageFlags.None && GroupOf(perk, perk.SecondaryRole, 2) == Mode.Captain);

        private static bool MaskMatchesFormation(TroopUsageFlags mask, TroopUsageFlags formation) =>
            mask != TroopUsageFlags.Undefined && mask != TroopUsageFlags.None && formation.HasAllFlags(mask);

        private static TroopUsageFlags FormationFlagsOf(CaptainTroopClass troopClass)
        {
            TroopUsageFlags flags;
            switch (troopClass)
            {
                case CaptainTroopClass.Infantry: flags = FormationClass.Infantry.GetTroopUsageFlags(); break;
                case CaptainTroopClass.Ranged: flags = FormationClass.Ranged.GetTroopUsageFlags(); break;
                case CaptainTroopClass.Cavalry: flags = FormationClass.Cavalry.GetTroopUsageFlags(); break;
                case CaptainTroopClass.HorseArcher: flags = FormationClass.HorseArcher.GetTroopUsageFlags(); break;
                default: return TroopUsageFlags.Undefined;
            }

            // PerkFixes scores land formations with the Land bit ORed in; keep the troop presets aligned.
            if (PerkFixesLoaded)
            {
                flags |= LandBit;
            }

            return flags;
        }

        private static bool TryParseFormationKeyword(string query, out TroopUsageFlags flags)
        {
            flags = TroopUsageFlags.Undefined;
            string key = query.Trim().ToLowerInvariant().Replace("-", " ").Replace("_", " ");
            while (key.Contains("  "))
            {
                key = key.Replace("  ", " ");
            }

            switch (key)
            {
                case "infantry":
                case "foot":
                case "on foot":
                    flags = FormationFlagsOf(CaptainTroopClass.Infantry);
                    return true;
                case "ranged":
                case "archer":
                case "archers":
                    flags = FormationFlagsOf(CaptainTroopClass.Ranged);
                    return true;
                case "cavalry":
                case "horseman":
                case "horsemen":
                    flags = FormationFlagsOf(CaptainTroopClass.Cavalry);
                    return true;
                case "horse archer":
                case "horse archers":
                case "horsearcher":
                case "horsearchers":
                case "mounted archer":
                case "mounted archers":
                    flags = FormationFlagsOf(CaptainTroopClass.HorseArcher);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Personal effects that break their tree's rule (combat trees are combat, other trees campaign). "Id" flips
        /// both of a perk's personal effects, "Id/1" or "Id/2" only its primary or secondary one. Healing and
        /// recovering lost hit points count as campaign (managing health between battles); max hit points as combat.
        /// </summary>
        private static readonly HashSet<string> PersonalExceptions = new HashSet<string>
        {
            // Campaign effects in combat trees: persuasion, crafting stamina, attributes, party XP.
            "AthleticsImposingStature", "AthleticsStamina", "AthleticsDurable", "AthleticsSteady", "AthleticsStrong",
            "TwoHandedBaptisedInBlood",
            // Combat effects in other trees: damage, stun, speed, hit points, shields, battle morale.
            "RogueryCarver", "RogueryDirtyFighting", "RogueryDashAndSlash", "RogueryFleetFooted/1",
            "MedicinePreventiveMedicine/1", "MedicineSelfMedication/2",
            "MedicineDoctorsOath", "MedicineFortitudeTonic", "MedicineMinisterOfHealth",
            "EngineeringScaffolds", "EngineeringTorsionEngines", "LeadershipMakeADifference",
            // War Sails: recruiting pirates in Mariner; deck movement, damage and damage taken in the other trees.
            "Arr",
            "WindRider", "TheHelmsmansShield", "TheCorsairsEdge",
        };

        // Mariner is matched by id so War Sails stays an optional dependency.
        private static bool IsCombatSkill(SkillObject skill) =>
            skill == DefaultSkills.OneHanded || skill == DefaultSkills.TwoHanded || skill == DefaultSkills.Polearm
            || skill == DefaultSkills.Bow || skill == DefaultSkills.Crossbow || skill == DefaultSkills.Throwing
            || skill == DefaultSkills.Riding || skill == DefaultSkills.Athletics
            || skill?.StringId == "Mariner";

        private static Mode PersonalGroupOf(PerkObject perk, int effect)
        {
            bool flipped = PersonalExceptions.Contains(perk.StringId) || PersonalExceptions.Contains(perk.StringId + "/" + effect);
            return IsCombatSkill(perk.Skill) != flipped ? Mode.PersonalCombat : Mode.PersonalCampaign;
        }

        /// <param name="effect">1 for the perk's primary effect, 2 for its secondary.</param>
        private static Mode GroupOf(PerkObject perk, PartyRole role, int effect)
        {
            switch (role)
            {
                case PartyRole.Personal: return PersonalGroupOf(perk, effect);
                case PartyRole.PartyLeader: return Mode.PartyLeader;
                case PartyRole.ArmyCommander: return Mode.ArmyCommander;
                case PartyRole.Captain: return Mode.Captain;
                case PartyRole.Governor: return Mode.Governor;
                case PartyRole.ClanLeader: return Mode.ClanLeader;
                case PartyRole.Scout:
                case PartyRole.Engineer:
                case PartyRole.Quartermaster:
                case PartyRole.Surgeon:
                case PartyRole.FirstMate:
                case PartyRole.Navigator:
                    return Mode.PartyRole;
                default: return Mode.None;
            }
        }
    }

    /// <summary>One tickbox; <see cref="PerkHighlighter.Mode.All"/> ticks or clears every role.</summary>
    public sealed class PerkHighlightRoleVM : ViewModel
    {
        private readonly Action<PerkHighlightRoleVM> _onToggled;
        private bool _isSelected;

        internal PerkHighlightRoleVM(PerkHighlighter.Mode role, Action<PerkHighlightRoleVM> onToggled)
        {
            Role = role;
            _onToggled = onToggled;
            Name = PerkHighlighter.NameOf(role);
            Color = PerkHighlighter.LabelColorOf(role);
        }

        internal PerkHighlighter.Mode Role { get; }

        [DataSourceProperty]
        public string Name { get; }

        [DataSourceProperty]
        public Color Color { get; }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value != _isSelected)
                {
                    _isSelected = value;
                    OnPropertyChangedWithValue(value);
                    _onToggled(this);
                }
            }
        }

        /// <summary>Updates the tick without reporting it as a click.</summary>
        internal void Sync(bool selected)
        {
            if (selected != _isSelected)
            {
                _isSelected = selected;
                OnPropertyChangedWithValue(selected, nameof(IsSelected));
            }
        }
    }

    /// <summary>One troop-flag tick in the captain flag menu. Generic is the untagged captain effects.</summary>
    public sealed class CaptainFlagVM : ViewModel
    {
        private readonly Action<CaptainFlagVM> _onToggled;
        private bool _isSelected;
        private bool _suppress;

        internal CaptainFlagVM(PerkHighlighter.CaptainFlagOption option, bool selected, Action<CaptainFlagVM> onToggled)
        {
            Option = option;
            _onToggled = onToggled;
            _isSelected = selected;
            Name = option.Name.ToString();
            string hint = option.Hint.ToString();
            Hint = new BasicTooltipViewModel(() => hint);
        }

        internal PerkHighlighter.CaptainFlagOption Option { get; }

        [DataSourceProperty]
        public string Name { get; }

        [DataSourceProperty]
        public BasicTooltipViewModel Hint { get; }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value == _isSelected)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChangedWithValue(value);
                if (!_suppress)
                {
                    _onToggled(this);
                }
            }
        }

        internal void Sync(bool selected)
        {
            _suppress = true;
            IsSelected = selected;
            _suppress = false;
        }
    }

    /// <summary>One row in the captain flag window's profile list (preset, Custom, or a saved template).</summary>
    public sealed class CaptainProfileVM : ViewModel
    {
        private readonly Action<CaptainProfileVM> _onSelect;
        private bool _isSelected;

        internal CaptainProfileVM(string name, int filterIndex, Action<CaptainProfileVM> onSelect, bool selected)
        {
            Name = name;
            FilterIndex = filterIndex;
            _onSelect = onSelect;
            _isSelected = selected;
        }

        /// <summary>Same index space as the troop dropdown: presets 0–4, Custom 5, templates from 6.</summary>
        internal int FilterIndex { get; }

        [DataSourceProperty]
        public string Name { get; }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value != _isSelected)
                {
                    _isSelected = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        public void ExecuteSelect() => _onSelect(this);
    }

    [ViewModelMixin]
    public sealed class PerkHighlightScreenMixin : BaseViewModelMixin<CharacterDeveloperVM>
    {
        private MBBindingList<PerkHighlightRoleVM> _perkHighlightRoles;
        private MBBindingList<CaptainFlagVM> _captainFlags;
        private MBBindingList<CaptainProfileVM> _captainProfiles;
        private SelectorVM<SelectorItemVM> _captainFilter;
        private string _searchText;
        private string _templateName = "";
        private bool _isCaptainFilterVisible;
        private bool _isFlagMenuOpen;
        private bool _suppressDropdown;

        public PerkHighlightScreenMixin(CharacterDeveloperVM vm) : base(vm)
        {
            // Two columns, row by row. The right column ends with Captain so its troop menu can sit indented under that label.
            _perkHighlightRoles = new MBBindingList<PerkHighlightRoleVM>();
            foreach (PerkHighlighter.Mode role in new[]
            {
                PerkHighlighter.Mode.PersonalCombat, PerkHighlighter.Mode.ArmyCommander,
                PerkHighlighter.Mode.PersonalCampaign, PerkHighlighter.Mode.ClanLeader,
                PerkHighlighter.Mode.PartyRole, PerkHighlighter.Mode.PartyLeader,
                PerkHighlighter.Mode.Governor, PerkHighlighter.Mode.Captain,
                PerkHighlighter.Mode.All,
            })
            {
                _perkHighlightRoles.Add(new PerkHighlightRoleVM(role, OnRoleToggled));
            }

            _captainFlags = new MBBindingList<CaptainFlagVM>();
            foreach (PerkHighlighter.CaptainFlagOption option in PerkHighlighter.FlagOptions)
            {
                _captainFlags.Add(new CaptainFlagVM(option, PerkHighlighter.IsFlagOn(option), OnFlagToggled));
            }

            _captainProfiles = new MBBindingList<CaptainProfileVM>();
            RebuildProfiles();

            _suppressDropdown = true;
            _captainFilter = new SelectorVM<SelectorItemVM>(
                PerkHighlighter.CaptainFilterLabels(),
                PerkHighlighter.PresetIndex(),
                OnCaptainFilterChanged);
            _suppressDropdown = false;

            _searchText = PerkHighlighter.SearchText;
            PerkHighlighter.BindScreen(this);
            SyncTickboxes();
            UpdateCaptainFilterVisible();
        }

        [DataSourceProperty]
        public bool IsPerkQOLHighlightVisible => Settings.Current.PerkHighlight;

        [DataSourceProperty]
        public string PerkQOLSearchPlaceholder => PerkHighlighter.SearchPlaceholder.ToString();

        [DataSourceProperty]
        public string PerkQOLSearchText
        {
            get => _searchText;
            set
            {
                if (value != _searchText)
                {
                    _searchText = value ?? "";
                    OnPropertyChangedWithValue(_searchText);
                    PerkHighlighter.SearchText = _searchText;
                }
            }
        }

        [DataSourceProperty]
        public SelectorVM<SelectorItemVM> PerkQOLCaptainFilter
        {
            get => _captainFilter;
            set
            {
                if (value != _captainFilter)
                {
                    _captainFilter = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool IsPerkQOLCaptainFilterVisible
        {
            get => _isCaptainFilterVisible;
            set
            {
                if (value != _isCaptainFilterVisible)
                {
                    _isCaptainFilterVisible = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        private void OnCaptainFilterChanged(SelectorVM<SelectorItemVM> selector)
        {
            if (_suppressDropdown || selector.SelectedIndex < 0)
            {
                return;
            }

            if (selector.SelectedIndex == PerkHighlighter.CustomPresetIndex)
            {
                SyncDropdown();
                return;
            }

            if (selector.SelectedIndex >= PerkHighlighter.TemplateIndexBase)
            {
                if (!PerkHighlighter.ApplyTemplate(selector.SelectedIndex - PerkHighlighter.TemplateIndexBase))
                {
                    SyncDropdown();
                }

                SyncTemplateName();
                return;
            }

            PerkHighlighter.ApplyPreset((PerkHighlighter.CaptainTroopClass)selector.SelectedIndex);
        }

        [DataSourceMethod]
        public void ExecuteSavePerkQOLTemplate()
        {
            if (!CaptainTemplates.TryAdd(PerkQOLTemplateName, PerkHighlighter.CurrentFlags, PerkHighlighter.IncludeUntagged, out TextObject error))
            {
                ShowTemplateMessage(error);
                return;
            }

            PinTemplateByName(PerkQOLTemplateName);
            ReloadTemplateDropdown();
        }

        [DataSourceMethod]
        public void ExecuteUpdatePerkQOLTemplate()
        {
            if (!CaptainTemplates.TryUpdate(PerkQOLTemplateName, PerkHighlighter.CurrentFlags, PerkHighlighter.IncludeUntagged, out TextObject error))
            {
                ShowTemplateMessage(error);
                return;
            }

            ReloadTemplateDropdown();
        }

        [DataSourceMethod]
        public void ExecuteDeletePerkQOLTemplate()
        {
            if (!CaptainTemplates.TryDelete(PerkQOLTemplateName, out TextObject error))
            {
                ShowTemplateMessage(error);
                return;
            }

            PerkQOLTemplateName = "";
            ReloadTemplateDropdown();
        }

        [DataSourceMethod]
        public void ExecuteDuplicatePerkQOLTemplate()
        {
            string baseName = PerkQOLTemplateName;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                int index = PerkHighlighter.PresetIndex();
                if (index < PerkHighlighter.CustomPresetIndex)
                {
                    baseName = PerkHighlighter.PresetNames.ElementAtOrDefault(index)?.ToString() ?? "Copy";
                }
                else
                {
                    baseName = new TextObject("{=PQOL_CT_Copy}Copy").ToString();
                }
            }

            if (!CaptainTemplates.TryDuplicate(
                    baseName,
                    PerkHighlighter.CurrentFlags,
                    PerkHighlighter.IncludeUntagged,
                    out string created,
                    out TextObject error))
            {
                ShowTemplateMessage(error);
                return;
            }

            PerkQOLTemplateName = created;
            PinTemplateByName(created);
            ReloadTemplateDropdown();
        }

        private static void PinTemplateByName(string name)
        {
            IReadOnlyList<CaptainTemplate> templates = CaptainTemplates.All;
            for (int i = 0; i < templates.Count; i++)
            {
                if (string.Equals(templates[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    PerkHighlighter.PinProfile(PerkHighlighter.TemplateIndexBase + i);
                    return;
                }
            }
        }

        private static void ShowTemplateMessage(TextObject message) =>
            InformationManager.DisplayMessage(new InformationMessage(message.ToString(), Colors.Red));

        private void ReloadTemplateDropdown()
        {
            _suppressDropdown = true;
            _captainFilter.Refresh(PerkHighlighter.CaptainFilterLabels(), PerkHighlighter.PresetIndex(), OnCaptainFilterChanged);
            _suppressDropdown = false;
            SyncTemplateName();
            RebuildProfiles();
        }

        private void OnFlagToggled(CaptainFlagVM item) => PerkHighlighter.SetFlag(item.Option, item.IsSelected);

        private void RebuildProfiles()
        {
            _captainProfiles.Clear();
            int selected = PerkHighlighter.PresetIndex();
            for (int i = 0; i < PerkHighlighter.CustomPresetIndex; i++)
            {
                _captainProfiles.Add(new CaptainProfileVM(
                    PerkHighlighter.PresetNames.ElementAt(i).ToString(),
                    i,
                    OnProfileSelected,
                    i == selected));
            }

            IReadOnlyList<CaptainTemplate> templates = CaptainTemplates.All;
            for (int i = 0; i < templates.Count; i++)
            {
                int filterIndex = PerkHighlighter.TemplateIndexBase + i;
                _captainProfiles.Add(new CaptainProfileVM(
                    templates[i].Name,
                    filterIndex,
                    OnProfileSelected,
                    filterIndex == selected));
            }

            OnPropertyChanged(nameof(PerkQOLCaptainProfiles));
        }

        private void OnProfileSelected(CaptainProfileVM profile)
        {
            if (profile.FilterIndex < PerkHighlighter.CustomPresetIndex)
            {
                PerkHighlighter.ApplyPreset((PerkHighlighter.CaptainTroopClass)profile.FilterIndex);
            }
            else if (!PerkHighlighter.ApplyTemplate(profile.FilterIndex - PerkHighlighter.TemplateIndexBase))
            {
                RebuildProfiles();
                return;
            }

            SyncDropdown();
            SyncTemplateName();
            SyncProfileSelection();
            foreach (CaptainFlagVM item in _captainFlags)
            {
                item.Sync(PerkHighlighter.IsFlagOn(item.Option));
            }
        }

        private void SyncProfileSelection()
        {
            int selected = PerkHighlighter.PresetIndex();
            foreach (CaptainProfileVM profile in _captainProfiles)
            {
                profile.IsSelected = profile.FilterIndex == selected;
            }
        }

        [DataSourceMethod]
        public void ExecuteTogglePerkQOLFlagMenu() => IsPerkQOLFlagMenuOpen = !_isFlagMenuOpen;

        [DataSourceMethod]
        public void ExecuteClosePerkQOLFlagMenu() => IsPerkQOLFlagMenuOpen = false;

        /// <summary>Pulls the flag ticks and the preset label back in line with the highlighter.</summary>
        internal void SyncCaptainFlags()
        {
            foreach (CaptainFlagVM item in _captainFlags)
            {
                item.Sync(PerkHighlighter.IsFlagOn(item.Option));
            }

            SyncDropdown();
            SyncTemplateName();
            if (_captainProfiles.Count != PerkHighlighter.CustomPresetIndex + CaptainTemplates.All.Count)
            {
                RebuildProfiles();
            }
            else
            {
                SyncProfileSelection();
            }
        }

        /// <summary>
        /// A matching template fills the name box. A built-in preset clears it. Custom leaves whatever was typed,
        /// so Update still knows which template the ticks came from.
        /// </summary>
        private void SyncTemplateName()
        {
            string template = PerkHighlighter.SelectedTemplateName();
            if (template != null)
            {
                PerkQOLTemplateName = template;
            }
            else if (PerkHighlighter.PresetIndex() < PerkHighlighter.CustomPresetIndex)
            {
                PerkQOLTemplateName = "";
            }
        }

        private void SyncDropdown()
        {
            int index = PerkHighlighter.PresetIndex();
            if (_captainFilter.SelectedIndex == index)
            {
                return;
            }

            _suppressDropdown = true;
            _captainFilter.SelectedIndex = index;
            _suppressDropdown = false;
        }

        private void OnRoleToggled(PerkHighlightRoleVM item)
        {
            if (item.Role == PerkHighlighter.Mode.All)
            {
                PerkHighlighter.SelectAll(item.IsSelected);
            }
            else
            {
                PerkHighlighter.Select(item.Role, item.IsSelected);
            }

            SyncTickboxes();
            UpdateCaptainFilterVisible();
        }

        private void SyncTickboxes()
        {
            foreach (PerkHighlightRoleVM item in _perkHighlightRoles)
            {
                item.Sync(item.Role == PerkHighlighter.Mode.All ? PerkHighlighter.AllSelected : PerkHighlighter.IsSelected(item.Role));
            }
        }

        private void UpdateCaptainFilterVisible()
        {
            IsPerkQOLCaptainFilterVisible = Settings.Current.PerkHighlight && PerkHighlighter.IsSelected(PerkHighlighter.Mode.Captain);
            if (!IsPerkQOLCaptainFilterVisible)
            {
                IsPerkQOLFlagMenuOpen = false;
            }
        }

        [DataSourceProperty]
        public string PerkQOLTemplateName
        {
            get => _templateName;
            set
            {
                string next = value ?? "";
                if (next != _templateName)
                {
                    _templateName = next;
                    OnPropertyChangedWithValue(next);
                }
            }
        }

        [DataSourceProperty]
        public string PerkQOLTemplateSaveLabel => new TextObject("{=PQOL_CT_Save}Save").ToString();

        [DataSourceProperty]
        public string PerkQOLTemplateUpdateLabel => new TextObject("{=PQOL_CT_Update}Update").ToString();

        [DataSourceProperty]
        public string PerkQOLTemplateDeleteLabel => new TextObject("{=PQOL_CT_Delete}Delete").ToString();

        [DataSourceProperty]
        public string PerkQOLTemplateDuplicateLabel => new TextObject("{=PQOL_CT_Duplicate}Duplicate").ToString();

        [DataSourceProperty]
        public string PerkQOLFlagWindowTitle => new TextObject("{=PQOL_CT_Title}Captain flags").ToString();

        [DataSourceProperty]
        public string PerkQOLFlagProfilesLabel => new TextObject("{=PQOL_CT_Profiles}Profiles").ToString();

        [DataSourceProperty]
        public string PerkQOLFlagWindowCloseLabel => new TextObject("{=PQOL_CT_Close}Close").ToString();

        [DataSourceProperty]
        public bool IsPerkQOLFlagMenuOpen
        {
            get => _isFlagMenuOpen;
            set
            {
                if (value != _isFlagMenuOpen)
                {
                    _isFlagMenuOpen = value;
                    OnPropertyChangedWithValue(value);
                    if (value)
                    {
                        RebuildProfiles();
                    }
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<CaptainFlagVM> PerkQOLCaptainFlags
        {
            get => _captainFlags;
            set
            {
                if (value != _captainFlags)
                {
                    _captainFlags = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<CaptainProfileVM> PerkQOLCaptainProfiles
        {
            get => _captainProfiles;
            set
            {
                if (value != _captainProfiles)
                {
                    _captainProfiles = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<PerkHighlightRoleVM> PerkHighlightRoles
        {
            get => _perkHighlightRoles;
            set
            {
                if (value != _perkHighlightRoles)
                {
                    _perkHighlightRoles = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }
    }

    [ViewModelMixin]
    public sealed class PerkHighlightPerkMixin : BaseViewModelMixin<PerkVM>
    {
        private readonly PerkObject _perk;
        private bool _isPerkQOLHighlighted;
        private bool _isPerkQOLSearchHighlighted;
        private Color _leftColor;
        private Color _rightColor;

        public PerkHighlightPerkMixin(PerkVM vm) : base(vm)
        {
            _perk = vm.Perk;
            Refresh();
            PerkHighlighter.Track(this);
        }

        internal void Refresh()
        {
            bool role = PerkHighlighter.TryGetRoleColors(_perk, out Color left, out Color right);
            bool show = PerkHighlighter.PerkIsHighlighted(_perk, out bool useRoleColors);
            if (show && useRoleColors && role)
            {
                PerkQOLHighlightLeftColor = left;
                PerkQOLHighlightRightColor = right;
            }

            IsPerkQOLHighlighted = show && useRoleColors;
            IsPerkQOLSearchHighlighted = show && !useRoleColors;
        }

        [DataSourceProperty]
        public Color PerkQOLHighlightLeftColor
        {
            get => _leftColor;
            set
            {
                if (value != _leftColor)
                {
                    _leftColor = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public Color PerkQOLHighlightRightColor
        {
            get => _rightColor;
            set
            {
                if (value != _rightColor)
                {
                    _rightColor = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool IsPerkQOLHighlighted
        {
            get => _isPerkQOLHighlighted;
            set
            {
                if (value != _isPerkQOLHighlighted)
                {
                    _isPerkQOLHighlighted = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool IsPerkQOLSearchHighlighted
        {
            get => _isPerkQOLSearchHighlighted;
            set
            {
                if (value != _isPerkQOLSearchHighlighted)
                {
                    _isPerkQOLSearchHighlighted = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }
    }

    [ViewModelMixin("RefreshValues")]
    public sealed class PerkHighlightSkillMixin : BaseViewModelMixin<SkillVM>
    {
        // Color.FromUint is AARRGGBB. The skill-name brush is #FDF1DFFF.
        private static readonly Color NormalColor = Color.FromUint(0xFFFDF1DFu);
        private static readonly Color MatchBrightColor = Color.FromUint(0xFFFF5C4Cu);
        private static readonly Color MatchSelectedColor = Color.FromUint(0xFF5C1A12u);
        private static readonly Color GoldColor = Color.FromUint(0xFFFFC45Au);

        private readonly SkillObject _skill;
        private Color _nameColor = NormalColor;
        private string _nameText = "";
        private string _baseName = "";
        private string _matchSuffix = "";
        private bool _matched;
        private bool _styleSuffix;
        private bool _styleSelect;
        private bool _styleGold;

        public PerkHighlightSkillMixin(SkillVM vm) : base(vm)
        {
            _skill = vm.Skill;
            PerkHighlighter.Track(this);
            Apply();
        }

        internal SkillObject Skill => _skill;

        public override void OnRefresh() => Apply();

        internal void Refresh() => Apply();

        private void Apply()
        {
            string baseName = ViewModel?.NameText;
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = _skill?.Name?.ToString() ?? "";
            }

            PerkHighlighter.SkillMatchVisual visual = PerkHighlighter.MatchVisualOf(_skill);
            IsPerkQOLSkillStyleSuffix = visual == PerkHighlighter.SkillMatchVisual.SuffixTint;
            IsPerkQOLSkillStyleSelect = visual == PerkHighlighter.SkillMatchVisual.SelectionAware;
            IsPerkQOLSkillStyleGold = visual == PerkHighlighter.SkillMatchVisual.GoldOutline;

            bool matched = PerkHighlighter.TryGetSkillMatchStats(_skill, out int matchCount, out int maxRequirement);
            IsPerkQOLSkillMatched = matched;
            PerkQOLSkillBaseName = baseName;
            PerkQOLSkillMatchSuffix = matched ? ", " + matchCount + ", " + maxRequirement : "";
            PerkQOLSkillNameText = matched ? baseName + PerkQOLSkillMatchSuffix : baseName;

            if (!matched)
            {
                PerkQOLSkillNameColor = NormalColor;
                return;
            }

            switch (visual)
            {
                case PerkHighlighter.SkillMatchVisual.SelectionAware:
                    PerkQOLSkillNameColor = ViewModel != null && ViewModel.IsInspected
                        ? MatchSelectedColor
                        : MatchBrightColor;
                    break;
                case PerkHighlighter.SkillMatchVisual.GoldOutline:
                    PerkQOLSkillNameColor = GoldColor;
                    break;
                default:
                    // SuffixTint paints the suffix in the prefab; keep the name cream.
                    PerkQOLSkillNameColor = NormalColor;
                    break;
            }
        }

        [DataSourceProperty]
        public Color PerkQOLSkillNameColor
        {
            get => _nameColor;
            set
            {
                if (_nameColor.ToUnsignedInteger() == value.ToUnsignedInteger())
                {
                    return;
                }

                _nameColor = value;
                OnPropertyChangedWithValue(value);
            }
        }

        [DataSourceProperty]
        public string PerkQOLSkillNameText
        {
            get => _nameText;
            set
            {
                if (value == _nameText)
                {
                    return;
                }

                _nameText = value ?? "";
                OnPropertyChangedWithValue(_nameText);
            }
        }

        [DataSourceProperty]
        public string PerkQOLSkillBaseName
        {
            get => _baseName;
            set
            {
                if (value == _baseName)
                {
                    return;
                }

                _baseName = value ?? "";
                OnPropertyChangedWithValue(_baseName);
            }
        }

        [DataSourceProperty]
        public string PerkQOLSkillMatchSuffix
        {
            get => _matchSuffix;
            set
            {
                if (value == _matchSuffix)
                {
                    return;
                }

                _matchSuffix = value ?? "";
                OnPropertyChangedWithValue(_matchSuffix);
            }
        }

        [DataSourceProperty]
        public bool IsPerkQOLSkillMatched
        {
            get => _matched;
            set
            {
                if (value == _matched)
                {
                    return;
                }

                _matched = value;
                OnPropertyChangedWithValue(value);
            }
        }

        [DataSourceProperty]
        public bool IsPerkQOLSkillStyleSuffix
        {
            get => _styleSuffix;
            set
            {
                if (value == _styleSuffix)
                {
                    return;
                }

                _styleSuffix = value;
                OnPropertyChangedWithValue(value);
            }
        }

        [DataSourceProperty]
        public bool IsPerkQOLSkillStyleSelect
        {
            get => _styleSelect;
            set
            {
                if (value == _styleSelect)
                {
                    return;
                }

                _styleSelect = value;
                OnPropertyChangedWithValue(value);
            }
        }

        [DataSourceProperty]
        public bool IsPerkQOLSkillStyleGold
        {
            get => _styleGold;
            set
            {
                if (value == _styleGold)
                {
                    return;
                }

                _styleGold = value;
                OnPropertyChangedWithValue(value);
            }
        }
    }

    /// <summary>SkillVM.RefreshValues runs on character switches and after the grid rebinds, which would otherwise leave the name on the brush default.</summary>
    [HarmonyPatch(typeof(SkillVM), nameof(SkillVM.RefreshValues))]
    internal static class SkillNameColorRefreshPatch
    {
        private static void Postfix(SkillVM __instance) => PerkHighlighter.RefreshSkill(__instance.Skill);
    }

    /// <summary>Selection-aware name colors need a refresh when the inspected skill card changes.</summary>
    [HarmonyPatch(typeof(SkillVM), nameof(SkillVM.IsInspected), MethodType.Setter)]
    internal static class SkillInspectedRefreshPatch
    {
        private static void Postfix(SkillVM __instance) => PerkHighlighter.RefreshSkill(__instance.Skill);
    }

    /// <summary>
    /// Frame over the perk icon, using the sprite vanilla uses for its hover frame (57x62, not nine-sliced, so the line
    /// scales with the frame). Drawn as two clipped halves of the same frame inside the 80x86 parent so each half can
    /// take its own role colour. A second white frame is used for search-only matches.
    /// </summary>
    [PrefabExtension("CharacterDeveloper", "descendant::BrushWidget[@Id='PerkVisualWidgetParent']/Children")]
    public sealed class PerkHighlightFramePatch : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Child;

        public override int Index => int.MaxValue;

        [PrefabExtensionText]
        public string Content => @"
<Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" DoNotAcceptEvents=""true"">
  <Children>
    <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" DoNotAcceptEvents=""true"" IsVisible=""@IsPerkQOLHighlighted"">
      <Children>
        <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""StretchToParent"" SuggestedWidth=""40"" HorizontalAlignment=""Left"" ClipContents=""true"" DoNotAcceptEvents=""true"">
          <Children>
            <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""44"" SuggestedHeight=""48"" HorizontalAlignment=""Left"" MarginLeft=""18"" MarginTop=""19"" Sprite=""CharacterDeveloper\selected_perk_indicator_hover"" Color=""@PerkQOLHighlightLeftColor"" ColorFactor=""1.6"" DoNotAcceptEvents=""true"" />
          </Children>
        </Widget>
        <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""StretchToParent"" SuggestedWidth=""40"" HorizontalAlignment=""Right"" ClipContents=""true"" DoNotAcceptEvents=""true"">
          <Children>
            <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""44"" SuggestedHeight=""48"" HorizontalAlignment=""Right"" MarginRight=""18"" MarginTop=""19"" Sprite=""CharacterDeveloper\selected_perk_indicator_hover"" Color=""@PerkQOLHighlightRightColor"" ColorFactor=""1.6"" DoNotAcceptEvents=""true"" />
          </Children>
        </Widget>
      </Children>
    </Widget>
    <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" DoNotAcceptEvents=""true"" IsVisible=""@IsPerkQOLSearchHighlighted"">
      <Children>
        <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""44"" SuggestedHeight=""48"" HorizontalAlignment=""Center"" MarginTop=""19"" Sprite=""CharacterDeveloper\selected_perk_indicator_hover"" Color=""#FFFFFFFF"" ColorFactor=""1.6"" DoNotAcceptEvents=""true"" />
      </Children>
    </Widget>
  </Children>
</Widget>";
    }

    /// <summary>
    /// Filter box, then the role tickboxes. Captain is the last row of the right column; its troop menu sits indented
    /// under that label. The flag editor is a separate window, so opening it does not move this block.
    /// </summary>
    [PrefabExtension("CharacterDeveloper", "descendant::Widget[@VisualDefinition='RightPanel']/Children")]
    public sealed class PerkHighlightControlsPatch : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Child;

        public override int Index => int.MaxValue;

        [PrefabExtensionText]
        public string Content => @"
<ListPanel IsVisible=""@IsPerkQOLHighlightVisible"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""CoverChildren"" SuggestedWidth=""430"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" MarginTop=""575"" PositionXOffset=""245"" StackLayout.LayoutMethod=""VerticalTopToBottom"">
  <Children>
    <BrushWidget UpdateChildrenStates=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""36"" MarginBottom=""6"" Brush=""SaveLoad.Search.Button"">
      <Children>
        <EditableTextWidget UpdateChildrenStates=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""12"" MarginRight=""12"" MarginTop=""4"" MarginBottom=""4"" Brush=""SaveLoad.Search.InputText"" Brush.FontSize=""18"" DefaultSearchText=""@PerkQOLSearchPlaceholder"" Text=""@PerkQOLSearchText"" />
      </Children>
    </BrushWidget>
    <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""160"">
      <Children>
        <GridWidget DataSource=""{PerkHighlightRoles}"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""CoverChildren"" DefaultCellWidth=""215"" DefaultCellHeight=""30"" ColumnCount=""2"">
          <ItemTemplate>
            <ListPanel WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" StackLayout.LayoutMethod=""HorizontalLeftToRight"">
              <Children>
                <ButtonWidget DoNotPassEventsToChildren=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""26"" SuggestedHeight=""26"" VerticalAlignment=""Center"" Brush=""SPOptions.Checkbox.Empty.Button"" ButtonType=""Toggle"" IsSelected=""@IsSelected"" ToggleIndicator=""ToggleIndicator"" UpdateChildrenStates=""true"">
                  <Children>
                    <ImageWidget Id=""ToggleIndicator"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Brush=""SPOptions.Checkbox.Full.Button"" />
                  </Children>
                </ButtonWidget>
                <TextWidget WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" VerticalAlignment=""Center"" MarginLeft=""6"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""18"" Brush.FontColor=""@Color"" Text=""@Name"" />
              </Children>
            </ListPanel>
          </ItemTemplate>
        </GridWidget>
        <ListPanel IsVisible=""@IsPerkQOLCaptainFilterVisible"" WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""Fixed"" SuggestedHeight=""30"" HorizontalAlignment=""Left"" VerticalAlignment=""Top"" MarginTop=""122"" MarginLeft=""248"" StackLayout.LayoutMethod=""HorizontalLeftToRight"">
          <Children>
            <DropdownWidget Id=""CaptainTroopDropdown"" DataSource=""{PerkQOLCaptainFilter}"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""120"" SuggestedHeight=""28"" VerticalAlignment=""Center"" ListPanel=""SelectionList"" Button=""DropdownButton"" TextWidget=""DropdownButton\SelectedText"" CurrentSelectedIndex=""@SelectedIndex"">
              <Children>
                <ButtonWidget Id=""DropdownButton"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""120"" SuggestedHeight=""28"" Brush=""SaveLoad.Search.Button"" ButtonType=""Toggle"" DoNotPassEventsToChildren=""true"" UpdateChildrenStates=""true"">
                  <Children>
                    <TextWidget Id=""SelectedText"" DataSource=""{SelectedItem}"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""8"" MarginRight=""8"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""18"" Brush.TextHorizontalAlignment=""Center"" Text=""@StringItem"" />
                  </Children>
                </ButtonWidget>
                <ListPanel Id=""SelectionList"" DataSource=""{ItemList}"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""CoverChildren"" SuggestedWidth=""160"" Sprite=""BlankWhiteSquare_9"" Color=""#1A120CFF"" IsVisible=""false"" StackLayout.LayoutMethod=""VerticalTopToBottom"" ClipContents=""true"">
                  <ItemTemplate>
                    <ButtonWidget Id=""DropdownItemButton"" DoNotPassEventsToChildren=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""160"" SuggestedHeight=""26"" ButtonType=""Radio"" UpdateChildrenStates=""true"" Brush=""Standard.DropdownItem.SoundBrush"">
                      <Children>
                        <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""2"" MarginRight=""2"" MarginTop=""1"" MarginBottom=""1"" Sprite=""BlankWhiteSquare_9"" Color=""#2A2118FF"" DoNotAcceptEvents=""true"" />
                        <ImageWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""4"" MarginRight=""4"" Brush=""Standard.DropdownItem"" />
                        <TextWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""8"" MarginRight=""8"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""18"" Brush.TextHorizontalAlignment=""Center"" Text=""@StringItem"" />
                      </Children>
                    </ButtonWidget>
                  </ItemTemplate>
                </ListPanel>
              </Children>
            </DropdownWidget>
            <ButtonWidget DoNotPassEventsToChildren=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""30"" SuggestedHeight=""30"" VerticalAlignment=""Center"" MarginLeft=""6"" Command.Click=""ExecuteTogglePerkQOLFlagMenu"" Brush=""PerkQOL.CaptainFlags.Button"" UpdateChildrenStates=""true"" />
          </Children>
        </ListPanel>
      </Children>
    </Widget>
  </Children>
</ListPanel>";
    }

    /// <summary>
    /// Flag editor as its own window: a dimmed screen and the standard popup frame, so the character sheet does not move.
    /// Clicking the dimmed area closes it.
    /// </summary>
    [PrefabExtension("CharacterDeveloper", "descendant::Window/Widget/Children")]
    public sealed class PerkHighlightFlagWindowPatch : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Child;

        public override int Index => int.MaxValue;

        [PrefabExtensionText]
        public string Content => @"
<Widget IsVisible=""@IsPerkQOLFlagMenuOpen"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"">
  <Children>
    <ButtonWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Command.Click=""ExecuteClosePerkQOLFlagMenu"" DoNotPassEventsToChildren=""true"">
      <Children>
        <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Sprite=""BlankWhiteSquare_9"" Color=""#000000FF"" AlphaFactor=""0.6"" DoNotAcceptEvents=""true"" />
      </Children>
    </ButtonWidget>
    <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""680"" SuggestedHeight=""520"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"">
      <Children>
        <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Sprite=""StdAssets\Popup\canvas"" />
        <TextWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""36"" MarginTop=""18"" Brush=""SPOptions.GameKeysGroup.Title.Text"" Brush.FontSize=""30"" Brush.TextHorizontalAlignment=""Center"" Text=""@PerkQOLFlagWindowTitle"" />
        <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""18"" MarginTop=""58"" MarginLeft=""36"" MarginRight=""36"" Sprite=""StdAssets\Popup\divider"" />

        <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""200"" SuggestedHeight=""310"" MarginLeft=""28"" MarginTop=""86"">
          <Children>
            <TextWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""24"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""20"" Text=""@PerkQOLFlagProfilesLabel"" />
            <ScrollablePanel WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""280"" MarginTop=""28"" AutoHideScrollBars=""true"" ClipRect=""ProfileClip"" InnerPanel=""ProfileClip\ProfileList"" VerticalScrollbar=""ProfileScroll"">
              <Children>
                <Widget Id=""ProfileClip"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" ClipContents=""true"">
                  <Children>
                    <ListPanel Id=""ProfileList"" DataSource=""{PerkQOLCaptainProfiles}"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""CoverChildren"" StackLayout.LayoutMethod=""VerticalTopToBottom"">
                      <ItemTemplate>
                        <ButtonWidget DoNotPassEventsToChildren=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""30"" MarginBottom=""2"" ButtonType=""Radio"" IsSelected=""@IsSelected"" UpdateChildrenStates=""true"" Command.Click=""ExecuteSelect"" Brush=""Standard.DropdownItem.SoundBrush"">
                          <Children>
                            <ImageWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""2"" MarginRight=""2"" Brush=""Standard.DropdownItem"" />
                            <TextWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""10"" MarginRight=""6"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""19"" Brush.TextHorizontalAlignment=""Left"" Text=""@Name"" />
                          </Children>
                        </ButtonWidget>
                      </ItemTemplate>
                    </ListPanel>
                  </Children>
                </Widget>
                <ScrollbarWidget Id=""ProfileScroll"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""StretchToParent"" SuggestedWidth=""8"" HorizontalAlignment=""Right"" MarginRight=""2"" AlignmentAxis=""Vertical"" Handle=""ProfileScrollHandle"" MaxValue=""100"" MinValue=""0"">
                  <Children>
                    <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""StretchToParent"" SuggestedWidth=""4"" HorizontalAlignment=""Center"" Sprite=""BlankWhiteSquare_9"" AlphaFactor=""0.15"" Color=""#3A2A20FF"" />
                    <Widget Id=""ProfileScrollHandle"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""8"" SuggestedHeight=""10"" HorizontalAlignment=""Center"" Sprite=""BlankWhiteSquare_9"" AlphaFactor=""0.45"" Color=""#8B7355FF"" />
                  </Children>
                </ScrollbarWidget>
              </Children>
            </ScrollablePanel>
          </Children>
        </Widget>

        <GridWidget DataSource=""{PerkQOLCaptainFlags}"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""CoverChildren"" SuggestedWidth=""360"" MarginLeft=""250"" MarginTop=""86"" DefaultCellWidth=""180"" DefaultCellHeight=""32"" ColumnCount=""2"">
          <ItemTemplate>
            <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"">
              <Children>
                <ListPanel WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" StackLayout.LayoutMethod=""HorizontalLeftToRight"">
                  <Children>
                    <ButtonWidget DoNotPassEventsToChildren=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""24"" SuggestedHeight=""24"" VerticalAlignment=""Center"" Brush=""SPOptions.Checkbox.Empty.Button"" ButtonType=""Toggle"" IsSelected=""@IsSelected"" ToggleIndicator=""ToggleIndicator"" UpdateChildrenStates=""true"">
                      <Children>
                        <ImageWidget Id=""ToggleIndicator"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Brush=""SPOptions.Checkbox.Full.Button"" />
                      </Children>
                    </ButtonWidget>
                    <TextWidget WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" VerticalAlignment=""Center"" MarginLeft=""6"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""20"" Text=""@Name"" />
                  </Children>
                </ListPanel>
                <HintWidget DataSource=""{Hint}"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Command.HoverBegin=""ExecuteBeginHint"" Command.HoverEnd=""ExecuteEndHint"" IsEnabled=""false"" />
              </Children>
            </Widget>
          </ItemTemplate>
        </GridWidget>

        <BrushWidget UpdateChildrenStates=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""360"" SuggestedHeight=""34"" MarginLeft=""250"" MarginTop=""330"" Brush=""SaveLoad.Search.Button"">
          <Children>
            <EditableTextWidget UpdateChildrenStates=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""12"" MarginRight=""12"" MarginTop=""4"" MarginBottom=""4"" Brush=""SaveLoad.Search.InputText"" Brush.FontSize=""18"" Text=""@PerkQOLTemplateName"" />
          </Children>
        </BrushWidget>

        <ListPanel WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""Fixed"" SuggestedHeight=""44"" HorizontalAlignment=""Right"" VerticalAlignment=""Bottom"" MarginRight=""28"" MarginBottom=""24"" StackLayout.LayoutMethod=""HorizontalLeftToRight"">
          <Children>
            <ButtonWidget DoNotPassEventsToChildren=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""100"" SuggestedHeight=""40"" MarginLeft=""4"" Brush=""Popup.Cancel.Button.NineGrid"" Command.Click=""ExecuteSavePerkQOLTemplate"" UpdateChildrenStates=""true"">
              <Children>
                <TextWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""6"" MarginRight=""6"" Brush=""SPOptions.Dropdown.Center.Text"" Brush.FontSize=""18"" Brush.TextHorizontalAlignment=""Center"" Text=""@PerkQOLTemplateSaveLabel"" />
              </Children>
            </ButtonWidget>
            <ButtonWidget DoNotPassEventsToChildren=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""110"" SuggestedHeight=""40"" MarginLeft=""4"" Brush=""Popup.Cancel.Button.NineGrid"" Command.Click=""ExecuteDuplicatePerkQOLTemplate"" UpdateChildrenStates=""true"">
              <Children>
                <TextWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""6"" MarginRight=""6"" Brush=""SPOptions.Dropdown.Center.Text"" Brush.FontSize=""18"" Brush.TextHorizontalAlignment=""Center"" Text=""@PerkQOLTemplateDuplicateLabel"" />
              </Children>
            </ButtonWidget>
            <ButtonWidget DoNotPassEventsToChildren=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""100"" SuggestedHeight=""40"" MarginLeft=""4"" Brush=""Popup.Cancel.Button.NineGrid"" Command.Click=""ExecuteUpdatePerkQOLTemplate"" UpdateChildrenStates=""true"">
              <Children>
                <TextWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""6"" MarginRight=""6"" Brush=""SPOptions.Dropdown.Center.Text"" Brush.FontSize=""18"" Brush.TextHorizontalAlignment=""Center"" Text=""@PerkQOLTemplateUpdateLabel"" />
              </Children>
            </ButtonWidget>
            <ButtonWidget DoNotPassEventsToChildren=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""100"" SuggestedHeight=""40"" MarginLeft=""4"" Brush=""Popup.Cancel.Button.NineGrid"" Command.Click=""ExecuteDeletePerkQOLTemplate"" UpdateChildrenStates=""true"">
              <Children>
                <TextWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""6"" MarginRight=""6"" Brush=""SPOptions.Dropdown.Center.Text"" Brush.FontSize=""18"" Brush.TextHorizontalAlignment=""Center"" Text=""@PerkQOLTemplateDeleteLabel"" />
              </Children>
            </ButtonWidget>
            <ButtonWidget DoNotPassEventsToChildren=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""100"" SuggestedHeight=""40"" MarginLeft=""4"" Brush=""Popup.Cancel.Button.NineGrid"" Command.Click=""ExecuteClosePerkQOLFlagMenu"" UpdateChildrenStates=""true"">
              <Children>
                <TextWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""6"" MarginRight=""6"" Brush=""SPOptions.Dropdown.Center.Text"" Brush.FontSize=""18"" Brush.TextHorizontalAlignment=""Center"" Text=""@PerkQOLFlagWindowCloseLabel"" />
              </Children>
            </ButtonWidget>
          </Children>
        </ListPanel>
        <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Sprite=""frame_9"" ExtendLeft=""18"" ExtendRight=""18"" ExtendTop=""18"" ExtendBottom=""18"" DoNotAcceptEvents=""true"" />
      </Children>
    </Widget>
  </Children>
</Widget>";
    }

    /// <summary>Replaces the skill name so its font color comes from the skill view-model.</summary>
    [PrefabExtension("SkillGridItem", "descendant::TextWidget[@Text='@NameText']")]
    public sealed class PerkHighlightSkillNamePatch : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Replace;

        private readonly XmlDocument _document = new XmlDocument();

        public PerkHighlightSkillNamePatch()
        {
            // Three temporary match styles for side-by-side comparison (left / middle / right skill columns).
            _document.LoadXml(
                "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"!NameText.Height\" " +
                "VerticalAlignment=\"Top\" MarginLeft=\"10\" DoNotAcceptEvents=\"true\">" +
                "<Children>" +
                "<ListPanel IsVisible=\"@IsPerkQOLSkillStyleSuffix\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
                "StackLayout.LayoutMethod=\"HorizontalLeftToRight\" DoNotAcceptEvents=\"true\">" +
                "<Children>" +
                "<TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"StretchToParent\" Brush=\"CharacterDeveloper.GridSkillName.Text\" " +
                "Brush.FontColor=\"#FDF1DFFF\" IsEnabled=\"@CanLearnSkill\" Text=\"@PerkQOLSkillBaseName\" DoNotAcceptEvents=\"true\" />" +
                "<TextWidget IsVisible=\"@IsPerkQOLSkillMatched\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"StretchToParent\" " +
                "Brush=\"CharacterDeveloper.GridSkillName.Text\" Brush.FontColor=\"#FF4D4DFF\" IsEnabled=\"@CanLearnSkill\" " +
                "Text=\"@PerkQOLSkillMatchSuffix\" DoNotAcceptEvents=\"true\" />" +
                "</Children>" +
                "</ListPanel>" +
                "<TextWidget IsVisible=\"@IsPerkQOLSkillStyleSelect\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
                "Brush=\"CharacterDeveloper.GridSkillName.Text\" Brush.FontColor=\"@PerkQOLSkillNameColor\" IsEnabled=\"@CanLearnSkill\" " +
                "Text=\"@PerkQOLSkillNameText\" DoNotAcceptEvents=\"true\" />" +
                "<Widget IsVisible=\"@IsPerkQOLSkillStyleGold\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" DoNotAcceptEvents=\"true\">" +
                "<Children>" +
                "<TextWidget IsVisible=\"@IsPerkQOLSkillMatched\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
                "PositionXOffset=\"1\" PositionYOffset=\"1\" Brush=\"CharacterDeveloper.GridSkillName.Text\" Brush.FontColor=\"#1A120CFF\" " +
                "IsEnabled=\"@CanLearnSkill\" Text=\"@PerkQOLSkillNameText\" DoNotAcceptEvents=\"true\" />" +
                "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
                "Brush=\"CharacterDeveloper.GridSkillName.Text\" Brush.FontColor=\"@PerkQOLSkillNameColor\" IsEnabled=\"@CanLearnSkill\" " +
                "Text=\"@PerkQOLSkillNameText\" DoNotAcceptEvents=\"true\" />" +
                "</Children>" +
                "</Widget>" +
                "</Children>" +
                "</Widget>");
        }

        [PrefabExtensionXmlNode]
        public XmlNode Node => _document.DocumentElement;
    }

    /// <summary>
    /// The prisoner-recruit icon lives in the party-screen atlas, which the character screen does not load. Load it with
    /// the screen and unload it again only if this screen was the one that loaded it.
    /// </summary>
    [HarmonyPatch]
    internal static class PartyScreenSpriteLoadPatch
    {
        private static MethodBase TargetMethod() => CharacterScreenMethodHolder.Method("TaleWorlds.Core.IGameStateListener.OnActivate");

    private static void Postfix() => PartyScreenSprites.Load();
}

[HarmonyPatch]
internal static class PartyScreenSpriteUnloadPatch
{
        private static MethodBase TargetMethod() => CharacterScreenMethodHolder.Method("TaleWorlds.Core.IGameStateListener.OnFinalize");

    private static void Postfix() => PartyScreenSprites.Unload();
}

internal static class PartyScreenSprites
{
    private static object _category;
    private static bool _owned;

    internal static void Load()
    {
        if (_category != null || !Settings.Current.PerkHighlight)
        {
            return;
        }

        Type manager = AccessTools.TypeByName("TaleWorlds.Engine.GauntletUI.UIResourceManager");
        object category = AccessTools.Method(manager, "GetSpriteCategory", new[] { typeof(string) })
            ?.Invoke(null, new object[] { "ui_partyscreen" });
        if (category == null)
        {
            return;
        }

        bool already = AccessTools.Property(category.GetType(), "IsLoaded")?.GetValue(category) is bool loaded && loaded;
        if (!already)
        {
            AccessTools.Method(manager, "LoadSpriteCategory", new[] { typeof(string) })
                ?.Invoke(null, new object[] { "ui_partyscreen" });
            _owned = true;
        }

        _category = category;
    }

    internal static void Unload()
    {
        if (_owned && _category != null)
        {
            AccessTools.Method(_category.GetType(), "Unload")?.Invoke(_category, null);
        }

        _category = null;
        _owned = false;
    }
}

internal static class CharacterScreenMethodHolder
{
    internal static MethodBase Method(string name)
    {
        Type screen = AccessTools.TypeByName("SandBox.GauntletUI.GauntletCharacterDeveloperScreen");
        return screen == null ? null : AccessTools.Method(screen, name);
    }
}
}

