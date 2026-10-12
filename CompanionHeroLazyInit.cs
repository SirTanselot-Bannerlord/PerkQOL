using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterDeveloper;
using TaleWorlds.Library;

namespace PerkQOL
{
    /// <summary>
    /// Vanilla builds every companion's SkillVM/PerkVM tree while constructing CharacterDeveloperVM.
    /// During that bulk build, skip InitializeCharacter for everyone except the hero being selected;
    /// fill a companion the first time they become current. That is the dominant open-cost for PerkQOL
    /// (mixins + perk chrome bind only exist after InitializeCharacter).
    /// </summary>
    internal static class CompanionHeroLazyInit
    {
        private static bool _bulkBuild;
        private static CharacterDeveloperHeroItemVM _unlock;
        private static readonly ConditionalWeakTable<CharacterDeveloperHeroItemVM, ShellState> Shells =
            new ConditionalWeakTable<CharacterDeveloperHeroItemVM, ShellState>();

        private sealed class ShellState
        {
            internal bool Ready;
        }

        internal static void BeginBulkBuild() => _bulkBuild = true;

        internal static void EndBulkBuild()
        {
            _bulkBuild = false;
            _unlock = null;
        }

        /// <summary>Returns false from Harmony Prefix to skip the original InitializeCharacter.</summary>
        internal static bool AllowInitialize(CharacterDeveloperHeroItemVM item)
        {
            if (item == null)
            {
                return true;
            }

            if (_unlock == item)
            {
                return true;
            }

            if (!_bulkBuild)
            {
                // Outside bulk: if this item was left as a shell, keep skipping until EnsureReady.
                if (Shells.TryGetValue(item, out ShellState state) && !state.Ready)
                {
                    EnsureEmptySkills(item);
                    return false;
                }

                return true;
            }

            // Bulk construction of the character screen: leave a shell; SetCurrentHero will unlock one.
            if (!Shells.TryGetValue(item, out ShellState shell))
            {
                shell = new ShellState();
                Shells.Add(item, shell);
            }

            shell.Ready = false;
            EnsureEmptySkills(item);
            return false;
        }

        internal static void EnsureReady(CharacterDeveloperHeroItemVM item)
        {
            if (item == null)
            {
                return;
            }

            if (!Shells.TryGetValue(item, out ShellState state) || state.Ready)
            {
                return;
            }

            _unlock = item;
            try
            {
                AccessTools.Method(typeof(CharacterDeveloperHeroItemVM), "InitializeCharacter")
                    ?.Invoke(item, null);
                state.Ready = true;
            }
            finally
            {
                _unlock = null;
            }
        }

        private static void EnsureEmptySkills(CharacterDeveloperHeroItemVM item)
        {
            if (item.Skills == null)
            {
                item.Skills = new MBBindingList<SkillVM>();
            }
        }
    }

    [HarmonyPatch(typeof(CharacterDeveloperVM), MethodType.Constructor, new Type[] { typeof(Action) })]
    internal static class CharacterDeveloperVMCtorLazyPatch
    {
        private static void Prefix() => CompanionHeroLazyInit.BeginBulkBuild();

        private static void Postfix() => CompanionHeroLazyInit.EndBulkBuild();
    }

    [HarmonyPatch(typeof(CharacterDeveloperHeroItemVM), "InitializeCharacter")]
    internal static class HeroItemInitializeCharacterLazyPatch
    {
        private static bool Prefix(CharacterDeveloperHeroItemVM __instance) =>
            CompanionHeroLazyInit.AllowInitialize(__instance);
    }

    [HarmonyPatch(typeof(CharacterDeveloperVM), "SetCurrentHero")]
    internal static class SetCurrentHeroEnsureInitPatch
    {
        private static void Prefix(CharacterDeveloperHeroItemVM currentHero) =>
            CompanionHeroLazyInit.EnsureReady(currentHero);
    }
}
