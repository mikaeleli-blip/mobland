# Mobland

Top-down 3D mafia strategy game set in Prohibition-era New York (inspired by City of Gangsters / Empire of Sin).
Turn-based empire layer; fights are auto-resolved for now, and the systems are built so tactical combat can be added later.

## Opening the project

1. Clone the repo.
2. Unity Hub → **Add → Add project from disk** → select the `mobland` folder. Pick a Unity 6 editor if asked.
3. On first open the editor creates `Assets/Mobland/Scenes/Main.unity` and opens it (or use the **Mobland → Create Main Scene** menu).
4. Press **Play**.

Uses URP (configured automatically on first open: Forward+, HDR, soft shadows, SSAO, post-processing). Menu **Mobland → Graphics** re-runs the URP setup or rebuilds materials.

## Controls

| Input | Action |
|---|---|
| WASD / arrows | Pan (Shift = faster) |
| Mouse wheel | Zoom |
| Q / E, middle-drag | Rotate |
| Right-drag | Grab and pan |
| Left-click | Select building (Esc clears) |
| Tab | District map overlay |
| Home | Reset view |
| T | Pause / resume the clock |
| N | Skip to next time of day (morning, noon, dusk, night) |
| R | Generate a new city |
| F1 | Toggle help |

## Roadmap

1. **City generator** (done): procedural Manhattan-style grid, districts, lots, 3D buildings, strategy camera.
1b. **Graphics** (done): URP + post-processing, procedural PBR textures, bay-mapped facades, street props, day/night cycle with lit windows, street lamps and chimney smoke.
2. **Economy loop**: buy buildings, set up rackets (speakeasy, brewery, casino, protection), cash per turn, booze supply chains.
3. **Crew**: hire gangsters with stats and loyalty, assign them to buildings and jobs.
4. **Rival families**: AI gangs that expand, raid and negotiate; auto-resolved fights.
5. **Heat**: cops, raids, bribes, the feds.
6. **Win/lose, save/load, UI polish.**
7. Later: tactical turn-based combat.

## Code layout

```
Assets/Mobland/
  Scripts/
    Core/           GameRoot (entry point), input wrapper, RNG, mesh/material helpers
    City/           CityConfig (tunables), CityModel (data), CityGenerator, BuildingMesher
    CameraControl/  Strategy camera rig
    UI/             Temporary IMGUI HUD and building selection
  Editor/           First-open scene setup
```

City data (`CityModel`: districts, blocks, buildings) is separate from the visuals, so gameplay systems never depend on meshes.
Tune the city via **Assets → Create → Mobland → City Config** and assign it to the `GameRoot`.

## Textures

All textures are generated procedurally by `Tools/textures/generate.py` (Python + numpy + Pillow):

```
python Tools/textures/generate.py            # everything
python Tools/textures/generate.py asphalt    # just one set
```

Facade textures are atlases of 4 window bays x 4 floors; buildings map one atlas cell per bay with a random offset,
and the `_e` emission maps light windows at night.

## Vehicle and boat models

Cars, trucks and boats are modelled in code with Blender's Python module and exported as compact `.bytes` meshes
(`Assets/Mobland/Resources/Mobland/Models`) that the game loads at runtime with per-vehicle paint:

```
pip install bpy            # Blender as a Python module (Python 3.13 for bpy 5.x)
python Tools/blender/build_models.py                        # all models
python Tools/blender/build_models.py car_limo --preview out # one model + a Cycles preview render
```

Models: `car_sedan`, `car_limo`, `car_touring`, `truck_stake`, `truck_van`, `boat_freighter`, `boat_tug`, `boat_rumrunner`, `boat_barge`.
