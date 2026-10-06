using System;
using System.Collections.Generic;
using System.Reflection;
using Bannerlord.UIExtenderEx.Attributes;
using HarmonyLib;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterDeveloper;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace PerkQOL
{
    /// <summary>
    /// Character screen role highlighting: frames every perk with an effect for the selected roles, each role in its own
    /// colour, picked with a tickbox per role. Captain can be narrowed to a troop class. A search bar highlights matching
    /// perks (and related captain perks via troop-usage masks) in white, and skills with any match get a white marker.
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

        internal static readonly TextObject SearchPlaceholder = new TextObject("{=PQOL_HL_Search}Search perks");
        internal static readonly TextObject SearchLabel = new TextObject("{=PQOL_HL_SearchLabel}Search perks:");

        private static readonly TextObject[] CaptainFilterNames =
        {
            new TextObject("{=PQOL_CF_Any}Any"),
            new TextObject("{=PQOL_CF_Infantry}Infantry"),
            new TextObject("{=PQOL_CF_Ranged}Archer"),
            new TextObject("{=PQOL_CF_Cavalry}Cavalry"),
            new TextObject("{=PQOL_CF_HorseArcher}H. Archer"),
            new TextObject("{=PQOL_CF_Custom}Custom"),
        };

        /// <summary>Index of the dropdown row that means "the ticks no longer match a preset".</summary>
        internal const int CustomPresetIndex = 5;

        private const TroopUsageFlags AllTroopFlags =
            TroopUsageFlags.OnFoot | TroopUsageFlags.Mounted | TroopUsageFlags.Melee | TroopUsageFlags.Ranged
            | TroopUsageFlags.OneHandedUser | TroopUsageFlags.ShieldUser | TroopUsageFlags.TwoHandedUser
            | TroopUsageFlags.PolearmUser | TroopUsageFlags.BowUser | TroopUsageFlags.ThrownUser
            | TroopUsageFlags.CrossbowUser;

        internal readonly struct CaptainFlagOption
        {
            internal readonly TextObject Name;
            internal readonly TroopUsageFlags Flag;
            internal readonly bool Generic;

            internal CaptainFlagOption(TextObject name, TroopUsageFlags flag, bool generic = false)
            {
                Name = name;
                Flag = flag;
                Generic = generic;
            }
        }

        /// <summary>
        /// Flags the captain menu can tick. Generic is the untagged captain effects (mask None), not a troop flag.
        /// Bandit culture is left out on purpose.
        /// </summary>
        internal static readonly CaptainFlagOption[] FlagOptions =
        {
            new CaptainFlagOption(new TextObject("{=PQOL_FF_OnFoot}On foot"), TroopUsageFlags.OnFoot),
            new CaptainFlagOption(new TextObject("{=PQOL_FF_Mounted}Mounted"), TroopUsageFlags.Mounted),
            new CaptainFlagOption(new TextObject("{=PQOL_FF_Melee}Melee"), TroopUsageFlags.Melee),
            new CaptainFlagOption(new TextObject("{=PQOL_FF_Ranged}Ranged"), TroopUsageFlags.Ranged),
            new CaptainFlagOption(new TextObject("{=PQOL_FF_OneHanded}1-Handed"), TroopUsageFlags.OneHandedUser),
            new CaptainFlagOption(new TextObject("{=PQOL_FF_Shield}Shield"), TroopUsageFlags.ShieldUser),
            new CaptainFlagOption(new TextObject("{=PQOL_FF_TwoHanded}2-Handed"), TroopUsageFlags.TwoHandedUser),
            new CaptainFlagOption(new TextObject("{=PQOL_FF_Polearm}Polearm"), TroopUsageFlags.PolearmUser),
            new CaptainFlagOption(new TextObject("{=PQOL_FF_Bow}Bow"), TroopUsageFlags.BowUser),
            new CaptainFlagOption(new TextObject("{=PQOL_FF_Thrown}Thrown"), TroopUsageFlags.ThrownUser),
            new CaptainFlagOption(new TextObject("{=PQOL_FF_Crossbow}Crossbow"), TroopUsageFlags.CrossbowUser),
            new CaptainFlagOption(new TextObject("{=PQOL_FF_Generic}Generic"), TroopUsageFlags.None, true),
        };

        private static readonly HashSet<Mode> Selected = new HashSet<Mode>();

        private static readonly List<WeakReference<PerkHighlightPerkMixin>> Perks = new List<WeakReference<PerkHighlightPerkMixin>>();
        private static readonly List<WeakReference<PerkHighlightSkillMixin>> Skills = new List<WeakReference<PerkHighlightSkillMixin>>();

        private static TroopUsageFlags _captainFlags = AllTroopFlags;
        private static bool _includeUntagged = true;
        private static string _searchText = "";
        private static WeakReference<PerkHighlightScreenMixin> _screen;

        internal static string NameOf(Mode mode) => Names[mode].ToString();

        internal static IEnumerable<TextObject> CaptainFilterLabels => CaptainFilterNames;

        internal static bool IsSelected(Mode role) => Selected.Contains(role);

        internal static bool AllSelected => Selected.Count == Roles.Length;

        /// <summary>Dropdown index: a preset, or <see cref="CustomPresetIndex"/> when the ticks match none.</summary>
        internal static int PresetIndex()
        {
            if (_includeUntagged && _captainFlags == AllTroopFlags)
            {
                return (int)CaptainTroopClass.Any;
            }

            if (!_includeUntagged)
            {
                if (_captainFlags == FormationFlagsOf(CaptainTroopClass.Infantry)) return (int)CaptainTroopClass.Infantry;
                if (_captainFlags == FormationFlagsOf(CaptainTroopClass.Ranged)) return (int)CaptainTroopClass.Ranged;
                if (_captainFlags == FormationFlagsOf(CaptainTroopClass.Cavalry)) return (int)CaptainTroopClass.Cavalry;
                if (_captainFlags == FormationFlagsOf(CaptainTroopClass.HorseArcher)) return (int)CaptainTroopClass.HorseArcher;
            }

            return CustomPresetIndex;
        }

        internal static bool IsFlagOn(CaptainFlagOption option) =>
            option.Generic ? _includeUntagged : _captainFlags.HasAllFlags(option.Flag);

        internal static void BindScreen(PerkHighlightScreenMixin screen) =>
            _screen = new WeakReference<PerkHighlightScreenMixin>(screen);

        internal static void ApplyPreset(CaptainTroopClass troopClass)
        {
            TroopUsageFlags flags = troopClass == CaptainTroopClass.Any ? AllTroopFlags : FormationFlagsOf(troopClass);
            bool generic = troopClass == CaptainTroopClass.Any;
            if (flags == _captainFlags && generic == _includeUntagged)
            {
                return;
            }

            _captainFlags = flags;
            _includeUntagged = generic;
            RefreshAll();
            NotifyScreen();
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
                TroopUsageFlags next = selected ? _captainFlags | option.Flag : _captainFlags & ~option.Flag;
                if (next == _captainFlags)
                {
                    return;
                }

                _captainFlags = next;
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

            // None is "everyone in the formation", which the presets leave off except Any. The flag menu's Generic tick
            // is that bucket. A real mask matches when every one of its flags is ticked.
            if (mask == TroopUsageFlags.None)
            {
                return _includeUntagged;
            }

            return MaskMatchesFormation(mask, _captainFlags);
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
                    || MaskMatchesFormation(perk.SecondaryTroopUsageMask, formation)))
            {
                return true;
            }

            return false;
        }

        internal static bool SkillHasMatch(SkillObject skill)
        {
            if (skill == null || (Selected.Count == 0 && !HasActiveSearch()))
            {
                return false;
            }

            foreach (PerkObject perk in PerkObject.All)
            {
                if (perk.Skill != skill)
                {
                    continue;
                }

                if (TryGetRoleColors(perk, out _, out _) || MatchesSearch(perk))
                {
                    return true;
                }
            }

            return false;
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
        private static bool MaskMatchesFormation(TroopUsageFlags mask, TroopUsageFlags formation) =>
            mask != TroopUsageFlags.Undefined && mask != TroopUsageFlags.None && formation.HasAllFlags(mask);

        private static TroopUsageFlags FormationFlagsOf(CaptainTroopClass troopClass)
        {
            switch (troopClass)
            {
                case CaptainTroopClass.Infantry: return FormationClass.Infantry.GetTroopUsageFlags();
                case CaptainTroopClass.Ranged: return FormationClass.Ranged.GetTroopUsageFlags();
                case CaptainTroopClass.Cavalry: return FormationClass.Cavalry.GetTroopUsageFlags();
                case CaptainTroopClass.HorseArcher: return FormationClass.HorseArcher.GetTroopUsageFlags();
                default: return TroopUsageFlags.Undefined;
            }
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
        }

        internal PerkHighlighter.CaptainFlagOption Option { get; }

        [DataSourceProperty]
        public string Name { get; }

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

    [ViewModelMixin]
    public sealed class PerkHighlightScreenMixin : BaseViewModelMixin<CharacterDeveloperVM>
    {
        private MBBindingList<PerkHighlightRoleVM> _perkHighlightRoles;
        private MBBindingList<CaptainFlagVM> _captainFlags;
        private SelectorVM<SelectorItemVM> _captainFilter;
        private string _searchText;
        private bool _isCaptainFilterVisible;
        private bool _isFlagMenuOpen;
        private bool _suppressDropdown;

        public PerkHighlightScreenMixin(CharacterDeveloperVM vm) : base(vm)
        {
            // Two columns filled row by row, with All alone on the last row. The right column grows in command scope.
            _perkHighlightRoles = new MBBindingList<PerkHighlightRoleVM>();
            foreach (PerkHighlighter.Mode role in new[]
            {
                PerkHighlighter.Mode.PersonalCombat, PerkHighlighter.Mode.Captain,
                PerkHighlighter.Mode.PersonalCampaign, PerkHighlighter.Mode.PartyLeader,
                PerkHighlighter.Mode.PartyRole, PerkHighlighter.Mode.ClanLeader,
                PerkHighlighter.Mode.Governor, PerkHighlighter.Mode.ArmyCommander,
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

            _captainFilter = new SelectorVM<SelectorItemVM>(
                PerkHighlighter.CaptainFilterLabels,
                PerkHighlighter.PresetIndex(),
                OnCaptainFilterChanged);

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
        public string PerkQOLSearchLabel => PerkHighlighter.SearchLabel.ToString();

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

            if (selector.SelectedIndex >= PerkHighlighter.CustomPresetIndex)
            {
                SyncDropdown();
                return;
            }

            PerkHighlighter.ApplyPreset((PerkHighlighter.CaptainTroopClass)selector.SelectedIndex);
        }

        private void OnFlagToggled(CaptainFlagVM item) => PerkHighlighter.SetFlag(item.Option, item.IsSelected);

        public void ExecuteTogglePerkQOLFlagMenu() => IsPerkQOLFlagMenuOpen = !_isFlagMenuOpen;

        /// <summary>Pulls the flag ticks and the preset label back in line with the highlighter.</summary>
        internal void SyncCaptainFlags()
        {
            foreach (CaptainFlagVM item in _captainFlags)
            {
                item.Sync(PerkHighlighter.IsFlagOn(item.Option));
            }

            SyncDropdown();
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
        public bool IsPerkQOLFlagMenuOpen
        {
            get => _isFlagMenuOpen;
            set
            {
                if (value != _isFlagMenuOpen)
                {
                    _isFlagMenuOpen = value;
                    OnPropertyChangedWithValue(value);
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
            bool search = PerkHighlighter.MatchesSearch(_perk);
            if (role)
            {
                PerkQOLHighlightLeftColor = left;
                PerkQOLHighlightRightColor = right;
            }

            IsPerkQOLHighlighted = role;
            // Role colours win when both match; search still lights the skill via MatchesSearch.
            IsPerkQOLSearchHighlighted = search && !role;
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

    [ViewModelMixin]
    public sealed class PerkHighlightSkillMixin : BaseViewModelMixin<SkillVM>
    {
        private readonly SkillObject _skill;
        private bool _isHighlighted;

        public PerkHighlightSkillMixin(SkillVM vm) : base(vm)
        {
            _skill = vm.Skill;
            _isHighlighted = PerkHighlighter.SkillHasMatch(_skill);
            PerkHighlighter.Track(this);
        }

        internal void Refresh() => IsPerkQOLSkillHighlighted = PerkHighlighter.SkillHasMatch(_skill);

        [DataSourceProperty]
        public bool IsPerkQOLSkillHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (value != _isHighlighted)
                {
                    _isHighlighted = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }
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
    /// Search label plus input, then the role tickboxes. The captain preset sits on the Captain row, with the party
    /// upgrade icon opening the flag ticks. Both show only while Captain is on.
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
    <ListPanel WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""28"" MarginBottom=""4"" StackLayout.LayoutMethod=""HorizontalLeftToRight"">
      <Children>
        <TextWidget WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" VerticalAlignment=""Center"" MarginRight=""8"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""18"" Text=""@PerkQOLSearchLabel"" />
        <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Sprite=""BlankWhiteSquare_9"" Color=""#100C09E6"" ClipContents=""true"">
          <Children>
            <EditableTextWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""8"" MarginRight=""8"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""18"" Text=""@PerkQOLSearchText"" />
          </Children>
        </Widget>
      </Children>
    </ListPanel>
    <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""150"">
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
        <ListPanel IsVisible=""@IsPerkQOLCaptainFilterVisible"" WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""Fixed"" SuggestedHeight=""26"" HorizontalAlignment=""Right"" VerticalAlignment=""Top"" MarginTop=""2"" MarginRight=""2"" StackLayout.LayoutMethod=""HorizontalLeftToRight"">
              <Children>
                <DropdownWidget Id=""CaptainTroopDropdown"" DataSource=""{PerkQOLCaptainFilter}"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""100"" SuggestedHeight=""26"" ListPanel=""SelectionList"" Button=""DropdownButton"" TextWidget=""DropdownButton\SelectedText"" CurrentSelectedIndex=""@SelectedIndex"">
                  <Children>
                    <ButtonWidget Id=""DropdownButton"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""100"" SuggestedHeight=""26"" Sprite=""BlankWhiteSquare_9"" Color=""#2A2118FF"" ButtonType=""Toggle"" DoNotPassEventsToChildren=""true"" UpdateChildrenStates=""true"" ClipContents=""true"">
                      <Children>
                        <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""1"" VerticalAlignment=""Bottom"" Sprite=""BlankWhiteSquare_9"" Color=""#C1A57BFF"" DoNotAcceptEvents=""true"" />
                        <TextWidget Id=""SelectedText"" DataSource=""{SelectedItem}"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""14"" Brush.TextHorizontalAlignment=""Center"" Text=""@StringItem"" />
                      </Children>
                    </ButtonWidget>
                    <ListPanel Id=""SelectionList"" DataSource=""{ItemList}"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""CoverChildren"" SuggestedWidth=""100"" Sprite=""BlankWhiteSquare_9"" Color=""#1A120CFF"" IsVisible=""false"" StackLayout.LayoutMethod=""VerticalTopToBottom"" ClipContents=""true"">
                      <ItemTemplate>
                        <ButtonWidget Id=""DropdownItemButton"" DoNotPassEventsToChildren=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""100"" SuggestedHeight=""22"" ButtonType=""Radio"" UpdateChildrenStates=""true"" Brush=""Standard.DropdownItem"">
                          <Children>
                            <TextWidget WidthSizePolicy=""Fixed"" HeightSizePolicy=""StretchToParent"" SuggestedWidth=""100"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""14"" Brush.TextHorizontalAlignment=""Center"" Text=""@StringItem"" />
                          </Children>
                        </ButtonWidget>
                      </ItemTemplate>
                    </ListPanel>
                  </Children>
                </DropdownWidget>
                <ButtonWidget WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""24"" SuggestedHeight=""20"" VerticalAlignment=""Center"" MarginLeft=""4"" Command.Click=""ExecuteTogglePerkQOLFlagMenu"" DoNotPassEventsToChildren=""true"">
                  <Children>
                    <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Sprite=""PartyScreen\upgrade_icon"" DoNotAcceptEvents=""true"" />
                  </Children>
                </ButtonWidget>
              </Children>
            </ListPanel>
        <Widget IsVisible=""@IsPerkQOLFlagMenuOpen"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""CoverChildren"" SuggestedWidth=""248"" HorizontalAlignment=""Right"" VerticalAlignment=""Top"" MarginTop=""32"" MarginRight=""2"" Sprite=""BlankWhiteSquare_9"" Color=""#1A120CFF"">
              <Children>
                <GridWidget DataSource=""{PerkQOLCaptainFlags}"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""CoverChildren"" MarginLeft=""4"" MarginTop=""4"" MarginBottom=""4"" DefaultCellWidth=""120"" DefaultCellHeight=""26"" ColumnCount=""2"">
                  <ItemTemplate>
                    <ListPanel WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" StackLayout.LayoutMethod=""HorizontalLeftToRight"">
                      <Children>
                        <ButtonWidget DoNotPassEventsToChildren=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""22"" SuggestedHeight=""22"" VerticalAlignment=""Center"" Brush=""SPOptions.Checkbox.Empty.Button"" ButtonType=""Toggle"" IsSelected=""@IsSelected"" ToggleIndicator=""ToggleIndicator"" UpdateChildrenStates=""true"">
                          <Children>
                            <ImageWidget Id=""ToggleIndicator"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Brush=""SPOptions.Checkbox.Full.Button"" />
                          </Children>
                        </ButtonWidget>
                        <TextWidget WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" VerticalAlignment=""Center"" MarginLeft=""4"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""16"" Text=""@Name"" />
                      </Children>
                    </ListPanel>
                  </ItemTemplate>
                </GridWidget>
              </Children>
            </Widget>
      </Children>
    </Widget>
  </Children>
</ListPanel>";
    }

    /// <summary>White outline on a skill card when any of its perks is highlighted.</summary>
    [PrefabExtension("SkillGridItem", "descendant::SkillGridItemButtonWidget/Children")]
    public sealed class PerkHighlightSkillFramePatch : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Child;

        public override int Index => int.MaxValue;

        [PrefabExtensionText]
        public string Content => @"
<Widget IsVisible=""@IsPerkQOLSkillHighlighted"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" DoNotAcceptEvents=""true"">
  <Children>
    <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""2"" VerticalAlignment=""Top"" Sprite=""BlankWhiteSquare_9"" Color=""#FFFFFFFF"" DoNotAcceptEvents=""true"" />
    <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""2"" VerticalAlignment=""Bottom"" Sprite=""BlankWhiteSquare_9"" Color=""#FFFFFFFF"" DoNotAcceptEvents=""true"" />
    <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""StretchToParent"" SuggestedWidth=""2"" HorizontalAlignment=""Left"" Sprite=""BlankWhiteSquare_9"" Color=""#FFFFFFFF"" DoNotAcceptEvents=""true"" />
    <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""StretchToParent"" SuggestedWidth=""2"" HorizontalAlignment=""Right"" Sprite=""BlankWhiteSquare_9"" Color=""#FFFFFFFF"" DoNotAcceptEvents=""true"" />
  </Children>
</Widget>";
    }

    /// <summary>
    /// The upgrade icon lives in the party-screen atlas, which the character screen does not load. Load it with the screen
    /// and unload it again only if this screen was the one that loaded it.
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

