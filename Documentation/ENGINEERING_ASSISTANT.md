# Engineering Assistant

## Scope

The Engineering Assistant is a local-first module built on the Elite Dangerous Player Journal. It provides:

- combined Horizons, Ship Locker, and Backpack inventory;
- live updates when materials are collected, traded, discarded, or consumed;
- engineer unlock/rank status;
- persistent blueprint wishlist;
- exact material requirements for the requested number of crafts;
- acquisition guidance for missing materials.

## Access

Open the assistant from either:

- **ENGINEERING** on the startup/control window;
- **Engineering Assistant** in the tray menu.
- Select **ИНЖЕНЕРИЯ** in the activity drop-down or use its configurable shortcut (`Ctrl+2` by default) while Elite Dangerous is active.

The compact overlay shows up to five highest deficits. It is click-through in passive mode; `Ctrl+6` enables its controls and dragging.

Each displayed deficit includes the preferred acquisition method and a `FLY` destination when the local knowledge base has a reliable fixed site. The system name can be copied from the overlay. HGE locations remain dynamic, so the fixed destination shown for manufactured materials is explicitly presented as a fallback rather than a guaranteed G5 source.

It remains available even when Elite Dangerous is not running, using the last cached commander inventory.

## Storage

Local state is stored in:

```text
%APPDATA%/EDActivityOverlay/companion.db
```

The SQLite database uses WAL mode and stores wishlist, latest material counts, and engineer progress. No commander data is uploaded.

Recipe cache:

```text
%APPDATA%/EDActivityOverlay/catalogs/
```

## Recipe semantics

Wishlist quantity means the exact number of engineering crafts/rolls. It does not attempt to guarantee that a module reaches 100% of a grade because roll progress is not deterministic in all game versions and contexts.

Recipes are loaded from the public [EDCD/coriolis-data](https://github.com/EDCD/coriolis-data) engineering data and cached locally. If download and cache are unavailable, a starter catalog containing common recipes is used.

## Acquisition guidance

The advisor uses specific rules for important materials such as Pharmaceutical Isolators, Imperial Shielding, Core Dynamics Composites, Datamined Wake Exceptions, Selenium, and other high-grade materials. Unknown materials receive category-level guidance:

- Raw: DSS body search, surface/geological/biological collection, Raw trader;
- Manufactured: signal sources, HGE, salvage, missions, Manufactured trader;
- Encoded: ship/wake/data-point scanning, Encoded trader;
- Odyssey: settlement type/economy, mission rewards, bartender/carrier trade.

Advice is intentionally phrased as a strategy rather than claiming that a remote signal source currently exists. Live HGE signals only become authoritative after the player enters the system and discovers them through the nav beacon or FSS.

## Architecture

```text
JournalMonitorService
  -> JournalEventHub
      -> trading reducer
      -> EngineeringService
          -> inventory state
          -> BlueprintCatalogService
          -> wishlist calculation
          -> MaterialAcquisitionAdvisor
          -> EngineeringRepository (SQLite)
```

The event hub is the extension point for exploration and exobiology modules.

## Third-party data

- Coriolis engineering data: `EDCD/coriolis-data`; game data belongs to Frontier Developments and is governed by the terms referenced by that repository.
- The open-source EDOMH application was reviewed as an architectural reference. Its proprietary `edomh-core` component and official binary assets are not included or accessed.
