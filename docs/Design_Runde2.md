# SEALEBRITIES – Design Round 2 (implementation spec)

Version 2 · 11.10.2026 · Game design · for the 11 round-2 engineers (core, ui, player, world+fx, props, fishing+boat,
npc+story, livestream, fauna, net, filming) · v2 adds **section K** (second player message) and **section L** (third
player message) and marks every older line they replace

Sources: GDD `docs/SEALEBRITIES_Spielkonzept.md` §13–§18 (§17 = the player's 100 answers, §18 = feedback on
Testversion 3, both binding), `docs/Testbericht_Nacht_1.md`, `docs/ArtBible_HowToFish.md` (incl. Calibration v1.1 –
never revert calibrated values unless a line below says so), the current code (read 11.10.2026).

**How to read this document**

* Every block names its **owner** in brackets, e.g. *(world+fx)*. Section J has the full file-ownership table and the
  contracts the core engineer creates first.
* `Q21` = answer 21 of GDD §17. `(a)…(f)` = the six feedback points of GDD §18; `(g)…(n)` = the second player
  message (section K); `(o)…(ac)` = the third player message (section L). **K and L win over older lines**; every
  replaced line carries a marker „→ K.x / L.x“, and K.0 lists all conflicts.
* All player-visible text is given as **DE / EN** pairs and goes through `Loc.Tr("de", "en")`. Keep the texts short;
  they are written for subtitles/toasts. Ellipsis = `…` (one glyph), quotes in DE = „…“.
* Numbers are design targets. If a number turns out wrong in the build, change it in **one** named constant and tell
  the core engineer (Section J, "constants owner").
* `scratchpad/…` = `C:/Users/ADMINI~1/AppData/Local/Temp/claude/C--Users-Administrator-Desktop-ll/ef7aff3c-ffeb-428b-96d7-37d6e4608b77/scratchpad/…`
  (design harness `design2_island/`, economy simulation `econsim_core/`, compile check `compilecheck/`).
* Global rules from the task stay in force: namespace `Sealebrities`, C# 9, input only via `GameInput`, no per-frame
  allocations, explicit `UnityEngine.Object` null checks, deterministic generation, shared state only where
  `Net.HasAuthority`, offline single player must keep working, no `UnityEditor` outside `Editor/`.

---

## 0. Round-2 priorities (what the player feels first)

| Prio | Item | Owner |
|---|---|---|
| P0 | (a) smaller island, (d) items never clip, (e) slider bug, (f) boat comes home | world+fx, props, player, ui, fishing+boat |
| P0 | (b) co-op with a friend on another PC via Steam test app 480 | net |
| P0 | (c) character screen with live 3D preview | ui + player |
| P1 | A intro NPC Horst + tutorial, D fishing 2.0, E survival light, Q45 money from video 1 | npc+story, fishing+boat, core |
| P1 | C livestream on KRILL.tv | livestream (+ui, net) |
| P2 | B story, G fauna, H world/art, I UI/UX polish, F co-op extras | all |
| **P0 (round 2b)** | (s) island 0.62 (L.5), (h) filming rework (K.2), (o) open-world spawning (L.1), (g) faster money (K.1), (w) sleep vote (L.9), (x/z) quest line + notifications (L.10), (ac) smoother characters (L.13) | worldfx, props, filming, fauna, core, ui, player, net |
| **P1 (round 2b)** | (k) prologue + story NPCs (K.5), (j) selfie (K.4), (i) underwater activities (K.3), (m) sponsor deals (K.7), (r) title + collage (L.4), (p) Sealog counters (L.2), (q) reefs (L.3), (t) secrets (L.6), (v) animations (L.13) | all |
| **P2 (round 2b)** | (aa) village (L.8), (u) side quests (L.7), (n) Toni + Fog Isle (K.8), (y) island animals (L.11), (ab) Chopper Shop + mishaps (L.12), (l) music (K.6) | npc, props, fauna, fishboat, ui, worldfx |

Definition of done for round 2: the AutoPlaytest (solo) and AutoPlaytestMP runs pass, the compile check passes with
your tag, and every P0 item has an AutoShot or log line that proves it (Section J.6).

---

## 1. New player feedback after Testversion 3

### 1a. The main island is 75 % of today's size *(world+fx: terrain; props: placement; core: WorldLayout values)*

> **→ L.5: superseded by the third player message (s).** The island is now **0.62** of the Testversion-3 width; all
> values (WorldLayout, WorldFeatures, WorldHeightField, DiveSites, literals) are in L.5 and were verified in
> `scratchpad/design2_l/r3`. The method below (map-scale × S, feature geometry fixed, `Isl()`, lagoon, literal list)
> stays valid; the cave ramp and the cave position of this section are replaced by the Waterfall Grotto (L.5).

**Decision (0.75 plan, history):** linear scale **0.75** for every *map-scale* distance on the main island (shoreline radius, anchor
positions of the POIs, trail waypoints, coast lobes, noise frequencies). **Feature geometry keeps its size** (hut and
helipad plateaus, dock width, cliff heights, cave passage, ravine width/depth, pool radius, ladder lengths) – a 75 %
cave would be too narrow to walk. Result (measured with the reference harness, see "Verification"):

| | Testversion 3 | Round 2 | Change |
|---|---|---|---|
| Mean outer shoreline radius | 196 m | 147 m | −25 % |
| Land area | 11.4 ha | 6.4 ha | −44 % |
| Sum of all 12 trails | 1,162 m | 881 m | −24 % |
| Hut → lighthouse / waterfall / cave / radio tower / hidden bay | 198 / 195 / 246 / 270 / 307 m | 147 / 139 / 189 / 206 / 230 m | −25 % |
| Hut → dock end / helipad / tide pools / second beach | 89 / 100 / 117 / 211 m | 71 / 75 / 88 / 158 m | −20…−25 % |
| Highest point (lighthouse) | 42 m | 34 m | slopes stay walkable |
| Land steeper than 30° | 17.3 % | 21.5 % | (mostly POI cliffs) |

**Dive sites and landmark islands stay where they are** (boat trips were not criticised), except the House Reef,
which must stay "directly off the dock" and moves with the island. Dock end → site centre: House Reef 66 m (was 76),
Kelp 579 m (620), Wreck 489 m (462), Blue Drop 949 m (990), Trench 890 m (857).

#### New `WorldLayout` values (Core/Contracts.cs) *(core writes them in phase 0)*

```csharp
public const float IslandScale = 0.75f;          // NEW – documents the round-2 scale, used by Isl()
public const float IslandRadius = 142f;          // was 190
public const float IslandPeak = 34f;             // was 42 (= lighthouse plateau)
public static Vector2 Isl(float x, float z) => new Vector2(x * IslandScale, z * IslandScale);  // NEW helper for legacy literals

public static readonly Vector3 Hut = new Vector3(88f, 0f, 14f);          // was (118, 18)
public static readonly Vector3 Bed = new Vector3(86f, 0f, 18f);          // Hut + (-2, 4) – unchanged offset
public static readonly Vector3 TV = new Vector3(91f, 0f, 10f);           // Hut + (3, -4)
public static readonly Vector3 PlayerSpawn = new Vector3(84f, 0f, 18f);  // Hut + (-4, 4)
public static readonly Vector3 Helipad = new Vector3(52f, 0f, -52f);     // was (70, -70)
public static readonly Vector3 DockStart = new Vector3(126f, 0f, 4f);    // was (168, 6) – shoreline
public static readonly Vector3 DockEnd = new Vector3(158f, 0f, 4f);      // was (206, 6) – dock now 32 m (was 38)
public static readonly Vector3 BoatSpawn = new Vector3(164f, 0f, 12f);   // DockEnd + (6, 8), water 2.9 m
public static readonly Vector3 FishingSpot = new Vector3(157f, 0f, 1f);  // DockEnd + (-1, -3)
public static readonly Vector3 Lighthouse = new Vector3(-22f, 0f, 112f); // was (-30, 150)
public static readonly Vector3 HiddenBay = new Vector3(-139f, 0f, -22f); // was (-185, -30)
public static readonly Vector3 WaterfallTop = new Vector3(-47f, 0f, 47f);   // was (-70, 70) – 5 m further inland than ×0.75
public static readonly Vector3 WaterfallPool = new Vector3(-32f, 0f, 22f);  // Top + (15, -25) – feature offset kept
public static readonly Vector3 CaveEntrance = new Vector3(-88f, 0f, 84f);   // was (-110, 110) – keeps 53 m behind the falls' lip
public static readonly Vector3 Ravine = new Vector3(-15f, 0f, -30f);        // was (-20, -40)
public static readonly Vector3 TidePools = new Vector3(112f, 0f, -71f);     // was (150, -95)
public static readonly Vector3 SecondBeach = new Vector3(30f, 0f, -133f);   // was (40, -178)
public static readonly Vector3 RadioTowerRuins = new Vector3(-87f, 0f, -95f); // was (-120, -110) – 13 m off the ×0.75 spot (gully room)
// KelpIsland, WreckIsland, BlueDropIsland, TrenchIsland, MiniIslandRadius: UNCHANGED
```

Why the three off-scale moves: plain ×0.75 put the waterfall plateau (22 m) on top of the cave clearing (13 m)
(7 m wall on the trail) and the radio-tower plateau (22 m) 20 m from the bay gully top. Fixed as above and verified.

#### `DiveSites` *(core)*

| Site | Centre | Radius | Note |
|---|---|---|---|
| house_reef | **(210, 0, 45)** (was 260, 60) | **110** (was 120) | still 3–12 m |
| kelp_forest / wreck / blue_drop / deep_trench | unchanged | unchanged | |

Also replace the fallback literals in `WorldHeightField.SiteOf("house_reef", 260f, 60f, 120f, …)` and
`PlayerIntroRunner` (`new Vector3(260f, 0f, 60f)`) with the new centre.

#### `WorldFeatures` changes *(world+fx)*

| Constant | Old | New |
|---|---|---|
| `RavineWestX / RavineEastX` | −66 / 26 | **−49.5 / 19.5** (ravine 69 m long) |
| `RavineRimY` | 23.5 | **21.0** (hills are lower; floor = rim − 7.5) |
| `HiddenBayBeach` | HiddenBay + (40, 0) | **HiddenBay + (32, 0)** |
| `BayGullyTop` | (−146, 16.4, −82) | **(−100, 21.5, −84)** (starts at the radio plateau rim) |
| `BayGullyBottom` | (−146, 0.9, −38) | **(−108, 0.9, −29)** (gully 56 m, 21°) |
| **NEW** `CaveRampTop`, `CaveRampBottom` | – | **(−40, 29.5, 97)**, **(−80, 13.2, 92)** – 5 m wide walkable ramp from the lighthouse trail into the cave clearing (22°), carved like the bay gully |
| `SecondBeachHeadlands` | sb ± (41, …) | **(sb.x − 33, 3.2, sb.z − 2, r 9)**, **(sb.x + 32, 2.6, sb.z + 3, r 8)** |
| `SeaStacks` (x, top, z, r) | 7 stacks | **(−51, 6, 141, 5), (−140, 7.5, 81, 6), (−183, 8.5, 32, 6.5), (−153, 6.5, −129, 7), (−56, 5, −155, 5), (83, 6, −144, 5.5), (30, 4.5, 173, 4.5)** – all stand in the new lagoon (~2 m water) |
| everything else (waterfall heights, cave sizes, tide pool offsets, radio plateau, mini islands) | | unchanged |

#### `WorldHeightField` changes *(world+fx)* – reference implementation exists, port it

Let `S = WorldLayout.IslandScale (0.75)`, `xs = x / S`, `zs = z / S` ("old island space" for noise).

| Where | Old | New |
|---|---|---|
| `LighthouseHeight` | 42 | **34** |
| POI/plateau evaluation (`Height`, `RockHint`, `Hint`) `r < 300` | 300 | **230** |
| `IslandBase` far branch | `r < 620`, `r − 190` | **`r < 470`**, **`r − WorldLayout.IslandRadius`** |
| Cove | `CoveX = BayX+7`, `CoveR 27`, mouth `(BayX−60, BayZ−18)`, channel half-width 10, basin noise 4 | **BayX+5, 22, (BayX−45, BayZ−13.5), 8, 3** |
| `CoveBeach` | `SmoothStep(16, 34, Dist(CoveX+CoveR+6))` | **`SmoothStep(12, 26, Dist(CoveX+CoveR+5))`** |
| `Coast`: every Gauss centre offset and sigma, the six lobe amplitudes, the Fbm/Perlin amplitudes (36, 9, 4) and the `−14 east +34 bay −7 lhN +12 sb +7 trail` terms | old | **× S**; noise sampled at `(xs, zs)` with the old frequencies |
| `Lowland` radii (30/90, 24/75, 14/55, 30/80) | old | **× S**; dock term centred at `DockStartX − 8` |
| `Interior` | heights 6 + 26·…, +7 centre bump, ±2.2, cap 30 ±2 | **× 0.85 (const HS)**; all distances × S; noise at (xs, zs); ridges flanking the cove: centres/sigmas × S, heights × 0.85 |
| Lighthouse hill radius | `(80 + 55·south)` | **`80·S + 55·south`** (steep sea side 60 m, gentle inland side 115 m) |
| `LandHeight` | `wide = 18·Gauss(SbX, SbZ−8, 55)`, `Lerp(60, 5, cliff)` | **× S** for 18/8/55 and 60 |
| `HouseReefDepth` | `SmoothStep(185, 372, r)` | **`SmoothStep(140, 300, r)`** |
| `Arm` start | 235 | **176** |
| `SeaDepth` ocean blend | `SmoothStep(250, 900, r)` | **`SmoothStep(220, 900, r)`** |
| `SeaDepth` cove-shallow | `r < 320`, `SmoothStep(CoveR+4, CoveR+34)` | **`r < 240`, `SmoothStep(CoveR+3, CoveR+26)`** |
| **Lagoon (Q4, new)** | `near = shelf·(1 − e^(−D/Lerp(65, 9, cliff)))` | see below |
| `DockBeach` | hard-coded x = 124/138/150/170/128/130/160/168 | **relative to `DockStartX`:** −44/−30/−18/+2/−40/−38/−8/0 |
| `Features` | – | **cave ramp carve**: `DistSeg` to the ramp, `< 5.5 m → Lerp(h, rampY, 1 − SmoothStep(2.6, 5.5, d))` (same as the gully) |

Lagoon profile (Q4: wide turquoise lagoon, then blue) – replaces `near` in `SeaDepth`:

```csharp
const float LagoonWidth = 70f, LagoonWidthCliff = 22f, LagoonEdgeDepth = 3.4f;
float lw = Lerp(LagoonWidth, LagoonWidthCliff, cliff);
float near = D < lw
    ? 1.9f * SmoothStep(0f, 9f, D) + (LagoonEdgeDepth - 1.9f) * SmoothStep(9f, lw, D)
    : LagoonEdgeDepth + MathF.Max(0f, shelf - LagoonEdgeDepth) * (1f - MathF.Exp(-(D - lw) / Lerp(45f, 9f, cliff)));
```

Measured: water 1.6–2.5 m deep 20 m off every beach, ~3 m at 50 m, then the old shelf. The boat (draft < 1 m) can
cross the lagoon everywhere; the dock end keeps 2.75 m.

**Reference implementation (tested):**
`C:/Users/ADMINI~1/AppData/Local/Temp/claude/C--Users-Administrator-Desktop-ll/ef7aff3c-ffeb-428b-96d7-37d6e4608b77/scratchpad/design2_island/new/`
(`WorldLayout.cs`, `WorldFeatures.cs`, `WorldHeightField.cs`, `DiveSites.cs` – diff them against the project files).
Probe: `cd …/design2_island/new && dotnet build && dotnet bin/Debug/net8.0/new.dll ../out new` → prints every POI
height, shore radius per 15°, lagoon depths, land area, all 12 trails with their steepest 3 m step, and writes
`../out/island_new.png`, `zNW/zSW/zE/zS_new.png` (3 px/m contour maps, red = steeper than 35°). The `old/` folder
holds Testversion 3 for comparison. Run it after every change.

#### Literal coordinates outside the height field *(props unless noted)*

Rule: a hard-coded **map** coordinate (not an offset from an anchor) is wrapped in `WorldLayout.Isl(x, z)` (= old
value × 0.75). Offsets from an anchor (`hut.x + 6f`, `rv.z − 11f`) stay as they are.

| File | What | New |
|---|---|---|
| `Props/PropPaths.cs` Routes + Junctions | all literal `V(…)` points | `Isl(…)`, plus the two re-routes below |
| | route `cave` | `Isl(-14,142), Isl(-40,133), CaveRampTop, CaveRampBottom, cave − CaveDir·6` |
| | route `waterfall` | `CaveRampTop, (wt.x−6, wt.z+24), (wt.x−4, wt.z+5), FallRampTop, FallRampBottom, FallRampBottom+(9,−9), FallRampBottom+(11.5,−19), PoolBeach` |
| | junction cave/waterfall/lighthouse (was `V(-85,120)`) | `XZ(CaveRampTop) + (2, 2)`, toward `{cave, wt, lh}` |
| | junction radio (toward `V(-146,-80)`) | toward `XZ(BayGullyTop)` |
| | route `dock` last point `V(158, DockStart.z+0.2)` | `(DockStart.x − 10, DockStart.z + 0.2)` |
| `Props/PropBeach.cs` 45/64/83/90 | `FindLand(new Vector3(146,0,24))` etc. | `Isl()` of the same values |
| `Props/PropCollect.cs` | `JungleHeart` radius 120, `Fallback` disc 150 | **90**, **112** |
| `World/WorldTerrainData.cs` *(world+fx)* | `TrailControlPoints`, `BuildPaths` main/side literals | `Isl()`; `RectCircle(…, 340)` stays (lagoon keeps fine chunks) |
| `World/WorldBuilder.cs` *(world+fx)* + `Boot/GameBootstrap.cs` *(core)* | menu camera fallback (300, 55, −230) → look (60, 10, 20) | **(225, 41, −172) → (45, 7.5, 15)** |
| `Player/PlayerIntroRunner.cs` *(player)* | p0 (160,−62), p1 (184,−26), p2 (199,−5), look0 (150,3,12) | **(120,−46.5), (138,−19.5), (149,−4), (112.5,3,9)** |
| `Debug/AutoShot.cs` *(core)* | 7 literal camera points | `Isl()` of x/z, heights unchanged |

Everything that reads `WorldLayout` / `WorldFeatures` / `DiveSites` / `IslandRadius` (dock, hut, POI props,
collectibles, map app, FX shore audio, PropJungle, HeliService, boat rescue) follows automatically – check it in the
AutoShot tour.

**Acceptance (a):** probe numbers as in the table above (±5 %); no trail has a 3 m step steeper than 35° except
≤ 3 m on `cave` and `lighthouse` (Testversion 3 had 13–19 m there); every POI prop stands on its plateau (AutoShot
`03_hut_dock_boat`, `02_island_air`, new shots `13_cave`, `14_bay`); all 92 collectibles reachable (Section H);
AutoPlaytest steps "walk to helipad", "dock fishing", "house reef dive" still pass.

### 1b. Co-op with a friend on another PC – without publishing on Steam *(net; docs: net)*

Steam's public test app **480 ("Spacewar")** needs no store page and no publishing. Both players need Steam running,
logged in, and must be Steam friends. IP / LAN / Radmin VPN / ZeroTier stay as fallback.

