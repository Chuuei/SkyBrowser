# Camera framing equipment presets

The bundled snapshot contains 29 configurations, checked against manufacturer
documentation on 2026-10-20. They describe native fields rather than a vendor's
expanded mosaic mode. Historical models are included where documented; announced
configurations are identified in their notes. This is an offline planning snapshot,
not a compatibility list for controlling hardware.

| Manufacturer | Included configurations | Manufacturer references |
| --- | --- | --- |
| ZWO | Seestar S50; S30, S30 Pro and S50 Pro, each with separate telephoto/wide-angle configurations (7) | [S50](https://www.seestar.com/products/seestar-s50), [S30](https://us.seestar.com/products/seestar-s30-all-in-one-smart-telescope), [S30 Pro manual](https://i.seestar.com/owe__prod/static/manuals/Seestar%20S30%20Pro%20Manual.pdf), [Pro comparison](https://us.seestar.com/blogs/seestar-guide/seestar-s30-pro-vs-s50-pro-which-pro-model-is-right-for-you) |
| DWARFLAB | DWARF II, DWARF 3 and DWARF mini, each with separate telephoto/wide-angle configurations (6) | [DWARF 3 / II comparison](https://www.dwarflab.com/ca/products/dwarf-3-smart-telescope), [DWARF mini](https://www.dwarflab.com/us/pages/dwarf-mini-smart-telescope) |
| Vaonis | Vespera Classic, Passengers, II, II X Edition, Pro, 3, Pro 2; Stellina; Atlas 80 mm module; announced Hyperia 150 mm / 45 MP (10) | [Classic](https://support.vaonis.com/portal/en/kb/articles/how-does-mosaic-mode-work), [Passengers](https://vaonis.com/pages/vespera-passengers), [II / Pro comparison](https://vaonis.com/pages/product/vespera-ii), [X Edition](https://vaonis.com/pages/product/vespera-x-edition), [new generation](https://vaonis.com/pages/vespera-new-generation), [Stellina](https://support.vaonis.com/portal/en/kb/articles/que-peut-on-photographier-avec-stellina), [Atlas](https://vaonis.com/pages/product/atlas), [Hyperia](https://vaonis.com/pages/hyperia) |
| Unistellar | ODYSSEY, ODYSSEY PRO, EQUINOX 2 and EVSCOPE 2 (4) | [manufacturer comparison PDF](https://www.unistellar.com/wp-content/uploads/2024/01/UNISTELLAR-Smart-Telescopes-Comparison-Table.pdf) |
| Celestron | Origin with original IMX178, and Origin Mark II / upgraded Origin with 678C (2) | [original](https://www.celestron.com/products/celestron-origin-intelligent-home-observatory-12099-old-version), [Mark II](https://www.celestron.com/products/celestron-origin-intelligent-home-observatory) |

Each entry in [`smart-telescopes.json`](../src/SkyBrowser.Core/Data/smart-telescopes.json)
retains its source URL, date and approximation notes, shown in the equipment
chooser. Direct width/height specifications are used as published. Arcminutes are
converted to degrees. Diagonal specifications use the sensor/output aspect ratio
in tangent space: `tan(diagonal/2)` is divided between the two sensor dimensions,
then each angle is recovered with `2*atan`. Treating a diagonal as a width would
produce an incorrectly sized rectangle. Portrait defaults are retained for Seestar
telephoto fields and Pro wide cameras; rotate as needed for the intended exposure.

Manufacturer figures are rounded. Optical distortion, digital zoom, cropping,
stacking and field rotation can change usable coverage. Wide-angle fields in
particular are approximate rectilinear footprints. Stellina sources round its
width differently; the preset uses the manufacturer's approximate 1.0° by 0.7°
native-field description. Atlas uses the same single-module field for single/twin
setups. Hyperia describes the announced 150 mm / 45 MP version, rather than older
hardware. Hestia/phone combinations need a custom measured field because their
camera geometry varies. A preset does not imply telescope support for arbitrary
rotations, grid sizes, CSV imports or automatic execution of a plan.

Custom fields can be entered directly or calculated from sensor size and effective
focal length, including any reducer/Barlow. Custom profiles are stored in
`equipment-profiles.json` beside settings, with atomic saves and backup recovery.
Saving the same custom name replaces that profile; factory profiles remain fixed.

Mosaic centers form a grid in the central J2000 tangent plane. Panel axes are
parallel-transported to each center, and CSV records each actual panel rotation,
which can differ from the central rotation near a pole or across a wide field.
Requested overlap defines grid spacing in that central plane; actual sky overlap
varies at the boundaries. A maximum corner distance of 55° from the plan center
bounds plans to a 110° span. Verify usable overlap in the telescope app.
