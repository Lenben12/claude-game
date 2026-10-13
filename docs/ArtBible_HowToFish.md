# SEALEBRITIES – Art Bible "How to Fish" (binding style guide)

Version 1 · 11.10.2026 · Art director · applies to every visual change (world, props, fx, player, ui, fauna, net avatars)

The player's instruction: **"Perfect the graphics exactly like How to Fish, like in the pictures I sent."**
This document decodes How to Fish (HtF, Dazed Games) into numbers and lists what our build must change.
If a value here conflicts with an older comment in the code (e.g. "Neutral tonemapping keeps the pastels"), **this document wins**.

Sources studied: the 9 HtF reference images (`htf_*.jpg`, `2.png`, `3.png`), `1.png` (our bug), and all 12 current
AutoShot screenshots (`Builds/Screenshots/01_menu … 12_night`). Every "HtF" hex below was **measured** from the
references (box-averaged pixels, center of frame, away from the vignette), not guessed.

---

## 0. How to use this document

* **Two kinds of numbers.**
  *Input values* (albedo hex, light intensity, fog density, post settings) are starting points.
  *On-screen targets* (marked **▣**) are what the final screenshot must measure. Tune **global** lighting/exposure
  first, palette second, never per-object hacks.
* **Tolerance:** ±14 per RGB channel (0–255) on a box average unless stated otherwise.
* **Measuring:** run the AutoShot tour (`SEALEBRITIES.exe -autoshot "<folder>"`), then sample boxes with
  `powershell -ExecutionPolicy Bypass -File "C:/Users/ADMINI~1/AppData/Local/Temp/claude/C--Users-Administrator-Desktop-ll/ef7aff3c-ffeb-428b-96d7-37d6e4608b77/scratchpad/artdir/sample.ps1" -Img <png> -Boxes @("label:x0,y0,x1,y1", ...)`
  (prints the average hex of each box). Acceptance boxes are listed in §5.
* **Determinism:** every new random art variation uses `System.Random(fixedSeed)` or the world hash (co-op machines
  must build the identical island). No time-based randomness, no `UnityEngine.Random` in generation.
* **Performance:** gradient/ramp textures are built once at startup (no per-frame allocation); everything batches
  per material as today.

### The 10 golden rules (memorise these)

1. **Medium-key, filmic, not pastel-washed.** ACES tonemapping, vignette 0.28, contrast +12. Nothing on screen is
   pure white except the sun disc, foam crests and UI text.
2. **Sky is a real mid-blue**: zenith ▣ `#4A88BF` → horizon ▣ `#8DBDEA`. No white or grey horizon band, ever.
3. **The sea is steel/navy blue, not mint**: near ▣ `#2F6290`, mid ▣ `#4F7FAE`, far ▣ `#6E9FCC`. Turquoise only in
   the last 1.5 m of depth at the shore and **under** water. Depth colour blends smoothly – no squares, no bands.
4. **Greens are olive/yellow-green, never lime**: grass ▣ `#6E9445`, frond top ▣ `#7FA243`, dark ▣ `#4E6F2E`.
5. **Shadows are strong and cool-violet**: shadow on sand ▣ `#8E8F95` next to lit sand ▣ `#E6D0A4`
   (shadow/lit luminance ≈ 0.60). Darks lean violet (`#1E1B2B`, `#5C5A69`), never pure black or brown-grey.
6. **Environment = faceted flat shading; characters, hands and held items = low-poly silhouettes with smooth
   normals** (HtF hands and bean people are soft blobs, rocks/terrain/water are hard facets).
7. **Lush and layered land**: dense tall grass clumps (0.5–1.1 m) everywhere it is green, broad two-tone palm
   fronds, closed jungle canopy inland. A bare lawn with scattered sticks is the #1 "not How to Fish" tell.
8. **First-person items are small and tucked into the lower corners**: one-hand item ≤ 240 × 260 px at 1080p,
   only items that sit in an active hotbar slot are visible, never two items in one hand.
9. **UI is minimal floating white text/icons with a thin dark outline** (Fredoka), no dark-navy boxes; a hotbar
   at the bottom centre; light, sunny colours (white, sand, turquoise, coral).
10. **Under water: bright turquoise near the surface, visibility 15–20 m in the shallows, less deeper**; objects
    within 6 m keep ≥ 80 % of their own colour; deeper = darker teal; corals are chunky 3D in 2–3 flat tones.

---

## 1. The How to Fish look decoded

### 1.1 Measured HtF reference colours (on-screen, sunny day)

| Surface | HtF measured | Source image | Ours today (for comparison) |
|---|---|---|---|
| Sky zenith (top of frame) | `#4387B4` / `#4A86BE` | 2.png, beach | `#769DD0` |
| Sky mid (≈30°) | `#64A6D5` / `#6798BD` | 2.png, beach | `#94BDE7` |
| Sky horizon | `#84BBEF` / `#82AED1` / `#83A6CA` | 2.png, palms, grill | `#E1F5F2` (near white) |
| Sea far (horizon band) | `#74ADE2` / `#6590B5` | 2.png, beach | `#9EE0F2` |
| Sea mid | `#577EA4` / `#4D749A` | beach, grill | – |
| Sea near (looking down) | `#2A658E` / `#2E507A` / `#3B668D` | 2.png, grill | `#BBE9F0` |
| Foam line | `#D7E7EF` / `#C3C8C8` | 2.png, grill | `#D6F0F4` (thin) |
| Sand lit | `#CCB592` … `#FAE9C4` (bright noon) | beach, 2.png | `#F4EBCD` |
| Sand in shadow | `#8C958E` / `#716F70` (cool grey) | beach, grill | `#B59886` (warm, weak) |
| Grass lit | `#668443` / `#6F914E` | 3.png, palms | `#9DD96E` (lime) |
| Grass dark / in shade | `#4F6A36` / `#3C2633` (violet) | 3.png | `#8FCF68` |
| Palm frond top (lit) | `#68852E` / `#6C8F4F` / `#637C39` | palms, beach | `#6CC24A`-ish |
| Palm trunk lit / shade | `#855F66` / `#4C2739` | palms, beach | `#673B3D` |
| Hazed island rock / grass at ~150 m | `#879AB1` / `#8BA881` | 2.png | grey-beige haze `#C4BBB9` |
| Dock plank (near) | `#9A7A5F` | 2.png | `#E2C79F` (too light) |
| Whitewashed plank lit / shade | `#C1B0A1` / `#85695C` | shop hut | `#97A19F` |
| Red-brown accent boards | `#6C4045` | shop hut | – |
| Bean skin lit | `#D5B27E` / `#DABA81` | beach | – |
| Orange overall | `#E77F2E` / `#F49537` | beach, palms | – |
| Navy-violet shirt / glove | `#4D4662` / `#2E1D38` (rod) | shop, beach | `#262A33` |
| Magenta flowering bush | `#895286` / `#864492` | pufferfish | – |
| Vignette corner vs centre | sky `#406A8F` vs `#6190B4` (≈ ×0.68) | beach | no vignette above water |

What the measurements say:
* HtF is **darker and more saturated in the mid-tones than ours**, with a clear vignette and violet-tinted darks
  (trunks `#4C2739`, rod `#2E1D38`, forest shade `#3C2633`). This is ACES-style filmic grading over a blue/violet ambient.
* HtF's distance haze is **blue** (hazed rocks `#879AB1`), not grey/beige.
* HtF's sea is **blue, with the far sea lighter than the near sea** (Fresnel sky reflection), and a visible horizon line.

### 1.2 Sky, sun, clouds  *(owner: world – WorldSky, WorldDayNight, WorldClouds)*

**Sky gradient targets ▣ (on-screen):**

| Sun elevation | Zenith | 30° up | Horizon away from sun | Horizon toward sun |
|---|---|---|---|---|
| ≥ 40° (≈ 10:00–15:00) | `#4A88BF` | `#64A3D3` | `#8DBDEA` | `#A4CBEE` |
| 20° | `#5189C0` | `#6EA6D4` | `#9CC4E8` | `#BBD3EA` |
| 8° (golden hour, menu) | `#5878B8` | `#8A9CCB` | `#B9B4D2` | `#F2BC8E` |
| 0° (sunset) | `#3D4C8C` | `#6E6FA8` | `#8E7FAE` | `#F59A6E` |
| −6° (dusk) | `#1F2A5C` | `#34407A` | `#4E5590` | `#8E6A8E` |
| Night (moon up) | `#121A3A` | `#1A2650` | `#2E4775` | – |

Rules: the horizon is at most **1.35× the luminance** of the 30° sky; HSV saturation of the horizon ≥ 0.25 by day;
no white band, no grey band, no yellow band.

