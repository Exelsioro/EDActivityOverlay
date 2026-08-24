# Third-party attribution and data

This document records third-party code/data sources that are either adapted into the repository, used as interoperability references, or queried at runtime.

## EDDiscovery / EliteDangerousCore

Fallback Russian names for Elite Dangerous engineering materials in `EngineeringLocalization.cs` are adapted from the Russian translation table in `EDDiscovery/EliteDangerousCore`.

Ship engineer locations/unlock stages and engineering recipe assignments are also derived from reduced Frontier-data tables in EliteDangerousCore.

Universal Cartographics scan and surface-mapping estimates in `ExplorationValueCalculator.cs` are adapted from the post-3.3 calculation in EliteDangerousCore's `EstimatedValues.cs`.

EliteDangerousCore is distributed under the Apache License 2.0:

- https://github.com/EDDiscovery/EliteDangerousCore
- https://github.com/EDDiscovery/EliteDangerousCore/blob/master/LICENSE

The application does not require or load an EDDiscovery installation.

## EDCD / Coriolis

Russian names for Coriolis engineering blueprints, experimental effects, modules and recipe ingredients in `CoriolisRussianLocalization.cs` are adapted from the Coriolis Russian translation data.

- https://github.com/EDCD/coriolis
- MIT License: https://github.com/EDCD/coriolis/blob/master/LICENSE

The engineering catalog also consumes public `EDCD/coriolis-data` data at runtime/cache time.

## Logitech X52 / EDDX52 interoperability reference

The optional X52 Pro integration uses a clean C# dynamic binding based on DirectOutput function signatures, MFD button masks and LED component indices reviewed from EDDX52.

- https://github.com/Theaninova/EDDX52
- Apache License 2.0

The implementation in this repository was rewritten for the application's own lifetime/state model. EDDX52 binaries are not loaded.

Logitech's proprietary `DirectOutput.dll` is not distributed. It is loaded only from the user's installed Logitech/Saitek driver package.

## Canonn Bioforge / Elite Dangerous Warboard

The files under:

```text
EDActivityOverlay/Resources/ExobiologyBioforge
```

are a snapshot/reduced representation of exobiology histogram data distributed by Elite Dangerous Warboard and attributed there to Canonn Bioforge.

Current source repository:

- https://github.com/Mirooz/EliteDangerousWarboard
- https://bioforge.canonn.tech/

Elite Dangerous Warboard is distributed under the MIT License.

Required MIT notice for the distributed source data:

> Copyright (c) 2025 Elite Dangerous Warboard contributors
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

The application's prediction implementation is native C# and treats these histograms as statistical hints. Journal events remain authoritative for commander discoveries and organic sampling progress.

## EDAstro and Canonn runtime data

Nearby exploration POIs may be requested at runtime from:

- EDAstro Galactic Exploration Catalog API: https://edastro.com/gec/APIinfo
- Canonn public Guardian/Thargoid datasets used by the Canonn EDMC plugin: https://github.com/canonn-science/EDMC-Canonn

Responses are cached locally and remain identified by provider.

## Spansh

Exploration routes can be imported from files explicitly exported by Spansh.

The application can also use the public form/job protocol for Road to Riches calculations. That protocol is treated as less stable than explicit file import because it is not part of the documented system-data OpenAPI surface.

- https://spansh.co.uk/
- interoperability reference reviewed: https://github.com/wuuthradd/EDMC-SpanshTools

No third-party plugin code, credentials or browser cookies are loaded.

## Frontier Developments

Journal event semantics and fields are based on Frontier Developments' Elite Dangerous Player Journal documentation.

Elite Dangerous game data, names and trademarks remain the property of their respective owners. This project is an independent community tool.