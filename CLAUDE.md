# Car Systems Extended — notes for Claude

My Summer Car mod (MSCLoader) by **qniman**: 1995-style aftermarket alarm, central locking, remote start for the Satsuma. Successor of qniman's older mod *Key System Extended*.

## Working agreement
- Talk to the user in **Russian**.
- **Agree the logic with the user before implementing** a new system; small fixes can go straight in.
- **No Claude attribution anywhere**: no `Co-Authored-By` trailers in commits, nothing in PRs. Author is `qniman <72304432+qniman@users.noreply.github.com>` — never the user's personal email. On a fresh clone run:
  ```
  git config user.name "qniman"
  git config user.email "72304432+qniman@users.noreply.github.com"
  ```
- Extend `TESTING.md` (in-game checklist) with every feature or fix.
- Write commits/comments in English, match the existing code style (C# 7.3, Unity 5.0 / .NET 3.5 APIs only).

## Setup on a new machine
- My Summer Car + MSCLoader 1.4.2 (Unity 5.0.0f4), .NET SDK (any recent), Git.
- Build: `dotnet build CarSystemsExtended -c Release -p:GameDir="<game folder>"` (default `D:\SteamLibrary\steamapps\common\My Summer Car`). The build copies the DLL into `<game>\Mods`.
- Compiles against the game's own Mono BCL in `mysummercar_Data\Managed` (`NoStdLib`, no NuGet reference packages — works offline).
- Mod save: `<game>\Mods\Config\Mod Settings\CarSystemsExtended\cse_save` (JSON). Mount editor output: `mounts.txt` there; after baking positions into `PartCatalog.cs` rename it to `mounts-applied-<date>.txt`.

## Layout
| | |
|---|---|
| `CarSystemsExtended/src/CseMod.cs` | Entry point: settings, keybinds, load/save, per-frame order, reset |
| `Core/` | `GameRefs` (globals, battery, menu mode), `Gui` (HUD hints), `Look` (crosshair raycast), `Sfx` (game sounds + synthesized clips, fob audio), `Shapes`/`Textures` (placeholder geometry, embedded textures), `ToolHook` (game wrench), `L` (EN/RU strings) |
| `Parts/` | `PartDef`/`MountDef`/`BoltDef`, `Part` (pickable → mounted → bolted; loose-physics safety), `Bolt`, `PartManager`, `PartCatalog` (all definitions + kits) |
| `Electrics/AlarmWiring.cs` | Wiring with the game's wiring mess, wire stubs |
| `Systems/` | `AlarmSystem` (modes, fob, sensors, siren, LEDs), `DoorLocks`, `Flasher` (own indicator copies + real hazards), `RemoteStart` |
| `Shop/` | `MailOrder` (brochure, envelope, post box, parcel, entries/prices), `CatalogUI` (IMGUI page) |
| `Save/SaveSystem.cs` | Save data |
| `Debug/` | Overlay, mount editor (arrows/PgUp/PgDn/`,`/`.`/Insert/Home/End — user has no numpad) |
| `Assets/` | Embedded icon, logo, catalog cover (`LogicalName="CSE.<file>"` in the csproj) |
| `CSE.Research/` | Dump tool (F8 full Satsuma dump, F9 snapshot+diff, F10 look dump, F11 PLAYER dump) → `Mods\Config\Mod Settings\CSE_Research\Dumps`. Not shipped. Use it to learn game FSMs before guessing. |

## Conventions
- **All player-facing text goes through `L.T` / `L.F`**; part names via `L.PartName` / `L.PartLabel` (keys `part_<id>`). Debug overlay and console stay English.
- New part: add a `PartDef` to `PartCatalog` (and to `All`, parents before children), name keys in `L`, a kit in `PartCatalog.Kits`, a `CatalogEntry` in `MailOrder.Entries` with `kit_<id>_title/_blurb` keys.
- Mount offsets are car space (+X right, +Y up, +Z forward), placed in game with the mount editor. Part origin = centre of its bounds.
- Car is left-hand drive.

## Game internals that matter (from research dumps)
- Car root `SATSUMA(557kg, 248)`; components `Drivetrain` (`gear`, `neutral`), `CarController` (`brakeInput`, `brakeKey`).
- **Bolts**: the game wrench (`PLAYER/.../FPSCamera/2Spanner/Raycast`, FSMs `Raycast` + `Check`) only sees objects named `BoltPM` on layer 12; size = `localScale.x` vs global `ToolWrenchSize` (0.7 = 7 mm). We hook states `Tighten`/`Untighten` (+` 2` for ratchet) with `FsmInject`. Do not raycast for bolts ourselves.
- **Doors**: `Body/pivot_door_left/door left(Clone)` FSM `Use` (`Open`). Lock by disabling the `GetMouseButtonDown` action in state `Mouse over 1` — **never disable the whole FSM** (re-enabling restarts it and adds a second FixedJoint; door looks welded shut).
- **Ignition** `Dashboard/Steering/steering_column2/IgnitionSatsuma` FSM `UseNew` (`ACC`); key mesh `.../Ignition/Keys/Key` active = key in lock. Global `PlayerKeySatsuma`.
- **Starter** `CarSimulation/Car/Starter` FSM `Starter`: `ACC` + `Starting` crank, `ShutOff` stops, state `Running`. Remote start also activates `CarSimulation/Car/Electrics`.
- **Hazards** `Dashboard/TurnSignals` FSM `Blinkers` var `BlinkerHazards` (needs ignition). Indicator lights live under `Electricity/PowerON` (off with ignition) — `Flasher` clones them.
- Choke `.../Knobs/ButtonsDash/Choke` FSMs `Use` (`Choke`, `KnobPos`) + `Choke` (`ChokeLevel`). Hood `Body/pivot_hood/hood(Clone)` FSM `Use` `Open`. Handbrake lever FSM `Use` `KnobPos` (20 = on). Battery `Database/PartsStatus/Battery` FSM `Data` `Charge`. Global `EngineTemp`, `GlobalDay`, `PlayerMoney`.
- **Wiring mess** = global `WiringTool`; game points react within 0.1 m + cInput button `Use`. Fuse box point `FuseboxDash`.
- Menu mode: globals `PlayerInMenu` (cursor, no mouse look) + `PlayerStop` (no walking). HUD: `GUIuse`, `GUIassemble`, `GUIdisassemble`, `GUIbuy`, `GUIinteraction`, `GUIsubtitle`.
- Store objects are LOD-switched; find them with `Transform.Find` from the `STORE` root. Post box `STORE/LOD/Post Box`, counter `STORE/LOD/ActivateStore/PostOffice/PostOrderBuy`.
- Esc is the game's pause menu — don't use it for our UI.
- **Physical keys** (`Systems/KeySystem.cs`, `KeyRingView.cs`): the mod owns global `PlayerKeySatsuma` (1 only while keys are in hand or in the lock). Keyring state = `KeyRing` (remote, A23 battery, spare) in `Part.Data` for loose keys, `KeySystem.Pocket` / `Ignition` otherwise. Lock state follows the game key mesh `.../Ignition/Keys/Key`. Key 3 uses the `SelectItem` FSM event `ITEM1` to put tools away (`PlayerHandRight`). Remote swings as a self-simulated pendulum (`Swing`, LateUpdate, driven by the pivot's measured acceleration) — PhysX joints lagged and jittered because the camera and the interpolated car move outside the physics step. Keys in hand are a child of `Camera.main`; HUD icon via `GUI/Indicators/Mode` shared material. Ring on the ignition key is placed automatically at the bow end of the key mesh. The `Key` mod (Mechanic) conflicts.

## Versions
- `v1.0.0` (tag) = everything before the catalog cover; `release/CarSystemsExtended-1.0.0.zip` matches it (release binaries are gitignored).
- `v1.1.0` (tag) = catalog cover, physical keys, A23 battery, blowing fuse, Blender models; `release/CarSystemsExtended-1.1.0/` (DLL + `CHANGELOG.txt`) and `.zip`. Next work goes into a new version — ask the user for the number; tag + zip only when they say it is ready.
- Nexus page text: `release/NEXUS_DESCRIPTION.txt` (EN/RU BBCode, Nexus sections).

## Open / next
- Unverified: does the MSC HUD font render Cyrillic (Russian hints)?
- **Part models are built by script in Blender** (`tools/blender/`): `bridge.py` sends Python to the user's running Blender through the Blender Lab MCP add-on bridge (TCP localhost:9876, null-terminated JSON `{"type":"execute","code":...,"strict_json":false}`) — no MCP client needed. `python bridge.py cse_models.py build_<part>.py` builds the part in the "CSE Models" scene, exports `CarSystemsExtended/Assets/models/<part>.obj` + `.mtl` and renders `tools/blender/previews/*.png` (read them to check the result). Exporter writes part space directly: `(-x, z, -y)` with reversed winding (RH→LH reflection). Loader `Core/Models.cs`; `PartDef.Model` + `Models.BuildOr` in `Build`; markers `#marker <name> x y z` (`ring`, `led`, `fob`, later `bolt_N`, `plug`). Done: fob, key, keyring (key → ring → remote is a chain of `Swing` pendulums: `Swing.Child` is stepped right after its parent); alarm unit, siren, status LED, choke servo, lock actuator, fuse, A23 battery, envelope, parcel (`build_parts.py`, `ONLY = [...]` in a file passed first builds a subset). Models are built at the placeholder layout (`P`/`S`/`AX` part-space helpers in `cse_models.py`) so the tuned mounts, bolts and plugs still fit; parts with an LED use `PartCatalog.WithModel` (glowing lens on the `led` marker); the blown fuse darkens material `fuse_body`. Brochure keeps its cover picture.
- Ideas left: catalog cover inside the catalog page, back cover, envelope/parcel textures, parking sensors, trip computer + oil temperature/vacuum gauges (the game already has volt/oil/water pressure "extra gauges" — don't duplicate).
