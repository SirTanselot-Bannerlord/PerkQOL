using System;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace PerkQOL
{
    public class SubModule : MBSubModuleBase
    {
        public const string HarmonyId = "com.PerkQOL";
        public const string ModuleId = "PerkQOL";

        private bool _uiChecked;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            // Pass assembly explicitly: parameterless PatchAll() can NRE under BLSE.
            Harmony harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(SubModule).Assembly);
            CaptainDropdownOpensUp.TryPatch(harmony);
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            CharacterScreenLazy.Tick(dt);
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            // MCM settings are loaded by now; the character screen prefab isn't, so patching it here still takes effect.
            if (_uiChecked)
            {
                return;
            }

            _uiChecked = true;
            if (!Settings.Current.PerkHighlight && !Settings.Current.PerkRespec)
            {
                return;
            }

            try
            {
                UIExtender extender = UIExtender.Create(ModuleId);
                extender.Register(typeof(SubModule).Assembly);
                extender.Enable();
            }
            catch (Exception e)
            {
                InformationManager.DisplayMessage(new InformationMessage("Perk QOL: character screen changes disabled (" + e.Message + ")", Colors.Red));
            }
        }
    }
}
