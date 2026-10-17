# Mobland

Top-down 3D mafia strategy game set in Prohibition-era New York (inspired by City of Gangsters / Empire of Sin).
Turn-based empire layer; fights are auto-resolved for now, and the systems are built so tactical combat can be added later.

## Opening the project

1. Clone the repo.
2. Unity Hub → **Add → Add project from disk** → select the `mobland` folder. Pick a Unity 6 editor if asked.
3. On first open the editor creates `Assets/Mobland/Scenes/Main.unity` and opens it (or use the **Mobland → Create Main Scene** menu).
4. Press **Play**.

Works with the Built-in render pipeline or URP, and with either the legacy Input Manager or the new Input System package.

## Controls (Milestone 1)

| Input | Action |
|---|---|
| WASD / arrows | Pan (Shift = faster) |
| Mouse wheel | Zoom |
| Q / E, middle-drag | Rotate |
| Right-drag | Grab and pan |
| Left-click | Select building (Esc clears) |
| Tab | District map overlay |
| Home | Reset view |
| R | Generate a new city |
| F1 | Toggle help |

## Roadmap

1. **City generator** (done): procedural Manhattan-style grid, districts, lots, placeholder 3D buildings, strategy camera.
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
All buildings are placeholder geometry, to be swapped for a modular Blender kit later.
Tune the city via **Assets → Create → Mobland → City Config** and assign it to the `GameRoot`.
