using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace PerkQOL
{
    public sealed class PerkQOLSettings : AttributeGlobalSettings<PerkQOLSettings>
    {
        private const string CharacterScreen = "Character screen";

        public override string Id => "PerkQOL";
        public override string DisplayName => "Perk QOL";
        public override string FolderName => "PerkQOL";
        public override string FormatType => "json2";
        public override char SubGroupDelimiter => '|';

        [SettingPropertyBool("Perk highlight", Order = 0, RequireRestart = true,
            HintText = "Adds role tickboxes under the perk bar on the character screen. Every perk with an effect for a ticked role gets a frame in that role's colour: Personal (split into combat and campaign effects), Party Leader, Army Commander, Captain, Party Role (scout, quartermaster, surgeon, engineer, first mate, navigator), Governor or Clan Leader. When a perk's two effects are for two ticked roles, the left half of the frame shows the primary role and the right half the secondary. Turn off if another mod changes the character screen. Requires a game restart.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool PerkHighlight { get; set; } = true;

        [SettingPropertyBool("Perk respec", Order = 1, RequireRestart = true,
            HintText = "Adds a 'Respec perks' tickbox to the character screen. While it is ticked, clicking an owned pair of perks stages a swap to the other perk (see 'click either perk to toggle'). Swaps only switch between the two perks of a pair, never refund. They take effect when you press Done, like attribute and focus points, and are only allowed while you can pay the total cost. Turn off if another mod changes the character screen. Requires a game restart.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool PerkRespec { get; set; } = true;

        [SettingPropertyBool("Respec ticked by default", Order = 2, RequireRestart = false,
            HintText = "Whether the 'Respec perks' tickbox starts ticked each time the character screen opens.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool RespecOnByDefault { get; set; } = true;

        [SettingPropertyBool("Respec: click either perk to toggle", Order = 3, RequireRestart = false,
            HintText = "On: clicking either perk of an owned pair stages the swap, and clicking either again undoes it. Off: click the new perk to stage a swap and the original perk to undo it.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool RespecClickToggles { get; set; } = true;

        [SettingPropertyBool("Respec: coin on swap arrows", Order = 8, RequireRestart = false,
            HintText = "Shows a small coin next to the arrow of each staged swap when swaps cost gold.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool RespecCoinMarker { get; set; } = false;

        [SettingPropertyInteger("Respec cost per perk", 0, 10000, "0 Denars", Order = 4, RequireRestart = false,
            HintText = "Gold paid for each swapped perk. The arena master charges 40 per skill level to reset a tree, which works out to roughly 1000 per perk. 0 makes swaps free.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public int RespecCostPerPerk { get; set; } = 1000;

        [SettingPropertyBool("Respec only in towns", Order = 5, RequireRestart = false,
            HintText = "Perk swaps are only possible while you are in a town.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool RespecTownOnly { get; set; } = true;

        [SettingPropertyBool("Companions must be with you to respec", Order = 6, RequireRestart = false,
            HintText = "Clan members can only swap perks while they are in your party, your army or the settlement you are in. Combined with 'Respec only in towns', they must be in that town with you.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool RespecCompanionsNearby { get; set; } = true;
    }

    /// <summary>MCM's instance is null until MCM has loaded; fall back to defaults until then.</summary>
    internal static class Settings
    {
        private static readonly PerkQOLSettings Defaults = new PerkQOLSettings();

        public static PerkQOLSettings Current => PerkQOLSettings.Instance ?? Defaults;
    }
}
