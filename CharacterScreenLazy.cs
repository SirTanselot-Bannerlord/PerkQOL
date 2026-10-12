using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterDeveloper;

namespace PerkQOL
{
    /// <summary>
    /// Defers highlight/respec chrome apply until shortly after open or hero switch, and only for the current hero.
    /// Other companions' perk/skill mixins stay untouched until selected.
    /// </summary>
    internal static class CharacterScreenLazy
    {
        private const float DelaySeconds = 0.1f;

        private static float _remaining = -1f;
        private static Hero _currentHero;
        private static bool _uiReady;
        private static WeakReference<PerkHighlightScreenMixin> _highlightScreen;
        private static WeakReference<PerkRespecScreenMixin> _respecScreen;

        internal static bool UiReady => _uiReady;

        internal static Hero CurrentHero => _currentHero;

        internal static bool IsCurrentHero(Hero hero) => hero != null && hero == _currentHero;

        internal static void BindHighlightScreen(PerkHighlightScreenMixin screen) =>
            _highlightScreen = new WeakReference<PerkHighlightScreenMixin>(screen);

        internal static void BindRespecScreen(PerkRespecScreenMixin screen) =>
            _respecScreen = new WeakReference<PerkRespecScreenMixin>(screen);

        internal static void OnScreenBound(CharacterDeveloperVM vm)
        {
            CaptureCurrentHero(vm);
            ScheduleApply();
        }

        internal static void OnCurrentHeroChanged(CharacterDeveloperVM vm)
        {
            Hero previous = _currentHero;
            CaptureCurrentHero(vm);
            if (previous == _currentHero && _uiReady)
            {
                return;
            }

            ScheduleApply();
        }

        internal static void Tick(float dt)
        {
            if (_remaining < 0f)
            {
                return;
            }

            _remaining -= dt;
            if (_remaining > 0f)
            {
                return;
            }

            _remaining = -1f;
            ApplyNow();
        }

        /// <summary>Filter changes while waiting: keep the pending apply; it will see the new filter state.</summary>
        internal static bool ShouldDeferRefresh => !_uiReady;

        private static void CaptureCurrentHero(CharacterDeveloperVM vm) =>
            _currentHero = vm?.CurrentCharacter?.Hero;

        private static void ScheduleApply()
        {
            SetUiReady(false);
            _remaining = DelaySeconds;
        }

        private static void ApplyNow()
        {
            PerkHighlighter.RefreshCurrentHero();
            PerkRespec.RefreshCurrentHero();
            SetUiReady(true);
        }

        private static void SetUiReady(bool ready)
        {
            if (_uiReady == ready)
            {
                return;
            }

            _uiReady = ready;
            if (_highlightScreen != null && _highlightScreen.TryGetTarget(out PerkHighlightScreenMixin highlight))
            {
                highlight.NotifyUiReadyChanged();
            }

            if (_respecScreen != null && _respecScreen.TryGetTarget(out PerkRespecScreenMixin respec))
            {
                respec.NotifyUiReadyChanged();
            }
        }
    }

    [HarmonyPatch(typeof(CharacterDeveloperVM), "SetCurrentHero")]
    internal static class CharacterDeveloperSetCurrentHeroPatch
    {
        private static void Postfix(CharacterDeveloperVM __instance) =>
            CharacterScreenLazy.OnCurrentHeroChanged(__instance);
    }
}
