# Biohazard

Biological ships for Cosmoteer, built from bone, meat and circulating blood. A bio ship carries no crew: organs do every job.

## What it does

- **Blood circulation.** The Blood Generator eats biomass and is the only source of blood. Depleted blood runs down the vein network to the lungs, which enrich it, and enriched blood comes back along the arteries to the parts that spend it. Hearts set the system pressure and one-way valves let you author the flow, so blood always runs from full pressure down to zero.
- **Self-repair.** A Blood Distributor turns enriched blood into healing energy for every organ in its area, and each organ keeps a small bank of it to knit itself back together. Meat heals; bone and cartilage do not.
- **Digestion.** A Bio Stomach digests every ore and resource into biomass, the fuel for blood, bone shards and the Bio Spewer thrusters. One biomass is worth one steel plate.
- **Brains.** Bio Brains give command points, steering and radar. Adjacent brain blocks form one brain that grows with the cluster. Every working organ needs a conscious brain in range and switches off outside it.
- **Tentacles.** They collect asteroid drops, move cargo, feed construction and salvage. Nothing else on a bio ship can build, so a ship in career mode needs one to grow.
- **Sight and weapons.** The Bio Eye projects a sight cone along its facing. The Bone Factory turns biomass into bone shards, a conveyor carries them and the Bio Bone Shooter fires them.
- **Armour.** Bone, meat, cartilage and meat + cartilage hybrids in every vanilla shape.
- **Ready-made ships.** 4 designs in the Ship Library under "Biohazard Ships": load one to see a working blood loop, or start from it.

## Requirements

- Cosmoteer 0.30.4c.
- The YAML mod loader (Yet Another Mod Loader), Steam Workshop 3577650065. Circulation, repair, brains, digestion and hauling all run inside `ZTX.BioCirculation.dll`; without the loader the parts place but do nothing.

## Installation

1. Set up YAML using the manual steps from its author (Workshop page above).
2. Enable Biohazard in the Cosmoteer Mods menu and restart Cosmoteer.
3. At the Welcome screen YAML lists `ZTX.BioCirculation.dll` under "Unknown libraries". Go back to the mods list, click this mod and press "Trust this mod".
4. Restart Cosmoteer once more.

## Tuning

Every number lives in `config.rules` next to the DLL: one group per part named after its folder, one nested group per component, plus the DLL's global `Behaviours` switches and the resource prices. The file opens with an index and every group is commented. Edit a value and restart Cosmoteer; nothing needs rebuilding.

## Multiplayer

Every player in the lobby needs this mod enabled with the same `config.rules`. The part values are part of the game's mod check; the `Behaviours` switches are not, so mismatched switches desync without a warning.

## Reporting issues

Cosmoteer writes two log files per launch in `Saved Games\Cosmoteer\<your id>\Logs`:

- `log<timestamp>_modloader.txt` - startup: the `config:` line listing the Behaviours values applied, and the `init OK` version banner.
- `log <timestamp>.txt` - everything after that.

Please include both files with a report, and the ship if the problem happens on one.

## Source

Full C# source: https://github.com/ZTXDragon/BIOHAZARD

`Biohazard-Source/` builds with the .NET 10 SDK and HarmonyLib: `Core/` is engine-free logic (blood graph, flow steps, field builder, digestion plan, heal spread, fair share, config parser), `Game/` holds the Harmony patches and part components, and `Config.cs`, `Log.cs` and `Main.cs` are the configuration, logging and entry point. Run `build_and_install.ps1` with Cosmoteer closed to compile and install the DLL, this README and the LICENSE in one step. The xunit tests sit beside the repo in the private workspace and compile `Core/` directly.

## Compatibility

Bio parts register into the game's Terran part list, so they build on any ship. No vanilla file is modified. The DLL patches the engine where a crewless ship needs it (defeat rules, hull walls, significance, eject, path-search bounds, construction mode in career) and leaves the rest alone.

## Notes

- AI was used during code generation of this mod.

## License

MIT - see `LICENSE`.
