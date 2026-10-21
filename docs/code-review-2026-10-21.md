# Code review — 21 October 2026

## Scope and outcome

Reviewed SkyBrowser's current working tree, including the new Moon overlay,
equipment/framing tools, and recent loading, inspector and cache changes. The
review also examined scientific loading/inspection, source signatures, library
solutions, survey restoration, rendering subscriptions, distance-map navigation,
solver execution and export publication. Existing uncommitted work was preserved;
the separate XISFLib.Net checkout and pinned dependency were not changed.

Three reproducible correctness defects were fixed, alongside two focused
performance improvements. The Core/Desktop boundary remains appropriate. A broad
framework or window rewrite would add migration risk without addressing the
problems found here.

## Correctness fixes

1. **One corrupt preference hid later valid preferences.**
   `ProcessedImageCache.GetPreferences` caught deserialization failures around the
   entire query. A damaged first row therefore returned an incomplete preference
   map, silently replacing other images' saved display choices with defaults.
   Validation and recovery now operate per row, while database failures retain
   their existing fallback. A regression corrupts image 0 and verifies that image
   1 still loads with its saved display mode.

2. **Damaged survey metadata prevented offline restoration.**
   `SurveyImageService.GetCached` validated records without isolating failures.
   An unknown survey, invalid declination or overflowing size could abort the
   entire restore. SQLite also silently coerced invalid numeric text to zero.
   Listing now checks numeric storage types, bounded dimensions/payloads, supported
   coordinates/providers and the request's cache key. Invalid records are skipped;
   valid regions retain their recency order and restore without network requests.
   Five regression cases cover these failures. Existing FITS payload validation
   and damaged-payload recovery remain in place.

3. **Display refresh retained obsolete manual geometry.**
   `SkyImageLayer.ReplaceDisplay` retained the previous placement when a source's
   dimensions changed. A manual field could then have the wrong aspect ratio.
   It also raised bitmap notifications before updating an automatic placement,
   exposing an inconsistent replacement to observers. The layer now prepares its
   bitmap and placement before publishing any state. Manual parameters survive,
   with geometry rebuilt when dimensions change. The loading workflow's separate
   manual-placement repair was removed because the layer now owns this invariant.
   The WPF regression checks placement during every replacement notification.

All seven added failure cases failed against the original implementation before
the fixes: one preference case, five survey cases and the shared desktop scenario's
manual-placement assertion.

## Performance

`DistanceSpaceCamera` previously reconstructed trigonometric axes and normalized
cross products repeatedly while projecting each catalogue point. It now retains
the orientation basis until Orbit or Home changes it, following the existing atlas
camera's approach. Framing, movement and zoom retain correct position/scale behavior.
A separate regression checks projection against independently calculated axes after
every camera operation.

| Release workload | Before | Final | Evidence |
| --- | ---: | ---: | --- |
| Distance-map projections, 100 frames | 8.5002 ms | 0.9129 ms | 89% less CPU time; identical checksum |
| Managed allocations for that workload | 96 bytes | 96 bytes | No new per-frame geometry allocation |

The probe uses all 3,283 bundled distance estimates, a changing camera orientation,
one warm-up and five measured iterations, reporting medians. Measurements used
Windows, .NET 10.0.13 and 32 logical processors. This measures projection CPU work,
not complete WPF frames or physical presentation. An initial after-run overlapped
the integration suite; the final run above was repeated without that contention.

Display replacement in individual and batch processing now uses
`SkyViewport.DeferLayerUpdates`, combining bitmap and placement notifications into
one scene reconstruction per replacement. No numeric speedup is claimed for this
change. Existing folder refresh already used a surrounding update scope.

Atlas/observer projection checksums and Automatic/Finished preview pixel hashes
also matched the baseline. No scientific image processing was changed. Timing
variation in those unchanged paths is not evidence of an image-processing gain or
regression. Local reports are under ignored `artifacts/review-performance-*.json`.
Reproduce with the existing probe:

```powershell
dotnet build SkyBrowser.slnx -c Release --artifacts-path ./artifacts
dotnet artifacts/bin/SkyBrowser.CacheProbe/release/SkyBrowser.CacheProbe.dll --performance $env:SKYBROWSER_PERFORMANCE_SOURCE artifacts/review-performance-local.json
```

## Design assessment and remaining costs

- Keep numerical rules, scientific data and persistence in Core; keep WPF input,
  drawing and window orchestration in Desktop. The feature partial files make the
  current windows navigable while preserving one owner for operation identity and
  cancellation. No additional service layer is needed for simple event handlers.
- Keep consistency rules on their owning model. Display replacement is now an
  example: callers no longer need to remember an extra manual-placement repair.
- Keep durable library/equipment data separate from disposable preview/survey
  stores. Recovery should be independent per record when records are independent.
- Full scientific decoding still dominates peak allocations. The measured large
  image allocated about 388–397 MB in managed memory, and native cleanup dominates
  Finished-mode time. Loaded previews and GPU textures also grow with collection
  size. Further work needs dedicated memory/codec profiling before changing these
  contracts; the current review did not add image-sized caches or weaken validation.
- The new framing overlay resamples panel edges while drawing. Its bounded
  100-panel plan is a possible future geometry-cache workload if profiling shows
  it affects navigation. No unmeasured optimization was introduced there.

## Verification and limits

- Debug and Release solution builds: zero warnings and errors.
- Complete Release suite: **319 passed, zero failed/skipped**, including real WPF
  interactions and cancellation, cache persistence/corruption, scientific
  coordinates, source preservation, framing and Moon scenarios.
- Optional real-image checks used two large solved FITS fields and the two
  existing external FITS/XISF import fixtures. The initial optional fixture path
  pointed at Git LFS stubs; the final run used the available full files instead.
- `git diff --check` passed. Review outputs and copied fixtures remain under
  ignored `artifacts/`; no source photographs or user settings were modified.

This is a local Windows review, not certification of every external ASTAP version,
astronomical file variant, driver, DPI configuration or arbitrarily large library.
No live survey downloads, release publication or manual/screenshot regeneration
were needed for these fixes.
