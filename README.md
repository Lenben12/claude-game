# claude-game – SEALEBRITIES: Live from the Deep

First-person diving vlog game for Steam (Unity 6, URP). You start on a scrap island with a cheap camera, film marine life, fill your journal (Sealog) with every species and its biggest specimen, upload videos and grow from 0 to 1,000,000 followers. Co-op for 1–4 players (one shared channel) is planned; this prototype is single player.

## Repository layout

| Path | Content |
|---|---|
| `SEALEBRITIES/` | Unity 6000.5.9f1 project (URP 3D template) |
| `SEALEBRITIES/Assets/_Sealebrities/Scripts/` | All game code, generated procedurally at runtime (no imported art) |
| `docs/SEALEBRITIES_Spielkonzept.md` | Game design document (German) with every design decision |

## Getting started

1. Open `SEALEBRITIES/` with Unity 6000.5.9f1 (Unity Hub).
2. Menu **SEALEBRITIES → Setup Project** (creates the main scene, UI theme, shader-include materials).
3. Open `Assets/_Sealebrities/Scenes/Main.unity` and press Play, or use **SEALEBRITIES → Setup + Build (Windows)**.

Automated screenshot tour of a build: `SEALEBRITIES.exe -autoshot <folder>`.

## Controls

WASD move · mouse look · Space up/jump · Ctrl/C down · Shift sprint · E interact · F flashlight · Tab/P phone · J journal · Esc pause · right mouse aim camera · left mouse record · wheel zoom · Q anchor (boat)
