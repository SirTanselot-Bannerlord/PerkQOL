using System;
using System.Collections.Generic;
using System.Linq;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterDeveloper;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace PerkQOL
{
    /// <summary>
    /// Swap-only perk respec on the character screen. Swaps are staged per hero (owned perk -> its alternative) and
    /// applied with the screen's other pending changes; the player pays for every hero's swaps.
    /// </summary>
    internal static class PerkRespec
    {
        private static readonly Dictionary<Hero, Dictionary<PerkObject, PerkObject>> Swaps = new Dictionary<Hero, Dictionary<PerkObject, PerkObject>>();

        private static readonly Dictionary<Hero, CharacterDeveloperHeroItemVM> HeroItems = new Dictionary<Hero, CharacterDeveloperHeroItemVM>();

        private static readonly List<WeakReference<PerkRespecPerkMixin>> Perks = new List<WeakReference<PerkRespecPerkMixin>>();

        private static WeakReference<PerkRespecScreenMixin> _screen;

        private static readonly AccessTools.FieldRef<SkillVM, CharacterDeveloperHeroItemVM> HeroItemOf =
            AccessTools.FieldRefAccess<SkillVM, CharacterDeveloperHeroItemVM>("_heroItem");

        private static readonly AccessTools.FieldRef<PerkVM, Func<PerkObject, bool>> IsPerkSelectedOf =
            AccessTools.FieldRefAccess<PerkVM, Func<PerkObject, bool>>("_getIsPerkSelected");

        private static readonly Action<Hero, PerkObject, bool> SetPerkValue =
            AccessTools.MethodDelegate<Action<Hero, PerkObject, bool>>(AccessTools.Method(typeof(Hero), "SetPerkValueInternal"));

        private static readonly Action<Hero, PerkObject> ClearPermanentBonuses =
            AccessTools.MethodDelegate<Action<Hero, PerkObject>>(AccessTools.Method(typeof(PerkHelper), "ClearPermanentBonusesIfExists"));

        internal static bool IsOn { get; set; }

        internal static bool IsActive => IsOn && Settings.Current.PerkRespec && IsLocationAllowed;

        internal static int TotalCost => Swaps.Values.Sum(CostOfSwaps);

        private static Settlement PlayerSettlement => MobileParty.MainParty?.CurrentSettlement ?? Hero.MainHero?.CurrentSettlement;

        internal static bool IsLocationAllowed => !Settings.Current.RespecTownOnly || PlayerSettlement?.IsTown == true;

        /// <summary>Starts a screen session. Perk mixins are created before the screen mixin, so they're kept.</summary>
        internal static void BeginSession(PerkRespecScreenMixin screen)
        {
            Swaps.Clear();
            HeroItems.Clear();
            Perks.RemoveAll(w => !w.TryGetTarget(out _));
            IsOn = Settings.Current.RespecOnByDefault;
            _screen = new WeakReference<PerkRespecScreenMixin>(screen);
        }

        private static bool IsWithPlayer(Hero hero)
        {
            if (hero == Hero.MainHero || !Settings.Current.RespecCompanionsNearby)
            {
                return true;
            }

            MobileParty main = MobileParty.MainParty;
            if (main != null && hero.PartyBelongedTo != null
                && (hero.PartyBelongedTo == main || (main.Army != null && hero.PartyBelongedTo.Army == main.Army)))
            {
                return true;
            }

            Settlement settlement = PlayerSettlement;
            return settlement != null && hero.CurrentSettlement == settlement;
        }

        internal static void Track(PerkRespecPerkMixin mixin)
        {
            Perks.RemoveAll(w => !w.TryGetTarget(out _));
            Perks.Add(new WeakReference<PerkRespecPerkMixin>(mixin));
        }

        internal static CharacterDeveloperHeroItemVM HeroItemOfPerk(PerkVM perk) =>
            IsPerkSelectedOf(perk)?.Target is SkillVM skill ? HeroItemOf(skill) : null;

        internal static CharacterDeveloperHeroItemVM HeroItemOfSkill(SkillVM skill) => HeroItemOf(skill);

        internal static bool IsPending(Hero hero, PerkObject perk) =>
            hero != null && Swaps.TryGetValue(hero, out var swaps) && swaps.ContainsValue(perk);

        internal static bool HasSwaps(Hero hero) => hero != null && Swaps.TryGetValue(hero, out var swaps) && swaps.Count > 0;

        /// <summary>Selection as the screen should show it: a swapped-out perk reads as unselected, its replacement as selected.</summary>
        internal static bool? Override(Hero hero, PerkObject perk)
        {
            if (hero == null || !Swaps.TryGetValue(hero, out var swaps))
            {
                return null;
            }

            if (swaps.ContainsKey(perk))
            {
                return false;
            }

            if (swaps.ContainsValue(perk))
            {
                return true;
            }

            return null;
        }

        /// <summary>Handles a perk click in respec mode. Returns false when the click isn't a swap, so vanilla handles it.</summary>
        internal static bool TryClick(CharacterDeveloperHeroItemVM heroItem, PerkObject clicked)
        {
            Hero hero = heroItem?.Hero;
            PerkObject alternative = clicked?.AlternativePerk;
            if (hero == null || alternative == null)
            {
                return false;
            }

            PerkObject owned = hero.GetPerkValue(clicked) ? clicked : hero.GetPerkValue(alternative) ? alternative : null;
            if (owned == null)
            {
                return false;
            }

            if (!Swaps.TryGetValue(hero, out var swaps))
            {
                swaps = new Dictionary<PerkObject, PerkObject>();
                Swaps[hero] = swaps;
            }

            bool toggles = Settings.Current.RespecClickToggles;
            if (swaps.ContainsKey(owned))
            {
                if (!toggles && clicked != owned)
                {
                    return true;
                }

                swaps.Remove(owned);
            }
            else
            {
                if (!toggles && clicked == owned)
                {
                    return true;
                }

                PerkObject target = owned.AlternativePerk;

                if (!IsWithPlayer(hero))
                {
                    TextObject away = new TextObject("{=PQOL_RS_Away}{HERO.NAME} must be with you to swap perks.");
                    StringHelpers.SetCharacterProperties("HERO", hero.CharacterObject, away);
                    InformationManager.DisplayMessage(new InformationMessage(away.ToString(), Colors.Red));
                    return true;
                }

                int cost = CostOf(target);
                int needed = TotalCost + cost;
                if (needed > Hero.MainHero.Gold)
                {
                    TextObject message = new TextObject("{=PQOL_RS_NoGold}Not enough gold for this swap ({NEEDED} needed in total).");
                    message.SetTextVariable("NEEDED", needed);
                    InformationManager.DisplayMessage(new InformationMessage(message.ToString(), Colors.Red));
                    return true;
                }

                swaps[owned] = target;
            }

            HeroItems[hero] = heroItem;
            heroItem.RefreshPerksOfSkill(clicked.Skill);
            Changed();
            return true;
        }

        /// <summary>Drops every staged swap, e.g. when the respec tickbox is turned off.</summary>
        internal static void ClearAll()
        {
            var cleared = Swaps.ToList();
            Swaps.Clear();
            foreach (KeyValuePair<Hero, Dictionary<PerkObject, PerkObject>> entry in cleared)
            {
                if (HeroItems.TryGetValue(entry.Key, out CharacterDeveloperHeroItemVM heroItem))
                {
                    foreach (SkillObject skill in entry.Value.Keys.Select(p => p.Skill).Distinct())
                    {
                        heroItem.RefreshPerksOfSkill(skill);
                    }
                }
            }

            Changed();
        }

        internal static void Apply(Hero hero)
        {
            if (hero == null || !Swaps.TryGetValue(hero, out var swaps) || swaps.Count == 0)
            {
                return;
            }

            Swaps.Remove(hero);
            int cost = CostOfSwaps(swaps);
            if (cost > Hero.MainHero.Gold)
            {
                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=PQOL_RS_Cancelled}Perk swaps cancelled: not enough gold.").ToString(), Colors.Red));
                Changed();
                return;
            }

            foreach (KeyValuePair<PerkObject, PerkObject> swap in swaps)
            {
                ClearPermanentBonuses(hero, swap.Key);
                SetPerkValue(hero, swap.Key, false);
                hero.HeroDeveloper.AddPerk(swap.Value);
            }

            if (hero.HitPoints > hero.MaxHitPoints)
            {
                hero.HitPoints = hero.MaxHitPoints;
            }

            PartyBase.MainParty?.MemberRoster.UpdateVersion();
            if (cost > 0)
            {
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, cost);
            }

            Changed();
        }

        internal static void Clear(Hero hero)
        {
            if (hero != null && Swaps.Remove(hero))
            {
                Changed();
            }
        }

        private static int CostOf(PerkObject perk) => Math.Max(0, Settings.Current.RespecCostPerPerk);

        private static int CostOfSwaps(Dictionary<PerkObject, PerkObject> swaps) => swaps.Values.Sum(CostOf);

        private static void Changed()
        {
            foreach (WeakReference<PerkRespecPerkMixin> weak in Perks)
            {
                if (weak.TryGetTarget(out PerkRespecPerkMixin mixin))
                {
                    mixin.Refresh();
                }
            }

            if (_screen != null && _screen.TryGetTarget(out PerkRespecScreenMixin screen))
            {
                screen.Refresh();
            }
        }
    }

    [HarmonyPatch(typeof(SkillVM), "IsPerkSelected")]
    internal static class PerkRespecIsPerkSelectedPatch
    {
        private static void Postfix(SkillVM __instance, PerkObject perk, ref bool __result)
        {
            bool? shown = PerkRespec.Override(PerkRespec.HeroItemOfSkill(__instance)?.Hero, perk);
            if (shown.HasValue)
            {
                __result = shown.Value;
            }
        }
    }

    [HarmonyPatch(typeof(PerkVM), nameof(PerkVM.ExecuteStartSelection))]
    internal static class PerkRespecClickPatch
    {
        private static bool Prefix(PerkVM __instance)
        {
            return !PerkRespec.IsActive || !PerkRespec.TryClick(PerkRespec.HeroItemOfPerk(__instance), __instance.Perk);
        }
    }

    [HarmonyPatch(typeof(CharacterDeveloperHeroItemVM), nameof(CharacterDeveloperHeroItemVM.ApplyChanges))]
    internal static class PerkRespecApplyPatch
    {
        private static void Prefix(CharacterDeveloperHeroItemVM __instance) => PerkRespec.Apply(__instance.Hero);
    }

    [HarmonyPatch(typeof(CharacterDeveloperHeroItemVM), nameof(CharacterDeveloperHeroItemVM.ResetChanges))]
    internal static class PerkRespecResetPatch
    {
        private static void Prefix(CharacterDeveloperHeroItemVM __instance) => PerkRespec.Clear(__instance.Hero);
    }

    [HarmonyPatch(typeof(CharacterDeveloperHeroItemVM), nameof(CharacterDeveloperHeroItemVM.IsThereAnyChanges))]
    internal static class PerkRespecAnyChangesPatch
    {
        private static void Postfix(CharacterDeveloperHeroItemVM __instance, ref bool __result) =>
            __result |= PerkRespec.HasSwaps(__instance.Hero);
    }

    [ViewModelMixin]
    public sealed class PerkRespecScreenMixin : BaseViewModelMixin<CharacterDeveloperVM>
    {
        private bool _isPerkQOLRespecOn;

        private string _perkQOLRespecCostText = "";

        private bool _isPerkQOLRespecCostVisible;

        public PerkRespecScreenMixin(CharacterDeveloperVM vm) : base(vm)
        {
            PerkRespec.BeginSession(this);
            _isPerkQOLRespecOn = PerkRespec.IsOn;
            Refresh();
        }

        internal void Refresh()
        {
            int cost = PerkRespec.TotalCost;
            TextObject text = new TextObject("{=PQOL_RS_Cost}Cost: {COST}");
            text.SetTextVariable("COST", cost);
            PerkQOLRespecCostText = text + " <img src=\"General\\Icons\\Coin@2x\" extend=\"8\">";
            IsPerkQOLRespecCostVisible = PerkRespec.IsActive || cost > 0;
        }

        [DataSourceProperty]
        public bool IsPerkQOLRespecVisible => Settings.Current.PerkRespec;

        [DataSourceProperty]
        public bool IsPerkQOLRespecEnabled => PerkRespec.IsLocationAllowed;

        [DataSourceProperty]
        public string PerkQOLRespecLabel => PerkRespec.IsLocationAllowed
            ? new TextObject("{=PQOL_RS_Label}Respec perks").ToString()
            : new TextObject("{=PQOL_RS_LabelTown}Respec perks (in towns)").ToString();

        [DataSourceProperty]
        public bool IsPerkQOLRespecOn
        {
            get => _isPerkQOLRespecOn;
            set
            {
                if (value != _isPerkQOLRespecOn)
                {
                    _isPerkQOLRespecOn = value;
                    PerkRespec.IsOn = value;
                    if (!value)
                    {
                        PerkRespec.ClearAll();
                    }

                    OnPropertyChangedWithValue(value);
                    Refresh();
                }
            }
        }

        [DataSourceProperty]
        public string PerkQOLRespecCostText
        {
            get => _perkQOLRespecCostText;
            set
            {
                if (value != _perkQOLRespecCostText)
                {
                    _perkQOLRespecCostText = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool IsPerkQOLRespecCostVisible
        {
            get => _isPerkQOLRespecCostVisible;
            set
            {
                if (value != _isPerkQOLRespecCostVisible)
                {
                    _isPerkQOLRespecCostVisible = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }
    }

    [ViewModelMixin]
    public sealed class PerkRespecPerkMixin : BaseViewModelMixin<PerkVM>
    {
        private readonly PerkObject _perk;

        private readonly Hero _hero;

        private readonly bool _isLowerOfPair;

        private bool _isPerkQOLRespecArrowUp;

        private bool _isPerkQOLRespecArrowDown;

        private bool _isPerkQOLRespecCoinVisible;

        public PerkRespecPerkMixin(PerkVM vm) : base(vm)
        {
            _perk = vm.Perk;
            _hero = PerkRespec.HeroItemOfPerk(vm)?.Hero;
            _isLowerOfPair = vm.AlternativeType == (int)PerkVM.PerkAlternativeType.SecondAlternative;
            PerkRespec.Track(this);
            Refresh();
        }

        internal void Refresh()
        {
            bool pending = PerkRespec.IsPending(_hero, _perk);
            IsPerkQOLRespecArrowDown = pending && _isLowerOfPair;
            IsPerkQOLRespecArrowUp = pending && !_isLowerOfPair;
            IsPerkQOLRespecCoinVisible = pending && Settings.Current.RespecCoinMarker && Settings.Current.RespecCostPerPerk > 0;
        }

        [DataSourceProperty]
        public bool IsPerkQOLRespecCoinVisible
        {
            get => _isPerkQOLRespecCoinVisible;
            set
            {
                if (value != _isPerkQOLRespecCoinVisible)
                {
                    _isPerkQOLRespecCoinVisible = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        /// <summary>This perk is the target of a staged swap from the perk above it.</summary>
        [DataSourceProperty]
        public bool IsPerkQOLRespecArrowDown
        {
            get => _isPerkQOLRespecArrowDown;
            set
            {
                if (value != _isPerkQOLRespecArrowDown)
                {
                    _isPerkQOLRespecArrowDown = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        /// <summary>This perk is the target of a staged swap from the perk below it.</summary>
        [DataSourceProperty]
        public bool IsPerkQOLRespecArrowUp
        {
            get => _isPerkQOLRespecArrowUp;
            set
            {
                if (value != _isPerkQOLRespecArrowUp)
                {
                    _isPerkQOLRespecArrowUp = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }
    }

    /// <summary>
    /// Arrow in the gap between a pair's shields, pointing at the perk a staged swap switches to, with a coin beside it
    /// when swaps cost gold. Pairs sit 81 apart with the shields at 19..67 of each 86-high item, so the gap is centred
    /// just below the upper item. The only always-loaded arrow sprite points left, so it's rotated (degrees, y down). The sprite is shaded
    /// on its lower half, so each arrow shows its lit half twice: one clipped half as-is and one vertically flipped. Each copy
    /// sits 1.5 outward so the sprite's dark centre line falls outside both clips.
    /// </summary>
    [PrefabExtension("CharacterDeveloper", "descendant::BrushWidget[@Id='PerkVisualWidgetParent']/Children")]
    public sealed class PerkRespecMarkerPatch : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Child;

        public override int Index => int.MaxValue;

        [PrefabExtensionText]
        public string Content => @"
<Widget WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" DoNotAcceptEvents=""true"">
  <Children>
    <Widget IsVisible=""@IsPerkQOLRespecArrowDown"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""28"" SuggestedHeight=""28"" HorizontalAlignment=""Center"" VerticalAlignment=""Top"" PositionYOffset=""-12"" DoNotAcceptEvents=""true"">
      <Children>
        <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""StretchToParent"" SuggestedWidth=""14"" HorizontalAlignment=""Left"" ClipContents=""true"" DoNotAcceptEvents=""true"">
          <Children>
            <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""20"" SuggestedHeight=""27"" HorizontalAlignment=""Left"" VerticalAlignment=""Center"" MarginLeft=""5.5"" Sprite=""StdAssets\arrow_little"" Rotation=""-90"" PivotX=""0.5"" PivotY=""0.5"" ColorFactor=""1.3"" DoNotAcceptEvents=""true"" />
          </Children>
        </Widget>
        <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""StretchToParent"" SuggestedWidth=""14"" HorizontalAlignment=""Right"" ClipContents=""true"" DoNotAcceptEvents=""true"">
          <Children>
            <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""20"" SuggestedHeight=""27"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"" MarginRight=""5.5"" Sprite=""StdAssets\arrow_little"" Rotation=""-90"" VerticalFlip=""true"" PivotX=""0.5"" PivotY=""0.5"" ColorFactor=""1.3"" DoNotAcceptEvents=""true"" />
          </Children>
        </Widget>
        <Widget IsVisible=""@IsPerkQOLRespecCoinVisible"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""17"" SuggestedHeight=""17"" HorizontalAlignment=""Left"" VerticalAlignment=""Top"" MarginLeft=""17"" MarginTop=""12"" Sprite=""General\Icons\Coin@2x"" DoNotAcceptEvents=""true"" />
      </Children>
    </Widget>
    <Widget IsVisible=""@IsPerkQOLRespecArrowUp"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""28"" SuggestedHeight=""28"" HorizontalAlignment=""Center"" VerticalAlignment=""Top"" MarginTop=""70"" DoNotAcceptEvents=""true"">
      <Children>
        <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""StretchToParent"" SuggestedWidth=""14"" HorizontalAlignment=""Left"" ClipContents=""true"" DoNotAcceptEvents=""true"">
          <Children>
            <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""20"" SuggestedHeight=""27"" HorizontalAlignment=""Left"" VerticalAlignment=""Center"" MarginLeft=""5.5"" Sprite=""StdAssets\arrow_little"" Rotation=""90"" VerticalFlip=""true"" PivotX=""0.5"" PivotY=""0.5"" ColorFactor=""1.3"" DoNotAcceptEvents=""true"" />
          </Children>
        </Widget>
        <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""StretchToParent"" SuggestedWidth=""14"" HorizontalAlignment=""Right"" ClipContents=""true"" DoNotAcceptEvents=""true"">
          <Children>
            <Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""20"" SuggestedHeight=""27"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"" MarginRight=""5.5"" Sprite=""StdAssets\arrow_little"" Rotation=""90"" PivotX=""0.5"" PivotY=""0.5"" ColorFactor=""1.3"" DoNotAcceptEvents=""true"" />
          </Children>
        </Widget>
        <Widget IsVisible=""@IsPerkQOLRespecCoinVisible"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""17"" SuggestedHeight=""17"" HorizontalAlignment=""Left"" VerticalAlignment=""Top"" MarginLeft=""17"" MarginTop=""12"" Sprite=""General\Icons\Coin@2x"" DoNotAcceptEvents=""true"" />
      </Children>
    </Widget>
  </Children>
</Widget>";
    }

    /// <summary>Respec toggle and total cost, under the highlight tickboxes.</summary>
    [PrefabExtension("CharacterDeveloper", "descendant::Widget[@VisualDefinition='RightPanel']/Children")]
    public sealed class PerkRespecTogglePatch : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Child;

        public override int Index => int.MaxValue;

        [PrefabExtensionText]
        public string Content => @"
<Widget IsVisible=""@IsPerkQOLRespecVisible"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""430"" SuggestedHeight=""30"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" MarginTop=""770"" PositionXOffset=""245"">
  <Children>
    <ListPanel WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""StretchToParent"" HorizontalAlignment=""Left"" StackLayout.LayoutMethod=""HorizontalLeftToRight"">
      <Children>
        <ButtonWidget DoNotPassEventsToChildren=""true"" WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""26"" SuggestedHeight=""26"" VerticalAlignment=""Center"" Brush=""SPOptions.Checkbox.Empty.Button"" ButtonType=""Toggle"" IsEnabled=""@IsPerkQOLRespecEnabled"" IsSelected=""@IsPerkQOLRespecOn"" ToggleIndicator=""ToggleIndicator"" UpdateChildrenStates=""true"">
          <Children>
            <ImageWidget Id=""ToggleIndicator"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Brush=""SPOptions.Checkbox.Full.Button"" />
          </Children>
        </ButtonWidget>
        <TextWidget WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" VerticalAlignment=""Center"" MarginLeft=""6"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""20"" Text=""@PerkQOLRespecLabel"" />
      </Children>
    </ListPanel>
    <RichTextWidget IsVisible=""@IsPerkQOLRespecCostVisible"" WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" HorizontalAlignment=""Left"" VerticalAlignment=""Center"" MarginLeft=""215"" Brush=""CharacterDeveloper.Skill.Stats.Text"" Brush.FontSize=""20"" Text=""@PerkQOLRespecCostText"" />
  </Children>
</Widget>";
    }
}