Implementation options:
* **A (fast):** keep `Skybox/Procedural`, starting values at noon: `_AtmosphereThickness 0.62`, `_SkyTint (0.36, 0.52, 0.80)`,
  `_Exposure 1.9`, `_GroundColor #3E6E9C`. Iterate against the table (3 rounds max).
* **B (exact, use if A misses by > 15 per channel):** runtime gradient cubemap (6 × 64 px faces, filled once per
  0.5° of sun elevation change, on `Skybox/Cubemap`; add `SB_SkyboxCubemap.mat` to `Resources/ShaderIncludes` in the
  editor setup so the shader is not stripped) + a sun-disc mesh like the existing moon disc.

**Sun:**
* Max elevation **58°** (was 72° – an overhead sun kills the shape-revealing side light HtF has). Midday shadows are
  roughly as long as the object is tall.
* Colour: ≥ 30° `#FFF2DE`; 12° `#FFDDB0`; 5° `#FFC58A`; 0° `#FF9E6A`.
* Intensity: 1.35 at ≥ 30°, 1.15 at 10°, 0.70 at 2°, 0 below −3° (moon takes over as today).
* Disc: ≈ 2.0° angular diameter, `#FFFDF2`, with a bloom halo ≈ 3× the disc (sun_disc measured `#FEFEF0`, halo `#92B1C6`).

**Clouds** (ours read as small grey UFOs): 8–10 clouds, each 60–120 m long, 14–24 m tall, flat bottoms, stacked
2–3 puffy layers, altitude 220–320 m, cover ≤ 8 % of the sky. Lit top `#FFFFFF`, shaded bottom never darker than
`#DCE5F1` by day (emission floor ≥ 0.85), golden hour tops `#FFD9B8`, undersides `#C9B3C8`. 0–3 clouds near the
sun azimuth so the sun stays clear.

**Night** (12_night is already close – keep the idea, brighten): moon intensity 0.45, colour `#A9BCFF`; stars as
today; ▣ grass `#2F4A3A`, ▣ sand `#59617A`, ▣ sea `#2C4F80`.

### 1.3 Light and shadows  *(owner: world – WorldDayNight; URP asset values: fx/editor)*

* **Shadow strength 0.90** by day (was 0.45–0.62). The shadow colour comes from the ambient below, not from
  a weak shadow.
* **Ambient (Trilight) – reduce and cool it** (ours is far too bright, which washes every shadow out):

| Sun elevation | Sky | Equator | Ground |
|---|---|---|---|
| ≥ 30° | `#8297C2` | `#8A88A0` | `#5E5260` |
| 10° (golden) | `#8C88B8` | `#B08A88` | `#5E4A55` |
| 0° | `#6C6A9C` | `#8E6E7E` | `#43384A` |
| Night | `#2A3866` | `#222C4C` | `#12162A` |

* Target ▣: shadow on sand `#8E8F95`, shadow on grass `#3E4C3A`, shaded side of a white wall `#9C93A2`
  (violet-grey, never brown).
* Soft shadows **Medium** quality, shadow map 2048 (High tier), 2 cascades (split 0.25), shadow distance 120 m,
  depth bias 1.0, normal bias 0.6. Penumbra should read as ≈ 6–10 cm at 2 m from the caster (soft but defined;
  see the grill shadow in `htf_beach_grill_sea.jpg`).
* Every prop casts shadows except grass clumps, flowers, foam, decals, particles (cost).

### 1.4 Fog above water  *(owner: fx – FXPostProcess / FXMath; colours from world EnvState.SkyFogColor)*

* **Mode: Exponential** (not squared) above water, **density 0.0016** by day, 0.0022 at night.
  ExponentialSquared stays for underwater (switch the mode when `under01` crosses 0.5 – the splash hides the pop).
* Resulting haze: 50 m → 8 %, 150 m → 21 %, 300 m → 38 %, 600 m → 62 %, 1200 m → 85 %. The landmark islands
  (600–1200 m away) stay visible as soft blue silhouettes; the main island seen from the dock end is lightly hazed
  like `2.png`.
* **Fog colour = sky horizon (away from sun) × 0.94 value**: noon `#85B2DC`, golden hour `#B7A6C2`, sunset
  `#8E7FAE`, night `#24365E`. Never grey: HSV saturation ≥ 0.25 by day. Never brighter than the sky horizon.
* Horizon line rule ▣: the sea right at the horizon measures 6–12 % darker than the sky directly above it.

### 1.5 Post-processing  *(owner: fx – FXPostProcess; SSAO/MSAA: Editor/SealebritiesSetup)*

Above water, day (starting values; tune exposure until lit sand ▣ `#E6D0A4`):

| Override | Value |
|---|---|
| Tonemapping | **ACES** (was Neutral) |
| Color Adjustments | postExposure **+0.45**, contrast **+12**, saturation **+8**, hue 0, filter white |
| White Balance | temperature **−3**, tint **+5** (HtF whites lean slightly pink-violet, e.g. trim `#AF9CA2`) |
| Shadows/Midtones/Highlights | shadows **(0.97, 0.95, 1.07, 0)**, midtones (1, 1, 1, 0), highlights **(1.02, 1.00, 0.97, 0)**; shadows range end 0.35, highlights start 0.55 |
| Bloom | threshold **1.0**, intensity **0.45**, scatter **0.70**, tint `#FFF3E4`, HQ filtering only on High tier |
| Vignette | intensity **0.28**, smoothness 0.42, rounded off, colour `#140E26` (dark violet) |
| SSAO (renderer feature) | intensity **1.0**, radius **0.30 m**, direct lighting strength **0.25**, samples Medium; if the view-model hands blotch, drop radius to 0.22 before touching intensity |
| Film grain / chromatic aberration / lens distortion / DoF / motion blur | **0** (CA only for narcosis, lens only for splash/narcosis as today) |
| Anti-aliasing | MSAA 4× (High), 2× (Medium), off + FXAA (Low) |

Time-of-day deltas: golden hour saturation +14, temperature +12, tint +4; night saturation −10, temperature −12,
postExposure **+0.25** (moonlit, brighter nights per GDD 15.29).
Underwater: see §2.