Implementation (the `Net/Steam/*` work in progress – keep its design):
* `steam_appid.txt` with `480` next to the exe (build step in `SealebritiesSetup`) and in the project root.
* `SteamService.Init()` without `RestartAppIfNecessary`; fails quietly → Steam options hidden, IP still works.
* Lobby screen ("Mit Freunden"): first tile **„Über Steam hosten“ / "Host via Steam"** (FriendsOnly lobby, max 4),
  second **„Beitreten“ / "Join"** (Steam friends' lobbies list + IP field below a divider „Oder per IP / VPN“ / "Or by
  IP / VPN"). Button **„Freunde einladen“ / "Invite friends"** opens the Steam overlay invite dialog – only on the
  player's click, never automatically. Joining via the Steam friends list ("Spiel beitreten") works too.
* Version check in lobby data (`sb_ver`); mismatch → „Dein Freund hat eine andere Spielversion.“ / "Your friend runs a
  different game version."
* Status line while connecting: „Verbinde über Steam …“ / "Connecting via Steam …", success → normal Welcome flow.
* Privacy: persona names and Steam IDs never in logs or reports. Automated tests: only PRIVATE lobbies, leave at once.
* Update `docs/Multiplayer_Testen.md` with a 6-step DE guide (ZIP the build folder → friend unpacks → both start Steam →
  host clicks „Über Steam hosten“ → „Freunde einladen“ → friend accepts in Steam). Note that Steam shows "Spacewar".

### 1c. Character screen with live 3D preview *(ui: screen; player: preview stage + model; core: contracts; net: look sync)*

**Goal:** one clear screen where you SEE your bean diver while you change it.

**Where it opens** (all call `CharacterScreen.Open(context)`):

| Context | Entry | Notes |
|---|---|---|
| `FirstStart` | automatically after the first-start intro (Q42), before the difficulty cards | title „Wie siehst du aus?“ / "What do you look like?", primary button „Los geht's!“ / "Let's go!" |
| `MainMenu` | new main-menu item **„Charakter“ / "Character"** between „Mit Freunden“ and „Einstellungen“ | |
| `Lobby` | co-op lobby profile card: live thumbnail 220 × 220 px + name + button **„Charakter anpassen“ / "Customise character"** | the inline suit swatches and hat chips in `NetLobby.BuildHome` are removed |
| `Pause` | pause menu button **„Charakter“** (icon person) | offline: game paused; online: world runs, own avatar stands still |
| `Locker` | new prop „Spind“ / "Locker" in the hut (E: „Spind öffnen“ / "Open locker") | GDD 14.25 |

The look options are removed from Settings → Koop (that tab keeps voice chat only; the name moves to this screen).

**Layout (1920 × 1080, scales with the panel):**

```
┌──────────────────────────────── 1920 ───────────────────────────────────┐
│  PREVIEW (left 56 % = 1075 px)              │  OPTIONS (right 44 %)      │
│  warm gradient stage, diver on a round      │  Charakter      [name____] │
│  plank platform, golden key light           │  [Anzug][Hut][Gesicht][Maske & Flossen]
│                                             │  ○○○○○○ colour swatches    │
│        (drag = rotate, wheel = zoom)        │  ▢▢▢▢ item cards (locked = │
│  ⟲  ⟳  (rotate buttons, bottom-left)        │       grey + lock + hint)  │
│  [Körper] [Kopf] [Gesicht] focus chips      │  [Zufall] [Zurücksetzen] [Fertig]          │
└─────────────────────────────────────────────┴────────────────────────────┘
```

* Background: the menu/game scene dimmed (black 45 %), right panel = light card `#F4EFE6` with 24 px radius, Fredoka,
  same style as the settings cards. No dark boxes.
* Tabs: 4 icon tabs (glyph 40 px + label 18 px). Swatches: 64 px circles, 8 per row, selected = 4 px ink ring + check.
  Item cards: 132 × 132 px, glyph or small 3D icon + name 16 px; locked: 40 % alpha, lock glyph, unlock hint as a
  1-line caption („Alle 40 goldenen Muscheln“ / "All 40 golden shells").
* Keyboard/controller: Tab / Shift+Tab switch tabs, arrows move the selection, Enter picks, Esc = cancel (asks
  „Änderungen verwerfen?“ / "Discard changes?" with „Verwerfen“ / "Discard" and „Weiter anpassen“ / "Keep editing" when
  something changed). A/D (GameAction Left/Right) rotate the preview, W/S (Forward/Back) zoom between the focus
  presets – read through `GameInput` while `UIState.CharacterOpen` blocks gameplay.
* Buttons: „Zufall“ / "Random" (dice glyph; random over the unlocked options – plain `System.Random`, this is not
  world generation), „Zurücksetzen“ / "Reset" (back to the state when the screen opened),
  „Fertig“ / "Done" (save + close).

**Preview stage (player – `Player/CharacterPreview.cs`, implements `ICharacterPreview`):**

* Separate stage at world position **(0, 420, 0)**, all objects on layer **29 (`GameLayers.Preview`)**; own camera
  (cullingMask = layer 29 only, clear = solid colour, FOV 28°, near 0.05, far 20, no post-processing, MSAA 4× on
  High/Medium) rendering into a RenderTexture: **High 1024², Medium 768², Low 512²** (`ARGB32`, created on Open,
  released on Close – zero cost while closed).
* Stage: plank disc r 0.9 m (dock plank palette), backdrop quad 6 × 4 m with a vertical gradient `#F2C48A` (top) →
  `#E98F6A` (bottom), key light `#FFE2B8` 1.1 from front-left (yaw 35°, pitch 30°), rim light `#7FD3E0` 0.6 from
  back-right; both lights `cullingMask = 1 << 29`, no shadows on Low.
* Diver: `DiverModels.Build(look)` (the same model co-op avatars use), rebuilt on every change (≤ 2 ms – cache meshes
  as today). Visible gear (Q51) from the local player's equipped items.
* Camera focus presets (target height / distance): **Body** 0.95 m / 4.4 m, **Head** 1.55 m / 2.0 m, **Face** 1.58 m /
  1.2 m, **Feet** 0.35 m / 2.2 m; transition 0.35 s ease-out. Choosing a hat focuses Head, face options focus Face,
  fins focus Feet, suit focuses Body. Mouse wheel zooms continuously between Body and Face.
* Orbit: drag on the preview 0.35°/px yaw, keys 120°/s, rotate buttons ±45° steps; pitch fixed 5°; after 5 s without
  input the diver auto-rotates 10°/s.
* Life: breathing bob 1.5 %, idle sway, head looks toward the cursor (±25° yaw), on every change a 0.6 s "ta-da"
  (arms up) + bubble pop sound, a new hat drops in from 0.4 m with a 0.3 s bounce.

**Options (data in `Data/CosmeticsDatabase.cs`, core; rendering in `DiverBuilder`, player):**

| Tab | Option | Values |
|---|---|---|
| Anzug / Suit | Suit colour | 12: `#EE7F2D #3FB8C4 #E0607E #F2C94C #8E6CD9 #6E9445 #4F7FAE #EDE6DA` + new `#C94F3D #2F8F6E #F29BBE #3A3F5C` |
| | Cuff colour | „Auto“ (suit lightened 55 %) + 8 swatches |
| | Pattern on the suit panel | 0 Uni / Plain, 1 Streifen / Stripes, 2 Punkte / Dots, 3 Haizähne / Shark teeth, 4 Fischgräte / Herringbone, 5 Wellen / Waves; unlockable 6 Gold (all treasures), 7 Leuchtturm / Lighthouse (Gus mission 3) |
| Hut / Hat | Hat | 0 Ohne, 1 Fischerhut, 2 Wikingerhelm, 3 Haiflosse, 4 Quietscheente, 5 Kapitänsmütze (all 6 free – today's settings only offered 0–4); unlockable 6 Goldener Fischerhut / Golden bucket hat (40 shells), 7 Gus' Originalmütze / Gus's original cap (24 logbook pages), 8 Partyhut / Party hat (100 followers), 9 Piraten-Dreispitz / Pirate tricorn (wreck captain's cabin secret), 10 Horsts Köderhut / Horst's lure hat (tutorial done), 11 Möwennest „Günther“ / Seagull nest "Günther" (achievement *Horst im Hafenbecken*), 12 Krone / Crown (1,000,000 followers), **13 Perles Strohhut / Pearl's straw hat (L.7 P2), 14 Papierkrone / Paper crown (L.7 L2)** |
| Gesicht / Face | Skin | 6: `#E8C08A #D6A574 #A8724A #6B4636` + new `#F0D2B4 #4A2F28` |
| | Mouth | 5 (smile, open, o, flat, grin – existing) |
| | Eyes | 0 Glupsch / Googly, 1 Müde / Sleepy (half lids), 2 Schielend / Cross-eyed, 3 Sternchen / Stars (100 % Sealog), 4 Herzchen / Hearts (Gerald filmed on 5 different days) |
| | Brows | 0 keine / none, 1 buschig / bushy, 2 grimmig / grumpy, 3 überrascht / surprised |
| Maske & Flossen / Mask & fins | Mask frame | `#1E1B2B` (default) `#EE7F2D #3FB8C4 #F2C94C #E0607E #EDE6DA`; unlockable Gold `#E8C65A` (all **13** secrets, L.6) |
| | Snorkel | an / aus – on / off |
| | Fins | `#2B2533` (default) `#F2C94C` (Quietschgelb) `#3FB8C4 #E0607E` |
| (header) | Name | text field, max 16 chars (`Settings.MaxNameLength`) |
| (badge) | Channel rank badge | read-only, shown on the chest (Q50) |

Saving: „Fertig“ writes `Settings.Data` (look fields, name) → `Settings.ApplyAndSave()`; in a session the net module
broadcasts the new look (`NetSyncMsg.LookChanged`) and every machine rebuilds that avatar. First start: whatever is on
screen when „Los geht's!“ is pressed is saved (the default is a random suit colour + random hat 0–5).

**Strings:** tab names above; header „Charakter“ / "Character"; locked toast „Noch gesperrt: {hint}“ / "Still locked:
{hint}"; locker prompt „E – Spind öffnen“ / "E – Open locker".

### 1d. Held items never clip into the world *(player; filming models: livestream owner)*

Cause: the view model (hands, lamp, camera, rod, phone) is drawn in the transparent queue (2600) with the normal
depth test, so a dock plank 0.3 m in front of the eye hides the lamp ("the dock shows through the lamp").

Fix (both parts):
1. **Overlay rendering:** every view-model renderer goes to layer **30 (`GameLayers.ViewModel`)**. The world camera
   excludes layer 30; a URP **overlay camera** (stacked on the player camera, same FOV/position, cullingMask 30, clear
   depth, near 0.01, far 5) draws it on top. Post-processing: URP runs the post pass once at the end of the stack –
   enable it on the overlay camera (and off on the base camera) so grading/vignette still cover the hands exactly as
   today; verify with the AutoShot colour boxes. Under-water fog does not reach 0.5 m – no visual change. Applies to: PlayerHands,
   PlayerPhone, FishingRod view model (rod + line segment from the tip to 1.5 m; the rest of the line and the bobber
   stay in the world), FilmHandCamera model, the new wrist dive computer (Q40). The flash/photo capture
   (`FilmPhoto`, near clip 0.65) is unaffected because it renders the world camera only.
2. **Pull-back:** SphereCast r 0.10 m from the eye along the view direction, length 0.6 m; when it hits at distance
   d < 0.55 m, all held items move back/down by `(0.55 − d) × 0.8` (smoothed 0.08 s) – so the hand never pokes "into"
   a wall visually either.

Acceptance: AutoShot `15_lamp_dock` (eye 0.25 m above the dock deck edge, lamp in hand, looking at the plank) – the
lamp is fully visible; same shot inside the hut against a wall and under water against the wreck hull.

### 1e. Slider value labels in the billions *(ui; core: Settings.Validate)*

Cause: `UISlider.FromPointer` divides by `layout.width`; when the slider was rebuilt/detached while the pointer is
captured (`UISettingsView.Rebuild` clears the content), `layout.width` is NaN, `NaN <= 1f` is false, the value becomes
NaN, and `Mathf.RoundToInt(NaN * 100)` prints −2,147,483,648 %. `Settings.Validate` keeps NaN because `Clamp01(NaN)`
returns NaN, so the bad value is even saved, and `AudioListener.volume` gets NaN.

Fix:
* `UISlider`: `if (!(w > 1f)) return;` and `if (float.IsNaN(v) || float.IsInfinity(v)) return;` in `SetValue`;
  release pointer capture on `DetachFromPanelEvent`.
* Every slider label uses one formatter: `UIFormat.Percent(v)` → clamp 0..1, „0 %“…„100 %“; `UIFormat.Multiplier(v)`
  → „0,20×“…„3,00×“ (culture-aware); `UIFormat.Degrees(v)` → „60°“…„100°“.
* `Settings.Validate`: NaN/∞ → default value (master 1, music 0.7, sfx 1, voice 1, sensitivity 1, fov 70), then clamp.
* All sliders (today: 4 volumes, mouse sensitivity; new: FOV, voice gate if shown) go through these helpers. Add an
  AutoPlaytest step that feeds NaN and 1e9 into each slider's callback and checks the label.

### 1f. „Zur Hütte“ also brings the boat home *(fishing+boat; ui calls it; net forwards)*

* `UIPauseMenu.OnReturnToHut` → after the player teleport: `BoatHome.Request()`.
* `BoatHome.Request()` (core contract): offline/host → `BoatHome.Handler()`; client →
  `Net.Command(NetCommand.Custom, CoreCommands.BoatHomeKey)`.
* Boat handler (`BoatController.ReturnHome()`): allowed when nobody drives (`NetFlags.BoatDriverId < 0`) and no other
  diver stands on/in the boat (`Divers.All` within the hull bounds + 1 m). Then: raise the anchor instantly, zero
  velocity, `PlaceAt(WorldInfo.BoatSpawn, WorldInfo.BoatSpawnYaw)`, `GameEvents.RaiseBoatReturnedHome()`. Not allowed
  → toast „Das Boot bleibt – jemand ist an Bord.“ / "The boat stays – someone is aboard."
* Success toast: „Boot liegt wieder am Steg.“ / "Boat is back at the dock."
* Also call it when the player is rescued after fainting on Normal/Hard and the boat is more than 300 m from the dock
  and empty (rescue lands you on the dock → you need the boat there).

---

## A. Intro NPC: Harbour master Horst *(npc+story; props: his spots; ui: subtitles)*

### A.1 Who he is

**„Hafenmeister Horst“ / "Harbourmaster Horst"** – full name Horst Hummerich. Gus' old drinking buddy, self-appointed
harbour master, island consultant and "social media coach". Wildly enthusiastic, speaks in influencer buzzwords he
does not understand („Engagement!“, „Algorithmus!“), always eating something, gets into small slapstick accidents and
pretends they were planned. Never mean, never in the way.

**Look (How to Fish low poly, exaggerated):**
* Pear-shaped bean: 1.70 m tall, bottom radius **0.42 m**, top radius **0.16 m** (tiny head, huge belly), smooth
  normals like our divers; belly peeks out under the shirt (skin triangle band `#E9A27A`, sunburnt).
* Skin `#E9A27A`, round red nose `#D9534F` (r 0.05 m), giant droopy walrus moustache `#E07B39` (0.46 m wide, 3 tufts,
  bobs when he talks), googly eyes like the divers but 1.3× and slightly cross-eyed.
* Sunglasses pushed up on the forehead (`#1E1B2B` frames, `#3FA9B5` lenses), bucket hat `#C9B48A` with three fishing
  lures (`#F2C94C`, `#E0607E`, `#3FB8C4`) dangling from the brim.
* Hawaiian shirt magenta `#A8478F` with yellow pineapple blocks `#F2C94C` (3–4 flat quads), khaki shorts `#B5A07A`,
  white socks pulled up to the knee, brown sandals `#6B4636`, a whistle on an orange lanyard.
* Tube arms/legs like the divers (legs 0.55 m – short!), mitten hands.
* Sidekick: seagull **„Günther“** (low-poly gull, 0.45 m span) that circles above him or sits on his hat.

**Idle gags** (one every 12–25 s when the player is within 15 m, never two in a row the same):
1. Twirls his moustache, it springs back and hits his nose („Au.“ / "Ow.").
2. Günther lands on his hat and steals the fish sandwich out of his hand.
3. Blows his whistle at a passing fish/seagull, shakes a finger.
4. Takes a selfie with the player (flash 0.1 s), checks it, nods solemnly.
5. Dozes standing up, snores small bubbles, jerks awake („Bin wach! Algorithmus!“ / "Awake! Algorithm!").
6. Rare (5 %, max once per day): steps backward off the dock, *splash*, climbs the ladder, poses: „Geplant.“ /
   "Planned." → achievement *Horst im Hafenbecken*.

**Babble voice (Q60):** base pitch 140 Hz, 11 syllables/s, bubbly vowel set (o, u, a), +30 % pitch on "!" lines, a
short "hm-hm" before questions. Subtitles always (Section I).

### A.2 Where and when

| When | Where |
|---|---|
| Day 1 (new game) – **→ K.5** | **prologue first:** waits at the helipad when Wanda's chopper lands, hands over Gus' letter and the main goals, then walks the player to the hut; there he stands beside the porch steps (`WorldLayout.Hut + (11, 0, 3.5)` – the steps are at `hut.x + 9.7`, keep them free) for beat 0 |
| During the tutorial | walks ahead to the place of the current task (dock end bench, helipad edge, beach) at 2.2 m/s with a waddle (±8° roll); never blocks the dock path (stands 1.5 m beside it) |
| After the tutorial | 07:00–19:00 on the bench at the dock start (`DockStart + (−3, 0, −3)`), nights in his tent next to the hut (prop) |
| Milestone 1,000 followers | comes running to the player (wherever on land within 150 m; else waits at the dock) for the livestream beat |
| Co-op | one Horst for everyone (host simulates, positions synced 4 Hz); his lines play for all players within 25 m (subtitles in each player's language) |

**Talking:** a beat starts when the player is within 6 m and looks at him (or presses E: „E – Horst“). Lines
auto-advance (3–4 s each); **E or LMB = next line**, **hold E 1 s = skip the whole beat** (icon ring around the E
prompt). Settings toggle „Tutorial-Hilfe von Horst“ / "Tutorial help from Horst" (default on; off = beats are skipped,
the phone task list still works). Walking away > 12 m pauses the beat; returning resumes the current line.

### A.3 Onboarding beats (DE / EN)

Tutorial steps stay those of `TutorialQuests` (0 phone → 1 catch 5 fish → 2 list 5 fish → 3 buy tank → 4 open crate
→ 5 take camera → 6 ~~film 3 species~~ **photograph 3 species for the Sealog (→ K.2)** → 7 upload → 8 sleep).
**→ K.5:** a prologue at the helipad (Wanda, Horst, Gus' letter, main goals) plays before beat 0; beat 0 lines 1–2
move into the prologue, lines 3–4 stay. Horst delivers each step; the phone app „Aufgaben“ keeps
the short task text. Beat N plays when step N becomes current. Key hints in brackets (`[Tab]`, `[2]`, `[1]`) are
replaced at runtime by the current binding / the slot that holds the item (same helpers as `TutorialQuests.Key` /
`SlotKey`), never hard-coded.

**Beat 0 – Hallo** (player leaves the hut on day 1)
1. „Moin! Du musst Gus' Neffe sein. Oder Nichte. Oder … Taucher!“ / "Ahoy! You must be Gus's nephew. Or niece. Or… diver!"
2. „Ich bin Horst. Hafenmeister, Inselberater und dein Social-Media-Coach.“ / "I'm Horst. Harbour master, island consultant and your social media coach."
3. „Gus sagt, du willst Fische berühmt machen. Ich sage: ENGAGEMENT!“ / "Gus says you want to make fish famous. I say: ENGAGEMENT!"
4. „Erst mal Handy auf – hier läuft alles übers Handy. [Tab]“ / "First, open your phone – everything runs through it. [Tab]"

**Beat 1 – Angeln** (phone opened; Horst walks to the dock end)
1. „Kein Geld, keine Luft. Keine Luft, kein Tauchen. Also: Angeln!“ / "No money, no air. No air, no diving. So: fishing!"
2. „Angel nehmen [2]. Maustaste halten, loslassen – zack, Wurf.“ / "Take the rod [2]. Hold the mouse button, let go – whoosh, cast."
3. „Taucht der Schwimmer ab: klicken! Dann einholen – nicht zu stramm.“ / "Bobber goes under: click! Then reel in – not too tight."
4. „Fünf Fische. Ich zähl mit. Ich zähl immer mit.“ / "Five fish. I'm counting. I'm always counting."
* on the first catch: „Ein Fisch! Der hat bestimmt drei Follower.“ / "A fish! He's got at least three followers."
* on the first snapped line: „Zu stramm! Die Schnur ist auch nur ein Mensch.“ / "Too tight! The line's only human."
* on an old boot: „Ein Stiefel! Vintage. Verkauft sich trotzdem.“ / "A boot! Vintage. Still sells."

**Beat 2 – Markt** (5 fish caught)
1. „Wunderbar. Die liegen jetzt in deiner Kühlbox und denken über ihr Leben nach.“ / "Wonderful. They're in your cooler now, reflecting on their lives."
2. „Handy → Fischmarkt → Anbieten. Die Leute kaufen alles. Wirklich alles.“ / "Phone → Fish Market → List. People buy anything. Anything."

**Beat 3 – Flasche** (5 fish listed)
1. „Ka-tsching! Weiter angeln, bis du 100 $ hast.“ / "Ka-ching! Keep fishing until you've got $100." *(→ K.1: tank 100 $)*
2. „Dann im Chopper Shop die Pressluftflasche. Der Heli liefert. Meistens aufs Dach.“ / "Then buy the air tank in the Chopper Shop. The chopper delivers. Usually onto the roof."

**Beat 4 – Heli** (tank ordered; Horst waits at the helipad edge)
1. „Hörst du das? Nein? Ich auch nicht. Kommt aber gleich!“ / "Hear that? No? Me neither. It's coming though!"
2. „Paket am Landeplatz öffnen. Nicht schütteln.“ / "Open the crate on the helipad. Don't shake it."

**Beat 5 – Kamera** (crate opened)
1. „Jetzt das Wichtigste: CONTENT!“ / "Now the most important thing: CONTENT!"
2. „Kamera auf [1]. Unter Wasser filmt sie von selbst – alles, was schwimmt, zählt.“ / "Camera's on [1]. Under water it films by itself – everything that swims counts." *(→ K.2)*
3. „Fürs Sealog: rechte Maustaste, ranzoomen, Klick – Foto!“ / "For the Sealog: right mouse, zoom in, click – photo!" *(→ K.2)*
4. „Drei Arten fotografieren, gleich vorm Steg. Und: Luft im Blick!“ / "Photograph three species, right off the dock. And: watch your air!" *(→ K.2)*

**Beat 6 – Hochladen** (3 species filmed, player back on land/boat)
1. „Das war … WUNDERSCHÖN. Ich hab geweint. Innerlich.“ / "That was… BEAUTIFUL. I cried. On the inside."
2. „Handy → FinTube → Hochladen. Ein Video pro Tag – das liebt der Algorithmus.“ / "Phone → FinTube → Upload. One video a day – the algorithm loves that."
3. „Denk dir einen Titel aus – und ein Vorschaubild mit vielen Tieren bringt Klicks. Glaub mir, ich hab Ahnung.“ / "Make up a title – and a thumbnail with lots of animals gets clicks. Trust me, I know things." *(→ L.4)*

**Beat 7 – Schlafen** (uploaded)
1. „Die Aufrufe kommen über Nacht. Ins Bett darfst du ab 18 Uhr.“ / "Views come in overnight. Bedtime is from 6 pm."
2. „Bis dahin: Grill am Strand, Kokosnüsse, Insel erkunden.“ / "Until then: grill on the beach, coconuts, explore the island."
3. „Ich … beobachte Möwen. Professionell.“ / "I'll… observe seagulls. Professionally."

**Beat 8 – Erster Morgen** (day 2, first video resolved)
1. „Schau dir das an! Follower! Echte! Vermutlich!“ / "Look at that! Followers! Real ones! Probably!"
2. „FinTube zahlt sogar schon ein bisschen. Reich sind wir nächste Woche.“ / "FinTube even pays a little already. We'll be rich next week."
3. „Ich sitz meistens am Steg. Ruf einfach. Oder nicht.“ / "I'm usually on the dock. Just shout. Or don't."

**Beat 9 – Live gehen** (milestone 1,000 followers; Q21 "earn money when you go live")
1. „TAUSEND! Weißt du, was das heißt? KRILL.tv!“ / "A THOUSAND! You know what that means? KRILL.tv!"
2. „Du gehst live. Die Leute schauen zu, während du tauchst.“ / "You go live. People watch while you dive."
3. „Und sie spenden Geld! Für Mut. Für Haie. Für Unfälle.“ / "And they donate money! For courage. For sharks. For accidents."
4. „Stream starten auf dem Boot: Handy → KRILL.tv.“ / "Start the stream on the boat: phone → KRILL.tv."
5. „Aber: Wirst du ohnmächtig, ist der Stream weg. Und Follower auch.“ / "But: faint, and the stream's gone. Followers too."
6. „Kein Druck! Viel Druck. Wasserdruck. Haha. Los!“ / "No pressure! Lots of pressure. Water pressure. Ha! Go!"

Reward when the tutorial ends: hat „Horsts Köderhut“ unlocked + toast „Neuer Hut: Horsts Köderhut“ / "New hat:
Horst's lure hat".

---

## B. Story *(npc+story; props: story objects; ui: radio call card)*

### B.1 Uncle Gus on the radio – one call per milestone (≤ 20 s)

Gus: retired, never seen, only on the radio. Old sea dog, warm, self-ironic. Catchphrase „Bei Neptuns Socken!“ /
"By Neptune's socks!". Babble voice: 95 Hz, 8 syllables/s, radio band-pass 300–3,000 Hz + crackle, ends with a
"click". UI: radio card bottom-left (portrait silhouette + „Onkel Gus · Funk“ / "Uncle Gus · Radio") – the card counts
as the 1 allowed notification card (Q37); the milestone banner waits until the call is over. Calls never interrupt a
dive countdown: under water they queue until the player surfaces (unless the underwater radio is owned).

| Milestone | DE | EN |
|---|---|---|
| 0 (arrival, → K.5) | „Gus hier! Bist du gut gelandet? Wanda fliegt wie ich koche – mit Gefühl und Feuerlöscher.“ | "Gus here! Landed safely? Wanda flies like I cook – with feeling and a fire extinguisher." |
| 100 | „Gus hier, Funkstation Ruhestand! Hundert Follower? In deinem Alter hatte ich hundert Seepocken. Am Hintern. Weiter so!“ | "Gus here, retirement radio! A hundred followers? At your age I had a hundred barnacles. On my backside. Keep going!" |
| 1,000 | „Tausend! Horst hat mich dreimal angerufen. Du sollst live gehen, sagt er. Er sagt auch, er war mal Delfin. Glaub ihm nur das Erste.“ | "A thousand! Horst called me three times. Says you should go live. Also says he used to be a dolphin. Only believe the first part." |
| 2,500 | „Zweitausendfünfhundert! Das Wrack da draußen, die MS Clickbait … da war ich mal Smutje. Lange Geschichte. Kurze Hose. Schau mal rein.“ | "Twenty-five hundred! That wreck out there, the MS Clickbait… I was her cook. Long story. Short trousers. Have a look inside." |
| 5,000 | „Fünftausend – jetzt kommen Sponsoren. Tipp vom Profi: Nimm das Geld, nicht den lebenslangen Mayo-Vorrat.“ | "Five thousand – sponsors incoming. Pro tip: take the money, not the lifetime supply of mayo." |
| 10,000 | „Zehntausend! Der ganze Hafen redet von dir. Na gut, Horst redet. Aber laut.“ | "Ten thousand! The whole harbour's talking about you. Fine, Horst is talking. But loudly." |
| 25,000 | „Fünfundzwanzigtausend. Die Bottom-Brüder sind wieder in der Gegend. Wenn die Yacht anlegt: Fenster zu, Ohren zu.“ | "Twenty-five thousand. The Bottom brothers are back around. When that yacht docks: shut the windows, shut your ears." |
| 50,000 | „Fünfzigtausend! Ein Doku-Sender hat angerufen. Ich hab gesagt, du bist beschäftigt. Wichtig wirken, Kind!“ | "Fifty thousand! A documentary channel called. I said you're busy. Look important, kid!" |
| 100,000 | „Hunderttausend. Sealebrity! Und … ich hör Signale aus dem Graben. Piepen. Wie ein alter Fernseher. Komisch, oder?“ | "A hundred thousand. Sealebrity! And… I'm hearing signals from the trench. Beeping. Like an old telly. Odd, eh?" |
| 250,000 | „Viertelmillion! Ich hab mir extra einen Fernseher gekauft. Baujahr 1987. Du bist sehr grün.“ | "Quarter of a million! Bought a TV just to watch you. Made in 1987. You look very green." |
| 500,000 | „Halbe Million. Weißt du, warum ich dir die Insel vererbt hab? Ich auch nicht mehr. Beste Idee meines Lebens.“ | "Half a million. Know why I left you the island? Neither do I anymore. Best idea I ever had." |
| 1,000,000 | „Eine Million … bei Neptuns Socken. Da unten wartet etwas auf dich. Etwas Großes. Geh live. Ich schau zu.“ | "A million… by Neptune's socks. Something's waiting for you down there. Something big. Go live. I'm watching." |

### B.2 Gus story missions (4)

Missions are shown in the phone app „Aufgaben“ under a new header „Gus' Aufträge“ / "Gus's jobs" (max 1 active; the
tutorial/long-term goal stays on top). State lives in shared `GameState.missions` (host authority).

| # | Title DE / EN | Trigger | Task | Reward |
|---|---|---|---|---|
| M1 | „Die Glücksflosse“ / "The Lucky Fin" | kelp forest unlocked + next morning | find Gus' golden fin between the kelp rocks within 60 m of the kelp landmark island (sparkle object at 14–18 m, props places it deterministically) | 400 $ + fins colour „Gold“ `#E8C65A` |
| M2 | „Funkstille“ / "Radio Silence" | wreck unlocked (2,500 followers) + next morning | find Gus' old radio in the galley of the MS Clickbait interior (Section H) | 1,000 $ + the „Unterwasser-Funk“ (Q71) costs half (1,250 $) |
| M3 | „Das Leuchtfeuer“ / "The Beacon" | message in a bottle found **or** 10,000 followers | collect 3 lens shards (lighthouse gallery, cave grotto, radio tower shack), place them at the lighthouse lamp (E) | 2,500 $ + suit pattern „Leuchtturm“; from now on the lighthouse beam at night points toward the trench (hint for the TV studio, Q64) |
| M4 | „Signal aus der Tiefe“ / "Signal from the Deep" | trench unlocked (100,000 followers) + next morning | film the old TV-station antenna mast on a ledge at 92 m in the trench for 5 s (the Big Eye's shadow passes behind it, unfilmable – scripted) | 10,000 $ + Sealog note; sets up the finale |

Dialogue (Gus on the radio; ≤ 6 lines each):

**M1 start:** „Kind, ich hab ein Geständnis.“ / "Kid, I've got a confession." · „1989 bin ich im Kelpwald gekentert. Mit Stil.“ / "In 1989 I capsized in the kelp forest. Stylishly." · „Dabei ging meine goldene Glücksflosse verloren.“ / "Lost my golden lucky fin." · „Seitdem fang ich nur noch Erkältungen.“ / "Since then I only catch colds." · „Bei der kleinen Insel am Kelpwald. Zwischen den Felsen. Bring sie heim!“ / "Near the little island by the kelp. Between the rocks. Bring her home!"
**M1 done:** „DA ist sie! Ich hör sie glänzen.“ / "THERE she is! I can hear her shine." · „Behalt sie. Steht dir besser. Mir stand sie nie.“ / "Keep her. Suits you better. Never suited me."

**M2 start:** „Die MS Clickbait war mein Schiff. Ich war Smutje.“ / "The MS Clickbait was my ship. I was the cook." · „Der Käpt'n wollte einen Rekord: schnellste Fahrt mit dem meisten Käse.“ / "The captain wanted a record: fastest run with the most cheese." · „Es lief … nicht gut.“ / "It went… not well." · „In der Kombüse liegt mein altes Funkgerät. Wenn's noch geht, kann ich dich unter Wasser hören!“ / "My old radio's in the galley. If it still works, I can hear you under water!"
**M2 done:** „Es piept! Es lebt! Wie ich.“ / "It beeps! It lives! Like me." · „Den Unterwasser-Funk im Shop gibt's jetzt zum halben Preis. Dann quatschen wir beim Tauchen.“ / "The underwater radio in the shop is half price now. Then we can chat while you dive."

**M3 start:** „Der Leuchtturm ist seit Jahren dunkel. Die Linse ist zersprungen.“ / "The lighthouse has been dark for years. The lens is cracked." · „Drei Scherben hab ich damals versteckt. Fragt nicht.“ / "I hid three shards back then. Don't ask." · „Ganz oben am Leuchtturm, in der Grotte, beim Funkturm.“ / "Top of the lighthouse, in the grotto, at the radio tower." · „Bau sie ein. Nachts zeigt das Licht dir was.“ / "Fit them back. At night the light will show you something."
**M3 done:** „Siehst du, wohin der Strahl zeigt? Zum Graben.“ / "See where the beam points? The trench." · „Da unten liegt was, das nicht dahin gehört.“ / "Something's down there that doesn't belong."

**M4 start:** „Das Piepen kommt aus dem Graben. Zweiundneunzig Meter.“ / "The beeping comes from the trench. Ninety-two metres." · „Eine Sendeantenne. Vom alten Fernsehstudio.“ / "A transmitter mast. From the old TV studio." · „Film sie. Und … wenn du was Großes siehst: nicht winken.“ / "Film it. And… if you see something big: don't wave."
**M4 done:** „Ein AUGE? So groß wie ein Bus?“ / "An EYE? Big as a bus?" · „Kind, das ist dein Finale. Aber erst bei einer Million. Die verdient man sich.“ / "Kid, that's your finale. But only at a million. You earn that one."

### B.3 Rivals: the Bottom brothers *(npc+story; props: yacht model)*

**Chad & Brad Bottomley**, channel **„Bottom Feeders“**, 2 million subscribers, show-off twins. Same bean as the
divers but spray-tanned (skin `#E8935A`), white captain outfits, gold chains, mirrored sunglasses; Chad has a
backwards cap, Brad a man bun. Always filming themselves with a selfie stick. Babble: Chad 120 Hz "bro" cadence 12
syll/s, Brad 165 Hz, they finish each other's sentences. Yacht **„Clout Nine“**: 24 m low-poly white yacht
(`#EDE6DA`, gold stripe `#E8C65A`, tinted windows `#2E3140`, a hot tub on deck, giant inflatable flamingo), docks on
the **south** side of the dock, parallel, centre `(DockStart.x + 14, 0, DockStart.z − 14)` (spans x 128–152, ends 6 m
before the dock end). The player's boat spawns on the north side (`BoatSpawn`) and the fishing cone (20 m east of the
dock end, ±25°) stays free. Berths (props places a bollard + fender pair at each): **A** = Dr. Fin's RV Plankton,
centre `(DockStart.x + 10, 0, DockStart.z − 7)`; **B** = Clout Nine as above.

| Beat | Trigger | What happens |
|---|---|---|
| R1 „Besuch von der Clout Nine“ | 1,000 followers, next morning 09:00–13:00 | they dock, brag, start the **weekly follower duel** (Q19): the Social app shows both counts; each Monday (day % 7 == 1) the winner is announced; your win = +5 % views on next week's videos + banner „Duell gewonnen!“ / "Duel won!", loss = Chad's mocking message. Nothing else happens (Q58). |
| R2 „Gerald ist CGI!“ | 25,000 followers | they post that Gerald is fake. Task: film Gerald ≥ 20 s with quality ≥ 1.0 and upload it with an honest title the same day. Reward: that video +25 % followers, rivals lose the duel that week. |
| R3 „Köder-Skandal“ | 100,000 followers | the Clout Nine anchors at the wreck and dumps chum (illegal bait, GDD 13.6): +3 sharks, frenzy for 1 in-game hour. Task: photograph the yacht dumping (photo with the yacht in frame while the chum particles are active) and survive. Upload any title: the clickbait title gets no hater penalty this time. Reward: +40 % views on that video, achievement *Bottom erreicht*. |
| R4 „Wettlauf zum Auge“ | 500,000 followers | they try to reach the Big Eye first in their gold mini sub „Bottom Line“ and get stuck on a trench ledge at 85 m. Task: bring them air (E on the sub's valve, needs 30 % of your air) within 1 in-game day. Reward: they appear as friendly guests in the finale chat (ending flavour "Freunde" – Q78), 15,000 $. Ignoring it: they get rescued by Dr. Fin and appear as hecklers. |

Extra visits after R2: 10 % chance per day (11:00–15:00), they just brag (1–2 lines) and leave.

**R1 lines:** Chad: „Yo! Chad Bottomley. Bottom Feeders. Zwei Millionen Abos. Und du so?“ / "Yo! Chad Bottomley. Bottom Feeders. Two million subs. You?" · Brad: „Brad. Der Hübschere.“ / "Brad. The handsome one." · Chad: „Süße Insel. Süßes Boot. Ist das … Rost?“ / "Cute island. Cute boat. Is that… rust?" · Brad: „Follower-Duell. Jede Woche. Verlierer muss …“ / "Follower duel. Every week. Loser has to…" · Chad: „… verlieren. Genau. Clout Nine, ablegen!“ / "…lose. Exactly. Clout Nine, cast off!" · Brad: „Bye-bye, Plankton!“ / "Bye-bye, plankton!"

**R2 lines:** Brad: „Breaking News: deine Schildkröte ist CGI.“ / "Breaking news: your turtle is CGI." · Chad: „Viel zu gut animiert. Niemand ist so entspannt.“ / "Way too well animated. Nobody is that relaxed." · Horst (after): „Gerald ist echt! Ich hab ihn mal umarmt. Er war dagegen.“ / "Gerald is real! I hugged him once. He objected."

**R3 lines:** Chad: „Ein bisschen Köder hat noch keinem geschadet.“ / "A little chum never hurt anyone." · Brad: „Außer den Fischen. Und den Tauchern. Und dem Gesetz.“ / "Except the fish. And the divers. And the law." · after exposure – Chad: „Das war … ein Sozialexperiment!“ / "That was… a social experiment!" · Brad: „Wir löschen alles. Sofort. Gas!“ / "We delete everything. Now. Floor it!"

**R4 lines (radio, panicked):** Brad: „Äh. Hallo. Rein hypothetisch …“ / "Uh. Hi. Purely hypothetically…" · Chad: „… wenn zwei sehr gutaussehende Brüder feststecken würden …“ / "…if two very handsome brothers were stuck…" · Brad: „… auf 85 Metern …“ / "…at 85 metres…" · Chad: „… mit wenig Luft. Und viel Reue.“ / "…low on air. High on regret." · after rescue – Chad: „Okay. Du bist cool. Sag's keinem.“ / "Okay. You're cool. Don't tell anyone."

### B.4 Dr. Fin and research quests (Q23, Q24, Q29) *(npc+story; fishing+boat: fishing quest checks)*

**Dr. Marina Fin**, marine biologist. Bean with a lab coat over a wetsuit, huge round glasses (`#1E1B2B`, lenses
`#9FE4EC`), waterproof clipboard, pencil behind the ear, skin `#A8724A`. Research boat **„RV Plankton“** (small
grey-blue catamaran with an A-frame crane). Babble: 230 Hz, 13 syll/s, rising "hm?" intonation. Visits: from 300
followers, every 3rd day (day % 3 == 0) 08:30–13:00 at berth A (south side of the dock, B.3). Quests come by phone (app „Dr. Fin“,
icon microscope, `#3FA9B5`) – 3 open quests at a time, a new one replaces a finished one next morning.

Her first visit: „Hallo! Dr. Fin, Meeresbiologie. Ich hab Ihre Videos gesehen.“ / "Hello! Dr. Fin, marine biology. I've seen your videos." · „Wissenschaftlich gesehen … sehr wackelig.“ / "Scientifically speaking… very shaky." · „Aber Sie finden Tiere, die ich seit Jahren suche!“ / "But you find animals I've been looking for for years!" · „Ich bezahle für Daten. Fotos, Fische, Messungen.“ / "I pay for data. Photos, fish, measurements." · „Ach, und Ihre Angel … darüber reden wir noch.“ / "Oh, and your rod… we'll talk about that."

Quest pool (deterministic order per save seed; rewards = money; R = rod chain):

| id | DE / EN | Condition | Reward |
|---|---|---|---|
| fin_photo_ray | „Foto: Blaupunktrochen von oben“ / "Photo: bluespotted ray from above" | photo with the ray in focus, camera pitch < −45° | 120 $ |
| fin_photo_octo_light | „Foto: Krake im Lampenlicht“ / "Photo: octopus in lamplight" | photo, lamp on, octopus in focus | 180 $ |
| fin_film_garibaldi3 | „3 Garibaldis in einem Clip“ / "3 garibaldi in one clip" | clip with ≥ 3 garibaldi framed | 250 $ |
| fin_measure_bass | „Riesen-Wrackbarsch, Stufe Groß+“ / "Giant sea bass, Large or bigger" | film one ≥ Large | 600 $ |
| fin_photo_jelly_night | „Foto: Mondqualle bei Nacht“ / "Photo: moon jelly at night" | photo 21:00–04:00 | 300 $ |
| fin_photo_starfish | „Foto: Dornenkronen-Seestern“ / "Photo: crown-of-thorns starfish" | photo | 200 $ |
| fin_film_dolphins | „Delfine an der Bugwelle filmen“ / "Film dolphins on the bow wave" | 10 s clip while the boat runs > 6 m/s | 900 $ |
| fin_photo_goblin | „Foto: Koboldhai“ / "Photo: goblin shark" | photo | 6,000 $ |
| **R1** fin_fish_bream | „3 Brassen über 1,2 kg“ / "3 sea bream over 1.2 kg" | catch | 150 $ + rod_t1 −50 % |
| **R2** fin_fish_snapper_boat | „Roter Schnapper vom Boot, über 2 kg“ / "Red snapper from the boat, over 2 kg" | catch from the boat | 400 $ |
| **R3** fin_fish_tuna | „Gelbflossen-Thun, über 8 kg“ / "Yellowfin tuna, over 8 kg" | catch (open sea) | 1,200 $ |
| **R4** fin_fish_sword | „Schwertfisch für die Forschung“ / "A swordfish for science" | catch (open sea) | rod **„Forschungsrute“** (rod_t3, not sold) |

The rod chain R1→R4 is always offered in order (one R quest is always among the 3 open quests until R4 is done).

---

## C. Livestream on KRILL.tv *(livestream; ui: app + overlay; net: sync; core: LiveState contract)*

### C.1 Rules

* Unlocked at **1,000 followers** (Horst beat 9 + Gus call). App **„KRILL.tv“** (icon: pink krill on purple
  `#8E5CC2`). Button **„Live gehen“ / "Go live"**.
* Start only **on the boat** (Q81; the phone works only on land/boat, Q35). From **2,500 followers** (relay buoy,
  GDD §4) also from the dock.
* **One stream per in-game day** (like one video per day). Max length **10 real minutes** (the „Datenvolumen“ /
  "data plan" bar in the overlay). End: phone (on the boat/dock) „Stream beenden“ / "End stream", the timer, or
  fainting.
* Signal: the stream needs the boat within **150 m** (from 2,500 followers: 400 m). Beyond: „Kein Signal!“ /
  "No signal!" – viewers drain 4 %/s, after 20 s the stream ends normally.
* The stream uses the **hand camera** (it streams whenever it is in hand – recording is not needed; recording clips
  for uploads still works in parallel).

### C.2 Viewers and hype

```
baseViewers(F) = 10 + 0.55 × F^0.75                // F = followers: 1k → 108, 10k → 560, 100k → 3,100, 1M → 17,400
viewers       = round(baseViewers × hype × (0.9 + 0.2 × smoothNoise(t / 8 s)))
hype          ∈ [0.3, 3.0], start 1.0, moves toward target with τ = 12 s; spikes add instantly
target hype   = 0.6  nothing on camera for > 15 s ("boring")
              = 1.0  ≥ 1 animal detected (K.2 – was: framed with quality ≥ 0.4)
              = 1.0 + 0.004 × baseValue × tierMult (cap 2.2) for the best detected animal
              × 1.4 a dangerous animal (IsDangerous) within 5 m and on camera
              × 1.3 while a dare is running
spikes        +0.8 new species · +1.0 size record · +0.5 predator bump · +0.4 Legendary tier · +1.0 dare done · −0.3 dare failed
```

### C.3 Money and followers (balanced against uploads)

Uploads stay the main income; a good daily stream roughly doubles money and adds ~30 % followers.

```
Vref(F)               = clamp(150 + 135 × log10(F / 1000), 150, 700)          // typical day's upload clip value (econsim)
ExpectedUploadViews(F) = Vref(F) × ViewsPerValue(40) × Reach(F)
ExpectedUploadMoney(F) = MoneyFromViews(ExpectedUploadViews(F), F)
ExpectedUploadFollowers(F) = FollowersFromViews(ExpectedUploadViews(F), F, Honest)

money per second      = StreamMoneyFactor(0.8) × ExpectedUploadMoney(F) / 600 × hype / 1.3
                        → 60 % as a ticking "ad revenue" counter, 40 % as donations (random 1–8 % chunks)
followers per second  = StreamFollowerFactor(0.3) × ExpectedUploadFollowers(F) / 600 × hype / 1.3
                        → collected as "pending followers", credited only when the stream ends normally
```

Examples (10 min, average hype): 1k followers → ~70 $ + ~65 followers (an upload: ~87 $ / ~216); 100k → ~5,700 $ +
~1,300 followers (upload ~7,100 $ / ~4,300). Money from ads/donations is paid live (host `AddMoney` once per 5 s,
batched). Co-op: × `CoopUploadFactor(players)`; difficulty × `incomeMultiplier`.

**Balance check (core + livestream):** extend `scratchpad/econsim_core` with one stream per day (average hype 1.3,
1.5 dares, 10 % faint chance). Target solo 1,000,000 followers at **36–42 h** (Q47: 35–45 h). Tune only
`StreamFollowerFactor` (0.2–0.4) and `StreamMoneyFactor` (0.6–1.0).
**→ K.1 (done):** run in `scratchpad/econsim_k` together with K.1 and K.2: **`StreamFollowerFactor = 0.25`,
`StreamMoneyFactor = 0.8`**, `FollowerConversion` 0.009 → 0.0058 → 1M at 38.1 h solo.

### C.4 Fainting ends the stream (Q84)

Any faint of the streamer (solo) or of any crew member (co-op) ends the stream immediately:
* pending followers are lost, **plus** `loss = clamp(round(0.02 × F), 10, 25,000)` followers (shown big).
* money already paid is kept.
* overlay: „STREAM ABGEBROCHEN“ / "STREAM LOST", chat last line „F“, the KRILL.tv app shows „−{n} Follower“.
* Relaxed difficulty (drone, no fainting): the drone pull-up ends the stream the same way but the loss is 1 %.

### C.5 Dares – really risky, big money (Q83)

A dare is offered every 75–120 s when hype ≥ 0.8 and no dare runs; max **3 per stream**. Card at the left screen
edge: chat user + dare text + reward + timer. **[T] annehmen / accept** (new `GameAction.StreamDare`, default T) within
8 s, otherwise it expires (hype −0.1). Reward = `k × ExpectedUploadMoney(F)` (+ hype spike +1.0, followers bonus
+0.5 × the stream's pending followers so far).
Dare money per stream is capped at `1.5 × ExpectedUploadMoney(F)` (StreamRules.DareMoneyCap; the economy sim models
no dare income). The dwell rule of dare_three: each species must be framed ≥ 1 s; dare_school needs the diver outside
the school once after accepting; dare_dark is offered only with the lamp on at night or below 30 m and starts when the
lamp is switched off; dare_lowair only when 15 % is reachable in 25–90 s at the current air rate; the poke dares name
their key („Stups den Hai an! [E]“) and show „E – Anstupsen“ in reach.

| id | Risk | k | DE / EN | Success condition | Time |
|---|---|---|---|---|---|
| dare_starfish | low | 0.3 | „Stups einen Seestern an!“ / "Boop a starfish!" | E within 1 m of a starfish | 40 s |
| dare_three | low | 0.3 | „3 Arten in 30 Sekunden filmen!“ / "Film 3 species in 30 s!" | 3 different species framed | 30 s |
| dare_dark | low | 0.35 | „Lampe aus – 20 Sekunden im Dunkeln!“ / "Lamp off – 20 s in the dark!" | lamp off, depth > 10 m, 20 s | 45 s |
| dare_school | medium | 0.6 | „Schwimm mitten durch den Fischschwarm!“ / "Swim right through the school!" | pass within 1 m of a school's centre | 45 s |
| dare_moray | medium | 0.7 | „Muräne aus 1,5 m filmen!“ / "Film a moray from 1.5 m!" | moray framed at ≤ 1.5 m for 3 s | 60 s |
| dare_deep5 | medium | 0.8 | „5 m tiefer als erlaubt – 10 Sekunden!“ / "5 m deeper than allowed – 10 s!" | depth ≥ MaxSafeDepth + 5 for 10 s (Q26: air drains faster, warning) | 60 s |
| dare_lowair | medium | 0.8 | „Tauch erst unter 15 % Luft auf!“ / "Surface only below 15 % air!" | surface with air < 15 % (no faint) | 120 s |
| dare_shark | high | 1.5 | „Stups den Hai an!“ / "Poke the shark!" | E within 1.5 m of a shark; the shark turns aggressive (bump chance 70 % in the next 10 s) | 90 s |
| dare_deep15 | high | 1.6 | „15 m tiefer als erlaubt – 15 Sekunden!“ / "15 m deeper than allowed – 15 s!" | as above, air drain ×2.5 | 90 s |
| dare_wreck_dark | high | 1.4 | „Lampe aus im Wrack – 30 Sekunden!“ / "Lamp off inside the wreck – 30 s!" | inside the wreck interior volume, lamp off | 60 s |
| dare_angler | high | 1.8 | „Folge dem Licht des Anglerfischs!“ / "Follow the anglerfish's light!" | stay within 3 m of an anglerfish for 15 s | 90 s |

Dares are only offered when possible (species present at the site, depth available, wreck interior near). Co-op:
the dare names one player (rotating) – „{name} soll den Hai anstupsen!“ / "{name} has to poke the shark!" – that
player's machine checks the condition and reports to the host.

### C.6 Live chat (Q82) *(livestream: generator; ui: bubbles)*

Small bubbles at the **right screen edge** (bottom 55 %), max **4** visible, each 6 s, newest at the bottom; width
≤ 360 px, Fredoka 18 px, white bubble 85 % α with an ink outline, username in a mood colour. Rate: one message every
`clamp(4.0 − 0.35 × hype − 0.25 × log10(viewers), 1.2, 4.0)` s. Donations: gold bubble + coin sound + „+{amount} $“.
Messages never repeat within 60 s. Usernames (fictional): krabbenkalle, SeegurkenSusi, xX_Hai_Xx, Opa_Plankton,
bubbleboi, TintenTina, MuschelMax, algen_alex, FlossenFred, QuallenQueen, gerald_fanclub, DeepDiveDieter.

| Event | DE examples | EN examples |
|---|---|---|
| species on camera (`{s}` = species name) | „{s}!!“ · „ist das ein {s}?“ · „{s} sieht heute gut aus“ | "{s}!!" · "is that a {s}?" · "{s} looking good today" |
| big / record | „DER IST RIESIG“ · „REKORD!!!“ · „Maßband raus!“ | "HE'S HUGE" · "RECORD!!!" · "get the tape measure!" |
| new species | „NEU IM SEALOG!“ · „noch nie gesehen!“ · „Dr. Fin, schau her!“ | "NEW IN THE SEALOG!" · "never seen that before!" · "Dr. Fin, look!" |
| camera not in hand | „Kamera weg?“ · „wir sehen nur Blasen“ | "camera gone?" · "all we see is bubbles" |
| danger close | „hinter dir!!!“ · „NICHT ANFASSEN“ · „ich kann nicht hinsehen“ | "behind you!!!" · "DON'T TOUCH IT" · "I can't watch" |
| bumped / fail | „AUA“ · „hahaha“ · „Clip it!“ · „der Hai so: nö“ | "OUCH" · "hahaha" · "clip it!" · "shark said no" |
| low air | „Luft??“ · „tauch auf!!“ · „das wird knapp“ | "air??" · "go up!!" · "this is gonna be close" |
| boring | „…“ · „Sand. Spannend.“ · „mehr Fische bitte“ | "…" · "sand. riveting." · "more fish pls" |
| dare offered | „Mutprobe! Mutprobe!“ · „trau dich!“ | "dare! dare!" · "do it!" |
| donation | „{u} spendet {n} $: Mehr Hai!“ · „{u}: für die Luft-Rechnung“ | "{u} tips ${n}: more shark!" · "{u}: for the air bill" |
| Gerald on camera | „GERALD!!!“ · „König Gerald“ · „wir lieben dich Gerald“ | "GERALD!!!" · "King Gerald" · "we love you Gerald" |
| hater (clickbait history / disturbing animals, GDD 14.40) | „Clickbait-Kanal“ · „lass die Tiere in Ruhe“ | "clickbait channel" · "leave the animals alone" |

(no emoji glyphs – Fredoka does not have them)

### C.7 Overlay and app *(ui)*

* Top centre: red **„LIVE“** pill + eye glyph + viewer count („1.240“), below a thin data-plan bar (10 min).
* Right edge: chat bubbles. Left edge: dare card (when offered/running). Money ticker under the money line
  (bottom-right): „+{x} $ live“ in gold.
* End card (normal end): „Stream beendet“ / "Stream ended" · duration · peak viewers · „+{n} $“ · „+{f} Follower“ ·
  best moment („Rekord: Riesen-Muräne 2,31 m“).
* KRILL.tv app: big „Live gehen“ button (disabled with reason: „Nur auf dem Boot“ / "Only on the boat" – from 2,500
  followers „Nur auf dem Boot oder am Steg“ / "Only on the boat or the dock", „Heute schon gestreamt“ / "Already
  streamed today", „Ab 1.000 Followern“ / "From 1,000 followers"), last streams list.
* Camera not in hand during a stream: hype target 0.6 ("boring") and the chat asks for it (C.6).

### C.8 Co-op rules (host authority)

* One stream for the shared channel. Any player on the boat can start it (`CoreCommands.StreamStartKey`).
* `LiveState` is simulated on the host and replicated (net: `StreamState` 2 Hz reliable).
* **Every player's camera counts:** each client sends `StreamFeed` (2 Hz, unreliable): best framed species id,
  quality, tier, danger flag, in-water flag. The host takes the best feed per second for the hype target ("multi-cam":
  +0.1 target per extra camera showing an animal, cap +0.3).
* Chat and donations are generated on the host and sent as message ids + arguments (each machine localizes).
* Any player fainting ends the stream for all (C.4).

---

## D. Fishing 2.0 *(fishing+boat; core: FishMarket data & GearDatabase fields; ui: gauge)*

### D.1 Reel tension minigame (Q22)

After the strike (window stays 1.1 s) the reel phase starts:

* UI (ui): a thin semicircle gauge 220 × 110 px just above the hotbar centre: tension needle 0–100 %, zones
  **green 35–75 %**, yellow 75–90 %, red 90–100 %; under it a fish-stamina bar. Fish icon wiggles left/right with the
  current run direction.
* Controls: **hold Use (LMB) = reel**: tension +55 %/s. Release: tension −70 %/s.
* Runs: every `runInterval` the fish pulls for 0.6–1.4 s: tension `+runStrength`/s. Optional skill: holding the
  opposite direction (A/D) during a run reduces that run's pull by 40 %.
* Progress: `progress += reelPower × zoneFactor × dt / stamina` with zoneFactor green 1.0 / yellow 0.5 / red 0.2 /
  below green 0.3. Caught at progress ≥ 1.
* Fail: tension ≥ 100 % for `0.35 s × lineStrength` → **„Schnur gerissen!“ / "Line snapped!"** (fish lost); tension
  ≤ 5 % for 2.0 s → **„Abgehauen!“ / "Got away!"**. No other penalty.
* Difficulty: Relaxed green 25–85 % and snap time ×2; Normal as above; Hard green 40–70 %.

Per fish kind (FishMarket data):

| Kind | Table | Stamina (s) | runInterval (s) | runStrength (%/s) |
|---|---|---|---|---|
| boot | shore | 1.5 | – | – |
| sardine | shore | 2.0 | 3–4 | 30 |
| garfish (NEW) | shore | 2.5 | 2–3 | 40 |
| mackerel | shore | 3.0 | 2.5–3.5 | 40 |
| bream | shore | 4.0 | 2.5–3.5 | 45 |
| snapper | shore + sea | 5.0 | 2–3 | 55 |
| bonito (NEW) | sea | 5.5 | 2–3 | 55 |
| tuna | sea (moved off the shore table) | 8.0 | 1.8–2.6 | 70 |
| amberjack (NEW) | sea | 9.0 | 1.8–2.6 | 75 |
| mahi | sea (moved off the shore table) | 10.0 | 1.5–2.3 | 75 |
| swordfish (NEW) | sea | 13.0 | 1.5–2.2 | 85 |
| marlin „Kapitän Pieks“ (NEW, legendary) | sea, needs rod_t2+ | 18.0 | 1.2–2.0 | 95 |

### D.2 Boat fishing on the open sea (Q23)

* **Open-sea table** when the bobber lands in water ≥ 12 m deep and ≥ 60 m from any shore (main island, landmark
  islands) – typically from the boat. Otherwise the shore table.
* With the bamboo rod (rod_t0) the open sea uses the shore table + toast once per day „Die Bambusrute ist zu schwach
  für die Hochsee.“ / "The bamboo rod is too weak for the open sea."
* Bite delay open sea: 20–45 s ÷ rod fishSpeed (shore stays 12–30 s).

New / changed fish kinds (weights kg, price $/kg → typical value):

| id | DE / EN | Table | Rarity / chance | kg | $/kg | ≈ $ | rareBias |
|---|---|---|---|---|---|---|---|
| garfish | Hornhecht / Garfish | shore | common 0.12 | 0.4–1.0 | 10 | 7 | 0 |
| bonito | Bonito / Bonito | sea | common 0.35 | 2–5 | 8 | 28 | 0 |
| tuna | Gelbflossen-Thun / Yellowfin Tuna | sea | uncommon 0.22 | 4–12 | 7 | 56 | 1.5 |
| amberjack | Bernsteinmakrele / Amberjack | sea | uncommon 0.18 | 5–15 | 6 | 60 | 1.5 |
| mahi | Goldmakrele / Mahi-Mahi | sea | rare 0.12 | 6–15 | 9 | 94 | 2.5 |
| snapper | Roter Schnapper / Red Snapper | shore + sea | uncommon (shore 0.10, sea 0.08) | 1–3 | 9 | 18 | 1.2 |
| swordfish | Schwertfisch / Swordfish | sea | epic 0.04 | 20–60 | 5 | 200 | 4 |
| marlin | Blauer Marlin „Kapitän Pieks“ / Blue Marlin "Captain Poke" | sea | legendary 0.01 (rod_t2+) | 80–120 | 6 | 600 | 6 |

Shore table chances: boot 0.05, sardine 0.30, garfish 0.12, mackerel 0.28, bream 0.15, snapper 0.10. The first 5
catches of a save are never junk (as today).

### D.3 Rods (Q24: farther + rarer, faster, Dr. Fin quests)

New GearItem fields (core): `castMax`, `reelPower`, `greenBonus` (widens green zone each side, %-points),
`lineStrength`.

| Rod | Price / unlock | cast (m) | bite ÷ | luck | reelPower | greenBonus | lineStrength | Note |
|---|---|---|---|---|---|---|---|---|
| rod_t0 Bambusrute / Bamboo Rod | start | 4–17 | 1.0 | 0 | 1.0 | 0 | 1.0 | shore only |
| rod_t1 Carbon-Angel / Carbon Rod | 350 $ | 4–20 | 1.35 | 0.35 | 1.15 | +3 | 1.25 | open sea allowed; Dr. Fin R1 = −50 % |
| rod_t2 Hochsee-Rute / Deep Sea Rod | 2,500 $, 1,000 F | 5–24 | 1.7 | 0.7 | 1.3 | +5 | 1.5 | marlin possible |
| rod_t3 Forschungsrute / Research Rod | Dr. Fin R4 (not sold) | 6–28 | 2.0 | 1.0 | 1.45 | +7 | 1.8 | "rare bite" ping: the bobber glows `#F2C94C` when a rare+ fish bites |

### D.4 Economy targets (Q21, Q45)

> **→ K.1: superseded by the second player message (g).** Tank 100 $, shore prices × 1.25, money from video 1 at 4 $
> per 1,000 views with a starter boost, 50 $ starter bonus, local sponsor deals, finds and side quests – verified in
> `econsim_k`. The lines below are the round-2 baseline.

* Shore average ≈ **8 $/fish** (table D.2: 0.05·1 + 0.30·4.4 + 0.12·7 + 0.28·7.7 + 0.15·11.5 + 0.10·18), ≈ 2 fish per
  real minute incl. the minigame (bite 21 s + cast/reel/land ≈ 9 s) → the 130 $ tank after ≈ 8 min of fishing; with
  Horst's intro and the market delays the **first tank is ordered at ≈ 10 min** (Q21). Verify in the econsim
  (`econsim_core`, "First tank" line: target 9–11 min); if it is off, tune only the shore `pricePerKg` values.
* **Q45 – first video already pays:** `MonetizationFollowers` 100 → **0**; below 100 followers the rate is
  **2 $ / 1,000 views** (from 100: 4 $ as today); plus a one-time **„Creator-Startbonus“ 25 $** with the first resolved
  video (GameState `firstVideoBonusPaid`). First video (~5,500 views) ≈ 11 $ + 25 $ = 36 $. Toast: „FinTube zahlt:
  +{n} $ (inkl. 25 $ Startbonus)“ / "FinTube pays: +${n} (incl. $25 starter bonus)".

---

## E. Survival light – bonus, never penalty (Q73–Q75, Q27, Q71) *(core: data/buffs; props: grill, palms, fridge, compressor; fishing+boat: heli snacks, boat compressor; ui: panels)*

No hunger or thirst meter. Food gives **buffs**; not eating has no downside. Max **1 food buff + 1 drink buff** at a
time (a new one of the same category replaces the old one). Buff icons (24 px) sit left of the air ring with the
minutes left. Buffs are per player (never synced, lost on returning to the menu).

### E.1 Food sources

| Source | Where / how | Result |
|---|---|---|
| **Grill** | 2 grills (props): the campfire in front of the hut (`PropInfo.Campfire`) and the second beach. E: „E – Makrele grillen“ / "E – Grill mackerel" (the prompt names the fish) → takes the CHEAPEST non-junk fish from the shared cooler (the cheapest fish ≥ 3 kg instead when it is worth at most 1.5× the cheapest – big-fish bonus; never the best catch, Q73), host command `GrillKey`, 6 s sizzling (smoke, sound), eaten at once (packed to go for Toni: no buff) | food buff *Gegrillter Fisch* |
| **Coconut palms** | 24 palms (deterministic: the 24 palms closest to the hut, dock and both beaches with visible coconut clusters). E: „E – Palme schütteln“ / "E – Shake the palm" → coconut drops (bounce), E: „E – Trinken“ / "E – Drink". 1 coconut per palm per in-game day (shared `GameState.dailyUsed`) | drink buff *Kokosnuss* |
| **Snacks by chopper** (Q74) | Chopper Shop tab „Snacks“ – delivered like gear (always the express 45 in-game minutes, standard price), crate → **shared pantry**. Eat at the hut fridge („Kühlschrank“, new prop) or the boat's cooler box: E opens a small list (icon, name, count, buff) – click to eat | see table |

### E.2 Food table (`Data/FoodDatabase.cs`)

| id | DE / EN | Kind | Price / unlock | Buff | Value | Duration (real min) |
|---|---|---|---|---|---|---|
| grilled_fish | Gegrillter Fisch / Grilled Fish | food | grill | AirCapacity | +15 % (+20 % if the fish ≥ 3 kg) | 10 |
| coconut | Kokosnuss / Coconut | drink | palm | SwimSpeed | +10 % | 6 |
| seaweed_chips | Algen-Chips / Seaweed Chips | food | 20 $ | AirCapacity | +10 % | 8 |
| krill_bar | Krill-Riegel / Krill Bar | food | 35 $ | SwimSpeed | +20 % | 6 |
| galley_coffee | Kombüsen-Kaffee / Galley Coffee | drink | 30 $ | SteadyHands | camera shake −40 %, bump shake −50 % | 8 |
| fish_sandwich | Fischbrötchen / Fish Sandwich | food | 60 $, 2,500 F | AirCapacity | +25 % | 10 |
| shark_gummies | Hai-Gummis / Shark Gummies | food | 50 $, 5,000 F | BumpResist | predator bump air loss −50 % | 8 |
| glow_soda | Leucht-Limo / Glow Soda | drink | 80 $, 25,000 F | NightEyes | deep residual light +50 % (Q10) | 6 |

Snacks come in packs of 3. Bonus „Gemütlich“ / "Cozy": if you grilled a fish that day and sleep in your bed, the
next upload gets +5 % views (toast at wake-up: „Gut gegessen, gut geschlafen: +5 % Aufrufe heute“ / "Ate well, slept
well: +5 % views today").

### E.3 Sleeping from 18:00 (Q75) *(props: bed; core: rule)*

* Bed interaction only when `timeOfDay ≥ 0.75` (18:00) or `< MorningTime` (06:29). Otherwise the prompt reads
  „Schlafen ab 18:00“ / "Sleep from 6 pm" and E does nothing.
* Tutorial step 8 detail: „Ab 18:00 – bis dahin grillen oder erkunden.“ / "From 6 pm – grill or explore until then."
  (Horst beat 7 covers the wait; from a typical 15:00 upload that is 5 real minutes.)
* Co-op: ~~one sleeps = all sleep~~ **→ L.9: everybody must lie in a bed or hammock – „1/2 · 2/2“.**

### E.4 Air refill rules (Q27) *(player: Air; props: compressor stations; fishing+boat: boat)*

| Where | Refill |
|---|---|
| **Hut zone** (≤ 18 m from `WorldLayout.Hut`) and **on the dock** (deck or ≤ 4 m from the dock line) | free, automatic as today (0.6 s delay, 5 s to full) – Gus' old compressor stands next to the hut (prop with a hose to the dock) |
| **Boat** | only with **„Boot-Kompressor“ / "Boat Compressor"** (GearSlot.Compressor, **600 $, 250 followers**), automatic on deck, 6 s to full |
| **Landmark islands** (Q54) | rest spot with an old air station: E „E – Luft auffüllen“ / "E – Refill air", 4 s, free |
| Anywhere else on land | no refill; one toast per dive: „Keine Luftstation hier – Hütte, Steg oder Boot-Kompressor.“ / "No air station here – hut, dock or boat compressor." |
| No tank (snorkel) | breath refills at the surface as today |

Shop text: „Boot-Kompressor“ – „Füllt deine Flasche direkt an Bord.“ / "Refills your tank right on board."
Save migration (GameState v2 → v3, core): saves with ≥ 250 followers get the boat compressor for free once, toast
„Geschenk von Gus: Boot-Kompressor“ / "A gift from Gus: boat compressor" – nobody loses boat refills they had before.

### E.5 Underwater radio (Q70, Q71)

GearItem **uw_radio „Unterwasser-Funk“ / "Underwater Radio"**, GearSlot.Radio, **2,500 $**, in the shop once the
wreck is unlocked (2,500 followers); Gus mission M2 halves the price (1,250 $). Effect: voice under water reaches **200 m** with a radio
filter (band-pass 400–3,000 Hz, light crackle) instead of 10 m muffled; Gus calls and Horst lines reach you under water.
Desc: „Quatschen beim Tauchen. Krächzt ein bisschen.“ / "Chat while diving. Crackles a bit."

---

## F. Co-op *(net + player; core: data model; ui: dialogs)*

### F.1 Rescue: tow a fainted friend (Q65)

* In a session (≥ 2 players) on Normal/Hard, running out of air under water does **not** faint at once: the diver
  becomes **unconscious** for 30 s (Normal) / 20 s (Hard): no control, floats up at 0.25 m/s, slumped pose, bubbles
  stop, screen dark-red vignette with „Bewusstlos – ein Freund kann dich retten!“ / "Unconscious – a friend can save
  you!" and a countdown.
* Any other diver within 2 m: „E – Abschleppen“ / "E – Tow" → the unconscious diver is attached 1.2 m behind the tower;
  tower swim speed ×0.7, tower air use ×1.2. E again releases.
* The unconscious diver reaching depth < 0.8 m (surface) → **revived**: control back, 30 % air (or full breath), keeps
  this dive's clips, no rescue fee. Toasts: „{name} hat dich gerettet!“ / "{name} saved you!" and „Gerettet!“ /
  "Rescued!" for the tower. Achievement *Rettungsschwimmer*.
* Countdown over (or every diver unconscious) → normal faint (footage lost, fee) as today.
* Solo: unchanged. Relaxed: drone as today.
* Net: the unconscious player's machine owns its body; `TowState` (any → host → all) with tower id / target id;
  the target follows the tower's avatar stream + offset; revive is decided on the target machine.

### F.2 Host leaves (Q66) *(ui dialog; net flow)*

Host pause menu „Speichern & Sitzung beenden“ and „Spiel beenden“ while guests are connected → dialog:
„Alle werden getrennt. Wirklich beenden?“ / "Everyone will be disconnected. Really end?" with „Abbrechen“ / "Cancel"
and „Beenden“ / "End" (danger style). Guests then see: „Der Host hat die Sitzung beendet. Dein Fortschritt ist beim
Host gespeichert.“ / "The host ended the session. Your progress is saved with the host."

### F.3 Guests keep everything (Q67) – per-player data in the host save

* Every machine has a stable random key: `Settings.Data.playerKey` (GUID, created once; never the Steam ID, never
  logged). Sent in Hello.
* Host save: `GameState.players: List<PlayerRecord>`:

```csharp
[Serializable] public class PlayerRecord {
    public string key;              // Settings.Data.playerKey of that machine
    public string name;             // last used name
    public DiverLook look;
    public string[] hotbarRight, hotbarLeft;
    public List<string> equipped = new List<string>();
    public List<string> achievements = new List<string>();
    public int fishCaught, speciesFilmed, rescues, dives, faints;
    public float playMinutes;
    public int lastSeenDay;
}
```

* The host's own record uses its own key. On Welcome the host sends the matching record; the guest applies hotbar,
  equipment choice and stats (`GameState.CopyPerPlayerFrom` + record). On PlayerLeft and every 60 s the host updates
  the record and saves. The guest's `coop_player.json` stays as offline fallback.
* Shared things (money, followers, Sealog, items, cosmetics unlocked by the channel) are shared anyway.

### F.4 More players = more danger (Q68) *(fauna)*

| Players | Predator count | Predator aggression | Jelly count | Bump air loss |
|---|---|---|---|---|
| 1 | ×1.0 | ×1.0 | ×1.0 | ×1.0 |
| 2 | ×1.25 | ×1.1 | ×1.2 | ×1.0 |
| 3 | ×1.5 | ×1.2 | ×1.4 | ×1.1 |
| 4 | ×1.75 | ×1.3 | ×1.6 | ×1.2 |

(on top of `DifficultySettings.predatorAggression`). Co-op spots for two (Q56): heavy door in the wreck hold (two
players press E within 3 s – solo detour through a vent), a stuck chest at the kelp island (two pull).

### F.5 Voice (Q69, Q70) *(core: Settings default; net: NetVoice)*

* Default voice mode **Offenes Mikro / Open mic** for new settings; one-time migration in Settings version 3:
  `PushToTalk` → `OpenMic` (the player never chose PTT). Noise gate −45 dBFS, 300 ms hang time.
* Ranges: above water 2–30 m linear (as today). **Under water (speaker or listener): 2–10 m, muffled.** With the
  underwater radio (owned by the shared crew, equipped per player): 200 m, radio filter, also to players on the boat.

### F.6 Achievements for everyone present (Q94) *(core data; net broadcast)*

Data only this round (Steam later). `Achievements.Unlock(id)` on the host → stored in `GameState.achievements` and
every connected player's `PlayerRecord`, broadcast so each machine shows the toast („Erfolg: {name}“ / "Achievement:
{name}", trophy glyph, counts as the notification card) and writes its own `achievements.json`.

| id | DE / EN | Condition |
|---|---|---|
| wet_feet | Nasse Füße / Wet Feet | first dive below 2 m |
| first_fish | Petri Heil / Gone Fishin' | first fish |
| boot | Stiefelkönig / Boot Scootin' | catch an old boot |
| first_upload | Content Creator | first upload |
| f100 | Kleiner Fisch / Small Fry | 100 followers |
| f1k | Krill-Star / Krill Star | 1,000 followers |
| f10k | Riffhai / Reef Shark | 10,000 followers |
| f100k | Sealebrity | 100,000 followers |
| f1m | Wal-Ikone / Whale of a Time | 1,000,000 followers |
| gerald_selfie | Gerald & ich / Gerald & Me | selfie with Gerald |
| lifeguard | Rettungsschwimmer / Lifeguard | tow a friend to the surface |
| live_fail | Live-Panne / Live Fail | faint during a stream |
| hai_five | Hai-Five | complete „Stups den Hai an!“ |
| legendary | Legendär! / Legendary! | film a Legendary-tier animal |
| golden_camera | Goldene Kamera / Golden Camera | Sealog 100 % (Q52) |
| shells_all | Muschelmillionär / Shell Shocked | all 40 golden shells |
| pages_all | Gus' Biograf / Gus's Biographer | all 24 logbook pages |
| roof_delivery | Lieferung aufs Dach / Roof Delivery | a crate lands on the hut roof |
| light_fingers | Lampenfieber / Light Fingers | an octopus steals your lamp |
| coconut10 | Kokosnuss-Junkie / Coconut Crazy | drink 10 coconuts |
| grill_master | Grillmeister / Grill Master | grill 20 fish |
| tuna | Thunfisch-Tycoon / Tuna Tycoon | catch a tuna |
| captain_poke | Kapitän Pieks / Captain Poke | catch the marlin |
| snap | Schnur gerissen / Snap! | lose 5 fish to snapped lines |
| clickbait10 | Clickbait-Baron / Clickbait Baron | 10 clickbait titles |
| honest10 | Ehrliche Haut / Honest Fin | 10 honest titles |
| night_owl | Nachtschwärmer / Night Owl | dive between 00:00 and 04:00 |
| rock_bottom | Tiefer geht's nicht / Rock Bottom | reach 95 m |
| big_bertha | Big Bertha! | film the humpback whale |
| bow_riders | Bugwelle / Bow Riders | dolphins ride your bow wave |
| bottomed_out | Bottom erreicht / Bottomed Out | expose the Bottom brothers (R3) |
| horst_overboard | Horst im Hafenbecken / Horst Overboard | see Horst fall off the dock |
| full_crew | Volle Crew / Full Crew | play with 4 divers |
| live_from_the_deep | Live aus der Tiefe / Live from the Deep | finish the finale |

---

## G. Fauna (Q13, Q15, Q16, Q68) *(fauna; core: enums/fields)*

### G.1 New species (SpeciesDef entries in `Data/SpeciesDatabase.cs`, owned by fauna after core adds the fields)

New fields/enums (core, phase 0): `SpeciesDef.spawnZone` (`SpawnZone { Site, TidePool, Beach, OpenSea, Seafloor }`,
default Site); `BodyPlan` += `Crab, Starfish, Dolphin, Whale`; `BehaviorType` += `Playful, Crawler`.
Sealog count 25 → **34**. Page sizes: House Reef 10 → 15 (60 % = 9), Blue Drop 3 → 5 (3), Trench 3 → 5 (3).
**→ L.1:** `spawnZone` is replaced by `zones` (open-world zones) + depth bands + gates for all species; the "Site /
zone" and "Depth" columns below are superseded by the L.1 table (sizes, looks and behaviours stay). With L.1/L.11/K.8
the Sealog has **55** species (house reef page 16, 60 % = 10).

| id | Names DE / EN (latin) | Site / zone | Depth m | Size cm | Rarity | Base | Plan / variant | Behaviour, group, speed | colorA / B / C, pattern, glows | Body h / w / fin |
|---|---|---|---|---|---|---|---|---|---|---|
| ghost_crab | Geisterkrabbe / Ghost Crab (*Ocypode ceratophthalmus*) | house_reef / Beach (sand above the waterline, dusk–dawn 18:00–08:00, 6 per beach) | −1.5–0 (negative = above sea level) | 3–9 | Common | 8 | Crab / "ghost" (stalk eyes) | Crawler, 1–3, 1.2 (sprints sideways, digs in when < 3 m) | `#E8D9B4` / `#F4EFE6` / `#2B2533`, Solid | 0.35 / 1.1 / 1 |
| hermit_crab | Einsiedlerkrebs / Hermit Crab (*Dardanus megistos*) | house_reef / TidePool + reef bed ≤ 4 m | 0–4 | 4–14 | Common | 10 | Crab / "hermit" (spiral shell) | Crawler, 1–4, 0.3 (hides in the shell when touched/filmed close < 0.8 m for 3 s) | `#D9455A` / `#E2C890` shell / `#F2C94C`, Spots | 0.8 / 0.9 / 1 |
| sally_crab | Rote Klippenkrabbe / Sally Lightfoot Crab (*Grapsus grapsus*) | house_reef / TidePool rocks, tide-pool rims | 0–2 | 5–9 | Uncommon | 18 | Crab / "flat" | Skittish, 1–3, 1.6 (jumps between rocks) | `#E2552E` / `#F28C4A` / `#3FA9B5`, Mottled | 0.25 / 1.2 / 1 |
| blue_starfish | Blauer Seestern / Blue Sea Star (*Linckia laevigata*) | house_reef / Seafloor | 1–12 | 15–30 | Common | 7 | Starfish / "slim" (5 long arms) | Crawler, 1–2, 0.02 | `#2F6FD6` / `#4F8FE6` / `#1E3A8A`, Solid | 0.08 / 1.0 / 1 |
| crown_star | Dornenkronen-Seestern / Crown-of-Thorns Starfish (*Acanthaster planci*) | house_reef / Seafloor on corals | 3–12 | 25–40 | Uncommon | 22 | Starfish / "thorns" (13 arms, spikes) | Crawler, 1, 0.02; touching = jelly-style sting (short pain gag, no damage, GDD 14.35) | `#7A3E8E` / `#D9455A` / `#F2C94C`, Spots | 0.15 / 1.0 / 1 |
| dolphin | Großer Tümmler / Bottlenose Dolphin (*Tursiops truncatus*) | blue_drop / OpenSea (between the island and every outer site; pods appear near the boat when it runs > 6 m/s in water > 20 m and bow-ride for 20–40 s) | 0–25 | 200–400 | Uncommon | 110 | Dolphin | Playful, 3–6, 2.2 (jumps at the surface, circles divers, clicks) | `#7D8A96` / `#D9DEE2` / `#4E5A63`, BellyGradient | 0.22 / 0.22 / 1.1 |
| humpback | Buckelwal „Big Bertha“ / Humpback Whale "Big Bertha" (*Megaptera novaeangliae*) | blue_drop / OpenSea within the site + 300 m | 0–40 | 1100–1600 | Epic | 900 | Whale | Wanderer, 1, 0.12; sings (audio Q12); 1 breach per in-game hour visible from the boat | `#2B3440` / `#E8E4DC` mottled belly / `#1E2430`, Mottled | 0.2 / 0.25 / 1.4 (long pectorals) |
| goblin_shark | Koboldhai / Goblin Shark (*Mitsukurina owstoni*) | deep_trench / Site | 82–98 | 250–620 | Rare | 400 | Shark / "goblin" (long flat snout, protruding jaw) | Ambush, 1, 0.5; jaw shoots out 0.3 m when the diver is < 3 m (scare + bump) | `#D99A9A` / `#F0C4C0` / `#8A4A52`, BellyGradient, glows (eyes) | 0.13 / 0.12 / 1 |
| gulper_eel | Pelikanaal / Gulper Eel (*Eurypharynx pelecanoides*) | deep_trench / Site | 80–98 | 40–100 | Rare | 260 | Eel / "gulper" (mouth balloons to 3× when close) | Drifter, 1, 0.3; glowing tail tip | `#14121E` / `#2E2A40` / `#FF6EC7` tail, Solid, glows | 0.08 / 0.07 / 1 |

Fun facts:

| id | DE | EN |
|---|---|---|
| ghost_crab | Rennt bis zu 4 m pro Sekunde – seitwärts. Und knurrt dabei mit dem Magen. | Runs up to 4 m per second – sideways. And growls with its stomach. |
| hermit_crab | Zieht um, wenn das Haus zu klein wird – manchmal stehen sie in einer Warteschlange nach Größe. | Moves house when it gets too small – sometimes they queue up by size. |
| sally_crab | Springt von Fels zu Fels und putzt Meerechsen die Zecken weg. | Hops from rock to rock and picks ticks off marine iguanas. |
| blue_starfish | Kann einen verlorenen Arm nachwachsen lassen – manchmal wächst aus dem Arm ein ganzer neuer Stern. | Can regrow a lost arm – sometimes the arm grows into a whole new star. |
| crown_star | Frisst Korallen und hat giftige Stacheln. Filmen ja, kuscheln nein. | Eats coral and has venomous spines. Film yes, cuddle no. |
| dolphin | Hat für jeden Freund einen eigenen Pfiff – eine Art Namen. | Has a signature whistle for every friend – a kind of name. |
| humpback | Singt Lieder, die stundenlang dauern. Big Bertha singt hauptsächlich falsch. | Sings songs that last for hours. Big Bertha mostly sings off-key. |
| goblin_shark | Schießt seinen ganzen Kiefer nach vorne. Ein lebendes Fossil mit Schreckfaktor. | Catapults its whole jaw forward. A living fossil with a fright factor. |
| gulper_eel | Das Maul ist größer als der ganze restliche Fisch. Der Schwanz leuchtet rosa. | Its mouth is bigger than the rest of the fish. Its tail glows pink. |

Mesh notes (Art Bible §2.5, low poly, faceted): crabs 120–260 tris, 8 legs (2-segment), claws, stalk eyes with
googly sclera; starfish 60–160 tris, puffy flat star, 2 tones; dolphin 500–700 tris, smooth back, cartoon eye
(sclera small); whale 700–800 tris, knobbly head, long white pectoral fins; goblin shark = shark plan variant with a
0.35-length snout plate and a jaw part that can slide; gulper = eel variant with a huge hinged mouth bag. Spawning:
crabs and starfish never swim; crabs on the beach are visible from land (filmable on land – the hand camera already
works above water).

### G.2 Deep creatures glow a little (Q13)

Emission floor (replaces the Art Bible §2.5 numbers below 35 m, keeps them above): `emission = albedo × lerp(0.18,
0.32, (depth − 35) / 60)` **independent of daylight**, plus for `glows` species a ±15 % pulse at 0.5 Hz on colorC.
Target ▣: the lit side of any creature at 60 m without lamp ≥ `#2E4A66`, a glowing species' accent ≥ `#9A5AB8`.

### G.3 Mischief (Q16) – occasional and funny

Max 1 mischief per player per 3 real minutes, 25 % chance when the condition holds; each ends with a gag, never with
damage.

| Kind | Condition | What happens |
|---|---|---|
| octopus_lamp | octopus within 2 m, lamp on | it grabs the lamp: lamp off and gone from the hand for 6 s (an ink puff, the octopus swims 3 m with a light in its arms), then drops it back into your hand; chat/toast „Die Krake hat deine Lampe geklaut!“ / "The octopus stole your lamp!"; achievement *Lampenfieber* |
| fish_nibble | school within 1.5 m, player still for 3 s | small fish nibble the gloves: 0.4 s camera wiggle, tiny bubbles, „Hihi, das kitzelt!“ / "Hehe, that tickles!" |
| sealion_bump | sea lion within 4 m | Sir Flapsalot bumps you playfully (no air loss), steals your hat for 5 s (hat floats off your avatar, then back) |
| gerald_photobomb | Gerald within 6 m while you film another animal | Gerald swims right in front of the lens for 2 s (the clip gets +0.2 action and Gerald is credited) |
| parrot_poop | parrotfish within 3 m | a small sand cloud („…Papageifisch-Sand“ / "…parrotfish sand") – 0.5 s white puff |
| crab_pinch | hermit/ghost crab, player standing on it | pinch: „Au! Zwick!“ / "Ow! Pinch!", the crab runs off |

### G.4 Co-op danger multipliers

See F.4 (fauna owns the implementation in FaunaSpawner).

---

## H. World / art (Q1–Q12, Q41–Q44, Q51, Q53, Q54, Q61–Q63) *(owners per row)*

| Q | Spec | Owner |
|---|---|---|
| Q1/Q2 | Hands and items first: all held items **×0.85** of the Art Bible §1.13 size (one-hand item ≤ 210 × 230 px at 1080p), glove colour lighter **`#4A4F66`** (palm pads `#3A3E52`) **everywhere** incl. the phone hand, cuffs from `DiverLook.cuff`; items +10 % value (lighter). No skin-coloured hand anywhere. | player (camera models: livestream) |
| Q3 | **SSAO off** on all quality tiers (renderer feature removed in `SealebritiesSetup`, `FXGraphics` no longer toggles it). Kills the grainy wall noise. | world+fx |
| Q4 | Wide turquoise lagoon (terrain profile in 1a). Ocean ramp: 0–0.6 m `#4FB8C8` α 0.55 · 1.5 m `#46ADC4` α 0.70 · 3.5 m `#3D9DBE` α 0.86 · 6 m `#357FAE` α 0.93 · 15 m `#2E5F93` · ≥ 35 m `#284F80`. This supersedes Art Bible §1.6 for depths < 6 m only (player answer Q4); the Calibration v1.1 pre-compensation of the ramp albedo still applies, deeper values stay as calibrated. On-screen ▣ lagoon at noon `#4FB3C6` ±16; open sea unchanged (`#326D9B` near). | world+fx |
| Q5 (**→ L.11** adds 9 more land species + ambient animals) | Island life: **gulls** 8 (circle 15–35 m high over beach/dock/helipad, land on dock posts and the hut roof, flee when the player is < 4 m, cry sounds), **beach crabs** (Sealog ghost crabs at dusk + 12 ambient crabs by day that only scuttle), **swaying palms**: crown sway 0.25 m at the frond tips, 0.18 Hz, trunk lean ±1.5°, phase from world-position hash, wind from ENE (vertex shader on the palm material, no per-frame CPU). | props (+world+fx shader) |
| Q6 | Signposts at every junction (exists) + the map app shows trails. Update the junctions to the new layout (1a). | props |
| Q7 | No fast travel; shortcuts: ~~the cave ramp (1a)~~ (→ L.5: the cave is now in the waterfall cliff), a rope ladder from the lighthouse cliff to the north beach (12 m), stepping stones across the waterfall creek. | props + world+fx |
| Q8 | Ravine: **rope ladders at both ends of the full-depth part** (the floor is 7.5 m deep between x = −33.5 and x = 3.5): west ladder x = −30 on the north wall, east ladder x = 0 on the south wall, 7.5 m, climbable like the dock ladder. On the floor at x ≈ −22 (7 m west of the bridge) a hidden find: **„Gus' Sparschwein“ / "Gus's piggy bank"** (Secret + 150 $ + logbook page 18). **→ L.5/L.6 at 0.62:** full depth between x = −25 and x = 0 → ladders at x = −23 (north wall) and x = −2 (south wall); the piggy bank in a side crevice 3 m east of the west ladder, invisible from the bridge. | props |
| Q9 | Entering the water: splash (32 droplets, 0.6 s) + bubble burst (40 faceted bubbles, 1.2 s) + "plunge" sound + camera dip 0.12 m over 0.25 s. The 1.5 s brightness clamp (Art Bible §2.1) stays. | world+fx (+player trigger) |
| Q10 | Residual light everywhere: underwater ambient floor 80 m ×0.10, 120 m ×0.07, 160 m ×0.05 of the 0–3 m stop; fog colour floor `#0E2E52` (80 m), `#0A2340` (≥ 120 m); view-model ambient floor 0.35 so hands are never black. Seabed shapes readable at 10 m without the lamp. | world+fx |
| Q11 | Seabed: terrain facets ≤ **3.2 m** within each site radius (was 6.4), sand ripples 0.2–0.5 m / 3–6 m wavelength, rock clusters `#6E6F86` with crevices (dark faces `#3B3A4E`), 6–10 crevices per site. | world+fx (terrain) + props (rocks) |
| Q12 | Underwater audio: breathing loop (inhale 1.2 s, exhale + bubbles 1.6 s) every 4.2 s → **2.6 s below 25 % air → 1.8 s below 10 %** (+ heartbeat 1.6 Hz below 10 %); bubble burst on every exhale; animal sounds (procedural): dolphin whistles/clicks, whale song (0.05–0.2 Hz moans, audible 300 m), crab clicks in the tide pools, snapping-shrimp crackle on the reef bed, garibaldi clicks, sea lion barks, anglerfish hum. | world+fx (FXAudio/FXAudioSynth) |
| Q13 | Deep glow – see G.2. | fauna |
| Q41 | Warmer menu: menu time 0.735 (17:38), sun elevation 8°, golden-hour grading + temp +18; ▣ lit sand `#E9C089`, sky horizon `#E7B98A`, shadows violet. | world+fx |
| Q42 | Intro only on the first start (`Settings.Data.introSeen`), then the character screen; main menu entry **„Intro ansehen“ / "Watch intro"** in the settings page footer. | player + ui |
| Q43 | Intro under water: Gerald swims through the reef shot + a colourful mixed school (40 fish: 20 blue tang, 12 clownfish, 8 parrotfish) circling the camera. | player (uses fauna factory) |
| Q44 | Menu crew = you + up to 3 friends from your last co-op session (`Settings.Data.lastCrew`: looks only, no names); fewer friends → default divers in player colours. | player + net (writes lastCrew on session end) |
| Q51 | Equipment visible on the character (own avatar in the preview, co-op avatars): tank by tier (t0 none, t1 dented grey `#9AA0A8`, t2 steel blue `#4F7FAE`, t3 twin grey, t4 nitrox yellow band `#F2C94C`, t5 trimix black `#2E3140` with orange `#EE7F2D`), fins by tier (t0 squeaky yellow, t1 split teal, t2 carbon black, t3 scooter under the body), lamp on the shoulder strap when not in hand, camera on a chest strap when not in hand. | player (+net look sync) |
| Q52 | Golden camera trophy (`#E8C65A`, emissive 0.2) on the hut shelf when the Sealog is 100 % + achievement. | props |
| Q53 | Wreck **MS Clickbait** interior (Section H.1). | props |
| Q54 | Landmark islands: each gets a **rest spot** (bench, sun sail, old air station – E refill, E.4) and one **secret** (H.2). | props |
| Q61–63 | Collectibles doubled (H.2). | props (+core totals) |

### H.1 Wreck interior „MS Clickbait“ (Q53) *(props; fauna: moray nest)*

* Hull stays where it is (`PropWreck`); the midship gets a walk/swim-in interior: entrance A = the torn hole on the
  port side (3 × 2.5 m), entrance B = the open hatch on deck. Corridor 2.2 m wide, 2.5 m high, a white guideline rope
  from both entrances (GDD 14.6 – lost-in-the-dark safety).
* Rooms (6 × 5 × 3 m each): **Brücke / Bridge** (captain's cabin: secret „Piraten-Dreispitz“ hat + logbook page 20,
  a cheese wheel on the chart table), **Kombüse / Galley** (Gus' radio for M2, pots floating, a creepy corner: a
  mannequin in old diving gear in the dark – on approach its mask drops and shows a painted smiley + a squeaky
  "honk"), **Laderaum / Cargo hold** (dark, a moray nest with 2 morays, 2 treasures, a stack of 400 cheese crates,
  the two-player door to a side room with treasure 3, solo detour through a 1.2 m vent).
* 3 scripted scares (Q11 fixed story spots + random): a shadow passing a porthole (barracuda), a door slamming in the
  current (no damage), the mannequin. Each ends with a gag (cheese wheel rolls out, a hermit crab wearing a tea cup).
* Lighting: interior only lit by the lamp + 3 portholes (god-ray slivers); residual light floor (Q10) applies.
* Performance: interior meshes batched per room, culled when the player is > 60 m from the wreck.

### H.2 Collectibles ≈ doubled: 46 → 92 (→ L.6: 94) (Q61–Q63) *(props; core: kinds/totals; ui: phone counter)*

Sparkle visible within 12 m (Q62), counter per kind in the phone app „Sammlung“ / "Collection".
**→ L.6:** secrets have no sparkle (glint only within 2.5 m) and are hidden behind climbs/dives/darkness/squeezes with
clues; 2 new secrets (village loft, Fog Isle archive) → secrets **13**, total **94**; pages and treasures move ≥ 3 m
off trails (half of them ≥ 10 m or behind a climb/dive).

| Kind | Old | New | Where the new ones go | Set reward (Q63) |
|---|---|---|---|---|
| Logbook pages | 15 | **24** | +ravine floor, wreck bridge, wreck galley, kelp island secret, blue-drop island, trench island, mini-island benches ×2, lighthouse cliff ladder | full **Gus story** (phone: „Gus' Logbuch“ in 4 chapters, Q64 hints inside) + hat „Gus' Originalmütze“ |
| Golden shells | 20 | **40** | 12 beach (fewer, island is smaller) + 6 lagoon shallows + **12 under water at the sites** (house reef 4, kelp 2, wreck 2, blue drop 2, trench 2) + **8 on the landmark islands** (2 each) + 2 in the tide pools | hat „Goldener Fischerhut“ |
| Treasures | 6 | **14** | +wreck hold ×3, kelp stuck chest (two players), blue-drop ledge, trench (old camera), hidden bay cave, lighthouse cellar | suit pattern „Gold“ |
| Secrets | 4 | **11** | +ravine piggy bank, wreck captain's cabin, 4 landmark-island secrets (kelp: a message carved into a tree „G+H 1989“; wreck: Gus' hammock; blue drop: a radio buoy from the TV studio; trench: a TV set on the beach that shows static), lighthouse cellar | mask frame „Gold“ |
| Messages in a bottle | 1 | **3** | day 3 / 6 / 10 at random beach spots (save seed); each adds one clue to the TV studio (Q64) | M3 trigger + TV studio coordinates |
| **Total** | 46 | **92** | | |

---

## I. UI / UX *(ui unless noted)*

| Q | Spec |
|---|---|
| Q37 (**→ L.10:** every card/toast = icon + one short line + optional chip) | On screen at most **1 card** (banner, radio card, achievement, Sealog popup) **+ 2 text toasts**. Queue: cards wait, toasts queue max 6 (drop the oldest; identical toasts merge as „… ×3“). |
| Q38 | The banner's second line sits on a light band: `#F4EFE6` α 0.88, 10 px radius, text `#1E1B2B` 24 px. |
| Q39 | Hotbar always visible (also with empty hands, on land and under water); idle after 4 s → 60 % α, full on any change. Hidden only with the phone/menus open. |
| Q40 | **Wrist dive computer** (player builds the 3D watch on the left wrist, view-model layer, bottom-left, ≤ 150 × 120 px): depth (big, „12 m“), max safe depth below („max 15 m“, orange when exceeded, red blink when > +5 m), a ring arrow to the boat (or to the dock when the boat is > 600 m away or nobody used it today) + distance („34 m“). Shown under water and while swimming at the surface. The top-left dive text shrinks to the site name for 4 s on entering a site (depth lives on the wrist now). |
| Q17 | Hotbar: keys 1–3 / 4–6 as today **+ mouse wheel**: wheel cycles the right-hand slots (incl. empty), **Shift+wheel** the left hand; while aiming the camera the wheel zooms (as today). Core: `GameInput.HotbarScroll` (−1/0/+1 per frame). |
| Q20 | Camera auto-equipped when you jump/climb into the water: if the camera is on the hotbar and the right hand is empty or holds the rod (rod stowed), the camera slot becomes active – unless the player emptied the hand within the last 3 s. *(player)* |
| Q30 (**→ K.2:** new hint set `Recording, NoAnimals, TooDark, Closer, SealogReady, New` + brackets around every detected animal; the words below for Closer/Light stay) | Viewfinder hints = icon + 1–2 words: „Näher“ / "Closer", „Mittig“ / "Centre", „Ruhig“ / "Steady", „Licht!“ / "Light!", „Zu weit“ / "Too far", „Gut!“ / "Good!", „Rekord?“ / "Record?" (ui draws; filming provides the reason enum `ShotHint`). |
| Q31 (**→ L.4:** collage of up to 5 animals + own title; the formula below stays) | **Photo as thumbnail:** the FinTube upload screen shows today's photos (max 8) – pick one (default: best photo of the top species). Views × `(1 + 0.15 × thumbQuality)` (0..1 from the photo's framing/light/species value) – no photo = ×1.0. Shown on the upload card and in the video list. *(ui + livestream/filming for thumbQuality + core for the formula)* |
| Q32 | Camera upgrades (GearSlot.CameraMod): **„Zoom-Objektiv“ / "Zoom Lens"** 3,000 $, 1,500 F: max zoom ×1.5; **„Nachtsicht-Modul“ / "Night Vision Module"** 12,000 $, 10,000 F: toggle with the lamp key while aiming – green-tinted viewfinder, clips in darkness count full light quality. |
| Q35 | Phone only on land/boat: opening it while swimming → toast „Das Handy bleibt trocken – erst an Land oder aufs Boot.“ / "The phone stays dry – get on land or the boat first." |
| Q36 | Weather app placeholder: icon „Wetter“ / "Weather" (sun/cloud), screen „Bald: Sturmwarnungen und Seegang.“ / "Coming soon: storm warnings and swell." |
| Q50 | Channel ranks with badges (badge glyph next to the follower line, on the nameplate, the FinTube channel page and the character's chest): 0 Plankton · 100 Garnele / Shrimp · 1,000 Clownfisch / Clownfish · 5,000 Seepferdchen / Seahorse · 25,000 Schildkröte / Turtle · 100,000 Sealebrity (gold seal) · 500,000 Hai-Legende / Shark Legend · 1,000,000 Wal-Ikone / Whale Icon. Rank-up banner: „Neuer Rang: {rank}!“ / "New rank: {rank}!" |
| Q52 | Golden camera (H). Sealog header shows „100 %“ with a gold glyph. |
| Q98 | FOV slider **60–100°** (default 70) in Settings → Grafik; `FilmHandCamera.BaseFov` follows. |
| Q99 | Two toggles in Settings → Spiel: **„Kopfwippen“ / "Head bob"** and **„Kamerawackeln“ / "Camera shake"** (predator bumps, fish nibble, water entry dip, rescue – every shake source × 0 when off; footage steadiness is not affected). |
| Q79 | Difficulty cards: Entspannt and Normal open; **Schwer locked** until a finale was finished on Normal or Hard on this PC (`Settings.Data.normalCompleted`): grey card, lock glyph, „Schwer – frei nach dem Finale auf Normal“ / "Hard – unlocks after finishing Normal". |
| post-finale | Sealog tabs **„Legenden“ / "Legends"** and **„Rekordjagd“ / "Record hunt"** after the finale: one named legend per site spawns (Tante Tentakel – octopus 2.2 m; Opa Barsch – giant sea bass 2.8 m; Hammer-Harry – hammerhead 5.2 m; Big Bertha's mum „Oma Bertha“ – humpback 17 m; Kalmar-König – giant squid 14 m); record hunt: gold target per species = `maxCm × 1.05`, gold badge per species beaten. |
| Q72 | No text chat (unchanged). |
| subtitles | NPC/radio subtitles: bottom centre above the hotbar, max 2 lines, 26 px, speaker name in the speaker's colour (Horst `#E07B39`, Gus `#3FA9B5`, Dr. Fin `#8E5CC2`, Chad/Brad `#E8C65A`; round 2b: Wanda `#E0607E`, Grandma Pearl `#6E9445`, Toni `#4F7FAE`, other villagers `#C9B48A`), white text, ink outline, no box. |

---

## J. Task split for the 11 engineers

(Updated for sections K and L: an 11th engineer **filming** takes `Filming/*` over from livestream; livestream now
also owns **sponsor deals**. Per-engineer K/L items: K.9 and L.14.)

### J.1 Phases

1. **Phase 0 (core only, first):** contracts below as compile-ready code (stubs where the logic belongs to others) –
   J.3 **and J.3b** (K/L contracts).
   Announce "contracts ready" with the compile check passing. Everyone else meanwhile reads, prototypes in their own
   files and must not touch files they do not own.
2. **Phase 1 (everyone in parallel):** P0 items, then P1.
3. **Phase 2:** P2 items, then integration tests (J.6). One Unity instance at a time – the core engineer schedules
   the AutoPlaytest/AutoShot runs.

### J.2 File ownership

| Engineer | Owns (may edit) | Delivers |
|---|---|---|
| **core** | `Core/*` (except `FishMarket.cs`), `Data/DiveSites.cs`, `Data/GearDatabase.cs`, `Data/DifficultySettings.cs`, new `Data/FoodDatabase.cs`, `Data/AchievementDatabase.cs`, `Data/CosmeticsDatabase.cs`, **new `Data/ChallengeDatabase.cs`, `Data/FindsDatabase.cs`, `Core/QuestTracker.cs`, `Core/SleepVote.cs`, `Core/Finds.cs`, `Core/Quests.cs`**, `Boot/*`, `Debug/*` | **K.9 + L.14 core items (0.62 layout, K.1 economy, Journal photo API + counters, notify API, quest/sleep/finds/carry state, GameState v4)**; phase-0 contracts; WorldLayout/DiveSites values (1a → L.5); Q45 money (→ K.1); ranks; Settings v3 (FOV, toggles, voice default, playerKey, lastCrew, normalCompleted, introSeen, look fields, NaN-safe Validate); GameState v3 + migration; Buffs, Pantry, Achievements, Cosmetics, StoryFlags, BoatHome, CharacterScreen, LiveState facades; CoreCommands keys; GameInput (HotbarScroll, StreamDare); econsim re-run (C.3, D.4); AutoPlaytest/AutoShot steps for everyone; Q100: remove the Ctrl+Shift+F12 / F5–F10 cheat keys (`DebugCheats` keeps its methods for the test harnesses, reachable only with the `-devcheats` command-line flag); the constants owner |
| **ui** | `UI/*` (all, incl. new `UICharacterScreen.cs`, `UISubtitles.cs`, `UIStreamOverlay.cs`, `UIAppKrill.cs`, `UIAppFin.cs`, `UIAppWeather.cs`, `UIFormat.cs`, `UIFoodPanel.cs`, `UIReelGauge.cs`, `UIRadioCard.cs`, **`UIAppDeals.cs`, `UIQuestLine.cs`, `UIThumbnailComposer.cs`, `UISleepCounter.cs`, `UILetterCard.cs`, `UISelfieFrame.cs`**) | **K.9 + L.14 ui items (viewfinder brackets/Sealog frame, Deals app, letter card, Sealog counters + depth tab, title field + collage, quest line + notifications, sleep counter, Chopper Shop app)**; 1c screen, 1e sliders, 1f call, I (Q37–Q39, Q30 drawing, Q31 picker, Q35, Q36, Q50, Q79, Q98/99 settings), host-leave dialog (F.2), stream overlay/app (C.7), subtitles, food panel, reel gauge, NetLobby profile card change is done by **net** (UI hands over a `UICharacterThumb` element) |
| **player** | `Player/*` (incl. new `CharacterPreview.cs`, `WristComputer.cs`, `PlayerTow.cs`, **`PlayerSelfie.cs`, `PlayerCarry.cs`, `PlayerLadders.cs`**) | **K.9 + L.14 player items (auto-record trigger, selfie camera + own avatar, carry, currents, crouch, generic ladders + ladder hands, motion set, high-res bean/face/hands/items, prologue arrival, lie-down)**; 1d view-model layer + pull-back, 1c preview stage, DiverBuilder options (patterns, eyes, brows, mask, fins, Q51 gear), hands Q1/Q2, Q17 apply, Q20, Q40 watch, Q42/43 intro, Q44 menu crew, unconscious/tow (F.1) movement, air refill zones (E.4), head bob/shake toggles, FOV, menu-camera / intro literals (1a) |
| **world+fx** | `World/*.cs` (not `Props/`), `FX/*` (**new `FXMusicDirector.cs`, `FXFogBank.cs`**), `Editor/SealebritiesSetup.cs` | **K.6 music, K.8 Fog Isle terrain + fog bank, L.5 terrain at 0.62 (+ ravine seam fix, village terrace), current/glow particles**; 1a terrain + features + menu camera + WorldTerrainData literals, lagoon (Q4) incl. ocean ramp, SSAO off (Q3), Q9 splash, Q10 residual light, Q11 terrain facets, Q12 audio, Q41 menu light, palm sway shader (Q5), steam_appid.txt build step if net asks |
| **props** | `World/Props/*` (**new `PropVillage.cs`, `PropFinds.cs`, `PropFogIsle.cs`, `PropDetails.cs`**) | **K.9 + L.14 props items (finds/salvage, stakes, caves/currents, hangar, Toni's raft + camp, Fog Isle, 0.62 placement, 17 detail spots, Waterfall Grotto, village, reefs + habitat anchors, secrets, sleep spots, ladders, drop zones, land-fauna spots)**; 1a placement literals, paths/junctions/cave ramp props, Q5 gulls + ambient crabs + palm material hook, Q7 shortcuts, Q8 ladders + piggy bank, Q52 trophy, Q53 wreck interior, Q54 rest spots + secrets, H.2 collectibles 92, grills/coconut palms/hut fridge/locker/compressor props (E), story objects (lucky fin, lens shards, antenna mast, radio in the galley), Horst's tent and bench, Clout Nine berth markers, bed 18:00 rule (E.3) |
| **fishing+boat** | `Boat/*`, `Core/FishMarket.cs` | **K.1 shore prices, K.3 boat winch + salvage delivery, K.8 fog drift/compass, L.12 mishaps + smoke beacon, village jetty landing**; D (minigame, open sea, new kinds, rods logic), 1f `ReturnHome`, boat compressor (E.4), heli snack crates → pantry (E.1), boat cooler eat spot, dolphin bow-wave hook (boat speed published via `BoatTelemetry.Speed`) |
| **npc+story** | new `Story/*` (NpcBuilder, Horst, Gus radio, missions, rivals + yacht, Dr. Fin + RV Plankton, visitor schedule, BabbleVoice, **Wanda, Toni, village people, side quests, prologue**), new `Data/DialogueDatabase.cs`, `Data/NpcDatabase.cs`, `Data/ResearchDatabase.cs`, **`Data/SideQuestDatabase.cs`**, **`Filming/TutorialQuests.cs`** | **K.5 prologue + story cast, K.8 Toni chain, K.2 tutorial step 6, L.7 side quests, L.8 village people, L.6 clue lines, quest-tracker feed**; A, B; tutorial texts/flow incl. the 18:00 sleep detail; Dr. Fin quest checks (fishing ones via `GameEvents.FishCaught` + FishItem data) |
| **livestream** | new `Stream/*` (incl. **`Stream/SponsorService.cs`**), **new `Data/SponsorDatabase.cs`** – no longer `Filming/*` | C (LiveState sim, viewers, hype, economy, dares, chat generator) with the K.1 factors; **K.7 sponsor deals** (offers, 2 slots, topic overlap, conditions, rewards, fail mails); deal lines for the quest tracker |
| **filming** (NEW) | `Filming/*` (except `TutorialQuests.cs`) | **K.2 detection, clips, crowd/event/selfie bonus, smart tape, Sealog photos, sightings; K.3 photo-challenge checks; K.4 selfie recording; L.2 counters; L.4 collage photo quality**; from the old livestream row: Q30 hint reasons (now K.2 `ShotHint`), Q31 thumbQuality, Q32 camera mods, camera models ×0.85 (Q1) on layer 30 (1d), **camera models redone smooth (L.13)** |
| **fauna** | `Fauna/**` (**new `Fauna/AI/FaunaZones.cs`, `Fauna/AI/FaunaEvents.cs`, `Fauna/Land/*`**), `Data/SpeciesDatabase.cs` (after phase 0) | **K.3 animal events, K.8 Fog Isle species, L.1 open-world zones + depth bands (55 species data), L.11 land fauna**; G (9 species, meshes, behaviours, glow, mischief), F.4 multipliers, Q43 intro school support |
| **net** | `Net/**` (incl. `Net/Steam/*`, `NetLobby.cs`), `docs/Multiplayer_Testen.md` | **K.9 + L.14 net items (FaunaEvent, SleepVote/SleepState, selfie + motion bytes, quest/deal/finds/carry state, RegisterFilmed args)**; 1b Steam 480 co-op, F.1 tow sync, F.3 player records over the wire, F.5 voice ranges/radio, F.6 achievement broadcast, look sync (1c), LiveState/stream feed sync (C.8), NPC/visitor sync, BoatHome client command forwarding, lastCrew on session end, lobby profile card with thumbnail |

Small unavoidable compile fixes in someone else's file are allowed – mention them in your report.

### J.3 Contracts the core engineer creates first (phase 0)

All new members are additive; existing signatures stay. Enum values are **appended at the end** (saved ints and net
bytes stay valid).

```csharp
// ---------- Core/Contracts.cs
public static class WorldLayout { /* 1a values */ public const float IslandScale = 0.75f; public static Vector2 Isl(float x, float z); }
public static class GameLayers { public const int Preview = 29, ViewModel = 30, CreatureIcons = 31; }
public static class UIState { /* + */ public static bool CharacterOpen; /* BlocksGameplay includes it */ }

public static class BoatHome {                       // 1f
    public static Func<bool> Handler;               // set by Boat (host/offline): true = moved
    public static bool Request();                   // client → Net.Command(Custom, CoreCommands.BoatHomeKey)
}
public enum CharacterScreenContext { FirstStart, MainMenu, Lobby, Pause, Locker }
public static class CharacterScreen {               // 1c
    public static Action<CharacterScreenContext> OpenHandler;   // set by UI
    public static void Open(CharacterScreenContext ctx);
    public static event Action Closed;
}
public enum PreviewFocus { Body, Head, Face, Feet }
public interface ICharacterPreview {                // 1c – implemented by Player/CharacterPreview
    RenderTexture Texture { get; }
    void SetLook(DiverLook look);
    void AddYaw(float degrees);
    void Focus(PreviewFocus focus);
    void PlayReaction();
    void Dispose();
}
public static class CharacterPreviewFactory { public static Func<int, ICharacterPreview> Create; }  // arg = texture size

public static class BoatTelemetry {                 // set by Boat every FixedUpdate; read by fauna (dolphins), stream (signal), wrist computer
    public static bool Valid; public static Vector3 Position; public static float Speed; public static bool Anchored;
}
public enum ShotHint : byte { None, Closer, Centre, Steady, Light, TooFar, Good, Record }   // Q30
public static class ViewfinderInfo { public static ShotHint Hint; public static float ThumbQuality01; }  // set by Filming, drawn by UI

// ---------- Core/LiveState.cs (new) – C
public enum ChatMood : byte { Neutral, Hype, Laugh, Scared, Donation, Hater, System }
public struct ChatMessage { public string user; public string text; public ChatMood mood; public long donation; }
public enum DareState : byte { None, Offered, Running, Done, Failed, Expired }
public sealed class DareInfo { public string id; public string text; public int targetPlayerId; public float secondsLeft; public long reward; public DareState state; }
public static class LiveState {
    public static bool IsLive; public static int StreamerId; public static int Viewers; public static float Hype;
    public static float Seconds, MaxSeconds; public static long MoneyThisStream, PendingFollowers; public static DareInfo Dare;
    public static event Action<ChatMessage> Chat; public static event Action Changed;
    public static void RaiseChat(ChatMessage m); public static void RaiseChanged();
}

// ---------- Core/Net.cs
public struct DiverLook { /* existing + */ public int pattern, eyes, brows, skin, mouth; public Color mask, fins; public bool snorkel;
                          public int tankTier, finTier; public bool lampOnStrap, cameraOnStrap; public int rank; }
// legacy: `face` stays and is always written as skin * 5 + mouth; a look with skin < 0 (old data) derives skin/mouth from face.
// `cuff.a == 0` means "Auto" (suit lightened 55 %).
public class NetPlayerInfo { /* existing + */ public DiverLook look; [NonSerialized] public string key; }

// ---------- Core/Types.cs (append)
public enum BodyPlan { /* … */ Crab, Starfish, Dolphin, Whale }
public enum BehaviorType { /* … */ Playful, Crawler }
public enum GearSlot { /* … */ Compressor, Radio, CameraMod }
public enum SpawnZone { Site, TidePool, Beach, OpenSea, Seafloor }
public enum BuffType { AirCapacity, SwimSpeed, SteadyHands, BumpResist, NightEyes }
public enum FoodKind { Food, Drink }

// ---------- Core/GameState.cs (version 3)
// shared: players (PlayerRecord), pantry (List<PantryItem{string foodId; int count;}>), storyFlags (List<string>),
// missions (List<MissionState{string id; int state; int progress;}>), research (List<ResearchState{string id; int progress; bool done;}>),
// achievements (List<string>), cosmetics (List<string>), dailyUsed (List<string>, cleared in StartNewDay),
// lastStreamDay = -1, rivalStage, lastDuelDay, firstVideoBonusPaid, cozyBonusDay = -1
public class PlayerRecord { /* F.3 */ }

// ---------- Core/GameEvents.cs (+ Raise* for each)
event Action<string, string, float> NpcLine;          // speakerId, localized text, seconds
event Action<string, bool> RadioCall;                 // speakerId, started/ended
event Action StreamStarted; event Action<bool, long, long> StreamEnded;   // fainted, money, followers delta
event Action<DareInfo> DareChanged;
event Action<FoodDef> FoodEaten; event Action BuffsChanged;
event Action<AchievementDef> AchievementUnlocked; event Action<string> CosmeticUnlocked;
event Action<string> Mischief;                        // kind id
event Action<int> PlayerUnconscious; event Action<int, int> PlayerRevived;   // id / (id, rescuerId)
event Action BoatReturnedHome; event Action<string> MissionChanged; event Action<int> RankChanged;

// ---------- Core/CoreCommands.cs – NetCommand.Custom keys (first arg)
BoatHomeKey = "BoatHome", EatKey = "Eat", GrillKey = "Grill", CoconutKey = "Coconut", StoryFlagKey = "StoryFlag",
MissionKey = "Mission", ResearchKey = "Research", StreamStartKey = "StreamStart", StreamEndKey = "StreamEnd",
DareKey = "Dare", AchievementKey = "Achievement", CosmeticKey = "Cosmetic", PlayerRecordKey = "PlayerRecord"

// ---------- Core/Buffs.cs (new, per player, never synced)
public struct ActiveBuff { public BuffType type; public FoodKind kind; public float value; public float secondsLeft; public string foodId; }
public static class Buffs { public static IReadOnlyList<ActiveBuff> Active { get; } public static void Apply(FoodDef f, float bonus = 0f);
    public static float Multiplier(BuffType t); public static void Tick(float dt); public static void Clear(); public static event Action Changed; }

// ---------- Core/Pantry.cs (new, shared)
public static class Pantry { public static int Count(string foodId); public static void Add(string foodId, int n); /* authority */
    public static bool TryEat(string foodId); /* client → EatKey */ public static event Action Changed; }

// ---------- Core/Achievements.cs, Core/Cosmetics.cs, Core/StoryFlags.cs (new)
public static class Achievements { public static bool Unlock(string id); public static bool IsUnlocked(string id); }
public static class Cosmetics { public static bool IsUnlocked(string id); public static void Unlock(string id); }
public static class StoryFlags { public static bool Has(string flag); public static void Set(string flag); }

// ---------- Data
public class FoodDef { public string id, nameDe, nameEn; public FoodKind kind; public long price, followersNeeded; public BuffType buff; public float value, minutes; }
public class AchievementDef { public string id, nameDe, nameEn, descDe, descEn; }
public class CosmeticDef { public string id; public string kind; /* Hat, Pattern, Eyes, Brows, Mask, Fins, Skin */ public int index; public string nameDe, nameEn, unlockHintDe, unlockHintEn; }
// SpeciesDef: + public SpawnZone spawnZone (→ replaced by `zones` etc., J.3b / L.1);   GearItem: + castMax, reelPower, greenBonus, lineStrength, zoomBonus; + bool nightVision
// GearStats: + castMax, reelPower, greenBonus, lineStrength, zoomBonus; + bool hasBoatCompressor, hasRadio, nightVision

// ---------- Core/Progression.cs
public const long MonetizationFollowers = 0; public const float StarterMoneyPerThousandViews = 2f; public const long StarterBonus = 25;
public static float Vref(long followers); public static float ExpectedUploadViews(long followers);
public static long ExpectedUploadMoney(long followers); public static long ExpectedUploadFollowers(long followers);
public static readonly (long followers, string de, string en)[] Ranks;   // I/Q50
public static int RankIndex(long followers);

// ---------- Core/GameInput.cs (append to GameAction): StreamDare (default T); + public static int HotbarScroll { get; }
// ---------- Core/Settings.cs v3: fov, headBob, cameraShake, npcTutorial, subtitles, introSeen, normalCompleted,
//            playerKey, List<DiverLook> lastCrew, look fields (cuffColor (α 0 = Auto), pattern, eyes, brows, skin, mouth,
//            mask, fins, snorkel; Settings.Look builds the full DiverLook),
//            voiceMode default OpenMic (+ migration), voiceGateDb = -45; Validate NaN-safe (1e)
```

Net message ids (net, Net/Sync range 64–127): `StreamState 90`, `StreamFeed 91`, `StreamChat 92`, `TowState 93`,
`Unconscious 94`, `Achievement 95`, `LookChanged 96`, `NpcState 97`, `StoryLine 98` (line id + args – each machine
localizes), `BoatHome` goes through `NetCommand.Custom`.

### J.3b Contracts for sections K and L (core, phase 0 – same rules: additive, enums appended)

```csharp
// ---------- Core/Contracts.cs
public static class WorldLayout { /* L.5 values */ public const float IslandScale = 0.62f;
    public static readonly Vector3 Village, DriftwoodBeach, HangarSpot, SwimRaft, FogIsland; }
public static class UIState { /* + */ public static bool TextInput; public static bool SelfieOpen; }  // TextInput: GameInput ignores every action except Pause/Enter
public static class SelfieState { public static bool Active; public static int Expression; }         // K.4 – set by Player, read by Filming/UI/Net
public enum ShotHint : byte { None, Recording, NoAnimals, TooDark, Closer, SealogReady, New }          // K.2 – replaces the J.3 list
public static class ViewfinderInfo { /* + */ public static int DetectedCount; public static readonly Vector4[] Brackets = new Vector4[8];
    public static readonly byte[] BracketTier = new byte[8]; public static int BracketCount; public static SpeciesDef SealogSubject; }
public enum MotionState : byte { Idle, Walk, Run, Jump, Fall, Land, Ladder, Swim, Dive, Sit, Lie, Carry, Tow, Selfie, Unconscious, Crouch }
public struct LadderInfo { public Vector3 bottom, top, outward; public float rungSpacing; public byte kind; }  // PropInfo.Ladders (props fill)
public enum NotifyKind : byte { Info, Money, Followers, Sealog, Clip, Delivery, Quest, Sponsor, Story, Collectible, Warning, Event, Achievement }
public static class QuestTracker { public static string Line, Progress; public static Vector3? Target; public static event Action Changed;
    public static void Set(string line, string progress, Vector3? target); }                         // L.10 – npc/livestream fill, ui draws
public static class SleepVote { public static int Lying, Total; public static bool LocalLying; public static event Action Changed;
    public static void SetLying(bool lying); }                                                         // L.9 – client → net → host evaluates
public static class FaunaEventInfo { public static string ActiveId, ZoneId; public static Vector3 Center; public static float EndsAt;
    public static event Action Changed; }                                                              // K.3 – fauna sets (host), net mirrors

// ---------- Core/Types.cs, Core/Hands.cs (append at the end)
public enum BodyPlan { /* … Crab, Starfish, Dolphin, Whale, */ Bird, Lizard, Quadruped, Primate, Bat }
public enum BehaviorType { /* … Playful, Crawler, */ Perch, Soar, Bask, Climb, Herd, Mischief }
public enum GearSlot { /* … Compressor, Radio, CameraMod, */ BoatWinch, Part }
[Flags] public enum DayMask : byte { Day = 1, Dusk = 2, Night = 4, All = 7 }
public enum HoldItem { None, Camera, Flashlight, FishingRod, /* + */ QuestItem }

// ---------- Data
// SpeciesDef: + string[] zones; float prefDepth; float nightShift; DayMask dayMask = DayMask.All; string homeSite; string page; string unlockFlag; bool land;
// DiveSite: + string unlockFlag;   DiveSites: house_reef (188, 38) r 105, + fog_isle (K.8)
// GearDatabase: tank_t1 100 $; + boat_winch (BoatWinch, 400 $, 250 F), radio_battery (Part, 80 $, only while K.8 T4 runs)
// new: Data/ChallengeDatabase.cs (ChallengeDef { id, de, en, kind, args }), Data/FindsDatabase.cs (FindDef { id, de, en, heavy, minValue, maxValue, sites }) – core;
//      Data/SponsorDatabase.cs (SponsorDef, DealDef { id, sponsorId, tags, condition, args, days, k }) – livestream; Data/SideQuestDatabase.cs – npc

// ---------- Core/GameState.cs (version 4; v3 saves: empty lists, sightings = timesFilmed, photos = discovered ? 1 : 0)
// JournalEntry: + int sightings, photos; float minDepthSeen = -1, maxDepthSeen; int zoneMask, lastSeenDay; string bestPhotoId
// FootageClip: + int crowd; string eventId; bool selfie; float depth
// PendingUpload / VideoRecord: + string[] thumbSpecies; string thumbPath; bool customTitle
// shared: finds (List<FindState{string id; bool taken;}> today), challenges (List<string> today + done), sponsor (SponsorState{ offers, active ≤ 2, cooldowns, done }),
//         sideQuests (List<QuestState{string id; int step; int progress; bool done;}>), trackedQuest, prologueDone, lastLanternDay = -1
// per player (PlayerRecord too): carried (item id or null)

// ---------- Journal (GameState.cs)
public static (bool isNew, bool isRecord) RegisterPhoto(SpeciesDef def, float sizeCm, SizeTier tier, float photoQuality, string photoId, float depth);
public static void AddSightings(string speciesId, int count, float minDepth, float maxDepth, int zoneBit);    // authority; clients via SightingsKey

// ---------- Core/GameEvents.cs (+ Raise* for each)
event Action<NotifyKind, string, string> Notify;   event Action<string> FaunaEventStarted, FaunaEventEnded;
event Action<string> DealChanged, SideQuestChanged;   event Action<SpeciesDef, int> Sighted;   event Action<PhotoEntry, SpeciesDef> SealogPhoto;

// ---------- Core/CoreCommands.cs – NetCommand.Custom keys (append)
SightingsKey = "Sightings", DealKey = "Deal", FindKey = "Find", ChallengeKey = "Challenge", QuestKey = "Quest",
CarryKey = "Carry", LanternKey = "Lantern", ForceSleepKey = "ForceSleep"

// ---------- Core/GameInput.cs: GameAction += Selfie (default H). Crouch = GameAction.Down on land (no new action).
// ---------- Core/Settings.cs: autoRecordUnderwater = true, musicWhileDiving = true, questDisplay = 0 (Normal / Small / Off)
// ---------- Core/Progression.cs: K.1 constants (StarterBoostMax 1.5, StarterBoostEnd 5000, StarterBonus 50, FollowerConversion 0.0058,
//            StreamFollowerFactor 0.25, StreamMoneyFactor 0.8, MonetizationFollowers 0)
```

Net message ids (net): `SleepVote 99`, `SleepState 100`, `FaunaEvent 101`, `CarryState 102` (net takes the next free
ids if any is already used); selfie expression + motion state/phase travel in the existing avatar state; deals, side
quests, finds and challenges travel in the GameState snapshot.

### J.4 Cross-module hand-offs (who calls whom)

| Caller | Calls | Provider |
|---|---|---|
| ui `UIPauseMenu.OnReturnToHut` | `BoatHome.Request()` | core → fishing+boat `BoatController.ReturnHome` (host) / net (client) |
| ui main menu, net lobby, ui pause, props locker | `CharacterScreen.Open(ctx)` | ui `UICharacterScreen` |
| ui `UICharacterScreen` | `CharacterPreviewFactory.Create(size)` | player `CharacterPreview` |
| livestream `StreamService` | `LiveState.*`, `GameManager.AddMoney/AddFollowers`, `Progression.Expected*` | core |
| ui `UIStreamOverlay` | `LiveState.Chat/Changed`, `GameEvents.DareChanged` | livestream |
| npc+story | `GameEvents.RaiseNpcLine`, `RadioCall`, `StoryFlags`, `GameState.missions` | core; ui draws |
| props interactables (grill, palm, fridge, locker, air station, wreck door, story objects) | `Pantry`, `Buffs`, `CoreCommands` keys, `StoryFlags`, `Collectibles.Collect`, `CharacterScreen.Open` | core |
| fishing+boat heli crates | `Pantry.Add` (host) | core |
| player Air | `Buffs.Multiplier(AirCapacity)`, refill zones (`WorldLayout.Hut`, dock line, `PropInfo.AirStations`), `GearStats.hasBoatCompressor` | core / props |
| fauna mischief | `GameEvents.RaiseMischief`, player hooks `PlayerHands.StealLamp(float seconds)` | player |
| fauna dolphins | `BoatTelemetry.Speed` / `Position` (static, set by Boat each FixedUpdate) | fishing+boat |
| livestream dares | `IPlayer` (depth, air, MaxSafeDepth), `CreatureRegistry`, wreck interior volume `PropInfo.WreckInterior` (Bounds) | player / fauna / props |
| filming detection / photos | `Journal.RegisterPhoto`, `Journal.AddSightings` (via `SightingsKey` on clients), `FaunaEventInfo`, `SelfieState`, `PhotoChallenges` data | core / fauna / player |
| ui viewfinder, selfie frame | `ViewfinderInfo` (brackets, count, Sealog subject), `SelfieState` | filming / player |
| fauna spawner (L.1) | `FaunaZones.ZoneAt`, `Progression.IsSiteUnlocked`, `StoryFlags.Has("fog_map")`, `PropInfo.Habitats`, `EnvState` (time of day) | fauna / core / props |
| fauna land fauna (L.11) | `PropInfo.LandSpots`, `PropInfo.DropZones` (goat on crates) | props |
| livestream `SponsorService` | `GameEvents.UploadResolved/FishCaught/SealogPhoto/StreamEnded`, `Footage` clips, `QuestTracker.Set`, `GameManager.AddMoney` | core / filming |
| npc side quests, prologue, Toni | `Quests`, `QuestTracker.Set`, `StoryFlags`, carry (`Hands` QuestItem), `GameEvents.SealogPhoto`/`FishCaught`/`Sighted`, `PropInfo` quest objects | core / player / props |
| props / player sleep spots | `SleepVote.SetLying` | core → net |
| fishboat mishaps | `HeliService.Hash`, `PropInfo.DropZones`, roof ladder in `PropInfo.Ladders`, `GameEvents.RaiseNotify` | props / core |
| worldfx music | `EnvState`, `IPlayer.Depth/IsUnderwater`, `GameEvents.NpcLine/RadioCall`, `LiveState.IsLive`, `Settings` | core / livestream |
| every module | `GameEvents.RaiseNotify(kind, line, chip)` instead of long toasts | core → ui |

### J.5 Constants owner

Balance constants live in one place each: economy/stream factors in `Core/Progression.cs` (core), fish data in
`Core/FishMarket.cs` (fishing+boat), food in `Data/FoodDatabase.cs` (core), world numbers in `WorldLayout` (core) /
`WorldFeatures` + `WorldHeightField` (world+fx). Ask the owner instead of copying a number. Round 2b: sponsor numbers
in `Data/SponsorDatabase.cs` (livestream), spawn targets and zone margins in `Fauna/AI/FaunaZones.cs` (fauna),
detection/photo thresholds in `Filming/FilmShotEvaluator.cs` (filming), music in `FX/FXMusicDirector.cs` (world+fx),
mishap chances in `Boat/HeliService.cs` (fishing+boat), quest rewards in `Data/SideQuestDatabase.cs` (npc).

### J.6 Tests and acceptance

| Area | Proof |
|---|---|
| (a) island | design-harness probe output pasted in the report (land area, trails, POI heights); AutoShot `02_island_air`, `03_hut_dock_boat`, new `13_cave`, `14_bay`; AutoPlaytest walk/dive/fish steps pass |
| (b) Steam | `-steamselftest` log: init OK, PRIVATE lobby created and left, no names/ids logged; IP join still passes AutoPlaytestMP |
| (c) character | AutoShot `16_character` (screen open, preview visible, hat changed → screenshot shows it); co-op: look change visible on the other window |
| (d) clipping | AutoShot `15_lamp_dock` + wall + wreck |
| (e) sliders | AutoPlaytest feeds NaN / 1e9 / −5 to every slider: labels in range, settings.json valid |
| (f) boat | AutoPlaytest: drive boat 200 m away, exit, pause → „Zur Hütte“ → boat within 2 m of `BoatSpawn`; MP: refused while the other player is aboard |
| Tutorial + Horst | AutoPlaytest completes steps 0–8 with Horst lines logged (line ids), first tank ordered ≤ 11 game-minutes of fishing |
| Livestream | AutoPlaytest at 1,000 followers: start on boat, 60 s, accept 1 dare (scripted success), end → money and followers > 0; second run: faint → stream ends, loss applied |
| Co-op | AutoPlaytestMP: tow rescue (client faints, host tows, client revived with clips), achievement broadcast, guest record restored after host reload, voice range 10 m under water (log) |
| Fauna | AutoShot `17_tidepools` (crabs, starfish), `18_dolphins` (boat at speed), trench shot with the goblin shark |
| Economy | econsim (`econsim_k`, K.1): first tank 6–9 min of fishing, money/day before 1k followers ≥ 200 $, camera t1 by day 5, 1M followers 36–42 h solo with daily streams (measured 38.1 h; 2 players 34.8 h, 4 players 31.3 h) |
| (s) island 0.62 | `design2_l/r3` probe output pasted in the report (land 4.4 ha ±5 %, trails 681 m ±5 %, every trail ≤ 3 m steeper than 32°, POI heights as L.5); AutoShot `02_island_air`, `03_hut_dock_boat`, `13_cave` (now the Waterfall Grotto), `14_bay`, new `23_village`; AutoPlaytest walk/dive/fish steps pass |
| (h) filming | AutoPlaytest: dive at the house reef with auto-record: ≥ 5 clips without aiming, a Sealog photo of a framed parrotfish registers it, a far photo does not; sightings counted; tape only runs with animals in frame |
| (o) spawning | AutoPlaytest census: ≥ 15 animals within 80 m at 6 sample points (lagoon, open sea halfway to the kelp, house reef, kelp, locked wreck, locked Fog Isle) – no species outside its band, no locked-site species |
| (j) selfie | AutoShot `24_selfie_reef` (own diver + ≥ 2 fish in frame); clip flagged `selfie` |
| (m) sponsors | AutoPlaytest: accept 2 non-overlapping deals, a third with a shared tag is refused with the reason, cancel works, completing one pays the reward |
| (n) Toni | AutoPlaytest with cheat flags: trigger at day 10 / 2,500 F, steps T1–T4 complete, `fog_map` set, the boat passes the fog lane and the Fog Isle spawns its species |
| (w) sleep | AutoPlaytestMP: host lies down → „1/2“ on both; client lies down → night skipped once; client gets up → vote drops |
| (r) FinTube | AutoPlaytest: custom title stored (60-char cut), collage with ≥ 3 tiles saved to `thumbs/` |
| (x, z) HUD | AutoShot `25_quest_line` (top centre, no box), notification samples at 1080p: one line each |
| (v, ac) art/animation | AutoShots `16_character`, `19_crew_closeup`, `21_ladder_fp`, `22_run_avatar` |

### J.7 Core phase 0 – delivered contracts, renames and additions *(core, 11.10.2026, compile tag `core2`)*

All J.3/J.3b contracts exist and compile (Runtime + EditorAll). Where the code differs from the sketches above, **the
code is binding** – this list names every rename and every addition other engineers should know.

**Renamed / changed against J.3 / J.3b**

| Spec | Delivered | Why |
|---|---|---|
| `BoatHome { Handler; Request() }` (J.3) | **not in core.** The Boat module already ships `Boat/BoatHome.cs` (`BoatHome.AfterLocalPlayerWentHome()` – the pause menu calls it; `BoatHome.Install()` registers the `"BoatHome"` custom command). `CoreCommands.BoatHomeKey = "BoatHome"` stays as a constant and is *not* a core-handled key | 1f was implemented this morning; a second class would not compile |
| `DiverLook`: "skin < 0 marks old data" | `lookVersion` (0 = legacy → `Normalized()` derives skin/mouth from `face`); colours with **alpha 0 = default** (cuff = Auto, mask `#1E1B2B`, fins `#2B2533`); helpers `Normalized()`, `ResolvedCuff/Mask/Fins`, `DefaultMask/Fins` | a struct cannot default to −1 |
| `achievements.json` (F.6) | per-machine list `Settings.Data.achievements`; cosmetics this machine has seen unlocked: `Settings.Data.unlockedCosmetics` | one profile file outside the save |
| `FindDef { minValue, maxValue }` | `FindDef { valueMin01, valueMax01, rare, sites, weight }` = position inside the **site's** range; the K.3 site table is `FindsDatabase.Sites` (`FindSiteDef`) | the value table is per site |
| `challenges (List<string> today + done)` | `GameState.challengesToday`, `challengesDone` (+ `challengeBonusDay`, `challengeBonusCount`) | |
| `SpeciesDef.spawnZone` (G.1) | not added (L.1 replaces it); the enum `SpawnZone` exists only for old code | |
| Enums of J.3/J.3b "in Contracts.cs" | all enums live in `Core/Types.cs`: `NotifyKind, ShotHint, MotionState, PreviewFocus, CharacterScreenContext, DeliveryMishap, ChatMood, DareState, SelfieExpression, DayMask, BuffType, FoodKind` | one enum file |
| `IDiverPuppet.SetPose(MotionState …)` (L.13) | **new interface `IDiverMotion`** (`SetMotion(state, speed, phase, right, left, lightOn)`, `SetExpression(int)`) next to `IDiverPuppet`; avatars/NPCs use `puppet as IDiverMotion` | changing IDiverPuppet would break the player build |
| `Progression.StarterMoneyPerThousandViews` (J.3) | dropped (K.1): `MoneyFromViews` = views/1000 × 4 × `StarterBoost(F)` | |

**Additions (use them instead of building your own)**

* **Notifications (L.10):** `GameEvents.RaiseNotify(kind, line, chip)`. `RaiseToast(text)` = `RaiseNotify(Info, text)`. The
  `Toast` event is a flattened mirror of every notification ("line · chip") for legacy listeners (FX sound, net forwarding,
  tests) – **the UI subscribes to `Notify` OR `Toast`, never both.** Core call sites are converted (Footage, Collectibles,
  Progression, GameManager, Shop, Hands, Gear).
* **WorldLayout (L.5):** `IslandScale 0.62`, `Isl(x, z)` (Vector2) and `Isl3(x, y, z)` (Vector3, y unchanged), all 0.62
  anchors, `Village`, `DriftwoodBeach`, `DriftwoodRaft (66, 104)`, `HangarSpot`, `SwimRaft`, `FogIsland`,
  `FogIslandShoreRadius 30`, `FogBankRadius 260`, `FogLaneStart/End/Width`, **`NewGameSpawn = Helipad + (4, 0, 6)`** +
  `NewGameSpawnYaw` (K.5 prologue; `GameManager.NeedsPrologue` until `StoryKeys.PrologueDone`). `DiveSites`: house reef
  (188, 38) r 105, `fog_isle` with `unlockFlag = "fog_map"` (`DiveSite.IsStorySite`; locked until the flag, hidden from the
  tutorial's "next site"). Boot menu-camera fallback and the 7 AutoShot literals are scaled.
* **Spawning data (L.1):** `SpeciesDef` + `zones, prefDepth, nightShift, dayMask, homeSite, page, unlockFlag, land` and
  extra `nightWeight` ("night ×2"), `unique`, `habitat` (anemone/crevice/cleaning/overhang/sand/seafan/seagrass),
  `homeRange` (sea lion 200 m, Bertha 300 m); helpers `Page`, `PrefDepth`, `HasZone`, `FitsTime`, `BandMin/BandMax(night)`,
  `DepthWeight(depth, night)` (1.0 at pref → 0.35 at the edges → 0 after a 10 % margin). All **55** entries exist with the
  L.1/G.1/K.8/L.11 data (fauna fills meshes/behaviour; unknown plans fall back to the fish mesh). `SpeciesDatabase.ByPage`,
  `ByZone`, `Count`; `BySite` = legacy spawn pool (siteId + zone + not land). `Zones` (ids, `Bit()`, `Name()`),
  `DayPhase.Of(time)`, `SealogPages` (8 pages, `site` flag), `Progression.IsGateOpen(def)`, `Journal.PageCompletion`.
* **Sealog (K.2/L.2):** `Journal.RegisterPhoto(def, cm, tier, quality, photoId, depth, zoneBit = 0)` – counts the subject
  as **1 sighting** (filming must not add it again); `Journal.AddSightings(id, n, minD, maxD, zoneBit)` – clients apply
  optimistically and core batches them to the host every 10 s (`SightingsKey`), so filming just calls it. Net:
  `RegisterFilmed` with ≥ 6 args = photo. `JournalEntry` + `sightings, photos, minDepthSeen (−1), maxDepthSeen, zoneMask,
  lastSeenDay, bestPhotoId, bestPhotoQuality`. `RegisterFilmed` stays a working legacy wrapper until filming switches.
* **Clip value (K.2/K.4):** `FootageClip` + `crowd, eventId, selfie, selfieMatch, depth`; `Progression.ClipValue` applies
  crowd (+4 %/animal, max 8), event ×1.25, selfie ×1.3 (×1.15 more with `selfieMatch`). `FaunaEventInfo.EventAt(pos)`.
* **Uploads (L.4/K.1):** `Progression.CreateUpload(style, title, thumbSpecies, thumbPath, customTitle, thumbQuality)`
  (net args: style, title, csv, 0/1, quality), `SanitizeTitle` (60 chars), `SuggestTitles(clips, style, list)` (3 chips),
  `ViewsBonus(state, day)` (challenge +10 % each, cozy/lantern +5 % not stacking, duel +5 %), thumbnail
  `× (1 + 0.15 × thumbQuality)`. `VideoRecord` + `thumbSpecies, thumbPath, customTitle, starterBoost, starterBonus`.
  Payout notification "FinTube zahlt · +86 $ · Starthilfe ×2,3".
* **Economy (K.1, verified):** `MonetizationFollowers 0`, `StarterBoostMax 1.5`, `StarterBoostEnd 5000`, `StarterBonus 50`,
  `FollowerConversion 0.0058`, `StreamMoneyFactor 0.8`, `StreamFollowerFactor 0.25`, `Vref`, `ExpectedUpload*`,
  `StreamFaintLoss(F, difficulty)`, `Local/National/BigSponsorFollowers 250/5,000/50,000`, `tank_t1` 100 $. Re-run of
  `econsim_k` with these constants (`rerun_core2_{1,2,4}p.txt`) is identical to `final_*.txt`: first tank 7.2 min,
  258 $/day before 1k, cam t1 day 4, fins t1 day 5, 1M at 38.1 / 34.8 / 31.3 h.
  (Shore prices × 1.25 are fishboat's `FishMarket`.)
* **Ranks (Q50):** `Progression.Ranks`, `RankIndex`, `RankName`; `GameEvents.RankChanged` (host and client diff).
* **Gear:** `GearItem` + `castMin/castMax, reelPower, greenBonus, lineStrength, openSea, rareBitePing, zoomBonus,
  nightVision, notSold, requiredFlag`, `Category` (`ShopCategory` Diving/Camera/Boat/Fishing/Snacks/Parts). Items:
  rod_t0–t3 (D.3; rod_t3 `notSold`), `boat_compressor`, `boat_winch`, `uw_radio`, `cammod_zoom`, `cammod_nv`,
  `radio_battery` (listed only with `StoryKeys.ToniT4Active`). `GearStats` + `castMin/Max, reelPower, greenBonus,
  lineStrength, zoomBonus (maxZoom already multiplied), rodOpenSea, rodRarePing, hasBoatCompressor, hasRadio, nightVision,
  hasBoatWinch, tankTier, finTier, rodTier, cameraTier` (boat equipment and camera mods count when owned by the crew).
  `GearDatabase.IsListed`. v2 saves with ≥ 250 followers get the boat compressor once (notification).
* **Shop (L.12/E.1):** `Shop.BasePrice` (rod_t1 −50 % after `StoryKeys.RodT1Discount`, uw_radio −50 % after Gus M2,
  „Angebot des Tages“ −20 %), `GameState.dealOfDayItem` + `Shop.RollDealOfTheDay` (host, daily), `IsListed`,
  `Shop.BuyFood(food)` (snack pack, delivery id `"food:<id>"`, express timing at the standard price, crate → pantry),
  `Shop.DeliveryName(d)`. `Delivery` + `mishap (DeliveryMishap), dropZone, nudges`; `GameState.lastMishapDay`;
  fishboat raises `GameEvents.RaiseDeliveryMishap(item, mishap)` (achievement "roof_delivery" hooks it).
* **Food (E):** `Data/FoodDatabase.cs` (E.2 + Ada's kiosk), `Buffs` (`Multiplier`: AirCapacity/SwimSpeed/NightEyes = 1+v,
  SteadyHands/BumpResist = 1−v; `Value`, `Has`, `Tick` by GameManager), `Pantry` (`Add`, `TryEat`, `BuyAtKiosk`,
  `TryGrill(out fish)` + `HostGrill`, `TryShakePalm(palmId)`/`PalmHasCoconut`, `DrinkCoconut`), cozy bonus granted by
  `SleepToMorning` when a fish was grilled that day.
* **Sleep (E.3/L.9):** `GameManager.CanSleepNow` / `SleepFromTime 0.75` / `SleepRuleHint` (the bed checks it;
  `SleepToMorning` itself stays unconditional for the vote and tests). `SleepVote.SetLying(bool)` (solo → sleeps at once),
  host `HostSet/Tick` (all lying 2 s → `SleepToMorning`), `ApplyState(lying, total)` for clients, `ForceSleepAvailable` /
  `ForceSleep()` after 60 s at n−1/n, `SendHandler` for net message 99 – **fallback `CoreCommands.SleepVoteKey`** so the
  vote already works over the command channel.
* **Quest line (L.10):** `QuestTracker.SetEntry(QuestSource, id, line, progress, target, kind)` – one entry per source
  (Tutorial, Story, Side, Deal, Other); shown = tracked id (`GameState.trackedQuest`) or the first by source order;
  `Line, Progress, Target, Kind, ActiveId, NextLine`, events `Changed`, `Completed`. `Set(line, progress, target)` = source
  Other.
* **Side quests / story (L.7/K.8/B):** `Quests` (`Start` – max 3 side quests, Toni's chain `"toni"` exempt – `SetProgress`,
  `AddProgress`, `Complete`, `Drop`, `Track`), `Missions` (M1–M4, state 0/1/2; M2 done sets the radio discount),
  `Research` (`AddProgress/SetProgress/Complete`), `Carry` (`Pick/Drop/Current/IsHeavy` + K.3 constants, `WinchRange 40`),
  `Village` (`IsLanternEvening`, `ReleaseLantern`), `StoryFlags.Set/Clear/Has` + **`StoryKeys`** (prologue_done,
  gus_letter, tutorial_done, toni_arrived, toni_t1–t4, toni_t4_active, fog_map, village_visited, rod/radio discounts,
  finale_done …), `StoryRules` (Toni arrives on the first morning with day ≥ 10 and ≥ 2,500 F, fallback day 16 – set by
  the host at the start of the day; Lantern Evening day % 7 == 4, 19–22 h). `GameManager.CompleteFinale()`.
* **Sponsor deals (K.7):** `GameState.sponsor` (`SponsorState`: `offers`/`active` of `DealState {id, dealId, sponsorId,
  tags, reward, days, offeredDay, expiresDay, acceptedDay, deadlineDay, progress, target}`, `cooldowns`, `done`, `failed`).
  `Deals.AddOffer` (host; max 3, sulking sponsors refused, unique ids), `CanAccept(offer, out reason, out conflict, out tag)`,
  `Accept`, `Cancel` (sulk 3 days), `Complete` (pays), `Fail`, `SetProgress`, `OnNewDay` (expire/fail), hooks
  `SponsorName`/`DealTitle` (livestream sets them), `TagName`. Tags are the English ids `shark, turtle, ray, octopus,
  school, wreck, night, depth, fishing, livestream, island, coral, whale`.
* **Finds (K.3):** `FindsDatabase.Daily(site, day, seed)` (deterministic ids `find:/salv:{site}:{day}:{i}` + u/v/w
  placement numbers), `TryResolve(id)`; `Finds.Today`, `Take` (small → inventory), `Deliver` (heavy), `Sell`, `SellAll`,
  `Inventory`, `IsTaken`; `GameState.finds` cleared daily, `foundItems` ("Funde" tab).
* **Challenges (K.3):** `Data/ChallengeDatabase.cs` (16 defs with `kind/args`, `needsSite`, `lanternOnly`; reward
  constants); `Challenges.RollForDay` (host, daily, seeded), `Today`, `Complete(id)` (pays, +10 % views today).
* **Co-op guests (F.3):** `Settings.PlayerKey` (GUID in settings; two windows on one PC derive their own key from
  `-netname`/`-playerkey`); `GameState.players` + per-player `GameState.localRecord`; `PlayerRecords.Local`, `Find`,
  `ForGuest`, `Store`, `StoreFromClient`, `CaptureLocal`, `ApplyToLocal`, `SendLocal` (guests every 60 s and on leaving);
  the host stores its own record on every save. `PlayerRecord` + `coconuts, grilled, snaps, photos, carried`.
* **Achievements / cosmetics:** `Data/AchievementDatabase.cs` (34), `Achievements.Unlock` (host → channel + present
  players' records; clients forward), `ApplyLocal(id)` (idempotent – net message 95 and snapshot diffs end here), hooks
  for first_fish, first_upload, f100…f1m, honest10/clickbait10, golden_camera, shells_all/pages_all, legendary,
  big_bertha, full_crew, roof_delivery, light_fingers, coconut10/grill_master/snap. `Data/CosmeticsDatabase.cs` (ids
  `"hat:6"`, colour arrays, unlock hints, rules) + `Cosmetics.IsUnlocked/Unlock/CheckRules/RememberLocal`.
* **Settings v3:** `fov` (60–100), `headBob`, `cameraShake`, `npcTutorial`, `subtitles`, `introSeen` (true for existing
  players), `normalCompleted` (`DifficultySettings.IsUnlocked/LockedHint`), `playerKey`, `lastCrew`, look fields
  (`cuffColor` α0 = Auto, `pattern, eyes, brows, skin, mouth, maskColor, finsColor, snorkel`), `voiceMode` default OpenMic
  (+ one-time migration), `voiceGateDb −45`, `autoRecordUnderwater`, `musicWhileDiving`, `questDisplay` (0/1/2),
  `unlockedCosmetics`, `achievements`. `Settings.Validate` is **public** and NaN/∞-safe for every value (1e);
  `Settings.Finite/Volume01`; `Settings.Look` (resolved cuff, gear tiers, rank) and `Settings.SetLook(look, cuffAuto)`.
* **Input:** `GameAction.StreamDare` (T), `GameAction.Selfie` (H); `GameInput.HotbarScroll` (+1 next / −1 previous),
  `HotbarScrollLeftHand` (Shift), `SimScroll`, `SelfiePressed`, `StreamDarePressed`; `UIState.TextInput` blocks every
  action except Pause. `Hands.HandleInput` already cycles the hotbar with the wheel (`Hands.Cycle`), not while aiming, in
  the selfie view or while carrying; `HoldItem.QuestItem` + `Hands.IsHotbarItem`.
* **Other facades:** `UIState.CharacterOpen/TextInput/SelfieOpen`, `GameLayers` (+ masks), `CharacterScreen` (+ `LookSaved`,
  `RaiseClosed`, `IsOpen`), `ICharacterPreview`, `CharacterPreviewFactory`, `BoatTelemetry`, `ViewfinderInfo` (+
  `BracketNew[]`, `Clear()`), `SelfieState`, `LadderInfo` (+ `Wood/Rope/Metal`), `FaunaEventInfo` (`Start/End/EventAt`),
  `LiveState` (+ `PeakViewers`, `NoSignal`, `Reset()`, rule constants), NPC bus `INpc`, `NpcRegistry`, `Dialogue`
  (`Resolve` set by npc, `Say`, `PlayLocal` for net, `LineSpoken`), `DifficultySettings` + `unconsciousSeconds`,
  `reelGreenMin/Max`, `snapTimeMultiplier`, `streamFaintLoss`.
* **CoreCommands keys:** J.3 + J.3b keys exist (`BoatHomeKey` handled by the Boat module, `StreamStart/StreamEnd/Dare` by
  livestream via `RegisterCustom`); every other key is handled in core and cannot be overridden. Extra: `SleepVoteKey`.
* **GameEvents extras:** `StoryFlagSet`, `ResearchChanged`, `FindTaken`, `ChallengeCompleted`, `CarryChanged`,
  `DeliveryMishapHappened`. Clients get `StoryFlagSet`, achievement/cosmetic, pantry, finds, deals, challenges, side-quest
  and rank events from the snapshot diff in `GameManager.ApplyHostSnapshot`.
* **Tests:** AutoPlaytest step **27b** exercises these rules (settings NaN, K.1, Sealog photo/sightings + depth weights,
  deals overlap/cancel, Fog Isle gate, title cut, pantry/buffs, finds, challenges, quest limit); the site tour skips story
  sites and the buy-everything step skips reward-only items.

---

## K. SECOND PLAYER MESSAGE (g)–(n) *(GDD §18 „Zweite Feedback-Nachricht“ – binding, overrides older answers)*

Engineer tags used in K and L: **core, ui, player, worldfx** (= world+fx), **props, fishboat** (= fishing+boat), **npc**
(= npc+story), **livestream** (now also owns **sponsor deals**), **fauna, net, filming** (NEW 11th engineer: takes
`Filming/*` over from livestream, see J.2).

### K.0 What K and L change in sections 1–J (conflict list)

| Old rule | New rule | Where |
|---|---|---|
| Q29 strict scan (central 70 %, ≥ 8 % size, ≤ 12 m, 1.5 s aimed footage logs the Sealog) | auto-detect every visible animal while recording; the Sealog needs a **photo** | K.2 (A.3 beat 5, I Q30, C.2 follow) |
| Q92 „up to 3 sponsors“; sponsors = daily money per full Sealog page only | max **2 active deals**, no topic overlap; deals = tasks with rewards; page money stays as the base pay (Q89) | K.7 |
| D.4 economy (2 $ starter rate, 25 $ bonus, tank 130 $) | faster early money, verified in a new econsim run | K.1 |
| A.2 Horst waits at the hut on day 1 | a **prologue at the helipad** before the tutorial; more story NPCs | K.5 |
| GDD 2 „Musik wechselt je nach Ort“ – only a menu loop exists | 6 relaxed procedural tracks | K.6 |
| E.3 „one sleeps = all sleep“ | everybody must lie in a bed: „1/2 · 2/2“ | L.9 |
| 1a scale 0.75 | scale **0.62**, cave inside the waterfall cliff, new village terrace | L.5 |
| G.1 „Site / zone“ + depth columns; spawner = per-site only | open-world zones + depth bands for all 55 species | L.1 |
| I Q31 one photo as thumbnail | collage of several animals + own title | L.4 |
| I Q37 toasts with sentences | icon + one short line | L.10 |
| GDD 15.6 „Aufgabe nur kurz einblenden“ | quest line always top centre, small | L.10 |
| H.2 secrets placed on/near trails; 11 secrets | hidden behind climbs/dives/darkness, need clues; 13 secrets | L.6 |
| Art Bible §1.12 „12 radial segments“ | characters much rounder and smoother (world stays faceted) | L.13 |

### K.1 (g) Money comes faster – early game *(core: Progression/Gear values; fishboat: fish prices; livestream: deals; props: finds)*

Verified with a new simulation `scratchpad/econsim_k` (copy of `econsim_core` + section K; 40 seeds, 16 active
minutes per in-game day). Run: `dotnet bin/Release/net8.0/econsim_k.dll 1 16 -1 Plan=1 NationalDealShare=0.25
EncounterMult=1.3 FramingMin=0.55 FramingMax=1.0 StreamMoneyFactor=0.8 StreamFollowerFactor=0.25 ConvBase=0.0058`
(results in `econsim_k/final_solo.txt`, `final_2p.txt`, `final_4p.txt`; the round-2 baseline before K is
`Plan=-1` → `baseline_solo.txt`).

| | Round-2 baseline (D.4) | **With K** |
|---|---|---|
| First tank: fish / real minutes of fishing | 17 / 10.8 min | **10.9 / 7.2 min** (tank ordered ≈ 8.5 min after the prologue) |
| Money per day before 1,000 followers | 71 $ | **258 $** |
| Purchase day (avg): camera t1 / rod t1 / fins t1 / tank t2 / boat t1 | 9 / 6 / 12 / 15 / 18 | **4 / 4 / 5 / 7 / 9** |
| 1,000 / 100,000 followers | 2.4 h / 14.8 h | 2.2 h / 13.2 h |
| **1,000,000 followers** solo / 2 players / 4 players | 39.4 h (no stream) | **38.1 h / 34.8 h / 31.3 h** (incl. daily livestream) |

The long road stays (target 35–45 h, Q47). The K run already contains the K.2 filming rework (more animals count:
encounters ×1.3, quality 0.55–1.0) and the C livestream – that is why the follower conversion has to drop.

**Constants** (each in exactly one place, J.5):

| Constant | Old | New | Owner / file |
|---|---|---|---|
| `Progression.MonetizationFollowers` | 100 (D.4: 0) | **0** | core |
| `Progression.MoneyPerThousandViews` | 4 (D.4: 2 below 100 F) | **4 from video 1** – D.4's `StarterMoneyPerThousandViews` is dropped | core |
| NEW `Progression.StarterBoostMax`, `StarterBoostEnd` | – | **1.5**, **5,000**: upload money × `(1 + 1.5 × max(0, 1 − F / 5,000))` (×2.5 at 0 followers → ×1.0 at 5,000) | core |
| `Progression.StarterBonus` | 25 $ (D.4) | **50 $** with the first resolved video | core |
| `Progression.FollowerConversion` | 0.009 | **0.0058** (compensates K.2 + livestream) | core |
| `Progression.StreamFollowerFactor` / `StreamMoneyFactor` (C.3) | 0.2–0.4 / 0.6–1.0 (to tune) | **0.25 / 0.8** (tuned) | core |
| `tank_t1` price | 130 $ | **100 $** | core (`GearDatabase`) |
| Shore fish `pricePerKg` (D.2 table) | sardine 40, garfish 10, mackerel 14, bream 11, snapper 9 | **× 1.25: 50 / 12.5 / 17.5 / 14 / 11** (shore average ≈ 9.9 $) | fishboat (`FishMarket`) |
| Local sponsor deals | – | from **250 followers** (K.7) | livestream |
| Finds & salvage under water | – | per dive day ≈ house reef 35 $, kelp 90 $, wreck 220 $, blue drop 450 $, trench 900 $ (K.3) | props + core |
| Side quests | – | 24 quests, ≈ 2,700 $ over the first 30 days (L.7) | npc |
| NEW „Angebot des Tages“ | – | one shop item −20 % per day (L.12) | ui + core |

Text changes: Horst beat 3 line 1 → „Ka-tsching! Weiter angeln, bis du 100 $ hast.“ / "Ka-ching! Keep fishing until
you've got $100." · tutorial step 3 detail „100 $“. FinTube payout notification (L.10 style):
„+{n} $ · Starthilfe ×{m}“ / "+${n} · starter boost ×{m}" (m with one decimal, only while the boost is > 1.05).

### K.2 (h) Filming rework – just keep recording; the Sealog needs a photo *(filming; ui: viewfinder; core: Journal API; net: commands)*

**Controls** (all through `GameInput`):

| Situation | Input | Result |
|---|---|---|
| Camera in hand, you go under water | – | recording **starts by itself** after 0.5 s (Settings → Spiel „Unter Wasser automatisch filmen“ / "Auto-record under water", default on) |
| Camera in hand, not aiming | Use (LMB) | recording on/off (also on land – crabs, birds, goats count) |
| Aiming (RMB) | wheel | zoom (camera tier max zoom) – recording keeps running in the background |
| Aiming (RMB) | Use (LMB) or Photo (P) | **photo** (0.6 s cooldown); a Sealog photo when an animal fills the Sealog frame |
| Selfie view | H | K.4 |

**Detection (replaces the FilmShotEvaluator gates).** Every 0.12 s each creature in `CreatureRegistry` (no
hallucinations) is *detected* when all hold:
1. its bounds centre is inside the viewport (5 % margin) – no central-box rule any more;
2. distance to its bounds ≤ `DetectRange = clamp(FXVisibility.Meters × 0.9, 4, 25)` m (= "within visibility");
3. projected size ≥ **2.5 %** of the screen height (**1.5 %** for animals ≤ 12 cm);
4. not occluded (same 2-ray test as today, but only for the 16 best-scoring candidates per tick – fixed arrays, no
   allocations).

Per detected animal and tick a **frame weight** `w = Size × Light × Steady × Centre × Hand × cameraQuality`:
`Size = clamp(extent / 0.12, 0.35, 1)`; `Light` as today; `Steady = lerp(0.7, 1, steadiness)`; `Centre` 1.0 inside
the central 50 % else 0.85; `Hand` 1.0 aiming / **0.9 from the hip** (was 0.6).

**Clips.** Every detected *individual* keeps its own track while recording (as today, `FilmInstanceTrack`): after
**1.0 s** (was 1.5 s) of detection it becomes a `FootageClip` (quality = average `w`, up to 8 s improve it). New
clip fields: `crowd` (most other animals detected at the same time, 0–8), `eventId` (K.3), `selfie` (K.4),
`depth` (m). `ClipValue × (1 + 0.04 × crowd)` (max +32 %), × 1.25 for an event clip, × 1.3 selfie bonus (K.4). The
upload pipeline stays (best clip per species, max 12) – so the video value grows with every species and every size
tier, rarer/bigger = more.
**Tape („Smart-Schnitt“):** only seconds with ≥ 1 detected animal use tape; empty water costs nothing. Tape
lengths stay (90 / 240 / 480 / 900 / 1,800 s).
**Sightings:** an individual that becomes a clip (or is photographed) adds 1 to that species' `sightings` (L.2).

**Photo → Sealog.** The viewfinder shows a **Sealog frame** (central 40 % box, thin white corners). On a photo the
*subject* is the detected animal with the largest projected size whose centre is inside the frame, extent ≥ **10 %**
of the screen height (zoom helps), not occluded, `Light` ≥ 0.35 (lamp at night / below 40 m). Subject found →
`Journal.RegisterPhoto(species, sizeCm, tier, photoQuality, photoId, depth)`: discovered / size record exactly as
before, best photo per species stored (Q36). Photo quality 0..1 = fill factor (ideal 25–60 % of the frame) ×
centring × light × steadiness. No subject → the photo still goes to the gallery, hint „Näher ran oder zoomen“ /
"Get closer or zoom in". Several animals in one photo: only the subject registers, the others count as sightings.
Filming alone **never** registers the Sealog any more (`Journal.RegisterFilmed` stays only as a legacy wrapper).

**Viewfinder feedback** (ui draws, filming publishes `ViewfinderInfo`): small corner brackets around every detected
animal (max 8, colour = size tier; undiscovered species = pulsing `#F2C94C` bracket with „?“). Top-left
„● REC 0:42 · 3“ (fish glyph + number of animals in frame). The Sealog frame turns green with the species name when a
valid subject is inside: „Foto fürs Sealog!“ / "Photo for the Sealog!" (+ „NEU!“ / "NEW!" badge for undiscovered
species). `ShotHint` (I Q30) becomes: `None, Recording, NoAnimals, TooDark, Closer, SealogReady, New`. Hip recording:
a mini line above the hotbar „● REC · 3“.

**Tutorial / Horst** (npc): step 6 becomes **„Fotografiere 3 Arten fürs Sealog“ / "Photograph 3 species for the
Sealog"** (counts Sealog photos; recording runs anyway). Beat 5 lines 2–4 are replaced by:
2. „Kamera auf [1]. Unter Wasser filmt sie von selbst – alles, was schwimmt, zählt.“ / "Camera's on [1]. Under water it films by itself – everything that swims counts."
3. „Fürs Sealog: rechte Maustaste, ranzoomen, Klick – Foto!“ / "For the Sealog: right mouse, zoom in, click – photo!"
4. „Drei Arten fotografieren, gleich vorm Steg. Und: Luft im Blick!“ / "Photograph three species, right off the dock. And: watch your air!"

**Co-op:** detection runs on every machine for its own camera (as today). `Journal.RegisterPhoto` on clients →
`NetCommand.RegisterFilmed` (same command, the args get `photoId` and depth appended – older hosts ignore extras).
Sightings are batched per machine every 10 s → `CoreCommands.SightingsKey` „id:n:minDepth:maxDepth;…“ (host adds).

**Other sections:** C.2 hype target „an animal framed (quality ≥ 0.4)“ → „≥ 1 animal detected“, value term uses
the best detected animal; C.5 dares unchanged (they use detection); B.4 photo quests use the photo subject.

### K.3 (i) More to do under water *(fauna: events; props: finds, caves, currents, stakes; core: data/state; filming: challenge checks; ui: notifications; net: event sync)*

Five systems, mixed per site. Rule of thumb: **something to do every 2–3 minutes of diving**.

**1. Animal events** (host-authoritative `FaunaEvents`, net message `FaunaEvent 101`). First event 60–120 s after a
diver enters a zone (≥ 3 m deep), then every 150–300 s of dive time; max 1 per zone; 60–120 s long; the actors
spawn 25–40 m away out of view and come in. Clips taken during the event within 20 m of its centre get its `eventId`
(× 1.25 value, action +0.3). Notification (L.10): event glyph + name, e.g. „Fressrausch!“.

| Event | DE / EN | Sites | What happens |
|---|---|---|---|
| feeding_frenzy | Fressrausch / Feeding Frenzy | kelp, wreck, blue drop, open sea | a bait ball of 80–120 sardines; 2–4 hunters (sea lions at the kelp, barracudas at the wreck, yellowfin + dolphins in the blue) dart through it |
| cleaning_station | Putzerstation / Cleaning Station | house reef (Gerald), wreck (grouper), blue drop (manta), Fog Isle | a big animal holds still at a bommie for 90 s while 6–10 cleaner wrasse work on it – perfect close-ups |
| big_school | Riesenschwarm / Big School | house reef (blue tang 150), wreck (barracuda tornado 40), open sea (sardines 250), blue drop (hammerhead school 6, rare) | a huge school passes or circles the diver |
| encounter | Begegnung / Encounter | house reef (Gerald or a ray swims up curious for 40 s), lagoon (eagle rays in formation), blue drop (manta circles you) | a calm, close meeting |
| predator_hunt | Jagdszene / Hunt | from **day 3** (GDD 14.4): kelp (leopard sharks), wreck (barracuda), blue drop (hammerhead); house reef only „Krake gegen Krabbe“ (cute) | scripted chase, 2–3 lunges, 50 % "catch" = prey vanishes in a puff of scales, no gore |
| night_glow | Leuchtnacht / Glow Night | lagoon, house reef, Fog Isle – 21:00–04:00 | bioluminescent sparkles around moving divers and fins, jellies rise |
| whale_song | Walgesang / Whale Song | blue drop | Big Bertha passes singing (guaranteed once while not yet in the Sealog) |
| deep_parade | Tiefenparade / Deep Parade | trench | glowing lanternfish and vampire squid rise in a column |

**2. Finds and salvage** (props place, core keeps `GameState.finds`): every day (seed = save seed + day + site) each
site has small finds in sand mounds that glint within 6 m (E „Graben“ / "Dig", 1.5 s dust) and heavy salvage. Sold
in the Fish Market app, new tab „Funde“ / "Finds".

| Site | Small finds / day (value) | Heavy salvage / day (value) |
|---|---|---|
| Lagoon (main island) | 3 (3–12 $) | – |
| House reef | 4 (5–25 $) | 1 (40–80 $) |
| Kelp forest | 4 (10–40 $) | 1 (90–160 $) |
| Wreck | 5 (20–60 $) | 2 (150–300 $) |
| Blue drop | 3 (40–120 $) | 1 (300–500 $) |
| Trench | 3 (100–300 $) | 1 (600–1,200 $) |
| Fog Isle | 4 (30–90 $) | 1 (200–400 $) |

Small finds: coins, sea glass, shark teeth, lost sunglasses, a ring (rare), a pearl (rare), **a lost action cam**
(rare: adds a ready-made funny clip, value 30, to the dive's footage). Heavy salvage: ship's bell, amphora, outboard
motor, 1980s TV set, ship's wheel, small cannon, brass diving helmet, sea chest (rare). Heavy items are **carried**
(both hands; the camera hangs on its chest strap and pauses recording): swim speed ×0.55, sinks 0.15 m/s
unless you swim up, air use ×1.2; two divers carrying together ×0.85 each (co-op spot, Q56). Deliver with E at the boat
or dock ladder. Shop upgrade **„Bootswinde“ / "Boat Winch"** (boat equipment, 400 $, 250 followers): E on the item
anywhere ≤ 40 m from the boat hooks it; it is winched aboard when you are back on deck.

**3. Photo challenges** (core data `Data/ChallengeDatabase.cs`, filming checks photos/clips): 2 per day (shared,
seeded), shown in the Tasks app and on a sign at each site buoy. Reward `clamp(0.3 × ExpectedUploadMoney(F), 40,
400)` $ + „+10 % Aufrufe heute“ / "+10 % views today".

| id | DE / EN |
|---|---|
| ch_ray_top | „Ein Rochen von oben“ / "A ray from above" (camera pitch < −45°) |
| ch_three | „3 Arten auf einem Foto“ / "3 species in one photo" |
| ch_macro | „Winzling ganz groß (≥ 50 % Bild)“ / "Tiny one, huge (≥ 50 % of the frame)" |
| ch_school20 | „Schwarm: 20+ Fische im Bild“ / "School: 20+ fish in frame" |
| ch_lamp_night | „Tier im Lampenlicht bei Nacht“ / "Animal in lamplight at night" |
| ch_turtle_surface | „Gerald beim Luftholen“ / "Gerald taking a breath" (turtle within 2 m of the surface) |
| ch_shark_eye | „Hai auf Augenhöhe (≤ 4 m)“ / "Shark at eye level (≤ 4 m)" |
| ch_selfie_reef | „Selfie mit 5 Tieren“ / "Selfie with 5 animals" |
| ch_record | „Ein neuer Größenrekord“ / "A new size record" |
| ch_legend_tier | „Ein Riesig- oder Legendär-Tier“ / "A Huge or Legendary animal" |
| ch_deep_glow | „Leuchtendes Tier unter 40 m“ / "Glowing animal below 40 m" |
| ch_event | „Ein Foto während eines Tier-Events“ / "A photo during an animal event" |
| ch_land | „Ein Inseltier aus 2 m“ / "An island animal from 2 m" |
| ch_village | „Laternen auf dem Wasser“ / "Lanterns on the water" (L.8) |
| ch_wreck_inside | „Tier im Wrack“ / "Animal inside the wreck" |
| ch_dolphin_bow | „Delfin an der Bugwelle“ / "Dolphin at the bow" |

**4. Research tasks** (npc extends Dr. Fin's pool B.4; props place **3 yellow research stakes per site**, E
„Probe nehmen“ / "Take sample", 2 s): `fin_samples_<site>` „3 Wasserproben am {site}“ / "3 water samples at {site}"
(150–1,500 $ by site), `fin_census_star` „Zähle 10 Seesterne“ / "Count 10 starfish" (sightings), `fin_depth_grouper`
„Wie tief lebt der Zackenbarsch?“ / "How deep does the grouper live?" (photo below 25 m), `fin_listen_whale`
„20 s beim singenden Wal bleiben“ / "Stay 20 s with the singing whale" (≤ 30 m).

**5. Currents and caves to explore** (props: volumes and geometry; player: current force; worldfx: particles):

| Site | Cave / swim-through | Current |
|---|---|---|
| House reef | coral arch swim-through (2 m) + „Krakenhöhle“ / "Octopus Den" overhang | „Riffkanal“ / "Reef Channel": a gentle 0.6 m/s lane between two bommies (40 m fun ride) |
| Lagoon | 3 rowboat wrecklets, a sunken jetty | – |
| Kelp | „Kelptunnel“ / "Kelp Tunnel" (cleared path 30 m) + „Seelöwen-Grotte“ / "Sea Lion Grotto" with an air pocket (GDD 14.6) | surge zone 0.8 m/s, sways the kelp |
| Wreck | interior (H.1) + anchor-chain trail | 0.7 m/s along the hull (GDD 14.8) |
| Blue drop | „Blaue Kammer“ / "Blue Chamber" cave at 45 m in the wall | drift current 1.2 m/s along the wall (drift dive) |
| Trench | lava tube at 85 m with glowing creatures (and the M4 antenna) | – |
| Fog Isle | „Nebelgrotte“ / "Fog Grotto": reef-wall cave at 22–28 m with an air-pocket chamber (L.6 secret, coelacanth) | 0.5 m/s through the south channel |

Currents push the diver (player: `CurrentZone` force, max 1.4 m/s, never against the exit of a cave), with streaming
particles; caves get the white guideline rope (GDD 14.6) and stay inside the 10 m voice range rule.

### K.4 (j) Selfie view on H *(player: camera + own avatar; filming: selfie detection/bonus; ui: frame; net: pose sync; core: input)*

* New `GameAction.Selfie` (appended, default **H**, rebindable, listed in Settings → Steuerung as „Selfie-Ansicht“ /
  "Selfie view"). Works **on land and under water**; needs the camera on the hotbar (it is taken into the right
  hand automatically). Without a camera: notification „Kamera auf die Hotbar legen“ / "Put the camera on your
  hotbar". H again, Esc or a menu leaves the view.
* View: a selfie camera at arm's length 1.1 m in front of the face, looking back; the mouse orbits it on a sphere
  around the head (yaw ±70°, pitch −30°…+40°), the wheel sets the arm length 0.8–1.4 m; WASD still moves at ×0.6.
  The first-person hands are hidden; the player's **own bean diver** (`DiverModels.Build(look)`, LOD0, local only) is
  shown, holding the camera toward the lens.
* **Expressions:** automatic (smile; scared when a predator is within 5 m; wow on a new species/Legendary; laughing
  on mischief) or by hand with the hotbar keys while in selfie view (hotbar switching is paused): 1 „Grinsen“ /
  "Grin", 2 „Staunen“ / "Wow", 3 „Schreck“ / "Scared", 4 „Kussmund“ / "Kissy", 5 „Zwinkern“ / "Wink", 6 „Lachen“ /
  "Laugh".
* Recording/photos work as in K.2 (Use = record, Photo/RMB = photo). Animals detected in the selfie frame make clips
  with `selfie = true`: **×1.3 views** when the diver and ≥ 1 animal are in frame (Q14 „Reaktions-Bonus“), another
  ×1.15 when the expression fits (Scared + predator in frame, Wow + new species or Huge/Legendary). Gerald in a selfie
  photo → achievement *Gerald & ich*.
* Co-op: the avatar shows the camera-out pose and the expression to everyone (2 extra bytes in the avatar state).
* UI: a rounded selfie frame instead of the camcorder frame, hint line „H – zurück · 1–6 Gesicht“ / "H – back · 1–6
  face".

### K.5 (k) Story NPCs and the pre-game prologue *(npc; props: hangar, letter; ui: letter card; player: arrival)*

**Why you are here** (the line every NPC repeats in their own way): Uncle Gus left you his island „Rostbucht“ / "Rust
Cove", his hut and his channel. In 1987 he filmed the live documentary „Live aus der Tiefe“ / "Live from the Deep" for
the station „Kanal Tiefsee“ / "Deep Sea TV"; the underwater studio vanished in the trench, nobody believed his story
about the giant eye. His wish: grow the channel to **1,000,000 followers**, fill the **Sealog**, and finish his
documentary **live from the deep**. The Tasks app gets a fixed header „Hauptziele“ / "Main goals" with these three
lines.

**Story cast** (all babble voices + subtitles, A.1 rules):

| NPC | Role | Section |
|---|---|---|
| Hafenmeister Horst | **pre-game NPC** (prologue), tutorial, comic relief | A, K.5 |
| Onkel Gus | radio calls + missions | B.1–B.2 |
| **Pilotin Wanda Wirbel** / Pilot Wanda Whirl (NEW) | flies you in, runs the Chopper Shop, explains deliveries, mishap calls | K.5, L.12 |
| Dr. Marina Fin | research | B.4 |
| Chad & Brad | rivals | B.3 |
| **Großmutter Perle** / Grandma Pearl (NEW) | village elder: island history, the night of 1987, secret clues | L.8 |
| **Toni Tonband** / Tony Tape (NEW, later) | stranded NPC, sound engineer of the 1987 documentary | K.8 |
| Village residents (NEW) | side quests | L.7, L.8 |

**Prologue (before the game really starts – new game only, skippable with hold E):** after the intro (Q42) and the
character screen, Wanda's chopper lands on the helipad (in-engine, 25 s; the player sits in the cabin and steps out).
`PlayerSpawn` for a new game = `Helipad + (4, 0, 6)`; later days start at the bed as today.

* P1 – Wanda (on the pad, rotor still turning): „Endstation Rostbucht! Gepäck? Ach, du hast keins. Sehr modern.“ /
  "Last stop, Rust Cove! Luggage? Oh, you don't have any. Very modern." · „Ich bin Wanda. Was du im Shop bestellst,
  bring ich. Meistens heil.“ / "I'm Wanda. Whatever you order in the shop, I deliver. Mostly in one piece."
* P2 – Horst (comes running, out of breath, holds an envelope): „Moin! Du bist Gus' Erbe! Willkommen auf Rostbucht!“ /
  "Ahoy! You're Gus's heir! Welcome to Rust Cove!" · „Gus hat dir einen Brief dagelassen. Ich hab ihn NICHT gelesen.
  Zweimal.“ / "Gus left you a letter. I did NOT read it. Twice."
* Letter card (ui, paper style, Gus' handwriting font fallback Fredoka; E = close): „Kind, die Insel gehört jetzt dir.
  Die Hütte, das Boot, mein Kanal. 1987 hab ich eine Live-Doku aus der Tiefe gedreht – dann war das Studio weg, und
  keiner hat mir geglaubt. Mach du sie fertig: Bring den Kanal auf eine Million, füll das Sealog, und geh live da
  runter. Horst hilft dir. Glaub ihm nur die Hälfte. – Gus“ / "Kid, the island is yours now. The hut, the boat, my
  channel. In 1987 I shot a live documentary from the deep – then the studio was gone and nobody believed me. You
  finish it: get the channel to a million, fill the Sealog, and go live down there. Horst will help. Believe half of
  what he says. – Gus"
* P3 – Horst: „Eine Million! Das schaffen wir. Ich hab schon 12 Follower. Drei davon sind echt.“ / "A million! We can
  do it. I've got 12 followers already. Three are real." · „Hier sind deine Aufgaben. Hauptziel oben, Kleinkram
  darunter.“ / "Here are your tasks. Main goal on top, small stuff below." · „Erst mal zur Hütte. Folg mir!“ / "First,
  to the hut. Follow me!" → Horst walks to the hut (helipad trail, 62 m), A.3 beat 0 continues there (its line 1–2
  are dropped; lines 3–4 stay).
* Gus' first radio call (B.1, new milestone „0“, plays when the player reaches the hut): „Gus hier! Bist du gut
  gelandet? Wanda fliegt wie ich koche – mit Gefühl und Feuerlöscher.“ / "Gus here! Landed safely? Wanda flies like I
  cook – with feeling and a fire extinguisher."

**Story beats of the new NPCs** (all ≤ 6 lines, DE/EN in `Data/DialogueDatabase.cs`): Pearl at the first village
visit tells the night of 1987 (L.8); Wanda after the 3rd delivery: „Gus hat mich damals für die Doku gebucht. Ich hab
das Studio als Letzte gesehen. Von oben. Es hat … geblinkt.“ / "Gus booked me for the documentary back then. I was
the last to see the studio. From above. It … blinked."; Toni (K.8) explains the eye and the lost tapes.

### K.6 (l) Relaxed background music *(worldfx: FXAudio, FXAudioSynth, new FX/FXMusicDirector.cs; core: Settings toggle)*

All procedural (extends the existing Karplus-Strong ukulele + marimba synth), chill, no drums except a soft shaker.

| Track | When | Tempo / key / chords | Instruments | Loop |
|---|---|---|---|---|
| `menu` „Strandhütte“ | main menu, character screen, prologue | 104 BPM, C – Am – F – G7 (today's loop, extended from 8 to 32 bars with 2 melody variations) | ukulele strum, marimba melody, round bass, shaker | 74 s |
| `day` „Inseltag“ | land/boat 07:00–17:00 | 92 BPM, F – Dm – B♭ – C | fingerpicked ukulele, soft marimba, upright bass, shaker, bird chirps | 84 s |
| `evening` „Goldene Stunde“ | 17:00–20:30 | 80 BPM, Dmaj7 – Bm7 – Gmaj7 – A6 | slow uke strum, warm pad, vibraphone | 96 s |
| `night` „Mondlagune“ | 20:30–06:30 | 66 BPM, Am9 – Fmaj7 – Cmaj7 – G6 | soft pad, kalimba / music-box notes, low bass hum | 116 s |
| `water` „Blaues Schweben“ | under water above 30 m | 72 BPM, E♭maj7 – Fadd9 (lydian) | airy pad with a slow filter sweep, low-passed marimba, sparse bells | 107 s |
| `deep` „Tiefenstille“ | under water below 30 m | 54 BPM, drone on D | low pad, distant chimes, no rhythm | 142 s |

(All loops are 32 bars in 4/4.) Rules: 22,050 Hz mono, rendered on a worker thread (`Task.Run`, as the menu loop
today) into **16-bit `short[]` buffers** played by a streaming `AudioClip` whose PCM callback copies from the buffer
(no allocations on the audio thread); **max 2 tracks in memory** (current + next, ≤ 13 MB) – a context change renders
the next track (≤ 1.5 s) and crossfades over 4 s. After 2 full
loops (not in the menu) the music rests 30–60 s (ambience only). Volume = `Settings.music` × 0.55; under water the
FX low-pass applies; **ducks −6 dB** under NPC lines/radio and −9 dB during a livestream (chat sounds). Predators do
not change the music (it stays chill). Settings → Audio: new toggle „Musik beim Tauchen“ / "Music while diving"
(default on). Juno's quest J1 (L.7) unlocks a 7th menu variation „Altriff-Lied“ / "Oldreef Song" (marimba + whale
motif).

### K.7 (m) Sponsor deals – tasks, max 2, no topic overlap *(livestream: logic + data; ui: app; core: state + commands; net: snapshot)*

* **Unlock:** local sponsors from **250 followers**, national from **5,000** (GDD), big brands from **50,000**. The
  daily base pay per full Sealog page (`SponsorPerPage`, Q89) stays.
* **App „Barnacle“** (the sponsor app of GDD §9; icon handshake, `#3FA9A0`, screen header „Deals“): offers arrive as notifications
  (L.10) „Neues Angebot · Krabbenkeks Co.“ / "New offer · Crabby Cookies"; max **3 open offers** (each expires after
  2 in-game days); **max 2 active deals**.
* **Topics:** every deal has 1–2 tags from `Hai, Schildkröte, Rochen, Krake, Schwarm, Wrack, Nacht, Tiefe, Angeln,
  Livestream, Insel, Korallen, Wal` (EN: shark, turtle, ray, octopus, school, wreck, night, depth, fishing, livestream,
  island, coral, whale). **Two active deals may not share a tag.** An overlapping offer shows a disabled button with
  „Überschneidung mit „{deal}“ ({tag}). Erst kündigen.“ / "Overlaps with "{deal}" ({tag}). Cancel it first." – even
  if it pays more.
* **Cancel:** button „Kündigen“ / "Cancel deal" → dialog „Deal kündigen? Kein Geld, und {sponsor} ist 3 Tage
  beleidigt.“ / "Cancel the deal? No money, and {sponsor} sulks for 3 days." – that sponsor sends no offers for 3
  days, nothing else.
* **Reward** = `round5(max(70, share × ExpectedUploadMoney(F)) × days × k)` with share **0.45** below 5,000
  followers, **0.25** from 5,000; `k` = 0.8 easy / 1.0 normal / 1.4 hard. (econsim: 2 slots ≈ +0.38 × upload money per
  day at 75 % completion.) While a deal runs, its fantasy logo shows on the boat flag and the suit's shoulder (Q90).
* **Fail** (deadline missed or a „no …“ rule broken in an upload): deal ends, no money, angry mail „Enttäuscht. –
  {Sponsor}“ / "Disappointed. – {Sponsor}" (Q91).
* Co-op: one shared deal list on the host; any player may accept/cancel (`CoreCommands.DealKey`); progress counts all
  players.

**Sponsors** (fictional, never real brands): local – „Horsts Fischbrötchen“ / "Horst's Fish Rolls", „Wandas
Wirbel-Lieferdienst“ / "Wanda's Whirl Delivery", „Altriff Webstube“ / "Oldreef Weavers"; national – „Krabbenkeks Co.“
/ "Crabby Cookies", „Flossenfix“ / "FinFix", „SharkSoda“, „Schildi-Schlafanzüge“ / "TurtleTogs", „Glubsch-Optik“ /
"Goggle Optics", „Tentakel Tours“ / "Tentacle Tours", „Mantastic Sonnencreme“ / "Mantastic Sunscreen"; big – „DeepByte
Kameras“ / "DeepByte Cameras", „Blubber Energy“.

**Deal templates** (`Data/SponsorDatabase.cs`; condition types checked by the livestream engineer's `SponsorService`
on the host: FilmSpecies, NoTagInUpload, PhotoTag, DiveDepth, NightDive, UploadStyle, StreamMinutes, CatchFish,
VisitSite, SelfieWith, EatFood):

| id | Sponsor | Tags | Condition | Days | k |
|---|---|---|---|---|---|
| d_fishroll | Horsts Fischbrötchen | Angeln | catch 8 fish | 1 | 0.8 |
| d_wanda_express | Wandas Wirbel | Insel | order 1 delivery and film the chopper landing | 1 | 0.8 |
| d_weavers | Altriff Webstube | Insel, Korallen | photograph 3 coral-reef species at the house reef | 2 | 1.0 |
| d_crabby | Krabbenkeks | Insel | film 3 crabs (any crab species) | 1 | 0.8 |
| d_shark3 | SharkSoda | Hai | 3 shark clips in one upload | 3 | 1.0 |
| d_noshark | Schildi-Schlafanzüge | Schildkröte, Hai | upload with Gerald and **no** shark | 2 | 1.0 |
| d_turtle_selfie | Schildi-Schlafanzüge | Schildkröte | selfie with Gerald | 2 | 1.0 |
| d_ray_photo | Mantastic | Rochen | photograph 2 ray species | 3 | 1.0 |
| d_manta | Mantastic | Rochen, Tiefe | film a manta deeper than 20 m | 3 | 1.4 |
| d_octo | Tentakel Tours | Krake | film an octopus or cuttlefish for 8 s | 2 | 1.0 |
| d_school | Flossenfix | Schwarm | clip with ≥ 20 fish detected | 2 | 1.0 |
| d_deep30 | Flossenfix | Tiefe | 60 s below 30 m | 2 | 1.0 |
| d_night | Glubsch-Optik | Nacht | dive 3 min between 21:00 and 04:00 | 2 | 1.0 |
| d_wreck_inside | DeepByte | Wrack | 3 animals photographed inside the wreck | 3 | 1.4 |
| d_honest | DeepByte | Livestream | 2 uploads with honest titles | 3 | 0.8 |
| d_stream20 | Blubber Energy | Livestream | stream 8 min with ≥ 200 viewers | 2 | 1.4 |
| d_whale | Blubber Energy | Wal | film Big Bertha or the whale shark | 4 | 1.4 |
| d_snack | Krabbenkeks | Insel | eat 2 Crabby snacks (pantry) and upload that day | 1 | 0.8 |

Strings: app header „Deals“; slot labels „Aktiv 1/2“ / "Active 1/2"; „Annehmen“ / "Accept"; „Läuft noch {n} Tage“ /
"{n} days left"; done notification „Deal erfüllt · +{n} $“ / "Deal done · +${n}".

### K.8 (n) The stranded NPC Toni and the Fog Isle *(npc: NPC + chain; props: raft, camp, buoys, Fog Isle props; worldfx: terrain + fog bank; fauna: 5 exclusive species; core: layout/site/flags; fishboat: fog current; ui: map)*

**Who:** **„Toni Tonband“ / "Tony Tape"**, ~65, the sound engineer of the 1987 documentary. He overslept the final
broadcast, has lived on a tiny foggy island since, recording "singing fish" – and threw the messages in a bottle
(H.2). Bean NPC: white beard to the belly, sun-bleached headphones made of coconut shells, patched Hawaiian shirt
`#3FA9A0`, cassette recorder on a strap, bare feet, sunburnt nose. Babble: 120 Hz, 9 syll/s, hums between lines.

**Trigger:** the first morning with **day ≥ 10 and ≥ 2,500 followers** (fallback: day 16). Horst radios: „Da liegt
einer am Strand! Mit Bart! Lebt er? Er summt.“ / "There's someone on the beach! With a beard! Is he alive? He's
humming." Toni lies next to a wrecked raft on the new **„Treibholzstrand“ / "Driftwood Beach"** at `WorldLayout.
DriftwoodBeach = (60, 0, 100)` (probe: sand 0.6 m, 11°; raft in the shallows at (66, 0, 104)). Map marker + quest line.

| Step | DE / EN | Task |
|---|---|---|
| T1 | „Schiffbrüchig!“ / "Castaway!" | talk to him |
| T2 | „Durst und Hunger“ / "Thirsty and hungry" | bring a coconut (shake a palm, carry it – L.7 carry item) and a grilled fish (grill with the quest active → „Grillfisch zum Mitnehmen“) |
| T3 | „Das Tonbandgerät“ / "The tape recorder" | his recorder sank 30 m off the beach at 3–4 m: find it (glint within 6 m) and carry it back |
| T4 | „Funk für Toni“ / "A radio for Toni" | 3 parts for his radio: a valve (radio tower shack), a copper coil (outboard motor of a rowboat wrecklet in the lagoon, 3 m – reachable without a deep tank) and a battery („Funkbatterie“, Chopper Shop 80 $, category „Teile“) |
| Reward | „Tonis Karte“ / "Toni's Map" | `StoryFlags.Set("fog_map")`: the Fog Isle appears on the sea chart, a line of 6 yellow buoys marks the way, toast „Neue Insel auf der Seekarte!“ / "New island on the sea chart!" |

Afterwards Toni lives in the village guesthouse (L.8) and gives quests N1/N2 (L.7); from then on he sits at his camp
on the Fog Isle 10:00–16:00.

Lines (selection): T1 „Ist das … Festland? Nein? Gut. Festland ist laut.“ / "Is this… the mainland? No? Good. The
mainland is loud." · „Ich bin Toni. Ton-Mann. 1987, „Live aus der Tiefe“. Ich hab den letzten Drehtag verschlafen.“ /
"I'm Tony. Sound guy. 1987, 'Live from the Deep'. I overslept the last day of shooting." · T4 done: „Hör mal: Auf
meiner Insel singen die Fische. Da gibt's Tiere, die gibt's sonst nirgends.“ / "Listen: on my island the fish sing.
There are animals there that exist nowhere else." · „Und das Auge … das Studio hat nicht geblinkt. Das Auge hat
geblinkt.“ / "And the eye… the studio didn't blink. The eye blinked."

**The Fog Isle („Nebelinsel“ / "Fog Isle")** – verified in `scratchpad/design2_l` (probe `out/r3.txt`):
* Island `WorldLayout.FogIsland = (−60, 0, 700)`: landmark-island parameters shore radius 30, peak 16, steepness 1.2,
  rocky 0.45 → 2,738 m² land, peak 16.4 m.
* Dive site `fog_isle`: centre **(−60, 0, 640)**, radius **85**, depth **4–28 m** (`FogDepth`: seamount lagoon 5 → 26 m,
  a horseshoe reef crest at 2.5 m 58 m from the centre, open to the south as a boat channel). Measured 0–26.5 m.
* Collision check: gap to the kelp site 225 m, to the kelp island 496 m, deep trench site 545 m, house reef 461 m;
  dock end → site 665 m (wreck 504 m, blue drop 930 m); open water on the way 2–30 m deep. The terrain border blend
  (|z| ≥ 860 m) starts 130 m behind the island's north shore.
* `DiveSite { id = "fog_isle", nameDe = "Nebelinsel", nameEn = "Fog Isle", radius 85, minDepth 4, maxDepth 28,
  followersNeeded 0, prevSiteId null, unlockFlag "fog_map", mapColor #F28CBE, descDe „Tonis Insel. Hier singen die
  Fische – und manche gibt's nur hier.“, descEn "Tony's island. The fish sing here – and some live nowhere else." }`.
  New `DiveSite.unlockFlag` (core): a site with a flag unlocks when the flag is set (no Sealog/follower rule).
* **Fog bank** (worldfx + fishboat): ring of fog radius 260 m around the island (FX fog volume, the island is
  invisible from outside). Without `fog_map`, inside the ring the boat drifts outward at 2 m/s and the compass spins;
  notification „Dichter Nebel – ohne Karte kommst du nicht durch.“ / "Thick fog – you won't get through without a
  map." With the map: the buoy lane from (−60, 420) to (−60, 560) stays clear (30 m wide), the fog thins to a light
  haze.
* On the island (props): Toni's camp (hut of driftwood, hammock, tape reels on strings, a satellite dish made of a
  wok), a rest spot with an air station (E.4), a small beach on the south side, the Nebelgrotte entrance (K.3).
* **Exclusive species** (fauna; spawn only in the `fog_isle` zone; Sealog page „Nebelinsel“ / "Fog Isle"):

| id | DE / EN (latin) | Depth m | Size cm | Rarity | Base | Plan / behaviour | Colours |
|---|---|---|---|---|---|---|---|
| pygmy_seahorse | Zwergseepferdchen / Pygmy Seahorse (*Hippocampus bargibanti*) | 8–25 (on sea fans) | 1.5–2.7 | Rare | 180 | Seahorse, BottomDweller, 1 | `#F28CBE` / `#F2C94C` / `#D9455A`, Spots |
| mandarinfish | Mandarinfisch / Mandarinfish (*Synchiropus splendidus*) | 2–15, dusk 17:30–20:30 only | 4–7 | Uncommon | 90 | Fish, Skittish, 1–2 | `#2F6FD6` / `#F28C4A` / `#2EC4A0`, HorizontalStripes |
| flamboyant_cuttle | Flamboyant-Sepia / Flamboyant Cuttlefish (*Metasepia pfefferi*) | 4–25 | 5–8 | Rare | 160 | Cuttlefish, Crawler (walks on the sand), 1 | `#7A3E8E` / `#F2C94C` / `#D9455A`, Mottled, flashes colour |
| ribbon_eel | Geistermuräne / Ribbon Eel (*Rhinomuraena quaesita*) | 3–25 | 65–130 | Uncommon | 110 | Eel, Ambush (no bump), 1 | `#2F6FD6` / `#F2C94C` / `#1E1B2B`, Solid |
| coelacanth | Quastenflosser „Opa Fossil“ / Coelacanth "Grandpa Fossil" (*Latimeria chalumnae*) | 22–28, Nebelgrotte only | 150–200 | Legendary | 1,200 | Fish/"grouper", Drifter, unique | `#2E3E5C` / `#8EA6C4` / `#F4EFE6`, Spots, slight glow |

Fun facts: pygmy seahorse „Sieht genau aus wie die Koralle, auf der es wohnt – Tarnung Stufe 100.“ / "Looks exactly
like the coral it lives on – camouflage level 100." · mandarinfish „Kommt nur in der Dämmerung raus. Zum Tanzen.“ /
"Only comes out at dusk. To dance." · flamboyant cuttle „Läuft lieber, als zu schwimmen – auf seinen Armen.“ /
"Prefers walking to swimming – on its arms." · ribbon eel „Wechselt im Leben die Farbe und sogar das Geschlecht.“ /
"Changes colour – and even sex – during its life." · coelacanth „Galt 66 Millionen Jahre als ausgestorben. Hat sich
nur versteckt.“ / "Was thought extinct for 66 million years. Was just hiding."

### K.9 Ownership for K (who builds what)

| Engineer | K items |
|---|---|
| **core** | K.1 constants; `Journal.RegisterPhoto`, `AddSightings`, JournalEntry/FootageClip fields; `GameAction.Selfie`; `UIState.TextInput`; `DiveSite.unlockFlag` + `fog_isle` site; `WorldLayout.FogIsland/DriftwoodBeach/Village/HangarSpot`; `StoryFlags` keys; `GameState` v4 (finds, challenges, sponsor state, quest state, carried item); `CoreCommands` keys (SightingsKey, DealKey, FindKey, ChallengeKey, QuestKey, CarryKey); `Data/ChallengeDatabase.cs`; `Data/FindsDatabase.cs`; econsim_k re-runs; AutoPlaytest steps |
| **ui** | K.2 viewfinder (brackets, Sealog frame, REC line), K.3 notifications + Finds tab, K.4 selfie frame, K.5 letter card + main-goals header, K.6 settings toggles, K.7 Deals app |
| **player** | K.2 auto-record trigger + photo input routing, K.3 carry + current force, K.4 selfie camera + own avatar, K.5 prologue arrival (chopper cabin pose, spawn) |
| **worldfx** | K.6 music director + 6 tracks; K.8 Fog Isle terrain (`FogDepth`, mini island) + fog bank volume; K.3 current particles, night-glow plankton |
| **props** | K.3 finds/salvage mounds, research stakes, site buoy signs, caves/swim-throughs/current volumes, lagoon wrecklets; K.5 hangar shack + letter prop; K.8 raft, Toni's camp, Fog Isle props, buoy lane |
| **fishboat** | K.1 shore prices; K.3 boat winch + salvage delivery at the boat; K.8 fog-bank drift + compass |
| **npc** | K.2 tutorial step 6 + beat 5 lines; K.5 prologue, Wanda, letter, main goals, Gus call 0; K.8 Toni + chain T1–T4; K.3 Dr. Fin research additions |
| **livestream** | **K.7 sponsor deals** (`Stream/SponsorService.cs`, `Data/SponsorDatabase.cs`); C tuning with K.1 values |
| **fauna** | K.3 animal events (`Fauna/AI/FaunaEvents.cs`), K.8 five Fog Isle species (meshes, behaviours, Nebelgrotte coelacanth) |
| **net** | `FaunaEvent 101`; selfie pose bytes in the avatar state; RegisterFilmed args; deal/quest/finds state in the snapshot; carry sync |
| **filming** | K.2 detection, clips, crowd/event/selfie bonus, smart tape, photo subject + Sealog photo, sightings batching, challenge checks (K.3), selfie recording (K.4) |

---

## L. THIRD PLAYER MESSAGE (o)–(ac) *(GDD §18 „Dritte Nachricht“ – binding, overrides older decisions)*

### L.0 Summary of what changes

Open-world fish with depth bands (L.1), Sealog counters (L.2), much better reefs (L.3), own video titles + collage
thumbnails (L.4), an even smaller and denser island at **0.62** (L.5), well-hidden secrets (L.6), 24 side quests
(L.7), the village of Altriff (L.8), the sleep vote (L.9), the quest line + clean notifications (L.10), island animals
(L.11), the new Chopper Shop + mishaps (L.12), animations and much smoother characters (L.13). The conflict list is in
K.0.

### L.1 (o) Fish everywhere: open-world spawning with depth bands *(fauna: spawner; core: SpeciesDef fields + zones; props: habitat anchors)*

**Zones.** `FaunaZones.ZoneAt(position)` (fauna, cached per 16 m grid cell, deterministic) returns one zone:

| Zone id | Where |
|---|---|
| a site id (`house_reef`, `kelp_forest`, `wreck`, `blue_drop`, `deep_trench`, `fog_isle`) | inside the site radius **+ 60 m** (`ZoneMargin`, was `SiteFalloff` 150), or within 70 m of that site's landmark island shore |
| `lagoon` | main island: r < `IslandRadius` + 90 m and seafloor ≤ 8 m deep (outside the house reef zone) |
| `open_sea` | any other water, spawn point less than 40 m deep |
| `deep_sea` | any other water, spawn point 40 m deep or more |
| `island` | land (ground above sea level) – the land fauna of L.11 |

**Who may spawn.** A species is eligible at a spawn point when (1) its `zones` contain the zone, (2) its **gate** is
open (its `homeSite` is unlocked – house reef always; Fog Isle species need `fog_map`), (3) its time mask fits, and
(4) the spawn **depth lies in its band**. Weight = rarity weight × depth preference (1.0 at `prefDepth`, falling
linearly to 0.35 at the band edges; 10 % soft margin outside the band = 0). So **near each reef only that reef's pool
(unlocked by progress) spawns, and the depth still decides what you meet.**
* Spawn depth is chosen first: 70 % within ±12 m of the diver's depth, 30 % anywhere in the water column; bottom
  species use the seafloor when it is inside their band.
* **Locked site:** its own pool is off, but `open_sea`/`lagoon` species whose gates are open still spawn at 50 %
  density (never empty water) + the existing "locked" notification once per visit.
* **Targets** (animals within the 80 m ring): house reef 70, lagoon 35, kelp 50, wreck 45, blue drop **30** (was 20),
  trench **18** (was 14), Fog Isle 60, open sea **22 + 1 school**, deep sea 10; night ×0.85 plus the night species.
  `MaxActive` 160 stays; in co-op max 60 per diver.
* **The surface is alive too:** with the diver on the boat or at the surface (< 2 m) in `open_sea`/`lagoon`, keep
  ≥ 6 surface animals within 40 m (flying fish glides, dolphin pods (G.1), turtles breathing, seabirds diving).
* Habitat anchors (L.3): bottom species are placed at a matching anchor within 30 m when there is one (clownfish →
  anemone, moray → crevice, cleaner wrasse → cleaning bommie, rays and flamboyant cuttle → sand, pygmy seahorse → sea
  fan).
* Performance: species lists per zone are built once in `Awake` (no LINQ, no per-frame allocations); zone lookups use
  the grid cache.

**Depth bands for all 55 species** (`SpeciesDef`: `zones`, `minDepth`/`maxDepth` (band), NEW `prefDepth`, NEW
`dayMask`, NEW `homeSite` (gate), NEW `page` (Sealog page; default `siteId`), NEW `unlockFlag`). Zone codes: HR house
reef, LG lagoon, KF kelp, WR wreck, BD blue drop, DT trench, OS open sea, DS deep sea, FI Fog Isle, TP tide pools, BE
beach, IS island land. Rarity: C common, U uncommon, R rare, E epic, L legendary. Bold = new species.

| Page | id | Zones | Band m (pref) | Time | Gate | Rarity / base |
|---|---|---|---|---|---|---|
| Hausriff | clownfish | HR, LG, FI (at anemones only) | 1–12 (4) | all | – | C 5 |
| | blue_tang | HR, LG, FI | 1–15 (6) | day | – | C 8 |
| | moon_jelly | HR, LG, KF, OS | 0–25 (3) | all, night ×2 | – | C 6 |
| | parrotfish | HR, LG, FI | 2–18 (7) | day | – | C 15 |
| | emperor_angelfish | HR, WR, FI | 3–25 (10) | day | – | U 20 |
| | lionfish | HR, WR | 3–30 (12) | dusk/night ×1.5 | – | U 25 |
| | bluespotted_ray | HR, LG | 1–15 (5) | day | – | U 30 |
| | seahorse | HR, LG (sea grass) | 1–10 (4) | all | – | R 50 |
| | octopus | HR, KF, WR | 2–30 (8) | night ×1.5 | – | R 70 |
| | green_turtle „Gerald“ | HR, LG | 0–15 (5) | day | – (unique) | U 60 |
| | ghost_crab | BE | −1.5–0 | 18:00–08:00 | – | C 8 |
| | hermit_crab | TP, HR, LG | 0–4 (1) | all | – | C 10 |
| | sally_crab | TP | 0–2 | day | – | U 18 |
| | blue_starfish | HR, LG, FI | 1–15 (5) | all | – | C 7 |
| | crown_star | HR, FI | 3–15 (8) | all | – | U 22 |
| | **cleaner_wrasse** | HR, LG, KF, WR, FI | 1–30 (8) | day | – | C 6 |
| Kelpwald | garibaldi | KF | 4–25 (10) | day | kelp | C 12 |
| | cuttlefish | KF, HR | 6–30 (14) | all | kelp | U 40 |
| | sea_lion „Sir Flapsalot“ | KF, OS (≤ 200 m from the kelp island) | 0–25 (6) | day | kelp (unique) | U 80 |
| | leopard_shark | KF, LG (night) | 5–25 (15) | all | kelp | U 90 |
| | giant_sea_bass | KF, WR | 10–35 (20) | all | kelp | R 120 |
| Wrack | moray | WR, HR (crevices) | 10–40 (25) | all | wreck | U 50 |
| | grouper | WR, BD | 15–45 (28) | all | wreck | U 70 |
| | barracuda | WR, OS, BD | 5–40 (15) | all | wreck | U 60 |
| | hammerhead | WR, BD, OS | 20–60 (35) | all | wreck | R 250 |
| Blauwasser | manta | BD, OS | 5–58 (15) | day | blue_drop | R 300 |
| | sunfish | BD, OS | 5–60 (20) | day | blue_drop | R 200 |
| | whale_shark | BD, OS | 0–58 (10) | day | blue_drop | E 600 |
| | dolphin | OS, BD, LG edge | 0–25 (3) | day | kelp | U 110 |
| | humpback „Big Bertha“ | BD, OS (≤ 300 m from the site) | 0–40 (10) | all | blue_drop (unique) | E 900 |
| Tiefseegraben | anglerfish „Lumen“ | DT, DS | 60–98 (85) | all | trench (unique) | R 150 |
| | vampire_squid | DT, DS | 70–98 (90) | all | trench | R 120 |
| | giant_squid | DT | 88–98 (95) | all | trench | L 1,500 |
| | goblin_shark | DT | 80–98 (90) | all | trench | R 400 |
| | gulper_eel | DT, DS | 75–98 (88) | all | trench | R 260 |
| **Hochsee / Open Sea** | **sardine** | OS, LG edge, KF | 0–30 (8) | day | – | C 4 (school 60–150) |
| | **flying_fish** | OS | 0–2 (glides 20–40 m above the water) | day | – | C 10 |
| | **spotted_eagle_ray** | LG, OS, KF | 1–40 (10) | all | kelp | U 70 |
| | **yellowfin** | OS, BD | 0–60 (15) | all | wreck | U 55 |
| | **lanternfish** | DS, DT, OS | 40–98 (60); at night the band moves up 30 m (NEW field `nightShift = −30`) | all | blue_drop | C 12 (glows) |
| **Nebelinsel / Fog Isle** | 5 species of K.8 | FI only | see K.8 | see K.8 | `fog_map` | – |
| **Insel / Island** | 10 species of L.11 | IS (+ LG for iguana and pig) | land | see L.11 | – | see L.11 |

New open-sea species (fauna): sardine – Sardine (*Sardinops sagax*) 10–25 cm, Fish, Schooling 60–150, `#9FB4C4` /
`#F4EFE6` / `#3F5A72`, BellyGradient; flying_fish – Fliegender Fisch / Flying Fish (*Exocoetus volitans*) 15–35 cm,
Fish with wing fins (finSize 2.2), Skittish, glides out of the water near the boat, `#2F6FD6` / `#D9DEE2` / `#7FD3E0`;
spotted_eagle_ray – Gefleckter Adlerrochen / Spotted Eagle Ray (*Aetobatus narinari*) 100–300 cm span, Ray, Wanderer
2–6 in formation, `#2E3E5C` / `#F4EFE6` / `#F4EFE6` spots; yellowfin – Gelbflossen-Thun / Yellowfin Tuna (*Thunnus
albacares*) 60–200 cm, Fish "barracuda" variant (torpedo), Schooling 4–10, fast, `#2B3A5C` / `#D9DEE2` / `#F2C94C`
finlets; lanternfish – Laternenfisch / Lanternfish (*Myctophum punctatum*) 3–15 cm, Fish, Schooling 20–40, glows
(photophores `#7FD3E0`). Cleaner wrasse – Putzerlippfisch / Cleaner Wrasse (*Labroides dimidiatus*) 6–14 cm,
Fish, Schooling 2–6 at cleaning bommies, `#2F6FD6` / `#F4EFE6` / `#1E1B2B` stripe.
Fun facts: sardine „Schwimmt im Schwarm, damit Räuber nicht wissen, wen sie nehmen sollen.“ / "Swims in a school so
predators can't decide whom to pick." · flying fish „Gleitet bis zu 50 Meter weit über das Wasser.“ / "Glides up to 50
metres above the water." · eagle ray „Springt manchmal einfach aus Spaß aus dem Wasser.“ / "Sometimes leaps out of the
water just for fun." · yellowfin „Kann über 70 km/h schnell schwimmen.“ / "Can swim faster than 70 km/h." · lanternfish
„Wandert jede Nacht hunderte Meter nach oben – die größte Reise der Welt.“ / "Rises hundreds of metres every night –
the biggest migration on Earth." · cleaner wrasse „Betreibt eine Waschanlage: Große Fische stehen Schlange.“ / "Runs a
car wash: big fish queue up."

**Sealog count 34 → 55** (pages: Hausriff 16, Kelpwald 5, Wrack 4, Blauwasser 5, Tiefseegraben 5, Hochsee 5,
Nebelinsel 5, Insel 10). Dive-site unlocks still use the site pages only (house reef 60 % = 10 of 16); the
Hochsee/Insel pages unlock nothing, the golden camera (Q52) needs all 55.

### L.2 (p) Sealog: how often found, at which depth *(core: JournalEntry; ui: Sealog app; filming: counters)*

`JournalEntry` new fields (core, saved): `int sightings` (individuals detected while recording or photographed, K.2),
`int photos`, `float minDepthSeen = -1`, `float maxDepthSeen`, `int zoneMask` (bit per zone of L.1 where seen),
`int lastSeenDay`, `string bestPhotoId`. `timesFilmed` stays for old saves.

Sealog app (ui):
* **Card list:** every card shows a badge **„×23“** (sightings) bottom-right and a depth chip **„2–12 m“** (species
  band) bottom-left. Undiscovered + sighted: silhouette + „Gesichtet ×3 – Foto fehlt!“ / "Sighted ×3 – photo
  missing!". Undiscovered + never sighted: silhouette + depth chip only („Lebt in 2–12 m“ / "Lives at 2–12 m").
* **Detail page** (below the record bar): „Gefunden: 23×“ / "Found: 23×" · „Fotos: 4“ / "Photos: 4" · „Lebt in:
  2–12 m“ / "Lives at: 2–12 m" · „Von dir gesehen: 3–9 m“ / "You saw it at: 3–9 m" · „Fundorte:“ / "Found at:" zone
  chips (Hausriff · Lagune …) · a small vertical depth bar 0–100 m with the band in teal `#3FA9B5` and your seen range
  in yellow `#F2C94C`; time-of-day icons (sun / dusk / moon) from `dayMask`.
* **New tab „Tiefen“ / "Depths":** a vertical ruler 0 → 100 m; every species icon sits at its `prefDepth` (grey
  silhouette when undiscovered) – the "which fish at which depth" overview; tap = detail page.
* Filter chip „Nach Tiefe sortieren“ / "Sort by depth" on every page.

### L.3 (q) Coral reefs everywhere, much better *(props: PropCoral, UnderwaterDecor; worldfx: seabed facets; fauna: habitat use)*

Supersedes the density numbers of Art Bible §2.4 (style rules stay: chunky 3D, 2–3 flat tones per colony, sand gaps,
no paper-flat shapes, no confetti).

* **Where reefs are:** house reef (patch reefs + a 30–40 m wide **reef crest** along the lagoon edge at 3–5 m),
  **14 patch reefs in the lagoon** all around the main island (6–12 m across, 1.5–4 m deep – "fish everywhere"),
  **fringing reefs** around every landmark island (shelf 0–6 m), the Fog Isle (densest, special colours), wall gardens
  at the blue drop, encrusting growth on the wreck, glowing sponge gardens on the trench terraces. Kelp: rocky reefs
  with sponges and anemones (no tropical corals).
* **Density:** house reef **90 patches** (was 24) covering ≥ 60 % of the 2–10 m area, + 40 bommie heads (coral
  colonies on every `BommieMask` peak); inside a patch **1 colony per 2 m²** (was 3); Fog Isle 1 per 1.5 m².
* **Patch recipe (HtF look):** 1 hero colony 1.2–1.8 m (table coral with a stem, big brain, barrel sponge or a sea
  fan) + 3–5 mid colonies 0.6–1.0 m + 6–12 small ones 0.3–0.5 m + a sand or sea-grass gap ≥ 30 % of the patch. Each
  patch uses a **3-colour sub-palette** from §2.4 (one warm, one cool, one accent); neighbouring patches differ.
* **New coral types:** mushroom coral (flat dome 0.2–0.4 m), staghorn thicket (low, 1.5 m wide), pillar coral (3–5
  columns, 1.2 m), soft-coral puffs (pink/purple, sway 0.2 Hz), **giant clam** (blue lips, closes when touched –
  gag + sound), anemone with a clownfish home (1 per 2 patches), sea fans with pygmy seahorses at the Fog Isle.
* **Habitat anchors:** every patch registers anchors in `PropInfo.Habitats` (position, radius, kind `Anemone,
  Crevice, Cleaning, Overhang, Sand, SeaFan`) for the spawner (L.1).
* **Performance:** batched per 64 m cell (PropBatch), colonies < 0.5 m culled beyond 30 m, ≤ 35k triangles in view at
  the house reef (Medium); fixed seeds per site.
* **Acceptance:** AutoShot `06_reef` shows ≥ 8 colonies and ≥ 12 fish; new AutoShot `20_lagoon_patch` (2 m deep,
  60 m off the beach) shows a patch reef with fish.

### L.4 (r) FinTube: your own title + a collage thumbnail *(ui; core: upload data; net: title in the command)*

* **Title:** a text field (max **60** characters) on the upload card, placeholder = the first suggestion. Under it
  **3 suggestion chips** for the selected title style (Honest/Spicy/Clickbait stay – they still set the multipliers),
  built from the video: top species, count of animals, a record, an event, the site. Examples: Honest „Tauchtag 12:
  Gerald, ein Rochen und 14 weitere Tiere“ / "Dive day 12: Gerald, a ray and 14 more animals"; Spicy „Ich war SO nah
  an einem 2-m-Hai!“ / "I got SO close to a 2 m shark!"; Clickbait „HAI GREIFT AN?! (nein)“ / "SHARK ATTACK?! (no)".
  Tapping a chip fills the field.
* **Typing:** while the field has focus `UIState.TextInput = true` → `GameInput` ignores every action except Pause
  (Esc = leave the field) and Enter (= upload); the phone key does not close the phone. Sanitise: no control
  characters, trim, max 60; empty → suggestion. Co-op: the title already travels in `NetCommand.Upload`; the host cuts
  it to 60.
* **Collage thumbnail** (Q31 extended): up to 5 animals of the video – the species with the best clip values – as
  tiles: 1 = full, 2 = split, 3 = one big + two small, 4 = 2 × 2, 5 = one big + four small. Each tile = today's best
  photo of that species (gallery) or, without a photo, its 3D icon on a depth-coloured gradient. Tap a tile to swap
  the species (picker). Style sticker by title style: none (Honest), „WOW!“ (Spicy), red arrow + „ECHT?!“ (Clickbait).
* `thumbQuality` (I Q31) = average tile quality (photo quality; icon tile 0.4) × (0.8 + 0.05 × tiles), max 1 → views ×
  `(1 + 0.15 × thumbQuality)` as before.
* Composed in UI Toolkit (512 × 288), saved once to `thumbs/day_{n}.png` in the save folder (max 60 kept);
  `PendingUpload`/`VideoRecord` get `thumbSpecies` (string[]) and `thumbPath`. The video list shows collage + title.

### L.5 (s) The island at 0.62 – smaller and much more detailed *(worldfx: terrain; props: placement + detail; core: WorldLayout/DiveSites; player/core: literals)*

**Decision:** linear scale **0.62** of the Testversion-3 island width (replaces the 0.75 plan of 1a). The 1a method
stays (map-scale anchors × S, feature geometry keeps its size, `WorldLayout.Isl()` for legacy literals). Verified with
the reference harness `scratchpad/design2_l/r3/` (copy of `design2_island/new` + the changes below; run
`cd …/design2_l/r3 && dotnet build && dotnet bin/Debug/net8.0/r3.dll ../out r3` → `out/r3.txt`, `island_r3.png`,
`map_r3.png`, `zNW/zSW/zE/zS_r3.png`).

| | Testversion 3 | 0.75 plan (1a) | **0.62 (new)** |
|---|---|---|---|
| Mean outer shoreline radius | 196 m | 147 m | **122 m** |
| Land area | 11.4 ha | 6.4 ha | **4.4 ha** |
| Sum of the 12 trails | 1,162 m | 881 m | **681 m** |
| Hut → lighthouse / waterfall / cave / radio tower / hidden bay | 198 / 195 / 246 / 270 / 307 m | 147 / 139 / 189 / 206 / 230 m | **123 / 120 / 131 / 167 / 190 m** |
| Hut → dock end / helipad / tide pools / village | 89 / 100 / 117 / 211 m | 71 / 75 / 88 / 158 m | **61 / 62 / 73 / 119 m** |
| Highest point (lighthouse) | 42 m | 34 m | **30 m** |
| Land steeper than 30° | 17.3 % | 21.5 % | **22.4 %** |
| Trail steps steeper than 32° | 13–19 m | ≤ 10 m | **≤ 3 m per trail** (only lighthouse, waterfall, bay) |

Trails (m): dock 11, lighthouse 132, helipad 47, waterfall 104, cave 26, ravine north 38, ravine south 62, bay 48,
helipad–ravine 45, village 50, tide pools 72, tide pools–hut 46. Dive sites and landmark islands stay (house reef moves
with the dock: dock end → centre 64 m, depth 2.0–11.2 m).

**Big layout change:** at 0.62 the cave, the waterfall plateau and the lighthouse hill overlap. The cave now opens
**in the waterfall cliff, 16 m west of the falls** – one complex „Wasserfall-Grotte“ / "Waterfall Grotto": the
passage runs straight into the plateau (`CaveDir = −FallDir`), the mouth sits at the amphitheater floor (14.4 m),
the chamber 17.5 m behind the cliff under the plateau. The cave ramp of 1a is dropped; the cave trail starts at the
foot of the waterfall ramp. The **second beach becomes the village Altriff** (L.8) with a flat terrace.

`WorldLayout` (core):

```csharp
public const float IslandScale = 0.62f;           // 0.75 plan → 0.62 (third player message s)
public const float IslandRadius = 118f;           // TV3 190
public const float IslandPeak = 30f;              // TV3 42 (lighthouse)
public static readonly Vector3 Hut = new Vector3(73f, 0f, 11f);
public static readonly Vector3 Bed = new Vector3(71f, 0f, 15f);
public static readonly Vector3 TV = new Vector3(76f, 0f, 7f);
public static readonly Vector3 PlayerSpawn = new Vector3(69f, 0f, 15f);      // later days; a new game starts at Helipad + (4, 0, 6) (K.5)
public static readonly Vector3 Helipad = new Vector3(43f, 0f, -43f);
public static readonly Vector3 DockStart = new Vector3(104f, 0f, 4f);
public static readonly Vector3 DockEnd = new Vector3(134f, 0f, 4f);          // dock 30 m, 2.75 m water at the end
public static readonly Vector3 BoatSpawn = new Vector3(140f, 0f, 12f);       // water 2.9 m
public static readonly Vector3 FishingSpot = new Vector3(133f, 0f, 1f);
public static readonly Vector3 Lighthouse = new Vector3(-19f, 0f, 93f);
public static readonly Vector3 HiddenBay = new Vector3(-115f, 0f, -19f);
public static readonly Vector3 WaterfallTop = new Vector3(-43f, 0f, 43f);
public static readonly Vector3 WaterfallPool = new Vector3(-28f, 0f, 18f);   // Top + (15, -25)
public static readonly Vector3 CaveEntrance = new Vector3(-56f, 0f, 33.5f);  // in the waterfall cliff (s 1.5, q -16)
public static readonly Vector3 Ravine = new Vector3(-12f, 0f, -25f);
public static readonly Vector3 TidePools = new Vector3(93f, 0f, -59f);
public static readonly Vector3 SecondBeach = new Vector3(25f, 0f, -110f);
public static readonly Vector3 RadioTowerRuins = new Vector3(-72f, 0f, -72f);
public static readonly Vector3 Village = new Vector3(25f, 0f, -98f);         // NEW – terrace centre (L.8)
public static readonly Vector3 DriftwoodBeach = new Vector3(60f, 0f, 100f);  // NEW – Toni (K.8)
public static readonly Vector3 HangarSpot = new Vector3(56f, 0f, -52f);      // NEW – Wanda's hangar (L.12), on the helipad plateau
public static readonly Vector3 SwimRaft = new Vector3(118f, 0f, 34f);        // NEW – floating raft in the lagoon (L.5 details)
public static readonly Vector3 FogIsland = new Vector3(-60f, 0f, 700f);      // NEW – K.8
// KelpIsland, WreckIsland, BlueDropIsland, TrenchIsland, MiniIslandRadius: UNCHANGED
```

`DiveSites` (core): `house_reef` centre **(188, 0, 38)**, radius **105**; NEW `fog_isle` (K.8); others unchanged.

`WorldFeatures` (worldfx):

| Constant | TV3 | 0.75 plan | **0.62** |
|---|---|---|---|
| `RavineRimY` | 23.5 | 21.0 | **18.5** (floor 11.0) |
| `RavineWestX / RavineEastX` | −66 / 26 | −49.5 / 19.5 | **−41 / 16** |
| `RadioPlateauY` | 22 | 22 | **15** (gully 36 m at ≈ 21°) |
| `HiddenBayBeach` | Bay + (40, 0) | Bay + (32, 0) | **Bay + (27, 0)** |
| `BayGullyTop / Bottom` | (−146, 16.4, −82) / (−146, 0.9, −38) | (−100, 21.5, −84) / (−108, 0.9, −29) | **(−76, 14.5, −60) / (−89, 0.9, −26)** |
| `CaveRampTop / Bottom` | – | (−40, 29.5, 97) / (−80, 13.2, 92) | **removed** (zero = unused) |
| `CaveDir` | (−0.6, 0, −0.8) | same | **−FallDir** (assigned after FallDir) |
| `CaveFloorY` / `CaveHillHeight` | 13 / 11 | 13 / 11 | **14.4 / 7.3** (hill top 21.7 < creek lip 21.85 – the hill never fills the creek) |
| `SecondBeachHeadlands` | sb ± 41 | (sb.x − 33, 3.2, sb.z − 2, r 9), (sb.x + 32, 2.6, sb.z + 3, r 8) | **(sb.x − 31, 3.2, sb.z − 1, r 8), (sb.x + 30, 2.6, sb.z + 3, r 7)** |
| `SeaStacks` (x, top, z, r) | 7 | 0.75 list | **(−42, 6, 117, 5), (−116, 7.5, 67, 6), (−151, 8.5, 26, 6.5), (−126, 6.5, −107, 7), (−46, 5, −128, 5), (69, 6, −119, 5.5), (25, 4.5, 143, 4.5)** – all ~2 m water around |
| NEW `VillageTerraceY`, `VillageHalfX`, `VillageHalfZ` | – | – | **1.7 m, 24 m, 12 m** (ellipse around `Village`) |
| `MiniIslands` | 4 | 4 | **+ fog_isle** (shore 30, peak 16, steepness 1.2, rocky 0.45) |

`WorldHeightField` (worldfx) – on top of the 1a changes:

| Where | 0.75 plan | **0.62** |
|---|---|---|
| `S`, `HS` | 0.75, 0.85 | **0.62** (= `WorldLayout.IslandScale`), **0.72** |
| `LighthouseHeight` | 34 | **30** |
| `IslandOuter` (POI evaluation radius) | 230 | **195** |
| `OceanStart`, `ArmStart` | 220, 176 | **200, 146** |
| `ReefInnerR / ReefOuterR` (house reef depth ramp) | 140 / 300 | **115 / 255** |
| Cove | BayX + 5, CoveR 22, mouth (BayX − 45, BayZ − 13.5) | **BayX + 4, CoveR 19, mouth (BayX − 37, BayZ − 11)** (channel 8, noise 3 as 0.75) |
| `CoveBeach` | SmoothStep(12, 26, Dist(CoveX + CoveR + 5)) | **SmoothStep(10, 22, Dist(CoveX + CoveR + 4))** |
| `SeaDepth` cove-shallow | r < 240, SmoothStep(CoveR + 3, CoveR + 26) | **r < 200, SmoothStep(CoveR + 3, CoveR + 22)** |
| `CaveHill` back slope / noise | 0.18 / 1.2 | **0.05 / 0.4** |
| `Ravine` return | `MathF.Min(h, prof)` | **`Lerp(h, MathF.Min(h, prof), Clamp01(dep / 1.5f))`** – fixes a straight seam at both ravine ends (exists in TV3 too) |
| `Features` | cave ramp carve | **skipped when `CaveRampTop == CaveRampBottom`**; NEW village terrace: inside the ellipse `VillageTerraceY + 0.12·Perlin`, blended over 9 m beyond the edge |
| `Height` | – | NEW Fog Isle blend: `dFog < r + 110 → Lerp(h, −FogDepth(x, z, dFog), 1 − SmoothStep(r, r + 110, dFog))` (K.8) |
| Lagoon (70 / 22 / 3.4) | – | unchanged |

Measured POI heights: hut 2.3, helipad 4.0, dock start 0.2 → dock end −2.75, lighthouse 30.0, waterfall lip 21.85,
pool 11.7 (water 13.5), cave mouth 14.4, chamber 14.75, ravine floor 11.1 (rims 18.5), radio plateau 15.0, gully top
15.0 → bottom 0.9, bay beach 0.4, village terrace 1.7 (beach 1.5 → water at z ≈ −120; stilt lagoon 1.2–2.0 m deep at
z = −125), tide pools 1.5, driftwood beach 0.6.

**Literal coordinates** (1a table, now with `Isl()` = × 0.62): `PropPaths` routes = the probe routes (dock
`Isl(140, 13.5), Isl(150, 9.5)`; **waterfall** `Isl(22, 114) → (wt.x + 22, wt.z + 20) → (wt.x + 9, wt.z + 9) →
FallRampTop → FallRampBottom → +(9, −9) → PoolBeach`; **cave** `FallRampBottom → FallFoot + FallDir·9 → cave −
CaveDir·6`; **helipad–ravine** `(rv.x + 2, rv.z − 16) → (−1, −45) → (14, −47) → (heli.x − 9, heli.z − 2)`; village
trail ends at `(sb.x, sb.z + 10)`); junction waterfall/lighthouse at `Isl(22, 114)`; `PropCollect` JungleHeart radius
**74**, fallback disc **93**; menu camera fallback **(186, 34, −143) → look (37, 6, 12)**; `PlayerIntroRunner` p0
**(99, −38.4)**, p1 **(114, −16)**, p2 **(123, −3)**, look0 **(93, 3, 7.4)**, house reef literal **(188, 0, 38)**;
AutoShot points `Isl()` of x/z; `WorldHeightField.SiteOf("house_reef", 188, 38, 105, …)`.

**Much more detailed (props – the "hand-made" pass).** Rules: every 15–20 m of trail has a prop cluster; no empty
20 × 20 m patch of land without detail (plateaus excepted); every spot below tells a tiny story. Spots:

| # | Spot | Anchor | Content |
|---|---|---|---|
| 1 | Hut yard | Hut | hammock between two palms, washing line with wetsuits, Gus' upturned rowboat „Rostlaube“ as a bench, surfboard rack, locker (1c), grill |
| 2 | Dock | DockStart–DockEnd | crab-trap stack, bait cooler, Horst's bench with sandwich wrappers, a badly painted sign „Hafenmeister“, lifebuoy, coiled ropes |
| 3 | Swim raft | `SwimRaft` | floating wooden raft with a diving board 2 m (jump = splash, fun selfie spot) |
| 4 | Helipad + hangar | Helipad, `HangarSpot` | wind sock, fuel drums, Wanda's hangar shack with a hammock and radio, painted „H“, delivery crates (L.12) |
| 5 | Driftwood beach | `DriftwoodBeach` | driftwood sculptures, a bottle-message board, later Toni's raft (K.8) |
| 6 | Lighthouse | Lighthouse | picnic bench, broken coin telescope (E = view the sea from up close), flower boxes, rope ladder to the north beach (Q7), maintenance ladder (L.6) |
| 7 | Waterfall Grotto | WaterfallTop/Pool | stepping stones, **rope swing** over the pool (E: swing, release = splash), mossy rocks, dragonflies, the cave mouth with a root curtain |
| 8 | „Kanal Tiefsee“ base camp 1987 | flat spot (slope < 15°) ≥ 8 m off the ravine-north trail, between the pool beach and the ravine | rusty sign „KANAL TIEFSEE – BASISLAGER“, collapsed tent frame, a tripod, a cable reel, a crate of 1987 tapes (story) |
| 9 | Ravine | Ravine | rope bridge with a missing plank (cosmetic), fish bunting, ladders (Q8) |
| 10 | Radio tower | RadioTowerRuins | toppled satellite dish, old generator, sign „Hochspannung (seit 1987 nicht mehr)“ / "High voltage (not since 1987)" |
| 11 | Hidden bay | HiddenBay | old rowboat wreck, hidden campfire, cave arch |
| 12 | Village Altriff | Village | L.8 |
| 13 | Village beach east | Village + (18, 0, −10) (sand west of the east headland) | kids' football goal, canoe racks, drying nets |
| 14 | Tide pools | TidePools | Dr. Fin's measuring stakes, bench with a giant magnifier |
| 15 | Jungle path | trails | fallen-log bridge, a ruined tree house (Gus' old lookout, climbable), a mushroom ring, wild banana plants |
| 16 | Lagoon | around the island | 14 patch reefs (L.3), 6 sandbars with birds, 3 rowboat wrecklets, mooring buoys, a sunken jetty |
| 17 | Coast | shoreline | tide-line debris, crab burrows, small rock pools, palm groves with coconut clusters (E.1) |

### L.6 (t) Secrets – really hidden *(props: placement + geometry; npc: clue lines; ui: Collection app; player: crouch)*

**Rules (all secrets):** never on a trail and not visible from a trail; each needs **one of** climb (ladder / ledge /
stacked crates), dive (under water or under the falling water), darkness (lamp needed), squeeze (crouch through a
0.9 m gap) or a two-player boost – **plus a clue** from a page, an NPC or a sound. Secrets have **no sparkle**; a faint
glint appears only within 2.5 m. The Collection app shows „?“ for every missing secret and the clue text once the clue
was heard. New player ability (player): **crouch on land** with `GameAction.Down` (eye 1.0 m, speed ×0.5) – needed for
squeezes. Logbook pages and treasures: none within 3 m of a trail centre line, at least half of them ≥ 10 m off a
trail or behind a climb/dive; golden shells may stay near beaches (the easy set).

| # | Secret (key) | New hiding | Clue (source) |
|---|---|---|---|
| 1 | cave_grotto „Gus' geheime Filmecke“ | behind a root curtain at the back of the Waterfall Grotto chamber, then a 0.9 m squeeze (crouch) into a side grotto; dark – lamp | page 6: „Hinter dem Vorhang aus Wurzeln, wo das Wasser rauscht.“ / "Behind the curtain of roots, where the water roars." |
| 2 | waterfall „Versteck hinter dem Wasserfall“ | dive from the pool under the rock ledge behind the falling water (1.2 m deep, 3 m long) into a dry alcove – invisible from outside | Juno: „Hinter dem Wasserfall klingt's hohl.“ / "Behind the waterfall it sounds hollow." |
| 3 | lighthouse_top „Ganz oben“ | carving on the lamp-room roof: a 4 m maintenance ladder on the **sea side** of the gallery (seen only from the sea) | Pearl: „Gus sagte immer: Die beste Aussicht hat die Möwe.“ / "Gus always said: the seagull has the best view." |
| 4 | radio_tower „Das alte Funkgerät“ | on the tower platform 12 m up; the lowest 2.5 m of the ladder are missing – climb the stacked crates or get a boost | a crackling radio audible within 25 m |
| 5 | ravine_piggy „Gus' Sparschwein“ | ravine floor, in a side crevice behind a boulder 3 m from the west ladder – invisible from the bridge | Lio: „Ich hab da unten was Rosa gesehen!“ / "I saw something pink down there!" |
| 6 | wreck_cabin (captain's cabin) | locked; the key lies in the moray nest in the cargo hold (morays retreat when the lamp is off for 5 s) | page 20 |
| 7 | kelp_carving „G+H 1989“ | far rocky side of the kelp island behind the summit knoll; only reachable by swimming round to a tiny cove | Pearl |
| 8 | wreck_hammock (Gus' hammock) | sea cave at the wreck island's waterline, entrance 1 m under water on the south-east side | Gus radio after M2: „Mein Mittagsschlaf-Platz … mit nassem Eingang.“ / "My nap spot… with a wet entrance." |
| 9 | blue_buoy (TV-studio radio buoy) | in a crack of the blue-drop wall at 18 m, dark; beeps (audible within 30 m under water) | Toni |
| 10 | trench_tv (TV set) | buried under palm fronds on the trench island's far beach; its screen glows with static **only at night** | Gus 100k call (signals) |
| 11 | lighthouse_cellar | trapdoor under a rug in the lighthouse base (E „Teppich anheben“ / "Lift the rug") | Pearl: „Gus hat nie verraten, was unter seinem Teppich liegt.“ / "Gus never told what's under his rug." |
| 12 | **village_loft „Gus' Schuldscheine“ / "Gus's IOUs"** (NEW) | cigar box in the rafters of the village boat shed – climb the canoe rack | Pearl's card-game line (L.8) |
| 13 | **fog_archive „Tonis Tonarchiv“ / "Tony's Sound Archive"** (NEW) | air-pocket chamber of the Nebelgrotte (K.3), 22–28 m, dark | Toni |

H.2 totals: secrets 11 → **13**, collectibles 92 → **94**; set reward unchanged (gold mask frame, now "all 13
secrets").

### L.7 (u) More side quests and the NPCs who give them *(npc: data + flow + lines; props: quest objects; core: quest state, carry; ui: Tasks app + markers)*

**System.** `Data/SideQuestDatabase.cs` (npc): `SideQuestDef { id, giverId, titleDe/En, prereq (day, followers,
flags, unlocked site), steps[], reward (money, cosmetic, flag, recipe), lines }`. Step types: `Talk, Bring (carry or
counter), Photo (species/tag + condition), Film (species/tag, seconds), Find (object), Catch (fish kind, kg), Visit
(place, radius), Collect (n), Upload (condition), Selfie (with NPC/species), Event (event id)`. Max **3 active** side
quests (+ tutorial, story mission, sponsor deals). Givers show a yellow **„!“** above the head (quest available) or a
green **„?“** (ready to hand in), visible within 40 m and on the map app. Shared state (host); progress counts all
players. **Carry items** (core + player): one quest item in the right hand (`HoldItem.QuestItem`, model by item id;
the camera goes to the chest strap); items found under water can be carried while swimming; fainting drops the item
where you are (it floats up).

| id | Giver | Title DE / EN | Prereq | Task | Reward |
|---|---|---|---|---|---|
| H1 | Horst | „Günthers Raubzug“ / "Günther's Heist" | tutorial done | photo of Günther (seagull) with food in his beak at the dock, 12:00–14:00 | 80 $ |
| H2 | Horst | „Horsts Sonnenbrille“ / "Horst's Sunglasses" | tutorial done | find them at 3 m between the dock posts and bring them back | 50 $ |
| H3 | Horst | „Influencer-Workshop“ / "Influencer Workshop" | day ≥ 5 | selfie with Horst, then an upload with an honest title | 120 $ |
| W1 | Wanda | „Treibende Fässer“ / "Drifting Drums" | first delivery | carry 3 fuel drums from the lagoon to the hangar | 150 $ + 2 free express deliveries |
| W2 | Wanda | „Prospekt-Fotos“ / "Brochure Photos" | day ≥ 4 | photograph lighthouse, hut and village from the water | 120 $ |
| P1 | Grandma Pearl | „Die Gezeitensteine“ / "The Tide Pegs" | first village visit, day ≥ 3 | find 3 carved pegs of the tide calendar (beach, tide pools, lagoon 2 m) | 200 $ + story hint (1987) |
| P2 | Grandma Pearl | „Kartenabend“ / "Card Night" | day ≥ 5 | bring Horst to the village at 18:00 (talk to him, he walks along) – he loses, again | hat 13 „Perles Strohhut“ / "Pearl's straw hat" |
| P3 | Grandma Pearl | „Lichter auf dem Wasser“ / "Lights on the Water" | a Lantern Evening (L.8) | release a lantern, then film ≥ 5 lanterns from the water | 150 $ + the story of the light in 1987 |
| M1 | Mira | „Treibholz“ / "Driftwood" | first village visit | collect 5 driftwood pieces (beaches; E) | 120 $ |
| M2 | Mira | „Gus' altes Paddel“ / "Gus's Old Paddle" | M1 | find it in the hidden-bay cave and carry it to the boat shed | 200 $ + boat flag „Wellen“ / "Waves" |
| M3 | Mira | „Das erste Paddeln“ / "First Paddle" | M2 | next day 10:00: film the launch and race of the new outrigger canoe | 300 $ + boat flag „Altriff“ |
| T1 | Tomas | „Geisternetz“ / "Ghost Net" | day ≥ 2 | cut a lost net off a house-reef bommie (3 spots, hold E 5 s each) | 200 $ + house reef population +10 % |
| T2 | Tomas | „Wo wohnt der Zackenbarsch?“ / "Where Does the Grouper Live?" | wreck unlocked | photo of a grouper below 25 m | 250 $ + Tomas marks the depth bands of 3 undiscovered species in the Sealog |
| T3 | Tomas | „Der große Fang“ / "The Big Catch" | rod t1 | catch a fish ≥ 3 kg from the boat | 150 $ |
| A1 | Ada | „Kokos-Curry“ / "Coconut Curry" | first village visit | bring 3 coconuts and 2 mackerel (cooler) | 80 $ + curry at her kiosk |
| A2 | Ada | „Seetrauben“ / "Sea Grapes" | day ≥ 4 | pick 6 sea grapes in the lagoon sea grass (1–3 m, E) | 80 $ + sea-grape salad at her kiosk |
| J1 | Juno | „Walgesang“ / "Whale Song" | blue drop unlocked | film Big Bertha while she sings | 400 $ + menu song „Altriff-Lied“ (K.6) |
| J2 | Juno | „Knallkrebs-Konzert“ / "Snapping Shrimp Concert" | day ≥ 3 | record 20 s at the house reef at night | 120 $ |
| L1 | Lio | „Mein Drachen!“ / "My Kite!" | first village visit | get his kite from the lighthouse gallery railing | 50 $ + selfie with Lio |
| L2 | Lio | „Ein echter Hai!“ / "A Real Shark!" | 300 followers | upload any video with a shark | 100 $ + hat 14 „Papierkrone“ / "Paper crown" |
| L3 | Lio | „Lios Schatzkarte“ / "Lio's Treasure Map" | day ≥ 6 | follow his crayon map to a real treasure (one of the 14) | the treasure + 50 $ |
| N1 | Toni | „Tonaufnahmen“ / "Sound Recordings" | K.8 done | film dolphins clicking, a sea lion barking and the whale singing | 300 $ |
| N2 | Toni | „Das letzte Band“ / "The Last Tape" | K.8 done + trench unlocked | find the last 1987 tape at the TV antenna ledge (92 m) | 2,000 $ + finale hint |
| B1 | Chad & Brad | „Der goldene Selfie-Stick“ / "The Golden Selfie Stick" | R1 done | it fell into the house reef (6 m): give it back (+200 $, they are nicer in R2–R4) **or** keep it (cosmetic hand prop, they sulk) | choice |

Lines: each quest has a 2–4 line start and a 1–2 line end in `DialogueDatabase` (DE/EN, ≤ 90 characters per line,
humour from the situation). Examples: H2 start „Meine Sonnenbrille ist ins Hafenbecken gefallen. Sie war teuer. Sie
war gefunden.“ / "My sunglasses fell into the harbour. They were expensive. Well, found." · T1 end „Siehst du? Jetzt
kommen sie zurück. Fische merken sich so was.“ / "See? Now they're coming back. Fish remember things like that."

### L.8 (aa) The village of Altriff – a long-established island community *(npc: people, lines, schedules, quests; props: village; fauna: goats, chickens, pig; worldfx: terrace; ui: map label; music: K.6)*

**Respect rules – binding for everyone who touches the village.** The player asked for "an old Indian tribe that
built up its people for years". We build an **invented island community** that has lived on Rostbucht for
generations – people with dignity, their own customs and crafts, modern lives. Humour comes from situations (cards,
goats, kids, Horst), never from who they are.
* **Never:** feather headdresses, war paint, tipis, totem poles, "tribe"/"chief"/"savages" wording, mock or invented
  pseudo-languages, chants or grunts, drums-and-dance clichés, sacred symbols or rites, idols, "noble savage" or
  "primitive vs modern" framing, a single "ethnic" skin tone, costumes.
* **Always:** ordinary names (some are nicknames from their crafts), the same DE/EN and babble voices as every other
  NPC, varied skin tones from the shared 6-tone palette, everyday clothes (linen shirts, wrap skirts or shorts, woven
  sun hats, aprons, rubber boots, reading glasses), modern things next to old crafts (phones, a solar panel, a
  satellite dish – they watch your channel), customs that are practical and joyful.
* Every village line in `DialogueDatabase` carries the tag `village` and is listed in the npc engineer's report for a
  final read by the designer.

**Name and history.** Village **„Altriff“ / "Oldreef"**, the people call themselves „die Altriffer“ / "the
Oldreefers". Fishing and boat-building families who settled the island long before Gus; they sold Gus the hut "for
three coconuts" (page 1) and still think he overpaid. They saw a strange light out at sea in the night the studio
vanished (1987) – their tide calendar marks that night.

**Layout** (props; terrace `WorldLayout.Village`, 48 × 24 m at 1.7 m, beach in front, stilt lagoon 1.2–2 m deep
south of it): **6 stilt houses** on a boardwalk over the lagoon (z −122 … −135; the beach reaches the water at z ≈ −121), 4 houses on the terrace, the
**meeting house** (open long house, woven roof, benches, the tide-calendar wheel 2.5 m), **boat shed** with two
outrigger canoes under construction (secret 12 in the rafters), net-drying racks, a smokehouse, **Ada's kiosk**,
a music stage for the Lantern Evening, vegetable gardens, a goat pen and a chicken run, a **village jetty** where the
player's boat can moor (second boat landing point), a solar panel and a satellite dish on the meeting house, a kids'
football goal on the beach. Palette: wood `#8E6C50`, roofs of woven palm `#C9B48A`, sails and cloth with geometric
wave patterns in `#3FA9A0`, `#EE7F2D`, `#F2C94C`.

**Customs and crafts:**
* **Gezeitenkalender / Tide calendar:** a carved wooden wheel showing moon phases, fish spawning times and festival
  days – folk knowledge, not sacred. In the game it shows the next Lantern Evening and, after P1, which night animals
  are out.
* **Laternenabend / Lantern Evening:** every 7th day (`day % 7 == 4`) 19:00–22:00: paper lanterns float on the lagoon,
  Juno plays marimba, everyone gathers at the jetty. The player can release a lantern (E) → next upload +5 % views
  (like the cozy bonus; does not stack with it) – and it is the best photo spot of the week.
* **Das erste Paddeln / First Paddle:** when a new canoe is finished, the whole village paddles once round the lagoon
  (M3).
* **Greeting:** a short ordinary wave (moving hand) or a nod, „Gute Flut!“ / "Good tide!".
* **Crafts:** weaving mats, hats and sails; mending nets; building outrigger canoes; shell wind chimes; carving local
  animals (turtle, ray, octopus) from driftwood; dyeing cloth with sea-urchin purple.

**People** (bean NPCs on the round-2 NPC builder, L.13 quality):

| NPC | Who | Look | Humour / role |
|---|---|---|---|
| **Großmutter Perle / Grandma Pearl** | village elder, 80+, keeps the tide calendar | skin `#6B4636`, white bun, reading glasses on a cord, woven shawl `#3FA9A0`, walking stick | beats Horst at cards every evening („Er schuldet mir 412 Fischbrötchen.“ / "He owes me 412 fish rolls."); story + secret clues; babble 175 Hz slow |
| **Mira Kielholz / Mira Keelwood** | boat builder, 30s | skin `#D6A574`, short hair, carpenter's apron, pencil behind the ear | roasts your rusty boat („Ist das ein Boot oder ein Hilferuf?“ / "Is that a boat or a cry for help?"); M1–M3 |
| **Tomas Netzflicker / Tomas Netmender** | fisherman, 50s | skin `#A8724A`, grey stubble, rubber boots, net over the shoulder | calm, knows where fish live – gives depth tips (T2); his goat „Kapitän Meck“ eats his sail |
| **Tante Ada / Auntie Ada** | cook, runs the kiosk | skin `#E8C08A`, flowered apron `#EE7F2D`, ladle | her sauce is "only a little spicy" (it is very); A1–A2; sells village food |
| **Juno Klang / Juno Chime** | musician, 20s | skin `#F0D2B4`, freckles, headphones, marimba mallets in the pocket | missing marimba key gag; J1–J2; Lantern Evening |
| **Lio** | kid, 9, „Abonnent Nr. 1“ / "Subscriber #1" | skin `#4A2F28`, oversized FinTube T-shirt, swim goggles on the forehead | endless questions, wants selfies; L1–L3; guards wrong-place crates (L.12) |
| + 6 background villagers | 2 weavers, a fisher couple, a teen with a ukulele, a grandpa napping in a hammock | varied | idle work loops (weave, mend, paddle, nap) |

Schedules (npc): day 07:00–18:00 at their work spots, evening at the meeting house / jetty, night in the houses (lights
on until 22:00). Co-op: host simulates, 4 Hz positions (like Horst).

**Story beats** (≤ 6 lines each): first visit – Pearl: „Gute Flut! Du bist also Gus' Erbe.“ / "Good tide! So you're
Gus's heir." · „Gus hat uns die Hütte für drei Kokosnüsse abgekauft. Zwei waren hohl.“ / "Gus bought the hut from us
for three coconuts. Two were hollow." · „In der Nacht, als sein Studio verschwand, war das Meer hell. Von unten.“ /
"The night his studio vanished, the sea was bright. From below." · „Unser Kalender hat die Nacht markiert. Wir nennen
sie die Augennacht.“ / "Our calendar marked that night. We call it the Night of the Eye." · Later beats after P1 and
P3 lead toward the finale (the light returns when a million people watch).

**Shop:** Ada's kiosk (E „Kiosk“) sells village food, unlocked by A1/A2: „Kokos-Fischcurry“ / "Coconut Fish Curry"
(food, AirCapacity +20 %, 10 min, 45 $), „Seetrauben-Salat“ / "Sea Grape Salad" (food, SwimSpeed +15 %, 8 min, 25 $),
„Ingwer-Limo“ / "Ginger Fizz" (drink, SteadyHands, 8 min, 30 $, from the start). Bought food goes to the pantry
(E.1).

### L.9 (w) Sleeping in co-op: everybody into bed – „1/2 · 2/2“ *(core: SleepVote; props: sleep spots; ui: counter; net: messages; player: lying pose)*

* Replaces E.3 „one sleeps = all sleep“. Solo: unchanged (E at the bed from 18:00, E.3).
* **Sleep spots** (props): the hut bed + 3 more: two porch hammocks and a sofa bunk in the hut (one per possible
  player). Prompt „E – Hinlegen“ / "E – Lie down" (from 18:00, E.3 rule). Lying: camera lies down, avatar pose `Lie`.
  Moving, jumping or E again = get up.
* **Counter** (ui, top centre under the quest line, L.10 style): moon glyph + **„Schlafen 1/2“ / "Sleep 1/2"** for all
  players as soon as one player lies down. When all connected players lie: „2/2 – Gute Nacht!“ / "2/2 – Good night!",
  2 s, then the host runs `SleepToMorning`. Players who are away get one notification „{name} will schlafen (1/2)“ /
  "{name} wants to sleep (1/2)".
* Who counts: every connected player; a player in the pause menu or unconscious does not count as lying. **Host
  safety valve:** after 60 s at „n−1/n“ the host's pause menu offers „Schlafen erzwingen“ / "Force sleep" (for an AFK
  friend).
* Net: `SleepVote 99` (client → host: lying on/off), `SleepState 100` (host → all: lying count, total; 2 Hz while
  > 0). `GameManager.SleepToMorning` is only called by the host when the vote is complete (the old `NetCommand.Sleep`
  path stays for solo/host).

### L.10 (x, z) Quest line always top centre + clean notifications *(ui; core: tracker + notify API; every module converts its messages)*

**Quest line** (replaces GDD 15.6 „nur kurz einblenden“): always on screen, **top centre**, 18 px from the top, max
640 px wide. One line for the **tracked** task: type glyph (40 % of the line height) + short text (≤ 42 characters) +
progress „2/3“ + a distance arrow for place tasks („↑ 34 m“). Optional second line for the next task in 85 % size.
Fredoka 22 px (second line 18 px), white, 2 px ink outline, **no box** (Art Bible §1.14). On progress the line pops
(scale 1.06 for 0.3 s + tick sound); on completion a check mark for 1.5 s, then the next task. Hidden while the phone,
menus, cutscenes, the viewfinder or the selfie view are open. While live, the LIVE pill (C.7) sits top centre and the
quest line moves 44 px down; the sleep counter (L.9) goes under the quest line. The Tasks app gets a pin „Verfolgen“ /
"Track" per task; default order: tutorial → story mission → side quest → sponsor deal. Settings → Spiel „Aufgaben-
Anzeige“ / "Task display": „Normal / Klein / Aus“ / "Normal / Small / Off" (Small = text only, 18 px). Core facade:
`QuestTracker { string Line; string Progress; Vector3? Target; event Changed; }`, filled by npc (tutorial, missions,
side quests) and livestream (deals).

**Notifications** (Q37 limits stay: 1 card + 2 toasts): every toast and push banner = **one icon + one short line
(≤ 40 characters) + an optional value chip**. No explanations in notifications – details live in the phone apps. New
API (core): `GameEvents.RaiseNotify(NotifyKind kind, string line, string chip = null)` with `NotifyKind { Info, Money,
Followers, Sealog, Clip, Delivery, Quest, Sponsor, Story, Collectible, Warning, Event, Achievement }` (each with a
glyph and colour; ui draws). `RaiseToast(string)` stays as `Info` for old callers, but every owner converts its call
sites (core: Footage, Collectibles, Progression; filming: FilmHandCamera; fauna: locked site; fishboat: FishMarket,
HeliService, boat; player: air, phone; ui: phone).

| Today | New (icon · line · chip) |
|---|---|
| „3 Clips gesichert – lade sie im Handy auf FinTube hoch.“ | Clip · „Clips gesichert“ / "Clips saved" · „+3“ |
| „3 Clips verloren.“ | Warning · „Clips verloren“ / "Clips lost" · „−3“ |
| „Tauchplatz „Wrack“ gesperrt: 2.500 Follower nötig“ | Warning · „Wrack gesperrt“ / "Wreck locked" · „2.500 F“ |
| „Sealog-Seite „Hausriff“ komplett! Ein Sponsor meldet sich …“ | Sponsor · „Hausriff komplett!“ / "House reef complete!" · „Sponsor“ |
| „Keine Luftstation hier – Hütte, Steg oder Boot-Kompressor.“ | Warning · „Keine Luftstation“ / "No air station" · „Hütte · Steg · Boot“ |
| „Das Handy bleibt trocken – erst an Land oder aufs Boot.“ | Warning · „Handy bleibt trocken“ / "Phone stays dry" |
| „FinTube zahlt: +36 $ (inkl. 25 $ Startbonus)“ | Money · „FinTube zahlt“ / "FinTube pays" · „+86 $“ |
| „Logbuch-Seite (7/24)“ | Collectible · „Logbuch-Seite“ / "Logbook page" · „7/24“ |
| „Fürs Sealog zielen [RMB]“ | removed (K.2) |
| Morning card (long) | 3 icons with numbers (views · +followers · +$) + the best clip's species name |

### L.11 (y) Animals on the island *(fauna: land fauna; props: perches, nests, pens; core: BodyPlans; filming: works on land)*

Land animals are creatures like the fish (same `ICreature`, `CreatureRegistry`, net sync), filmable and photographable
(Sealog page „Insel“ / "Island"), spawned by the fauna engineer's `LandFauna` around players on land within 60 m from
deterministic home spots (props publish perches, nests, burrows, pens in `PropInfo.LandSpots`). Cap 40 active.
New `BodyPlan`: `Bird, Lizard, Quadruped, Primate, Bat`; new `BehaviorType`: `Perch` (birds land on posts/roofs, fly
off when a player is < 4 m), `Soar`, `Bask`, `Climb`, `Herd`, `Mischief`.

| id | DE / EN (latin) | Where / when | Count | Size cm | Rarity / base | Behaviour |
|---|---|---|---|---|---|---|
| seagull | Möwe / Seagull (*Larus*) – incl. Horst's „Günther“ | dock, beach, hut roof, boat | 8 (Q5) | 40–55 | C 3 | Perch, steals snacks |
| frigatebird | Prachtfregattvogel / Magnificent Frigatebird (*Fregata magnificens*) | soars 30–60 m over the lighthouse; red throat pouch on display at the village | 2 | 90–110 | U 25 | Soar |
| tropicbird | Weißschwanz-Tropikvogel / White-tailed Tropicbird (*Phaethon lepturus*) | north cliffs | 3 | 70–85 | U 30 | Perch/Soar |
| marine_iguana | Meerechse / Marine Iguana (*Amblyrhynchus cristatus*) | tide pools, bay rocks; swims in the lagoon 0–5 m | 6 | 60–130 | U 40 | Bask, swims |
| gecko | Taggecko / Gold Dust Day Gecko (*Phelsuma laticauda*) | hut and village walls, palms | 6 | 10–14 | C 5 | Climb |
| coconut_crab | Palmendieb / Coconut Crab (*Birgus latro*) | palms and jungle, 20:00–05:00 | 3 | 30–100 | R 45 | Climb; 30 % steals a coconut you shook down nearby |
| goat | Ziege „Kapitän Meck“ & Herde / Goat "Captain Bleat" & herd | village pen, meadow near the radio tower | 5 | 100–140 | C 4 | Herd; eats sails, sits on crates (L.12) |
| pig | Schwein „Trüffel“ / Pig "Truffle" | village; swims at the village beach 10:00–12:00 | 1 (unique) | 90–140 | U 20 | Wander, swims |
| capuchin | Kapuzineraffe „Klappe“ / Capuchin "Clapper" | lighthouse railing, jungle | 1 (unique) | 35–50 | R 60 | Mischief: steals your hat for 5 s (G.3 rules) |
| fruit_bat | Flughund / Fruit Bat (*Pteropus*) | village palms, 18:30–21:00 | 10 | 15–30 | C 15 | Bat flights |

Ambient only (not in the Sealog): chickens (6, village), butterflies (jungle), dragonflies (waterfall pool), the Q5
day crabs. Fun facts (short): frigatebird „Kann wochenlang fliegen, ohne zu landen.“ / "Can fly for weeks without
landing." · marine iguana „Die einzige Echse der Welt, die im Meer taucht.“ / "The only lizard in the world that dives
in the sea." · coconut crab „Größter Krebs an Land – knackt Kokosnüsse.“ / "Biggest crab on land – cracks coconuts." ·
Klappe „War 1987 das Maskottchen des TV-Teams. Hat seitdem schlechte Manieren.“ / "Was the TV crew's mascot in 1987.
Has had bad manners ever since." · Trüffel „Schwimmt lieber als zu wühlen.“ / "Prefers swimming to digging."

### L.12 (ab) The Chopper Shop, redesigned – and mishaps *(ui: app; fishboat: HeliService/HeliChopper/HeliCrate; npc: Wanda lines; props: hangar, drop zones; fauna: goat)*

**App „Chopper Shop“** (ui):
* Header: Wanda's portrait (bean, aviator cap `#6B4636`, goggles, orange flight suit `#EE7F2D` with patches, scarf
  `#3FB8C4`) with a speech bubble – one line per day („Heute Rückenwind! Lieferung vielleicht sogar pünktlich.“ /
  "Tailwind today! Delivery might even be on time.").
* Categories as big tiles with glyphs: Tauchen / Diving, Kamera / Camera, Boot / Boat, Angeln / Fishing, Snacks,
  Teile / Parts.
* Item cards (2 per row): 3D icon on a light card, tier dots, **stat bars compared to what you own** („Luft +30 s“ in
  green, worse in grey), price, lock with the reason („ab 2.500 Followern“ / "from 2,500 followers").
* **„Angebot des Tages“ / "Deal of the day":** one item −20 % (seeded per day), gold ribbon.
* **Delivery tracker** strip at the bottom: chopper icon moving along a dotted line, ETA „noch 1:40 Std.“ / "1:40 h to
  go", express toggle.

**Wanda in the world:** after each delivery she stays 2 minutes on the pad (rotor idling) and says one line when a
player is within 20 m; her hangar shack at `HangarSpot` (wind sock, hammock, radio – W1 quest).

**Mishaps** (fishboat; deterministic from `HeliService.Hash(delivery)` so every co-op machine agrees; never on day 1,
never for the tutorial tank, at most 1 per day, express orders halve the chance):

| Roll | Mishap | Where | How to get it | Wanda on the radio (radio card) |
|---|---|---|---|---|
| 70 % | normal | helipad | E | – |
| 5 % | water (exists) | lagoon near the pad | swim, E | „Kleine Böe. Dein Paket badet.“ / "Small gust. Your parcel is taking a bath." |
| 6 % | hut roof | hut roof | climb the new roof ladder, E (achievement *Lieferung aufs Dach*) | „Ich dachte, das Dach ist der Landeplatz. Sieht von oben gleich aus.“ / "Thought the roof was the pad. Looks the same from up here." |
| 6 % | palm | a palm crown near the pad | shake the palm (E) – coconuts fall too | „Die Palme wollte es mehr als du.“ / "The palm wanted it more than you." |
| 6 % | **wrong place** | one of 12 drop zones: village beach, tide pools, lighthouse yard, pool beach, radio plateau, driftwood beach, hidden-bay beach, ravine north rim, second-beach headland, helipad meadow, waterfall plateau, hut beach | **orange smoke beacon** (25 m column for 3 min, then a blinking light), map marker, quest line „Paket suchen“ / "Find the parcel" | „Ups. Falsche Insel? Nein, richtige Insel, falsche Ecke. Folg dem Rauch!“ / "Oops. Wrong island? No – right island, wrong corner. Follow the smoke!" |
| 4 % | village | village terrace | Lio guards it: take a selfie with him | „Die Kinder haben's. Zahl mit einem Selfie.“ / "The kids have it. Pay with a selfie." |
| 2 % | goat | helipad | Kapitän Meck sits on the crate – nudge him 3× (E) | „Da sitzt eine Ziege drauf. Das ist nicht mein Problem.“ / "A goat is sitting on it. Not my problem." |
| 1 % | boat | on your boat at the dock (if it is there, else water) | E on deck | „Direkt aufs Boot! Ich bin ein Genie. Oder es war Zufall.“ / "Straight onto the boat! I'm a genius. Or it was luck." |

Crate positions for the drop zones are computed from the hash + `PropInfo.DropZones` (props publish 12 reachable,
flat points ≥ 3 m from water edges).

### L.13 (v, ac) Animations and much smoother characters *(player: DiverPuppet, DiverBuilder, DiverMeshes, PlayerHands, ladders; filming: camera models; npc: NPC builder on the same parts; net: motion state; props: ladder data)*

**Ladders everywhere:** props publish every ladder in `PropInfo.Ladders` (`bottom, top, outward normal, rung spacing
0.30 m, kind Wood/Rope/Metal`): dock, boat, ravine ×2, lighthouse rope ladder (Q7), lighthouse maintenance ladder (L.6),
radio tower, hut roof (L.12), tree house, boat shed rack. The player climbs any of them (today only the dock ladder):
Space/E within 1.2 m, facing it.

**Motion set** (procedural – no Animator; `DiverPuppet.SetPose(MotionState state, float speed, float phase, …)`
with `MotionState { Idle, Walk, Run, Jump, Fall, Land, Ladder, Swim, Dive, Sit, Lie, Carry, Tow, Selfie,
Unconscious, Crouch }`; net sends state + speed + phase in the existing avatar message, 2 bytes):

| State | Third person (co-op, selfie, preview, NPCs) | First person |
|---|---|---|
| Idle | breathing 1.5 %, weight shift every 4–7 s, look-around; fidgets every 10–20 s (adjust mask, scratch head, check the wrist computer) | hand idle sway (Art Bible 1.13) |
| Walk (≤ 2.2 m/s) | stride 0.55 m, 1.9 steps/s, bounce 4 cm, hip sway ±4°, arm swing ±22°, counter-rotating shoulders ±6°, head stabilised | bob 10 mm / sway 6 mm per step, hands lag 0.06 s, a soft "dip" on each footfall |
| Run (sprint) | lean 10° forward, stride 0.85 m, 2.6 steps/s, elbows bent 80° pumping ±45°, bounce 7 cm, 4 % squash on each footfall | hands lower 4 cm and pump, bob ×1.6, FOV +4° kick (respects the head-bob/shake toggles) |
| Start / stop | 0.15 s anticipation lean; 3° overshoot when stopping | small hand overshoot |
| Jump / Fall / Land | crouch 0.1 s (squash 8 %), stretch 6 % in the air with arms up; falls > 1.5 m windmill the arms; land squash 10 % for 0.12 s | camera dip 4 cm on landing, hands bounce |
| **Ladder** | faces the ladder 0.28 m away; hands alternate on rungs 0.3 m apart, knees alternate; top: both hands on the top rail, 0.5 s mantle | **both mitten hands visible** gripping rungs alternately (held items stowed, restored at the top): reach 0.22 s + pull 0.23 s per hand; camera rises per rung with an ease (not linear), roll ±1.5° alternating; W/S up 0.65 m/s / down 0.8 m/s; pitch limit −50°…+60°; grab sound per hand (wood/rope/metal); wet drips when climbing out of the water |
| Swim (surface) / Dive | breaststroke arms + flutter kick; dive: flutter kick (today) + trailing arms; sprint = dolphin kick | swim stroke (0.35 s, today) |
| Crouch (L.6) | knees 70°, lean 15° | eye height 1.0 m |
| Carry (K.3, L.7) | both arms forward holding the item | the item in both hands, lower screen |
| Sit / Lie | boat, benches / beds and hammocks (L.9) | camera lies down (L.9) |
| Selfie (K.4) | one arm stretched toward the lens, expression on the face | – (selfie camera) |
| Tow / Unconscious | F.1 | F.1 |

**Character art direction – rounder, smoother, nicer** (supersedes Art Bible §1.12 "12 radial segments"; the world
stays faceted; How-to-Fish bean proportions stay):
* **Silhouette:** 1.85 m bean, bottom radius 0.27 → **0.29 m** with a soft belly bulge, softer shoulders, slightly
  bigger fins/boots (cartoon). No visible facets on any character edge at 1080p from 2 m (silhouette polygon edge ≤ 10
  px).
* **Mesh density** (all smooth normals, LatheY with more segments): body **32 radial × 24 profile rings** (was 12 × 13);
  head **32 × 18** (was 14 × 9), seam hidden under the collar ring with matching radius and normals; eyes **12 × 20**
  spheres (was 6 × 12), pupils 8 × 16; limbs tapered capsules **16 radial × 8 rings** with round ends and **ball
  joints** at elbows/knees (radius = limb radius × 1.08 – bends never show gaps); tank 24 radial; hats 24–32 radial.
* **Budget per diver:** LOD0 ≤ 9,000 triangles (incl. face, gear, hat), LOD1 (> 12 m) ≤ 3,500, LOD2 (> 35 m) ≤ 1,200;
  character screen, selfie and AutoShots always LOD0. Meshes are built once and shared (cache as today).
* **Face:** eyes r 0.055 → **0.062 m** with a white highlight dot (r 0.012 m) on each pupil; **eyelids** (half-sphere
  caps in skin colour) blink every 3–6 s and shape expressions; brows (1c); **8 mouth shapes** (smile, open, o, flat,
  grin, wow-O, scared-wavy, kissy) swapped per expression; optional cheek blush discs (α 0.35) when happy.
* **Shading:** URP Lit as today; smoothness neoprene **0.18** (a soft sheen), skin 0.10, mask glass 0.6; no outlines,
  no textures except tiny details (Art Bible anti-patterns stay).
* **First-person hands:** mitten 0.085 × 0.10 m, **~900 triangles each**, separate rounded thumb with its own joint,
  palm pad, subtle knuckle bumps, idle finger-curl; cuffs from `DiverLook.cuff`.
* **Held items** (camera models: filming; others: player): rounded bevels 3–5 mm on every box edge, 24-segment lenses
  and knobs, the lamp with a knurled ring, the reel with a smooth spool, the phone with rounded corners, the wrist
  computer with a bevelled glass; ≤ 2,500 triangles per item; hard edges only where faces meet at > 50° (split
  normals).
* **NPCs** (Horst, Wanda, Pearl, villagers, Toni, Chad & Brad, Dr. Fin) use the same high-res parts with their own
  proportions (npc engineer builds on `DiverMeshes`).
* **Acceptance:** AutoShot `16_character` (preview), new `19_crew_closeup` (4 divers at 2 m), `21_ladder_fp` (first-
  person ladder mid-climb: both hands on rungs) and `22_run_avatar` (co-op avatar sprinting, side view).

### L.14 Ownership for L (who builds what)

| Engineer | L items |
|---|---|
| **core** | `SpeciesDef` fields (zones, prefDepth, dayMask, homeSite, page, unlockFlag) + 21 new entries' data skeletons (fauna fills meshes/behaviour); JournalEntry counters (L.2); `WorldLayout`/`DiveSites` 0.62 values (L.5); `Isl()` = × 0.62; literal coordinates in Boot/Debug; `QuestTracker`, `RaiseNotify` + `NotifyKind`, conversion of core call sites (L.10); `SleepVote` facade (L.9); `Quests`/carry state (L.7); `GameAction.Down` crouch use; BodyPlan/BehaviorType appends (L.11) |
| **ui** | Sealog counters, depth tab, depth bar (L.2); FinTube title field + chips + collage composer (L.4); quest line, notification style, conversion of ui call sites (L.10); sleep counter (L.9); Collection app „?“ + clues (L.6); Chopper Shop app (L.12); quest markers on the map app (L.7) |
| **player** | crouch (L.6); generic ladders + first-person ladder hands (L.13); run/jump/land feel; DiverPuppet motion set; DiverBuilder/DiverMeshes high-res bean, face, hands, items except camera models (L.13); lie-down pose + camera (L.9); carry pose (L.7); intro literals (L.5) |
| **worldfx** | `WorldHeightField`/`WorldFeatures`/`WorldTerrainData` 0.62 (L.5) incl. the ravine seam fix, village terrace, Fog Isle; menu camera literal; seabed facets around the new patch reefs |
| **props** | every placement literal (L.5), the detail pass (17 spots), Waterfall Grotto (cave mouth in the cliff, root curtain, squeeze), the village (L.8), new reefs + habitat anchors (L.3), secrets (L.6), sleep spots (L.9), ladders data (L.13), drop zones + hangar + roof ladder (L.12), land-fauna spots (L.11), quest objects (L.7) |
| **fishboat** | mishaps + smoke beacon + crate positions (L.12); village jetty as a boat landing point (L.8) |
| **npc** | side quests (L.7), village people + schedules + lines (L.8), Wanda lines (L.12), clue lines (L.6), quest tracker feed (L.10) |
| **livestream** | deal lines in the quest tracker (L.10) |
| **fauna** | open-world zones + depth-band spawner (L.1), 5 open-sea species + cleaner wrasse, land fauna with 10 species + ambient animals (L.11), habitat-anchor use |
| **net** | `SleepVote 99`, `SleepState 100` (L.9), motion state/phase bytes (L.13), quest and carry state, land-fauna proxies (existing creature sync) |
| **filming** | sightings/depth counters (L.2), collage photo quality (L.4), camera models redone (L.13), detection on land (L.11) |

### L.15 Open points for the next click-question round

1. Village name „Altriff“ / "Oldreef" and the community name „die Altriffer“ – OK, or another name?
2. Fog Isle: only reachable with Toni's map (fog bank) – or findable by accident?
3. Should the 10 island animals count for the golden camera (Sealog 100 %)?
4. Selfie: H + expressions on 1–6 – OK? Which expressions are missing?
5. Auto-record under water on by default – or always manual?
6. Sponsor overlap: only identical topics (e.g. shark) – or also related ones (shark + depth)?
7. Music under water: calm and dreamy – or slightly mysterious from 30 m?
8. Chopper mishaps: about 3 in 10 deliveries – too often, right, too rare?
9. Characters: stay How-to-Fish beans (only rounder) – or slightly more human proportions?
10. Quest line: one line only – or the next task as a small second line?
11. Island at 62 %: good – or even smaller / a bit bigger again?
12. Should the village sell more than food (e.g. boat cosmetics from Mira)?

---

## Appendix – quick reference of all new shop items

| id | DE / EN | Slot | Price | Followers | Note |
|---|---|---|---|---|---|
| boat_compressor | Boot-Kompressor / Boat Compressor | Compressor | 600 $ | 250 | E.4 |
| uw_radio | Unterwasser-Funk / Underwater Radio | Radio | 2,500 $ (1,250 $ after M2) | 2,500 | E.5 |
| cammod_zoom | Zoom-Objektiv / Zoom Lens | CameraMod | 3,000 $ | 1,500 | max zoom ×1.5 |
| cammod_nv | Nachtsicht-Modul / Night Vision Module | CameraMod | 12,000 $ | 10,000 | Q32 |
| rod_t3 | Forschungsrute / Research Rod | Rod | – | Dr. Fin R4 | D.3 |
| snacks (6) | see E.2 | – (pantry) | 20–80 $ / 3-pack | 0–25,000 | E.2 |
| tank_t1 (changed) | Pressluftflasche / Air Tank | Tank | **100 $** (was 130 $) | 0 | K.1 |
| boat_winch | Bootswinde / Boat Winch | BoatWinch | 400 $ | 250 | K.3 – winches heavy salvage aboard |
| radio_battery | Funkbatterie / Radio Battery | Part | 80 $ | – (only during K.8 T4) | K.8 |
| village food (3) | Kokos-Fischcurry / Coconut Fish Curry, Seetrauben-Salat / Sea Grape Salad, Ingwer-Limo / Ginger Fizz | – (pantry, Ada's kiosk, not by chopper) | 45 / 25 / 30 $ | A1 / A2 / – | L.8 |
| „Angebot des Tages“ | one shop item −20 % per day (seeded) | – | – | – | L.12 |
