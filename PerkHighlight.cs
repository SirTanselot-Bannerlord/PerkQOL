using System;
using System.Collections.Generic;
using Bannerlord.UIExtenderEx.Attributes;
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

        private static readonly TextObject[] CaptainFilterNames =
        {
            new TextObject("{=PQOL_CF_Any}Captain: Any"),
            new TextObject("{=PQOL_CF_Infantry}Captain: Infantry"),
            new TextObject("{=PQOL_CF_Ranged}Captain: Ranged"),
            new TextObject("{=PQOL_CF_Cavalry}Captain: Cavalry"),
            new TextObject("{=PQOL_CF_HorseArcher}Captain: Horse Archer"),
        };

        private static readonly HashSet<Mode> Selected = new HashSet<Mode>();

        private static readonly List<WeakReference<PerkHighlightPerkMixin>> Perks = new List<WeakReference<PerkHighlightPerkMixin>>();
        private static readonly List<WeakReference<PerkHighlightSkillMixin>> Skills = new List<WeakReference<PerkHighlightSkillMixin>>();

        private static CaptainTroopClass _captainFilter = CaptainTroopClass.Any;
        private static string _searchText = "";

        internal static string NameOf(Mode mode) => Names[mode].ToString();

        internal static IEnumerable<TextObject> CaptainFilterLabels => CaptainFilterNames;

        internal static bool IsSelected(Mode role) => Selected.Contains(role);

        internal static bool AllSelected => Selected.Count == Roles.Length;

        internal static CaptainTroopClass CaptainFilter
        {
            get => _captainFilter;
            set
            {
                if (value != _captainFilter)
                {
                    _captainFilter = value;
                    RefreshAll();
                }
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

            if (group != Mode.Captain || _captainFilter == CaptainTroopClass.Any)
            {
                return true;
            }

            return MaskMatchesFormation(mask, FormationFlagsOf(_captainFilter));
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

    [ViewModelMixin]
    public sealed class PerkHighlightScreenMixin : BaseViewModelMixin<CharacterDeveloperVM>
    {
        private MBBindingList<PerkHighlightRoleVM> _perkHighlightRoles;
        private SelectorVM<SelectorItemVM> _captainFilter;
        private string _searchText;
        private bool _isCaptainFilterVisible;

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

            _captainFilter = new SelectorVM<SelectorItemVM>(
                PerkHighlighter.CaptainFilterLabels,
                (int)PerkHighlighter.CaptainFilter,
                OnCaptainFilterChanged);

            _searchText = PerkHighlighter.SearchText;
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
            if (selector.SelectedIndex < 0)
            {
                return;
            }

            PerkHighlighter.CaptainFilter = (PerkHighlighter.CaptainTroopClass)selector.SelectedIndex;
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

        private void UpdateCaptainFilterVisible() =>
            IsPerkQOLCaptainFilterVisible = Settings.Current.PerkHighlight && PerkHighlighter.IsSelected(PerkHighlighter.Mode.Captain);

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
    /// Search, role tickboxes and the captain troop menu as one stack, so they cannot land on each other. The menu sits
    /// in the empty cell beside "All roles" and only while Captain is ticked; its list opens upward over the tickboxes.
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
    <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""28"" MarginBottom=""4"" Sprite=""BlankWhiteSquare_9"" Color=""#100C09E6"" ClipContents=""true"">
      <Children>
        <Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""1"" VerticalAlignment=""Bottom"" Sprite=""BlankWhiteSquare_9"" Color=""#C1A57BFF"" DoNotAcceptEvents=""true"" />
        <EditableTextWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""8"" MarginRight=""8"" MarginBottom=""2"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""18"" Text=""@PerkQOLSearchText"" DefaultSearchText=""@PerkQOLSearchPlaceholder"" />
      </Children>
    </Widget>
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
    <Widget IsVisible=""@IsPerkQOLCaptainFilterVisible"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""200"" SuggestedHeight=""28"" HorizontalAlignment=""Right"" MarginTop=""-30"">
      <Children>
        <DropdownWidget Id=""CaptainTroopDropdown"" DataSource=""{PerkQOLCaptainFilter}"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" ListPanel=""SelectionList"" Button=""DropdownButton"" TextWidget=""DropdownButton\SelectedText"" CurrentSelectedIndex=""@SelectedIndex"">
          <Children>
            <ButtonWidget Id=""DropdownButton"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Brush=""SPOptions.Dropdown.Center"" ButtonType=""Toggle"" DoNotPassEventsToChildren=""true"" UpdateChildrenStates=""true"">
              <Children>
                <TextWidget Id=""SelectedText"" DataSource=""{SelectedItem}"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" MarginLeft=""8"" MarginRight=""8"" Brush=""SPOptions.Dropdown.Center.Text"" Brush.FontSize=""16"" Text=""@StringItem"" />
              </Children>
            </ButtonWidget>
            <ListPanel Id=""SelectionList"" DataSource=""{ItemList}"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""CoverChildren"" VerticalAlignment=""Bottom"" MarginBottom=""28"" Sprite=""BlankWhiteSquare_9"" Color=""#1A120CFF"" IsVisible=""false"" StackLayout.LayoutMethod=""VerticalTopToBottom"">
              <ItemTemplate>
                <ButtonWidget Id=""DropdownItemButton"" DoNotPassEventsToChildren=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""26"" ButtonType=""Radio"" UpdateChildrenStates=""true"" Brush=""Standard.DropdownItem.SoundBrush"">
                  <Children>
                    <ImageWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Brush=""Standard.DropdownItem"" />
                    <TextWidget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Brush=""SPOptions.Dropdown.Item.Text"" Brush.FontSize=""16"" Text=""@StringItem"" />
                  </Children>
                </ButtonWidget>
              </ItemTemplate>
            </ListPanel>
          </Children>
        </DropdownWidget>
      </Children>
    </Widget>
  </Children>
</ListPanel>";
    }

    /// <summary>Thin white bar on skill cards that contain a highlighted perk. Not the card's own selection frame.</summary>
    [PrefabExtension("SkillGridItem", "descendant::SkillGridItemButtonWidget/Children")]
    public sealed class PerkHighlightSkillFramePatch : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Child;

        public override int Index => int.MaxValue;

        [PrefabExtensionText]
        public string Content => @"
<Widget IsVisible=""@IsPerkQOLSkillHighlighted"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""Fixed"" SuggestedHeight=""3"" VerticalAlignment=""Top"" MarginLeft=""6"" MarginRight=""6"" MarginTop=""2"" Sprite=""BlankWhiteSquare_9"" Color=""#FFFFFFFF"" DoNotAcceptEvents=""true"" />";
    }
}