**Graphics quality tiers** (feedback #21/#22):

| Setting | High | Medium | Low |
|---|---|---|---|
| Shadow map / cascades / distance | 2048 / 2 / 120 m | 1024 / 2 / 80 m | 1024 / 1 / 45 m, hard |
| SSAO | on, Medium samples | on, downsampled, Low samples | off |
| MSAA / post AA | 4× / none | 2× / none | off / FXAA |
| Render scale | 1.0 | 1.0 | 0.85 |
| Grass clump density / cull | 100 % / 60 m | 60 % / 40 m | 30 % / 25 m |
| Tree/prop cull | 140 m | 100 m | 70 m |
| Caustics / god rays | on / 8 shafts | on / 5 shafts | off / 3 shafts |
| Bloom | on (HQ) | on | on (low iterations) |

### 1.6 Ocean above the surface  *(owner: world – WorldOcean, WorldWaterMap, WorldCoast)*

**Targets ▣ (noon, eye 1.8 m above water, looking 10–30° down):** near (5–20 m) `#2F6290`, at 100 m `#4F7FAE`,
horizon band `#6E9FCC`; lagoon water < 1.5 m deep over sand reads teal-blue `#4E9DB8` (sand shows through), never mint.

**Base colours (albedo + alpha) by seafloor depth – continuous, not classes:**

| Depth | Colour | Alpha |
|---|---|---|
| 0–0.6 m (shore) | `#46A6BE` | 0.62 |
| 1.5 m | `#3D93B6` | 0.78 |
| 5 m | `#3778AB` | 0.90 |
| 15 m | `#2E5F93` | 0.94 |
| ≥ 35 m (open sea) | `#284F80` | 0.96 |

**Kill the squares (`02_island_air`, `01_menu`):** they come from (a) per-triangle discrete classes
(`WorldWaterMap.Pick`), (b) the 12 m far-ring cells coloured by their centre, and (c) alpha 0.8 showing the coarse
6.4 m seabed through the water. Fix:
1. Build a **64 × 1 RGBA ramp texture** once (the table above, bilinear, clamp) and give every ocean vertex
   `uv.x = clamp(depth / 35 m)` sampled from the depth at **that vertex** (`WorldWaterMap` stays as the depth source,
   continuous values only). One material for near + far + outer (outer rings uv.x = 1). URP Lit multiplies base map
   alpha – the ramp alpha does the transparency.
2. Delete the per-class submesh sorting (`ClassifyNear/ClassifyFar` become a UV refresh on snap).
3. Alpha ≥ 0.90 wherever depth > 4 m so the seabed never shows as blotches from above.

**Surface:** keep the faceted 3 m near grid and flat normals. Facets should vary ±8–12 % in brightness (HtF
`htf_sea_horizon_sun.jpg`). Wave height 0.25–0.45 m in calm weather. Smoothness **0.55** (was 0.34) so 2–8 % of near
facets flash as glints ▣ `#D5E6F5` when looking toward the sun; Fresnel lightening toward the horizon may never exceed
the sky horizon luminance (if it does, lower the reflection-probe intensity on the ocean, not the smoothness).

**Shoreline:** foam band **1.6–2.8 m** wide (varies along the coast with a deterministic noise, period ≈ 9 m),
irregular inner edge with 0.3–0.6 m lumps, colour `#EEF4F6` (▣ ≈ `#DCE7EE`), alpha 0.92, slides in/out 0.4 m at
0.12 Hz. Optional second foam line 2–4 m offshore at 40 % alpha. On the terrain behind it a **wet-sand band 1.2–2 m**
`#C9AF86`. (HtF `2.png`: thick lumpy white band; ours is a thin clean ribbon.)

**From below:** under-surface `#9FE4EC` α 0.70, emission `#2C8C9A` × daylight, facets ±10 % (bright, glittering, angular – GDD 15.28).

### 1.7 Master palette (albedo, sRGB) – replaces `Core/LowPoly.cs` values

Changing `LowPoly` touches every module; the **world** engineer applies it in one commit and announces it.

| LowPoly name | Old | **New** | Note |
|---|---|---|---|
| Sand | `#F2D9A6` | **`#E6CFA0`** | warmer, less white |
| WetSand | `#DCC08A` | **`#C9AF86`** | wet band + shore |
| Seabed | `#E6D2A2` | **`#E2C890`** | more saturated; teal light turns it sandy-green |
| Grass | `#7CC04B` | **`#6E9445`** | olive, not lime |
| GrassDark | `#55A23C` | **`#4F6F33`** | |
| Soil | `#A06C46` | **`#8E6A4C`** | paths use PathDirt |
| Rock | `#8E9098` | **`#8A8C96`** | |
| RockDark | `#61636E` | **`#5C5A69`** | violet grey |
| DeepRock | `#3E4256` | **`#3B3A4E`** | |
| Water | `#4DB3D6` | **`#3778AB`** | props using Water (shutters, cushions) get `Turquoise` instead |
| WaterDeep | `#2C72AA` | **`#284F80`** | |
| WaterShallow | `#7FD8E0` | **`#3D93B6`** | above-water shallow; underwater uses §2 |
| Foam | `#F4FBFF` | **`#EEF4F6`** | |
| PalmTrunk | `#7A3B32` | **`#7E4652`** | mauve-maroon |
| PalmLeaf | `#6CC24A` | **`#7FA243`** | frond top |
| PalmLeafDark | `#4FA03D` | **`#4E6F2E`** | frond underside |
| PalmLeafDry | `#C9B45A` | **`#B5A050`** | |
| Wood | `#C99A6B` | **`#9A7658`** | weathered dock |
| WoodDark | `#8B5A3C` | **`#5E4436`** | posts, stringers |
| WoodLight | `#E2C79F` | **`#C9B08E`** | light weathered |
| Roof | `#B24A3A` | **`#A8484A`** | desaturated red |
| Metal | `#9AA3AB` | **`#8E949C`** | |
| Rust | `#B5603A` | **`#A0583A`** | |
| White | `#F4F1EA` | **`#EDE6DA`** | warm off-white; never `#FFFFFF` on geometry |
| Ink | `#22252E` | **`#1E1B2B`** | violet black |
| CoralPink | `#FF7A8A` | **`#F2708A`** | |
| CoralOrange | `#FF9F5A` | **`#F28C4A`** | |
| CoralPurple | `#9B6BD6` | **`#8E5CC2`** | |
| CoralYellow | `#FFD25E` | **`#F2C94C`** | |
| Kelp / KelpDark | `#8FA83A` / `#5F7A2A` | **`#7E9A3A` / `#55702A`** | |
| SkyTop / SkyHorizon | `#79B4E8` / `#CFE6F2` | **`#4A88BF` / `#8DBDEA`** | targets, see §1.2 |
| Skin | `#E8B98F` | **`#E2B985`** | |
| Neoprene | `#262A33` | **`#343849`** | |
| SuitOrange | `#FF7A2E` | **`#EE7F2D`** | |

New palette entries: `GrassBase #3A552A`, `GrassTip #9DB65A`, `GrassDry #9AA65A`, `PathDirt #A88763`,
`Foliage #5E8A3A`, `FoliageDark #46682C`, `FoliageLight #7A9C45`, `Conifer #3F5F35`, `ConiferLight #567A3E`,
`TreeBark #5E4A44`, `Whitewash #DCCFBE`, `PlankGap #3A2C2A`, `AccentBoard #7A4448`, `Turquoise #3FA9B5`,
`FlowerMagenta #A8478F`, `FlowerPink #E86FA0`, `FlowerWhite #F4EFE6`, `FlowerPurple #8E5CC2`.
Smoothness stays 0.08 for everything matte (ocean, glass, wet rock are the only exceptions).

### 1.8 Terrain  *(owner: world – WorldTerrain, WorldTerrainData)*

* Class colours from §1.7. Sand → grass border stays one-triangle jagged, plus a 2–4 m transition where 30 % of
  grass triangles use `GrassDry #9AA65A`.
* Paths `PathDirt #A88763`, 1.6–2.4 m wide, edges broken by grass clumps (props).
* Underwater seabed: §2.4. No terrain facet larger than 6.4 m within 60 m of any dive-site centre (the huge pale
  triangles in `06_reef`/`07_reef_wide` read as glitches).

### 1.9 Vegetation  *(owner: props – PropPlants, PropJungle)*

**Palm (HtF `htf_palms_sky.jpg`, `htf_beach_palms_rod_hands.jpg`):**
* Height 6–11 m. Slender trunk: base radius **0.24 m**, top **0.15 m** (height ≈ 20× base diameter), 8 sides.
* Segmented: 7–10 segments of 0.9–1.3 m, each tapering to 0.92× so a 2 cm ring ledge shows. Alternate segments
  ±5 % value (not two different colours); bottom 8 cm of each segment `#5E3340`. Trunk `#7E4652`.
* Gentle S-curve, lean 0.18–0.35 × H toward the sea for coastal palms.
* Crown: **7–9 big fronds** + 1–2 short young near-vertical fronds + 0–2 dry hanging fronds `#B5A050`.
  Frond length **0.38–0.50 × H** (3.2–4.8 m), max blade half-width **0.17 × length** (was 0.21 of a much shorter
  frond, and the sawtooth made it read thin), V-fold along the midrib 25–35°, droop from +25° at the base to −60° at
  the tip. Outline: **6–8 jagged teeth per side**, tooth depth 0.35 × local width.
* **Two-tone:** upward-facing triangles `PalmLeaf #7FA243`, downward/back faces `PalmLeafDark #4E6F2E`
  (split the two-sided triangles into two materials by face orientation).
* Coconuts: cluster of 3–5, r 0.14 m, `#5A3E2E`.
* Placement: **≈ 260 palms** (was 500) mostly within 35 m of the beach (ground < 6 m), in clusters of 3–7
  (spacing 3–6 m) with open sand between clusters; 10 % inland.

**Grass clumps (the most important missing element):**
* 12–24 blades per clump, clump radius 0.25–0.5 m, blades lean out 8–30°.
* Blade height 0.45–0.7 m (meadow), 0.7–1.1 m (jungle floor, forest edge); base width 0.07–0.12 m, tapering to a
  point; long blades bend once (2 segments).
* Colour via a **1 × 8 vertical ramp texture** (built once, bilinear) and `uv.y = height fraction`, one material
  for all grass: bottom `#3A552A` → 45 % `#5E8A3A` → top `#9DB65A`. Dune variant ramp `#8A8A4E → #C9C07A`.
* Density: meadow 0.6 clumps/m², jungle floor 0.35/m², forest edge 0.8/m², sand dunes 0.08/m², none on paths.
* LOD: 24 blades < 20 m, 8 blades 20–60 m, culled beyond the tier distance (§1.5). No shadow casting.
* Bushes may stay passable (feedback #10), grass is always passable.

**Broadleaf jungle tree (inland, replaces the round "lollipop" trees):** height 8–14 m, trunk radius 0.30–0.45 m,
6–8 sides, `TreeBark #5E4A44`, 2–3 branches; canopy of 4–7 flattened faceted blobs (ico detail 1, y-scale 0.55–0.7,
radius 2–3.5 m) mixing `Foliage #5E8A3A`, `FoliageDark #46682C`, `FoliageLight #7A9C45` (light blob on top).
In the jungle core the canopies overlap into a closed roof so the floor is in shade (HtF `3.png`). ≈ 180 trees.

**Conifers (lighthouse hill, north cliffs – HtF forest):** height 9–16 m, trunk `#4A3A44`, 3–5 stacked wide angular
tiers built from flat leaf cards, `Conifer #3F5F35` / `ConiferLight #567A3E`. ≈ 40.

**Undergrowth:** bushes 0.8–1.8 m (2–4 blobs, Foliage colours), 25 % flowering: bougainvillea magenta
`#A8478F` (HtF pufferfish image), pink `#E86FA0`, white `#F4EFE6`, each flower a flat 5-petal star 0.08 m.
Ferns 0.6–1.2 m, 7–10 fronds (`#4E6F2E`/`#5E8A3A`). Broad-leaf plants (taro/banana) leaves 0.6–1.4 m, two-tone
(top `#6E9445`, under `#4E6F2E`); banana plants 2.5–4 m with stem `#8A8F4E`. Meadow flowers in groups of 3–9 on
0.3–0.6 m stems: `#E86FA0`, `#F2C94C`, `#F4EFE6`, `#8E5CC2`.

**Layering rule:** every green ground triangle within 120 m of the island centre is covered by at least grass
clumps; anything a player walks past at ≤ 10 m must show 3 height layers (grass < 1 m, bush/fern 1–2 m,
tree/palm > 6 m).

### 1.10 Rocks  *(owner: props – PropRocks; terrain rock class: world)*

Three variants `#8A8C96`, `#6E6F7E`, `#5C5A69`; beach rocks `#6E6A78`; cliffs `#8C8E9A` with crevice faces
`#4A4658`. Faces with normal.y > 0.7 on 30 % of inland rocks get moss `#6E9445`. Detail 1 icos with 2–5 cut planes
(as today) – the facet contrast must come from lighting: no rock face may be lighter than lit sand.

### 1.11 Wooden buildings, dock, lighthouse  *(owner: props – PropHut, PropStructures, PropBeach)*

* **Planks:** 0.18–0.25 m wide, 2–3 cm gaps showing a dark backing `PlankGap #3A2C2A`, 3 value variants ±6 %,
  random 0–1 cm out-of-plane offset (hand-built look). Weathered, pastel – never clean "new plastic".
* **Shack/forest style (HtF `3.png`):** vertical boards `#8B6B5A` / `#7A5E50`, door with frame, 4-pane window
  (frame `#EDE6DA`, glass `#7F9CB8`), gable roof with 0.35–0.5 m overhang, dark shingles `#5A4E52`.
* **Beach shop / our dive hut (HtF `htf_shop_hut_characters.jpg`):** horizontal whitewashed clapboard
  `Whitewash #DCCFBE` / `#CFC2B2`, accent boards and counter `AccentBoard #7A4448`, trim `#EDE6DA`, roof red
  `#A8484A` or dark shingles; shutters `Turquoise #3FA9B5` (desaturated, not `#4DB3D6`). Collision: roof and
  walls have solid colliders (feedback #10).
* **Dock:** planks 0.25 × 0.05 m with 3 cm gaps (water visible), `#9A7658`/`#8A6A50`/`#A88466`; stringers and
  posts `#5E4436`, posts r 0.12 m, 8 sides, wet bottom band `#4A3A30` up to 0.4 m above the water line; bollards,
  rope coils `#C9B48A`. Ladder: rails + rungs `#5E4436`, rung spacing 0.30 m, climbable collider.
* **Lighthouse:** tapered 8-sided tower `#EDE6DA` with 3 red bands `#B8454A`, black lantern room `#2A2833`, lamp
  glass `#FFF1C9` emissive at night.
* **New POIs (GDD 15.33):** rope bridge planks `#8E6B4E`, ropes `#C9B48A`; waterfall = stepped faceted ribbons
  `#4FA3C8` with white foam planes `#EEF4F6` at each step; cave interior `#2A2438` with a few emissive mushrooms
  `#9BFF6E`; tide pools teal `#4FB0BE` with orange starfish `#F28C4A`; radio-tower ruins rusty lattice `#8A5A42`.

### 1.12 Characters – bean divers, nameplates  *(owner: player – DiverModels.Build; net uses it)*

HtF people (`htf_beach_palms_rod_hands.jpg`, `htf_shop_hut_characters.jpg`, `htf_beach_grill_sea.jpg`) are tall
beans: no neck, the head is the top of the body, thin tube arms, big round eyes.

> **Round 3 update (GDD §19 „weder klotzig noch zu kugelig“, binding over the bullets below):** the divers are now
> bean-HUMANS – 1.85 m, a tall egg head (1.53–1.87 m, r 0.125) on a short neck with a wetsuit collar, a torso with
> defined shoulders (pelvis + chest on a spine joint, elliptical cross-section, depth 0.70 × width), tapered arms /
> legs with elbows and knees (upper arm 0.29, forearm 0.26, thigh / shin 0.42), mitten gloves, a readable face (eyes
> r 0.036 at 1.715 m, lids, brows in the hair colour, a nose, ears, mouth), hair that shows under every hat, body type
> and height options. Smooth normals, LOD0 ≤ 9,000 triangles per option. Builder: `Player/DiverBuilder.cs`.

* Total height **1.85 m**. Body bean: capsule with slight taper (bottom r 0.27 m, top r 0.22 m), 12 radial
  segments, **smooth normals**. Head = top 0.35 m of the bean.
* Legs: tubes 0.75 m, r 0.11 m, flat boots/fins `#2B2533`. Arms: tubes 0.70 m, r 0.06 m, mitten hands r 0.07 m.
* Face: 2 googly eyes – white spheres r 0.055 m at 1.62 m height, 0.09 m apart, protruding 0.03 m; black pupils
  r 0.028 m slightly off-axis (derpy). Small nose bump optional. Mouth = dark flat ellipse 0.05 × 0.02 m that flaps
  with `SetTalking`.
* Skin options `#E8C08A`, `#D6A574`, `#A8724A`, `#6B4636`. Wetsuit `#343849` with a `DiverLook.suit` colour panel
  (torso front/back + leg stripe) and `DiverLook.cuff` cuffs. Default player colours: P1 `#EE7F2D`, P2 `#3FB8C4`,
  P3 `#E0607E`, P4 `#F2C94C`. Hats: bucket `#C9B48A`, viking `#9A9AA6` (horns `#EDE6DA`), shark fin `#5E7A96`,
  duck `#F2C94C`.
* **Nameplate** (HtF "melvin"): lower-case name, Fredoka SemiBold, white 92 %, outline 0.12 em `#1A1430` at 60 %,
  **no background**, billboard at 2.15 m, on-screen height clamped 14–26 px, hidden beyond 60 m and when occluded.
  Speaker icon (white, 0.7× text height) left of the name while talking, pulsing 2 Hz.

### 1.13 First-person hands and held items  *(owner: player – PlayerHands, PlayerMeshes; camera models: filming – FilmRigBuilder)*

Ours (`1.png`, every screenshot): two huge forearms with teal sleeves, blocky fingers, a flashlight and a camcorder
held at the same time, each item ≈ 19–25 % of screen width. HtF: one small soft hand per held item, items small,
tucked to the lower corners/edge.

* **Visibility:** an item is visible only while it is in the **active hotbar slot of a hand** (`Hands` API).
  Empty hand = hidden (except the 0.35 s swim stroke). Never two items in one hand; the fishing rod occupies both.
* **Shading:** smooth normals on 8–12-sided low-poly shapes (soft like HtF), flat colours, no textures except tiny
  screen/label details.
* **Hand:** mitten silhouette 0.085 m wide × 0.10 m long, separate thumb, no individual finger blocks.
  Glove `#343849`, palm pads `#2A2D3A`, cuff = `DiverLook.cuff` (default `#3FB8C4`), cuff length 0.04 m; visible
  forearm ≤ 0.18 m (sleeve barely enters the frame).
* **Camera-space poses** (metres, x right / y up / z forward, vertical FOV 70°):

| Pose | Position | Rotation (x, y, z) |
|---|---|---|
| Right-hand item (camera, lamp, phone lowered) | (0.20, −0.20, 0.46) | (2°, −6°, −4°) |
| Left-hand (offhand) item | (−0.20, −0.21, 0.46) | (2°, 6°, 4°) |
| Fishing rod – right grip | (0.17, −0.27, 0.40) | rod axis toward (0.02, 0.22, 1) |
| Fishing rod – left hand on the rod | (0.10, −0.12, 0.52) | – |
| Phone raised (UI on its screen) | (0.10, −0.03, 0.36) | (−4°, −10°, 3°) |
| Camera aimed (viewfinder) | (0.0, −0.11, 0.30) | (2°, −4°, 0°) |

* **Size limits ▣ (1920 × 1080):** a one-hand item incl. hand fits in **240 × 260 px**; nothing except the raised
  phone/aimed camera reaches higher than 36 % of the screen height from the bottom.
* **Camera models:** scale all tiers to **0.62×** of today; the T2 camcorder ≤ 0.11 m long. Lamp T1 body
  `#F2C94C`, T2/T3 `#2E3140` with `#EE7F2D` accents.
* **Fishing rod:** butt r 0.016 m tapering to 0.004 m, `#2E2A40` with a `#3D7BC4` band, reel `#8E949C`; the tip
  leaves the screen top at x ≈ 55 %. Handle on screen ≤ 26 px thick.
* **Phone:** 0.075 × 0.155 × 0.012 m, old and scratched: body `#3A3F4C`, scratch lines `#6A7080` (1–2 px in a
  256 px texture), bezel 5 mm `#15171F`, corner radius 8 mm, a small crack in the top-left glass. Raised it fills
  ≈ 55 % of the screen height.
* **Motion:** idle sway ±4 mm at 0.35 Hz, walk bob 10 mm vertical / 6 mm lateral at step rate, look lag 0.06 s,
  equip = rise from −0.25 m y in 0.25 s, stow 0.20 s. Rod cast = 0.35 s wind-up back over the shoulder, 0.25 s
  whip forward (feedback #2).
* World FOV default **70°** vertical (slider 60–90); `FilmHandCamera.BaseFov` follows the same default.

### 1.14 UI  *(owner: ui)*

HtF HUD (`htf_beach_pufferfish_hud_hotbar.jpg`, `2.png`): tiny white outlined elements, no panels, a hotbar of
square slots at the bottom centre with almost no fill, the selected slot has a thin white frame and its name below.

**Typography:** Fredoka (`Resources.Load<Font>("Fonts/Fredoka-Variable")`). Weights: 700 titles, 600 numbers and
labels, 500 body. Sizes at 1080p: H1 64, H2 40, HUD numbers 34, labels 22, minimum 16. Every HUD text: outline
1.5 px `rgba(20,24,40,0.75)` + shadow offset (0, 2) blur 4 `rgba(0,0,0,0.35)`.

**HUD palette:** White `#FFFFFF`, Sand `#F6E7C8`, Turquoise `#3FC1C9`, Coral `#FF7A5C`, Sun `#F2C94C`,
Leaf `#9BE564` (gains/saved), Warn-orange `#FFB347` (unsaved), Danger `#FF5A5A`, Ink `#1B2433` (text on light
surfaces). **No `#0D1B2A` navy panels in the HUD** – navy is allowed nowhere outside the phone's dark-mode screens
(and the phone defaults to light mode).

**Layout (1920 × 1080, safe margin 32 px, no backgrounds):**
* Top-left – dive block (in water / on the boat): air ring Ø 76 px, stroke 7 px, track `rgba(255,255,255,0.25)`,
  fill `#6FE3D3` → `#F2C94C` below 40 % → `#FF5A5A` below 20 % blinking 2 Hz; seconds inside (600, 22 px).
  Right of it: depth "12 m" (44 px), "max 18 m" (18 px, `#CFE8F0`), site icon + name (20 px). Below: camera icon +
  clip counts (`#9BE564` saved / `#FFB347` unsaved).
* Top-right – sun/moon icon 30 px + "Tag 3 · 14:20" (26 px).
* Bottom-right – coin icon `#F2C94C` + money (34 px); person icon `#3FC1C9` + followers (34 px); right-aligned,
  8 px row gap; gains pop 18 px above in Leaf/Turquoise, fade 1.5 s.
* Bottom-centre – hotbar (below). Centre – dot crosshair 5 px white 70 % with 1 px dark ring. Interaction prompt at
  62 % screen height: key cap (32 × 32, 2 px white outline, radius 6) + verb (22 px).
* Quests are **not** on screen; a 4 s toast (icon + ≤ 6 words) and a red badge on the phone app "Aufgaben".
* Warnings: red screen-edge vignette (fx) + beep; no text boxes.

**Hotbar (two hands × 3 slots):** left group = left hand (offhand), right group = right hand; slot 84 × 84 px,
gap 10 px, gap between groups 56 px with a hand icon (28 px, white 70 %) centred above each group; bottom margin
28 px. Slot: fill `rgba(15,20,35,0.22)`, border 2 px `rgba(255,255,255,0.45)`, radius 12 px, key label 13 px
top-left. Active slot: border 3 px `#FFFFFF`, fill `rgba(255,255,255,0.16)`, scale 1.10, item name (16 px) under the
group. Two-handed rod: both groups show the rod in their active slot, joined by a 3 px white bracket under the gap.
Item icons = rendered 3D model snapshots at a 3/4 angle, 64 px, no background (like HtF).

**Inventory (equip screen):** 6-column grid of 84 px slots on a light frosted card `#F6F1E6` at 92 %, radius 24 px,
drag & drop to hotbar slots, icons over words (feedback #14).

**Notifications (new Sealog entry, delivery, sale):** phone-style banner from the top: 460 × 76 px, `#FFFFFF` 94 %,
radius 20 px, app icon 44 px, title Ink 20 px/600, subtitle 16 px `#5A6478`, shadow (0, 6, 18) `rgba(0,0,0,0.25)`,
3.5 s, ping.

**Phone screen (RenderTexture 540 × 1080, light theme):** wallpaper gradient `#8FD3E0` → `#F6E7C8`; app icons
112 px rounded squares (radius 26 px), flat colour + white glyph: FinTube `#FF5A5A`, Sealog `#3FC1C9`,
Fischmarkt `#F2C94C`, Aufgaben `#9BE564`, Shop `#FF7A5C`, Karte `#6EA6D4`, Galerie `#B59AD9`,
Nachrichten `#4FC3F7`, Einstellungen `#9AA3AE`; labels 22 px Ink; badge `#FF4D4D` Ø 30 px. Sealog: undiscovered
species = flat grey silhouette `#8C96A3` on card `#E9EEF2`.

**Main menu (feedback #20, GDD 15.1/2/4):** 3D scene = the bean crew (2–4 divers in player colours) sitting on the
dock edge at golden hour (sun elevation 8°, sky row "8°" in §1.2), one with a ukulele, legs dangling, gentle sea.
UI: logo top-left at (120, 120) px – "SEALEBRITIES" Fredoka 700 120 px white, lower 40 % of the letters filled
`#3FC1C9`, 3–5 bubble circles rising from the letters, dark outline 3 px `#1B2433` 60 %. Menu = plain text list
left-aligned at x 120 px, 40 px Fredoka 600 white with outline, 18 px gap; hover = Coral `#FF7A5C` + 8 px right
shift + small bubble bullet. **No panels, no pills, no "LIVE FROM THE DEEP" red pill, no subtitles under buttons.**
Footer version 14 px white 50 %. Difficulty choice = 3 big icon cards on a light card style (feedback #15).

**Loading screen (GDD 15.40):** full-screen underwater gradient `#5ECAD9` (top) → `#1A5A8C` (bottom), 3–5 slanted
light bands (alpha 0.12), rising bubbles, one low-poly fish crossing every ~4 s, an ocean fact centred at the bottom
(26 px white outlined, max 2 lines), progress = rounded bar 480 × 10 px, track `rgba(255,255,255,0.25)`, fill white.

**Launch intro (feedback #19):** 4–5 in-engine shots of 2–3 s each, hard cuts on the beat: dock at sunrise → reef
with a fish school (camera glide) → viewfinder REC frame on a turtle → follower counter rolling up on the phone →
logo splash with bubbles. Skippable with any key after 1 s. Letterbox bars 0 (full frame), vignette 0.35.

---

## 2. Underwater (extrapolated in the same language)

HtF barely goes under water, so we extend its rules: flat colours, strong but bright tints, chunky shapes,
clean graphic light effects, no murk.

### 2.1 Colour by depth  *(owner: fx – FXMath.UnderwaterColor; ambient/attenuation: world – WorldDayNight)*

| Camera depth | Fog + background ▣ | Ambient sky / equator / ground | Sand at 4 m distance ▣ |
|---|---|---|---|
| 0–3 m | `#5ECAD9` | `#9ED8DF` / `#78C2CF` / `#4F95A0` | `#DCCB94` |
| 10 m | `#3DAACB` | ×0.85 | `#C3C08F` |
| 18 m | `#2D90BC` | ×0.70 | `#93AFA0` |
| 25 m | `#2478AA` | ×0.55 | `#6E97A2` |
| 40 m | `#1A5A8C` | ×0.35 | `#4A7690` |
| 55 m | `#123E6A` | ×0.18 | `#2E5274` (lamp lit: `#B49A70`) |
| 80 m | `#0B2648` | ×0.06 | lamp only |
| 120 m | `#050F22` | ×0.02 | lamp only |

The shallow stops are more saturated teal and less milky than today (`06_reef` measured water `#83DBE7` and sand
`#CED4B8` – grey-green, washed). Keep the per-channel sun attenuation (red fades first).

**Entering the water must never look dark** (feedback #5): for the first 1.5 s after submerging the fog colour is
clamped to at least the 0–3 m stop, the splash vignette ≤ 0.15, transition ≤ 0.2 s. In daylight no pixel of open
water between 0 and 5 m depth may measure darker than `#3DAACB`.

### 2.2 Visibility  *(owner: fx)*

ExponentialSquared fog, density k by camera depth (night ×1.2):

| Depth | k | 10 % own colour left at |
|---|---|---|
| 0–8 m | 0.078 | 19.4 m |
| 15 m | 0.085 | 17.8 m |
| 25 m | 0.095 | 16.0 m |
| 40 m | 0.105 | 14.4 m |
| 60 m | 0.120 | 12.6 m |
| ≥ 80 m | 0.140 | 10.8 m (lamp range) |

Near field stays readable: at k 0.078 an object 6 m away keeps 80 % of its colour. (Today k = 0.034 → ≈ 45 m sight.)
The fauna spawner and the camera "scan" range should not exceed the visibility (filming: max scan range 12–15 m in
the shallows, feedback #16).

**Underwater post:** ACES as above; saturation +10 (0–10 m) → −20 (60 m); contrast +6 → 0; postExposure +0.35 in
the shallows → +0.10 at 40 m; vignette 0.30 → 0.40, colour `#03203A`; white balance temperature −4 → −14.

### 2.3 Surface, light rays, caustics, particles  *(owner: fx; surface: world)*

* Surface from below: §1.6. Sky is never visible from below (bright faceted ceiling only).
* **God rays:** 8 shafts in view (High), width 1.2–3.5 m, length 18 m, additive `#DFFBFF`, alpha 0.12 in the
  shallows → 0 at 30 m, slanted along the refracted sun, sway 0.1 Hz. Flat bands, no gaussian haze.
* **Caustics:** keep the light-web cookie: cell ≈ 1.6 m, +25 % brightness on the lines, full to 8 m, gone by 18 m.
* **Marine snow:** 0.5 flakes/m³, flat 2–4 cm quads, white α 0.35. Deep (> 60 m): bioluminescent sparks `#46E0FF`,
  `#9BFF6E`, `#FF6EC7` (GDD 15.31). Bubbles from the regulator: faceted, white α 0.6.

### 2.4 Seabed, corals, kelp, wreck  *(owner: props – PropCoral, PropKelp, PropWreck, PropDeep; seabed: world)*

* **Seabed:** saturated sand `#E2C890` with low faceted ripples/dunes (0.2–0.5 m amplitude, 3–6 m wavelength),
  rock clusters `#6E6F86` (violet-grey), sea-grass patches `#6F8F3A` / `#55702A`. Corals are grouped into patches
  6–12 m across with sand gaps between (HtF clarity), not sprinkled evenly.
* **Corals – chunky 3D, 2–3 flat tones per colony** (base 25 % darker, tips 15 % lighter):
  branching = 4–6-sided boxy branches; brain = faceted half-ico (detail 1) in 2 tones; table coral = faceted
  plate **with 0.12 m thickness** and a stem, at most 1 in 6 colonies (the paper-flat purple/blue octagons in
  `06_reef` read as UI shapes – remove them); sea fans; barrel sponges with dark insides; anemones as cone clusters.
  Sizes 0.4–1.8 m. Palette: `#F2708A`, `#F28C4A`, `#8E5CC2`, `#F2C94C`, `#D9455A`, `#B59AD9`, `#3FA9A0`, `#7E9A3A`.
  Density inside a patch: 1 colony per 3 m².
* **Kelp:** strips 8–15 m tall, 2 tones (`#7E9A3A` / `#55702A`), sway 0.15 Hz.
* **Wreck:** hull `#3B4A5E`, rust `#8A4A32`, deck `#5A4A44`, coral encrustation patches from the reef palette,
  portholes dark `#14121E`.

### 2.5 Fauna look  *(owner: fauna – CreatureMesh*, CreatureIcons)*

* Silhouettes exaggerated: fins 1.2× natural, bodies slightly chubbier; small fish 40–200 triangles, large
  300–800; faceted flat shading (as today) with point-filtered colour blocks (as today – this already fits).
* **Eyes:** white sclera disc + black pupil (55 % of the eye), eye 1.3× natural size – HtF fish/puffer have
  cartoon eyes. Sharks/rays: small dark eyes, no googly.
* Colours: saturated mid-tones from the species palette; no colour lighter than `#F4EFE6`.
* **Readability in deep/blue water** (`09_wreck`, `10_blue_drop`: creatures turn into flat dark silhouettes):
  emission floor = albedo × 0.10 (0–20 m) → × 0.18 (≥ 40 m), scaled by daylight; ▣ lit side of a shark at 24 m
  ≥ `#3A5A78`.
* Sealog/hotbar icons: 3/4 side view on light `#EAF4F6`, soft drop shadow; undiscovered = flat silhouette `#8C96A3`.

### 2.6 Night and lamp

Above water: §1.2 night row. Under water at night the fog is ×0.1 of the day stop + moon blue (near black
`#04101E` at 5 m). Lamp: warm yellow `#FFD9A0` (GDD 15.32), spot 55°, range 14 m, intensity 3; lit sand at 4 m
▣ ≈ `#B49A70`.

---

## 3. The 30 biggest visual differences (ranked) – ours vs How to Fish

| # | What is wrong (screenshots) | Owner (files) | Fix (numbers) |
|---|---|---|---|
| 1 | Sea is pale cyan/mint (`03`: `#9EE0F2`–`#BBE9F0`); HtF is steel/navy blue. | world – WorldOcean, LowPoly | Ramp colours/alpha from §1.6; ▣ near `#2F6290`, 100 m `#4F7FAE`, horizon `#6E9FCC`. |
| 2 | Square tiles / blotches around the island (`02_island_air`, `01_menu`). | world – WorldOcean, WorldWaterMap | Continuous per-vertex depth → 64 × 1 ramp UV, delete discrete classes + submesh sorting, alpha ≥ 0.90 beyond 4 m depth. |
| 3 | Sky too pale with a near-white horizon (`03`: top `#769DD0`, horizon `#E1F5F2`). | world – WorldSky, WorldDayNight | Sky table §1.2; zenith ▣ `#4A88BF`, horizon ▣ `#8DBDEA`; procedural values or gradient cubemap. |
| 4 | Grey-beige haze band over the sea (`02`: `#C4BBB9`), wrong fog colour/density. | fx – FXPostProcess/FXMath; world – FogColors | Exponential 0.0016, colour = horizon × 0.94 (noon `#85B2DC`), saturation ≥ 0.25. |
| 5 | View-model arms 2–3× too big, blocky, saturated teal sleeves (all shots, `1.png`). | player – PlayerHands/PlayerMeshes; filming – FilmRigBuilder | Poses + 240 × 260 px limit §1.13, camera models ×0.62, mitten hands with smooth normals, sleeve ≤ 0.18 m visible. |
| 6 | Lamp and camera held at once; camera always in hand (`1.png`, feedback #6/#7). | player + filming + ui | Visible only from the active hotbar slot of a hand (`Hands` API); one item per hand; rod = both hands. |
| 7 | Lime greens (`03` grass `#9DD96E`). | world – LowPoly/WorldTerrain; props | Grass `#6E9445`, GrassDark `#4F6F33`, frond `#7FA243`/`#4E6F2E`. |
| 8 | Bare lawn: sparse 3-blade tufts on flat colour (`01`, `02`, `03`). | props – PropPlants.GrassTuft, PropJungle | Clumps of 12–24 blades, 0.45–1.1 m, ramp-texture gradient `#3A552A`→`#9DB65A`, 0.35–0.8 clumps/m². |
| 9 | Weak, warm shadows (`03` shadow on sand `#B59886`), ambient far too bright. | world – WorldDayNight; fx/editor URP | Shadow strength 0.90, ambient table §1.3 (noon sky `#8297C2`); ▣ shadow on sand `#8E8F95`. |
| 10 | Flat high-key image, no vignette, Neutral tonemapping. | fx – FXPostProcess | ACES, exposure +0.45, contrast +12, sat +8, SMH violet shadows, vignette 0.28 `#140E26`, bloom 1.0/0.45. |
| 11 | Dark-navy HUD boxes everywhere (money, quest, clock, dive panel). | ui – UIHud, UIKit | Box-free outlined Fredoka text/icons, layout §1.14 (money bottom-right, dive top-left, clock top-right). |
| 12 | No hotbar (HtF has one). | ui + player | 2 hands × 3 slots, 84 px slots, styles §1.14. |
| 13 | Palm fronds thin/spiky, few, too saturated; crowns small. | props – PropPlants.Palm | 7–9 fronds 0.38–0.5 × H long, half-width 0.17 × len, 6–8 teeth, two-tone by face, trunk r 0.24→0.15. |
| 14 | Monotone "500 palms on a lawn" vegetation; no layers. | props – PropJungle | ≈ 260 clustered palms near the coast, ≈ 180 broadleaf canopy trees, ≈ 40 conifers, flowering bushes, 3-layer rule §1.9. |
| 15 | Under water milky/washed and visible far too far (`06`, `07`, `08`). | fx – FXMath; world – underwater ambient | Fog stops §2.1, k table §2.2 (19 m in the shallows), saturated sand ▣ `#DCCB94`. |
| 16 | Paper-flat octagon "lily pad" corals floating (`06`, `07`). | props – PropCoral | Chunky colonies 2–3 tones, table corals 0.12 m thick ≤ 1 in 6, patches 6–12 m. |
| 17 | Huge pale triangles on the seabed read as glitches (`06`, `07`, `08`). | world – WorldTerrainData; props | Facets ≤ 6.4 m near sites, ripples/dunes, rock clusters `#6E6F86`, sea grass patches. |
| 18 | Deep creatures are dark silhouettes (`09`, `10`, `11`). | fauna – CreatureMeshMaterials | Emission floor 0.10 → 0.18 × albedo; googly eyes 1.3×; ▣ lit side ≥ `#3A5A78` at 24 m. |
| 19 | Shore foam is a thin clean ribbon; no wet sand. | world – WorldCoast, terrain classes | Foam 1.6–2.8 m lumpy `#EEF4F6`, sliding 0.4 m; wet sand band 1.2–2 m `#C9AF86`. |
| 20 | Ocean reads as flat plastic, no glints. | world – WorldOcean | Smoothness 0.55, facet ±8–12 %, glints `#D5E6F5`, Fresnel ≤ horizon luminance. |
| 21 | Main menu: "AI-looking" panels, red pill, island flyover. | ui – UIMainMenu; player – menu crew/camera | Crew on the dock at golden hour, bubble logo, plain text list, no panels/pills (§1.14). |
| 22 | Loading screen is a plain blue screen with a pill. | ui – UILoadingScreen | Underwater gradient, light bands, bubbles, fish, ocean facts, bubble progress bar. |
| 23 | Hut looks like new plastic (clean grey-white walls, bright red roof). | props – PropHut | Whitewash `#DCCFBE`, plank gaps `#3A2C2A`, ±6 % plank variants, accent boards `#7A4448`, roof `#A8484A`. |
| 24 | Dock planks too light/clean (`04` `#E2C79F`), thin regular posts. | props – PropStructures | Planks `#9A7658` ±6 %, 3 cm gaps, posts r 0.12 m `#5E4436` with wet band, working ladder (feedback #4). |
| 25 | Rocks neutral grey and flat. | props – PropRocks; world – rock class | Violet-grey variants `#8A8C96`/`#6E6F7E`/`#5C5A69`, crevices `#4A4658`, moss tops. |
| 26 | Clouds are tiny grey puffs ("UFOs"). | world – WorldClouds | 8–10 clouds 60–120 m long, 220–320 m high, undersides ≥ `#DCE5F1`, ≤ 8 % sky cover. |
| 27 | Phone is a modern dark iPhone overlay, text-heavy. | ui – UIPhone; player – 3D phone | Old scratched 3D phone in hand (§1.13), light screen theme, flat app icons, badges. |
| 28 | Default sans font, no outlines, much text. | ui – UIKit | Fredoka everywhere, outline + shadow, min 16 px, icons instead of sentences. |
| 29 | No bean characters (co-op avatars, menu crew) and no nameplates. | player – DiverModels.Build; net | Bean diver spec §1.12, player colours, lower-case outlined nameplate + speaker icon. |
| 30 | Sun disc/bloom missing, overhead sun, golden hour too beige. | world + fx | Max elevation 58°, disc 2° `#FFFDF2` with bloom halo, golden-hour sky row 8° in §1.2. |

---

## 4. Per-module checklists

### World (`World/*.cs`, `Core/LowPoly.cs` palette)
- [ ] Apply the new `LowPoly` palette (§1.7) in one commit and announce it to all modules.
- [ ] Sky gradient matches the §1.2 table at noon, 20°, golden hour, sunset, night (option A or B).
- [ ] Sun max elevation 58°, colours/intensities per §1.2, visible 2° disc.
- [ ] Shadow strength 0.90; ambient trilight table §1.3; ▣ shadow on sand `#8E8F95`.
- [ ] `EnvState.SkyFogColor` = horizon × 0.94 (noon `#85B2DC`, golden `#B7A6C2`, night `#24365E`), never grey.
- [ ] Ocean: 64 × 1 RGBA ramp, per-vertex depth UV, one material, discrete classes and submesh sorting removed.
- [ ] Ocean alpha ≥ 0.90 beyond 4 m depth; smoothness 0.55; facet variance ±8–12 %; glints visible.
- [ ] `02_island_air` has no squares: adjacent 12 px blocks in open sea differ ≤ 10 per channel.
- [ ] Foam band 1.6–2.8 m lumpy, sliding; wet-sand terrain band 1.2–2 m `#C9AF86`.
- [ ] Under-surface `#9FE4EC` α 0.70 with daylight emission.
- [ ] Underwater ambient per §2.1 (brighter, more saturated teal in the shallows).
- [ ] Clouds per §1.2 (bigger, higher, fewer, bright undersides).
- [ ] Night: moon 0.45 `#A9BCFF`; ▣ grass `#2F4A3A`, sand `#59617A`.
- [ ] Seabed facets ≤ 6.4 m within 60 m of every dive site; GrassDry transition at the sand border.
- [ ] Everything still deterministic (fixed seeds / world hash).

### Props (`World/Props/*.cs`)
- [ ] Grass clumps (§1.9) with the ramp texture, densities, 2 LODs, no shadows, tier-based cull.
- [ ] Palm rebuild: slender segmented trunk, 7–9 broad two-tone toothed fronds, coconuts, ≈ 260 clustered palms.
- [ ] Broadleaf canopy trees (≈ 180), conifers on the hill (≈ 40), closed canopy in the jungle core.
- [ ] Bushes with magenta/pink/white flowers, ferns, taro/banana, meadow flowers.
- [ ] 3-layer rule along every path (grass < 1 m, bush 1–2 m, tree > 6 m).
- [ ] Rocks: violet-grey variants, crevice faces, moss tops.
- [ ] Hut: whitewash planks with gaps and variants, accent boards, roof `#A8484A`, turquoise shutters `#3FA9B5`, solid roof/wall colliders.
- [ ] Dock: weathered planks with gaps, thick posts with wet band, bollards, rope, climbable ladder.
- [ ] Lighthouse colours; new POIs (waterfall, cave, rope bridge, tide pools, radio ruins) in the §1.11 palette.
- [ ] Corals: chunky 2–3-tone colonies, thick table corals ≤ 1 in 6, patches 6–12 m with sand gaps.
- [ ] Seabed dressing: ripples, rock clusters `#6E6F86`, sea-grass patches; kelp 2 tones; wreck colours.
- [ ] Tire swing and other props: solid colliders except bushes/grass.

### FX (`FX/*.cs`, URP values in `Editor/SealebritiesSetup.cs`)
- [ ] Tonemapping ACES; exposure/contrast/saturation/white balance/SMH per §1.5; tune until lit sand ▣ `#E6D0A4`.
- [ ] Vignette 0.28 `#140E26` above water; underwater 0.30 → 0.40 `#03203A`.
- [ ] Bloom threshold 1.0 / intensity 0.45 / scatter 0.70.
- [ ] SSAO intensity 1.0, radius 0.30, direct lighting 0.25 (radius 0.22 if the hands blotch).
- [ ] Above-water fog Exponential 0.0016 (night 0.0022); underwater ExponentialSquared with the k table §2.2.
- [ ] Underwater fog colour stops §2.1; entering the water never darker than the 0–3 m stop.
- [ ] God rays (8 shafts, α 0.12, gone at 30 m), caustics (to 18 m), marine snow, bioluminescence.
- [ ] Golden-hour/night grading deltas; night exposure +0.25.
- [ ] Quality tiers Low/Medium/High per the §1.5 table (shadows, SSAO, MSAA, render scale, effect counts).
- [ ] No film grain, no CA (except narcosis), no DoF, no motion blur.

### Player (`Player/*.cs`, camera models with the filming owner)
- [ ] Only items in an active hotbar slot are visible; one item per hand; rod uses both hands; empty hands hidden.
- [ ] Mitten hands with smooth normals, glove `#343849`, cuff from `DiverLook.cuff`, sleeve ≤ 0.18 m visible.
- [ ] Poses from the §1.13 table; one-hand item ≤ 240 × 260 px at 1080p.
- [ ] Camera models ×0.62 (T2 ≤ 0.11 m); lamp tiers recoloured.
- [ ] Fishing rod model + 0.35 s wind-up / 0.25 s cast animation.
- [ ] 3D scratched phone (§1.13) showing `PhoneDisplay.Texture`, raised pose fills ≈ 55 % screen height.
- [ ] Idle sway / walk bob / equip-stow timings.
- [ ] `DiverModels.Build`: bean diver per §1.12 (smooth, googly eyes, hats, talking mouth, player colours).
- [ ] Menu crew sitting on the dock at golden hour (menu camera framing).
- [ ] Default FOV 70°, slider 60–90.

### UI (`UI/*.cs`)
- [ ] Remove all navy HUD panels; Fredoka with outline + shadow on every HUD text.
- [ ] HUD layout: dive block top-left, clock top-right, money/followers bottom-right, hotbar bottom-centre, dot crosshair.
- [ ] Air ring colours/blink; clip counter colours; gain popups.
- [ ] Quests only as toast + phone app "Aufgaben" with badge.
- [ ] Hotbar (2 × 3 slots) + inventory drag & drop, 3D item icons.
- [ ] Notification banners (white, rounded, 3.5 s, ping).
- [ ] Phone light theme, flat app icons with listed colours, badges, Sealog silhouettes `#8C96A3`, gallery app.
- [ ] Main menu: logo, plain text list, no pills/panels/subtitles; 3 difficulty cards.
- [ ] Loading screen: underwater animation + facts; launch intro shots.
- [ ] Settings screen in the same light style (volumes, FPS, quality, rebinding, voice mode, mic mute).
- [ ] Co-op nameplates (lower-case, outlined, speaker icon) if the UI owns them.

### Fauna (`Fauna/*`)
- [ ] Googly eyes (white sclera + black pupil 55 %, 1.3× size) except sharks/rays.
- [ ] Exaggerated fins (1.2×), chunky silhouettes, triangle budgets.
- [ ] Emission floor 0.10 → 0.18 × albedo with depth, scaled by daylight.
- [ ] No colour lighter than `#F4EFE6`; mid-tone saturated species palettes.
- [ ] Icons: 3/4 view on `#EAF4F6`; silhouettes `#8C96A3`.
- [ ] Spawn/visibility distances consistent with the new 15–20 m underwater visibility.

---

## 5. Acceptance – screenshot targets (1920 × 1080 AutoShot)

Measure with `sample.ps1` (see §0). Box coordinates are for the current AutoShot framing; if a framing changes, sample
the same surface.

| Shot | Box (x0,y0,x1,y1) | Surface | Target ▣ |
|---|---|---|---|
| 03_hut_dock_boat | 1000,5,1600,30 | sky top | `#4A88BF` ±16 |
| 03_hut_dock_boat | 1300,500,1600,520 | sky just above the horizon | `#8DBDEA` ±16, never above `#B0D4F0` |
| 03_hut_dock_boat | 1500,545,1900,560 | sea mid/far | `#4F7FAE` ±18 |
| 03_hut_dock_boat | 1600,620,1900,660 | sea near (shallow over sand allowed: `#4E9DB8`) | `#2F6290` ±18 |
| 03_hut_dock_boat | 900,700,1100,800 | lit sand | `#E6D0A4` ±14 |
| 03_hut_dock_boat | any palm shadow on sand | shadow on sand | `#8E8F95` ±14 |
| 03_hut_dock_boat | 100,600,250,640 | grass | `#6E9445` ±16 |
| 03_hut_dock_boat | lit frond / trunk | palm | `#7FA243` / `#8A4F55` ±18 |
| 03_hut_dock_boat | whole frame | each held item incl. hand | ≤ 240 × 260 px |
| 02_island_air | sea ring around the island | tiles | adjacent 12 px blocks differ ≤ 10 per channel |
| 02_island_air | 800,340,1200,380 | haze band | HSV saturation ≥ 0.25, blue hue (200–220°) |
| 06_reef | 800,100,1200,150 | water above | `#5ECAD9` ±18 |
| 06_reef | 300,600,600,700 | sand near | `#DCCB94` ±18 |
| 06_reef | objects ≥ 20 m away | fog | ≥ 90 % fog colour |
| 09_wreck | lit side of the shark | fauna readability | ≥ `#3A5A78` |
| 12_night | 800,100,1200,150 | night sky | `#121A3A`–`#222E64` |
| 12_night | 200,560,400,600 / 200,660,400,690 | grass / sand | `#2F4A3A` / `#59617A` ±12 |
| any HUD shot | – | HUD | no opaque rectangles behind HUD text; hotbar visible bottom-centre |

### Calibration pass v1.1 (art polish lead, measured in 3 builds – these values supersede the numbers above)
* **Grade:** white balance 0 / 0 (the −3 / +5 pair plus URP's gamma→linear SMH conversion pushed every frame
  violet: sky 10° toward violet, magenta trunks, lavender mornings). SMH shadows (0.99, 0.985, 1.04), highlights
  (1.012, 1.0, 0.985). Day exposure **+0.38** (keep `SkyGrade.Exposure` equal). Golden hour: sat +6, temp +14, tint −4.
  Night: sat −20, exposure +0.30, moon 0.6 `#B8CCFF`-ish (0.72/0.80/1.0).
* **Sky dome:** targets pass through `SkyGrade.AcesHueFix` (URP's real ACES ≠ the fit: blue targets −8° hue, light
  targets trimmed). Golden rows bluer/less lavender (8° away horizon `#AEC2DC`). Fog = horizon away × 0.93.
* **Sea:** ramp albedo pre-compensated for the lit + graded look (noon on-screen ≈ 1.65 × albedo); opaque from 2.5 m
  (α 0.99+), see-through only on the beach strip. Sky probe 0.72 above water at noon (0.85 at golden hour).
  Measured: near `#326D9B`, mid `#3487BB`, sky top `#4985B3`, horizon `#91BEE5`.
* **Under water:** shallow fog stop input `#50CDE2` (pre-compensated; `#5ECAD9` measured milky `#86CED5`), exposure
  +0.28, saturation +20 in the shallows, sun ×0.82 under water, ambient `#8EC2D8 / #6AA8C4 / #46829A`, seabed `#EAC27A`,
  under-surface `#4CBCD0` α 0.52. Measured at 2 m: water above `#66CDD8`, fog `#61C8D3`.
* **Palette:** Grass `#5D7C45`, GrassDark `#486036`, GrassDry `#8C9654`, grass ramp `#34502A → #557A38 → #8AA352`,
  Rock `#787A86`, RockDark `#56545F`, Wood `#8E6C50`, PalmTrunk `#7A4A50`; palm-frond undersides glow olive with a
  daylight-scaled emission (`PropMats.SetDaylight`), fronds flatter (fold 13–21°) and broader (half-width 0.215 × len).
* **Terrain:** the sand/grass border follows a coherent noise edge (`WorldTerrainData.GrassEdge`) – no checkerboard.

### Things that are never allowed (anti-patterns)
* Mint/cyan open sea, square water tiles, a white or grey horizon band, grey fog.
* Lime/neon greens, pure-white geometry, pure-black shadows, brown-grey shadows.
* Two items in one hand, a held item bigger than 240 × 260 px, both hands always on screen.
* Navy rounded boxes behind HUD text, pills, long sentences on the HUD.
* Noise/photo textures, normal maps on environment, toon outlines, film grain, DoF, motion blur.
* Paper-flat floating coral shapes, evenly sprinkled reef "confetti".
* Non-deterministic generation (anything that differs between co-op machines).
