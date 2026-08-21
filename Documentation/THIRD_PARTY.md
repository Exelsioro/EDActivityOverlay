# Third-party data attribution

The fallback Russian names for Elite Dangerous engineering materials in
`EngineeringLocalization.cs` are adapted from the Russian translation table in
[EDDiscovery/EliteDangerousCore](https://github.com/EDDiscovery/EliteDangerousCore/blob/master/EliteDangerous/Translations/translation-russian-ed.tlp).

EliteDangerousCore is distributed under the
[Apache License 2.0](https://github.com/EDDiscovery/EliteDangerousCore/blob/master/LICENSE).
The data was reduced to engineering-material identifier/name pairs and changed
into a C# lookup table. Values supplied directly by the local Player Journal
take precedence over this fallback table.

Russian names for Coriolis engineering blueprints, experimental effects,
modules and recipe ingredients in `CoriolisRussianLocalization.cs` are adapted
from [EDCD/coriolis](https://github.com/EDCD/coriolis/blob/master/src/app/i18n/ru.json).
The Coriolis project is distributed under the
[MIT License](https://github.com/EDCD/coriolis/blob/master/LICENSE).

Ship engineer locations and unlock stages in `EngineerCatalog.cs` are adapted
from [EDDiscovery/EliteDangerousCore](https://github.com/EDDiscovery/EliteDangerousCore/blob/master/EliteDangerous/FrontierData/Items/Engineers.cs).
EliteDangerousCore is distributed under the Apache License 2.0. The application
ships a reduced offline directory and overlays the commander's live Journal
progress instead of copying EDDiscovery's implementation.

The engineering catalog also downloads the engineer assignments from
[`RecipesEngineering.cs`](https://github.com/EDDiscovery/EliteDangerousCore/blob/master/EliteDangerous/FrontierData/Items/RecipesEngineering.cs),
caches the source beside the Coriolis catalog, and joins assignments to recipes
by Frontier blueprint identifier and grade. No EDDiscovery executable code is
loaded or executed.

Universal Cartographics scan and surface-mapping estimates in
`ExplorationValueCalculator.cs` are adapted from the post-3.3 calculation in
[`EstimatedValues.cs`](https://github.com/EDDiscovery/EliteDangerousCore/blob/master/EliteDangerous/FrontierData/Enumerations/EstimatedValues.cs).
The formula was reduced to an independent journal-string based module; no
EliteDangerousCore assembly, EDDiscovery installation, database, or process is
required. Copyright 2021-2023 EDDiscovery development team, Apache License 2.0.

The general-purpose part of
[EDDiscoveryData](https://github.com/EDDiscovery/EDDiscoveryData) was reviewed as
well. Its relevant exploration files are curated expedition routes rather than
reusable calculation modules, so they are deliberately not bundled or loaded.

The optional X52 Pro integration uses a clean C# dynamic binding based on the
DirectOutput function signatures, MFD button masks and LED component indices in
[Theaninova/EDDX52](https://github.com/Theaninova/EDDX52) (formerly
wulkanat/EDDX52). EDDX52 is distributed under the Apache License 2.0. The code
was rewritten for managed lifetime, optional settings and immutable journal
state; neither its binary nor EDDiscovery is loaded. Logitech's proprietary
`DirectOutput.dll` is not distributed and is used only from the user's installed
driver package.

Exobiology colony distances and conservative genus value ranges in
`ExobiologyCatalog.cs` are derived from the Canonn Bioforge dataset published
by [Elite Dangerous Warboard](https://github.com/njthomson/Elite-Dangerous-Warboard).
Elite Dangerous Warboard is distributed under the
[MIT License](https://github.com/njthomson/Elite-Dangerous-Warboard/blob/main/LICENSE).
Only a compact offline summary by genus is shipped; the application does not
copy Warboard's UI or executable code.

Nearby exploration POIs are requested at runtime from the documented
[EDAstro Galactic Exploration Catalog API](https://edastro.com/gec/APIinfo) and
from Canonn's public Guardian/Thargoid patrol datasets used by the
[Canonn EDMC plugin](https://github.com/canonn-science/EDMC-Canonn). Responses
are cached locally and remain identified by provider; no provider code is
loaded into the application.

Exploration routes can be imported from files explicitly exported by
[Spansh](https://spansh.co.uk/). The app can also opt into direct Road to Riches
calculation using Spansh's public form/job protocol (`/api/riches/route` and
`/api/results/{job}`), independently implemented after interoperability review
of [EDMC-SpanshTools](https://github.com/wuuthradd/EDMC-SpanshTools). No plugin
code, credentials or browser cookies are loaded. The endpoint is not part of
the documented system-data OpenAPI API, so file import remains the stable
fallback.

DSS result fields and their timing follow Frontier Developments'
Elite Dangerous Player Journal documentation for `SAAScanComplete`. The probe
layout diagrams are original configurable guidance and are not copied from a
third-party application.
