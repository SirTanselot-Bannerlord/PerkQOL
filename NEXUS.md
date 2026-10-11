# Perk QoL: Respec and Highlighting

**Module id / GitHub:** `PerkQOL`  
**Source:** https://github.com/SirTanselot-Bannerlord/PerkQOL

## Summary

Find the right perks faster and swap choices without an arena reset. Role filters, search, and captain troop flags highlight matching perks on the character screen; a respec mode stages perk-pair swaps (paid in gold, applied on Done). Fully configurable in MCM.

## Description

Find the right perks faster, and swap the ones you already picked without an arena reset.

Vanilla makes both hard: perk roles are buried in tooltips, captain troop tags are easy to miss, and changing a choice means resetting a whole skill at the arena.

This mod adds two character-screen tools (each can be turned off in MCM):

- **Highlight** — tick the roles you care about (Personal, Party Leader, Captain, Governor, and so on). Matching perks get coloured frames; skills with a match show a cyan name plus how many matches and the highest required level. A filter bar searches perk text and can pick up related captain perks by troop-usage tags.
- **Respec** — tick **Respec perks**, click a taken perk pair to stage a swap to its alternative, then confirm with Done. Swaps never refund a perk into an empty slot; they only exchange within a pair. Cost, town-only, and companion rules are MCM options (defaults match arena-ish pricing and town visits).

### Details

- **Role filters:** Tickboxes under the perk bar for All roles, Personal (Combat / Campaign), Party Leader, Army Commander, Captain, Party Role, Governor, and Clan Leader. Session-sticky while the character screen stays open.
- **Captain presets:** Dropdown for Any, Infantry, Archer, Cavalry, and Horse Archer. An upgrade/craft icon opens a flag editor for On foot / Mounted, Melee / Ranged, weapon and shield tags, Generic (untagged captain perks), and — when [Perk Fixes and Rebalance](https://github.com/SirTanselot-Bannerlord/PerkFixesAndRebalance) is loaded — Land / Naval. Save named templates (New / Save / Duplicate / Delete), up to 16 custom profiles.
- **Search:** Filter bar highlights matching perks in white; skills with any match still get the cyan name marker.
- **Respec staging:** Preview swaps on the sheet before committing. Optional coin markers on staged swap arrows. Player pays for every hero’s swaps from their gold. Defaults: 1000 denars per perk, town only, companions must be with you (party / army / same settlement).
- **MCM:** Toggle highlight and respec independently (restart required for those two). Respec defaults, click behaviour, cost, town/companion gates, and whether troop presets include Generic.

### Requirements

Tested with Bannerlord **v1.4.8**, Harmony **v2.4.2**, ButterLib **v2.12.0**, UIExtenderEx **v2.13.3**, MCM **v5.12.3**. ButterLib is required (pulled in with MCM’s usual stack).

### Compatibility

Safe to add to an existing save. Turning highlight or respec on/off in MCM needs a game restart (Gauntlet caches the character-screen prefab). Optional soft dependency on **Perk Fixes and Rebalance** for Land/Naval captain flags; War Sails / Naval DLC is optional. May conflict with other mods that heavily rewrite the character developer perk UI.

### Recommended mods

**Perk Fixes and Rebalance** — if you use it, this mod’s captain filter adds Land and Naval ticks and keeps infantry/archer presets aligned with land + naval scoring.
