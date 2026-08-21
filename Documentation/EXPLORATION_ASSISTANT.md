# Exploration and Exobiology Assistant

The exploration workspace combines local Player Journal state with optional
community system data. Personal progress is never inferred from Spansh or EDSM:
a community body may fill descriptive fields, but only `Scan`,
`SAAScanComplete`, signal and organic Journal events mark commander activity.

## Compact overlay

The activity shortcut (`Ctrl+3` by default) toggles the compact workspace. It
shows current-system FSS progress, detailed scans and DSS mappings made during
the current visit, efficient mappings, biological signals, valuable mapping
targets, organic sample spacing, fuel risk, imported-route progress and nearby
curated POIs. The surface radar points along the shortest route out of the last
sample's colony-exclusion radius; it deliberately does not claim to know the
location of an undiscovered colony. Use `Ctrl+6` to interact and open the full
system catalog.

## Full system catalog

The full view is opened only from the compact exploration widget. While open it
owns overlay interaction and the cursor until **Close** is pressed, then restores
the previous interaction state.

The catalog supports text search and filters for notable, valuable, biological,
unmapped and landable bodies. Selecting a body shows scan/mapping estimates,
distance, gravity, temperature, atmosphere, volcanism, biological signals and
the exact data source. System and body names can be copied independently.

## Exobiology prediction

Local Scan/FSS data is matched against an offline, attributed Canonn Bioforge
histogram catalog. The advisor ranks plausible species and variants using body
class, atmosphere, volcanism, temperature, gravity and pressure. Confirmed
genus signals narrow the result. These are predictions, not discoveries; the
Journal remains authoritative for personal progress and sample completion.

## Routes and fuel

The assistant imports official Spansh CSV/JSON exports for Road to Riches,
travel and neutron routes. The Route tab can also validate source/destination
system names, submit a Road to Riches calculation to Spansh, poll the background
job and install the verified non-empty response without opening a browser. File
import and the website remain available as fallbacks. A failed calculation does
not replace the active route.

The active plan is stored under the application data directory and advances
when the Journal reports arrival in a route system. The next system can be
copied from the full view.

Fuel advice uses `Status.json`, `Loadout`, observed jump consumption and
`NavRoute.json`. It identifies the next scoopable star, estimates fuel needed to
reach it and preserves an emergency reserve. It is a warning model rather than
a replacement for the galaxy-map route plotter.

## Nearby points of interest

With community POIs enabled, coordinates from the Journal are used to request
the nearest rated Galactic Exploration Catalog entry from EDAstro and the
nearest supported Guardian/Thargoid entry from Canonn's public datasets.
Providers fail independently: if either source is unavailable, the other can
still be shown. Responses are cached for 24 hours; no EDDiscovery installation
or database is required.

## DSS probe layouts

The full body panel includes numbered layouts for efficiency targets 2 through
12. Select the number displayed in the lower-right corner of the in-game DSS.
Markers distinguish the visible disc, limb shots and shots beyond the limb that
wrap towards the far side. The diagram is a repeatable starting pattern; live
coverage must still be used to correct aim because distance and engineered
probe radius change overlap.

After `SAAScanComplete`, the body stores `ProbesUsed` and `EfficiencyTarget` and
shows whether the efficiency bonus was achieved. Frontier writes both fields
only after mapping is complete, so pre-scan automatic selection cannot come
from the Journal. Screen OCR is therefore displayed as a disabled experimental
setting until resolution/UI-scale calibration is implemented and verified.

## Exploration log and unsold estimates

The Log & Findings tab records system visits, notable scans, DSS results,
biological signals and completed samples, and new Codex discoveries. Entries
can be bookmarked, filtered and copied. A body in the catalog can also be added
as a manual commander finding. Up to 500 recent/bookmarked entries are retained
in `%APPDATA%\ED_Inara_Overlay\exploration-log.json`.

The assistant reconstructs unsold Universal Cartographics and exobiology
estimates from all available Journal files in a background task. Body values
are replaced by their mapped result rather than double-counting Scan and DSS.
`SellExplorationData`/`MultiSellExplorationData` reset the cartographic ledger;
`SellOrganicData` resets the biological ledger. The result is explicitly shown
as an estimate because first-discovery/first-mapped and exobiology bonuses can
make the station's final payout differ.

## Community-data disclosure

Settings > Journal > Exploration data offers three modes:

- **Personal Journal only** never merges community body fields.
- **Enrich bodies already scanned** fills missing fields only for a body that
  produced a personal `Scan` event. This is the default.
- **Reveal the full system catalog** adds bodies known to Spansh/EDSM even when
  they have not been personally scanned.

The full mode can reveal discoveries before the player resolves them in game,
so it must remain an explicit choice.

## Persistence and limitations

- Closed historical Journal files are imported in the background into
  `%APPDATA%\ED_Inara_Overlay\companion.db`. The current Journal continues
  through the live monitor and becomes importable after it is closed.
- Personal body history is keyed by commander, system and body and records
  scanning, mapping, efficiency, first-discovery/mapping evidence, biological
  signal counts and completed organic species. Re-importing an unchanged file
  is skipped and changed files are safe to process again.
- The full view is a body catalog, not yet an orrery.
- Community data never marks a body as personally scanned or mapped.
- Direct Spansh Road to Riches calculation uses the same public job protocol as
  current community clients. It is not part of Spansh's documented system-data
  OpenAPI surface and can change; offline export import remains supported.
- OCR of the in-game DSS target is not enabled yet. Manual target selection is
  the stable supported mode.
