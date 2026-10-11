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
            HintText = "Role tickboxes and perk search under the perk bar. Captain has a troop preset and an upgrade icon for individual flags. Frames matching perks; skills with a match get a white marker. Restart required.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool PerkHighlight { get; set; } = true;

        [SettingPropertyBool("Perk respec", Order = 1, RequireRestart = true,
            HintText = "Adds a 'Respec perks' tickbox. While ticked, click an owned perk pair to stage a swap (applied on Done, never refunds). Restart required.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool PerkRespec { get; set; } = true;

        [SettingPropertyBool("Respec ticked by default", Order = 2, RequireRestart = false,
            HintText = "Starts the 'Respec perks' tickbox ticked when the character screen opens.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool RespecOnByDefault { get; set; } = true;

        [SettingPropertyBool("Respec: click either perk to toggle", Order = 3, RequireRestart = false,
            HintText = "On: click either perk of a pair to stage or undo a swap. Off: click the new perk to stage, the original to undo.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool RespecClickToggles { get; set; } = true;

        [SettingPropertyBool("Respec: coin on swap arrows", Order = 8, RequireRestart = false,
            HintText = "Shows a small coin next to each staged swap arrow. Hidden when cost per perk is 0.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool RespecCoinMarker { get; set; } = true;

        [SettingPropertyInteger("Respec cost per perk", 0, 10000, "0 Denars", Order = 4, RequireRestart = false,
            HintText = "Gold per swapped perk. Arena reset is ~1000/perk; 0 makes swaps free.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public int RespecCostPerPerk { get; set; } = 1000;

        [SettingPropertyBool("Respec only in towns", Order = 5, RequireRestart = false,
            HintText = "Only allow perk swaps while you are in a town.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool RespecTownOnly { get; set; } = true;

        [SettingPropertyBool("Companions must be with you to respec", Order = 6, RequireRestart = false,
            HintText = "Clan members can only swap while in your party, army, or settlement. With town-only, they must be in that town.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool RespecCompanionsNearby { get; set; } = true;

        private bool _captainPresetsIncludeGeneric = true;

        [SettingPropertyBool("Troop presets include Generic", Order = 7, RequireRestart = false,
            HintText = "Infantry, Archer, Cavalry, and H. Archer also count captain perks with no troop flag. The deployment captain preview follows this. Any always includes them.")]
        [SettingPropertyGroup(CharacterScreen, GroupOrder = 0)]
        public bool CaptainPresetsIncludeGeneric
        {
            get => _captainPresetsIncludeGeneric;
            set
            {
                if (value == _captainPresetsIncludeGeneric)
                {
                    return;
                }

                _captainPresetsIncludeGeneric = value;
                PerkHighlighter.OnGenericPresetSettingChanged(value);
            }
        }
    }

    /// <summary>MCM's instance is null until MCM has loaded; fall back to defaults until then.</summary>
    internal static class Settings
    {
        private static readonly PerkQOLSettings Defaults = new PerkQOLSettings();

        public static PerkQOLSettings Current => PerkQOLSettings.Instance ?? Defaults;
    }
}
