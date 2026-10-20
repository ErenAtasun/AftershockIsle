# Aftershock: Isle

<p align="center">
  <img src="Docs/images/banner.jpg" alt="Aftershock: Isle key art: a lone survivor walking through a ruined city after an earthquake" width="900" />
</p>

<p align="center">
  <strong>A 2.5D side-view survival base-builder set in a city hit by an earthquake.</strong><br>
  Keep a cracked building standing through aftershocks, floods and fires, long enough to call the evacuation helicopter.
</p>

<p align="center"><a href="#türkçe">Türkçe özet aşağıda</a></p>

| | |
|---|---|
| **Engine** | Unity 6000.3 LTS · URP · new Input System |
| **Genre** | Crisis management · survival base-builder |
| **View** | Side-view cross-section of a 4-storey building (3D models, movement locked to X/Y) |
| **Modes** | Campaign (evacuate by day 10) · Endless (supply drops every 3 days, survive as long as you can) |
| **Team** | [PBH Studio](https://erenatasun.github.io/pbh-studio-website/): Eren Atasun, Gonca Arabacı |
| **Made for** | Game jam, October 2026 |
| **Design doc** | [`GDD/Aftershock_ Isle GDD.docx`](GDD/) |

---

## The game

*This War of Mine*'s atmosphere meets *Oxygen Not Included*-style active-pause crisis management. Survivors shelter in a four-storey building seen in cross-section: basement, entrance floor, living floor with the workbench, and the roof with a broken radio and a helipad.

<p align="center">
  <img src="Docs/images/shelter.jpg" alt="The shelter in cross-section: four floors, survivors at work, HUD with resources and structural integrity" width="860" />
</p>

Every in-game day runs through three phases:

| Morning (~90 s) | Midday (~105 s) | Night (~45 s) |
|---|---|---|
| Dig rubble, brace columns, pump water, craft at the workbench | Leave through the exit door for the city map: scavenge, fight, rescue survivors | Aftershocks, flooding, fires and night raids. Plan who stays, who guards the door, who scouts and who eats |

**Win:** upgrade the workbench to tier 3, repair the rooftop radio, clear the helipad and hold a 60-second evacuation signal.
**Lose:** the building's Structural Integrity hits 0 %, the basement floods for too long, or every survivor dies.

## Expeditions

At midday a survivor walks out of the door and the city map opens: six locations (construction site, ruined clinic, abandoned workshop, rubble field, ruined market, gas station) with their own loot tables, hazards and enemies. Pick a companion, pack the bag, and go.

<table>
  <tr>
    <td width="44%"><img src="Docs/images/city_map.jpg" alt="Hand-drawn city map with the shelter and expedition locations" /></td>
    <td width="56%"><img src="Docs/images/loadout.jpg" alt="Expedition loadout: shelter storage on the left, the explorer's backpack on the right with a weight bar" /></td>
  </tr>
  <tr>
    <td align="center"><sub>City map: locations unlock day by day</sub></td>
    <td align="center"><sub>Loadout: weapon, ammo, kits and tools all weigh something</sub></td>
  </tr>
</table>

The bag is weight-based (10 kg, more with a companion or the large backpack): every kilo of gear you take is a kilo of loot you leave behind. A crowbar opens locked crates and a pickaxe digs out buried caches, so the choice is often *gun or tools*. In the field you sneak, take cover behind cars and barriers, fight raiders and dogs, and walk back to the exit (or press `X` to run for it) before time runs out.

<p align="center">
  <img src="Docs/images/outdoor_street.jpg" alt="An expedition street seen from the game camera: road, sidewalk, ruined shop fronts, a van and rubble" width="860" />
</p>

## Systems

- **Structural integrity:** instant damage from aftershocks and continuous drain from water and fire. A cracked column that isn't braced breaks in the next aftershock and doubles that aftershock's damage; steel reinforcement absorbs it.
- **Disaster director:** schedules aftershocks (with a 6-second "Triangle of Life" warning where safe spots glow), burst pipes and basement flooding (water touching the breaker panel electrifies the floor), fires, gas leaks, weather and night raids.
- **Active pause:** `Space` freezes time to inspect rooms and assign orders; `1` / `2` for normal and double speed.
- **Survivors:** tasks, health, stress, hunger, traits and chatter; a 3-branch skill tree (Construction · Expedition · Engineering).
- **Evening plan:** at nightfall, choose per survivor whether to rest, stand guard or scout at night, and who gets food.
- **Crafting & economy:** workbench tiers, tools (pickaxe, crowbar, toolbox, head lamp…), weapons and ammo, placeable structures, electric pump, power grid, shared storage chest.
- **Save system:** JSON save/load with an autosave at the start of each day.

## Controls

| Shelter | | Field | |
|---|---|---|---|
| `Space` | Pause / resume | `WASD` | Move |
| `1` / `2` | Normal / double speed | Mouse / `Left click` | Aim / fire |
| `Tab` | Next survivor | `Shift` | Crouch behind cover |
| `F` | Interact with nearby object | `Space` | Dodge |
| `B` | Build mode | `F` | Search, pick up, leave at the exit |
| `M` / `I` | City map / inventory | `R` | Use a life kit |
| `K` / `H` | Skill tree / help | `Q` | Switch weapon |
| `Alt` | Show all markers | `X` | Run to the exit |
| `Esc` | Menu | `I` | Field backpack |

Gamepad is supported throughout; the on-screen prompts switch to the controller layout automatically.

## Architecture

```
Assets/Scripts/
├── Core/          Day cycle, time manager, game session & modes, EventBus, A* NavGrid, input
├── Building/      Structural integrity, columns, floors, power grid, shelter grid, placement, model manager
├── Crisis/        Disaster director & schedule, aftershocks, flooding, fire, gas, weather, night defense
├── Characters/    Survivors, roster, tasks, upkeep, traits, character & dog views
├── Expedition/    City-map expeditions, field combat, enemies, companion, loot, outdoor kit
├── Crafting/      Workbench, recipes, tools, placeable structures, pumps
├── Interaction/   Breaker panel, burst pipe, rubble, furniture, extinguisher, pumps
├── Save/          JSON save data + autosave
├── UI/            HUD, windows, city map, loadout, evening plan, skill tree, tutorial hints, settings
└── Editor/        Greybox scene builder, asset sync, character library, outdoor kit builder, prop tools
```

~150 C# scripts, ~38k lines.

- **EventBus:** systems talk through small typed event structs instead of referencing each other directly.
- **NavGrid:** A* pathfinding on a walkability grid, with line-of-sight path smoothing so only the corners that matter remain.
- **Code-built UI:** HUD and windows are built with uGUI from code (`UiFactory`); icons are drawn procedurally, so there are no missing-glyph boxes.
- **Procedural outdoor kit:** the expedition scenery (ground tiles, cars, barriers, ruined buildings, street furniture) is generated by Python scripts in Blender and rebuilt into prefabs by an editor tool.

<p align="center">
  <img src="Docs/images/outdoor_kit.jpg" alt="Part of the procedural outdoor kit: wrecked cars, dumpster, barriers, sandbags, bus shelter and ruined buildings" width="860" />
</p>

## Running

1. Open the project with **Unity 6000.3.20f1** (URP).
2. Open `Assets/Scenes/MainMenu.unity` and press **Play**.

Editor tools live under the **Aftershock** menu:

| Menu item | What it does |
|---|---|
| `Varlıkları Yenile` | Rebuilds the asset registry (characters, field models, UI art). Runs automatically when new models or art are imported. |
| `Dış Mekan Kitini Kur` | Rebuilds the outdoor kit (materials, prefabs, `Resources/OutdoorKit.asset`) from `Assets/Models/Outdoor`. Runs automatically when the kit changes. |
| `Greybox Sahnesini Oluştur` | Rebuilds the main scene from code. Only needed after layout changes. |

To change the outdoor kit, edit the scripts in [`BlenderSource/Outdoor`](BlenderSource/Outdoor/) and export again (see its README). The README images of the kit are rendered with `blender -b --factory-startup -P BlenderSource/Outdoor/render_readme.py`.

---

## Türkçe

**Aftershock: Isle**, depremle yıkılmış bir şehirde geçen 2.5D yandan görünüm kriz yönetimi ve hayatta kalma oyunu. Kazazedeler dört katlı bir binada sığınıyor; her gün sabah bakım ve üretim, öğlen şehre sefer, gece afet baskısıyla geçiyor. Binanın **Yapısal Bütünlük** barı artçılar, su baskını ve yangınla eriyor; sıfırlanırsa bina çöküyor. Kazanmak için tezgâhı 3. kademeye yükseltip çatıdaki telsizi onarmak ve 60 saniye kesintisiz tahliye sinyali vermek gerekiyor.

Öne çıkan sistemler:
- Takvimli afet yöneticisi ve "Yaşam Üçgeni" uyarısı, yapısal bütünlük modeli, aktif duraklatma
- Kazazede görevleri, akşam planı (kim kalır, kim nöbet tutar, kim keşfe çıkar, kim yer) ve 3 dallı yetenek ağacı
- Şehir haritasında 6 lokasyon, kg tabanlı sefer çantası, siperli saha çatışması
- Tezgâh üretimi ve aletler, JSON kayıt sistemi, kampanya ve sonsuz mod
- EventBus mimarisi, A* yol bulma, Blender Python ile prosedürel üretilen dış mekân kiti

Unity 6000.3.20f1 ile `Assets/Scenes/MainMenu.unity` sahnesini açıp **Play**'e basman yeterli.

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="Docs/images/pbh_logo_light.png" />
    <img src="Docs/images/pbh_logo.png" alt="PBH Studio" width="140" />
  </picture>
  <br>
  <sub>PBH Studio tarafından bir game jam için geliştirildi (Ekim 2026).</sub>
</p>
