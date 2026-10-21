# Performance review and implemented changes

The [21 October 2026 code review](code-review-2026-10-21.md) records the subsequent
distance-map projection optimization, recovery/display fixes and 319-test
verification. The measurements below describe the earlier optimization pass.

Measured in Release on Windows, .NET 10.0.13, with 32 logical processors. The reviewed paths cover scientific loading/finishing, persistent preview reads, sky projection/drawing, astrometry sampling and bulk scene updates. Changes are in SkyBrowser; the XISFLib.Net display renderer, owned-image materialization and adaptive display-function paths were inspected, and no library edits were needed for these gains. Existing changes in that checkout were preserved.

## Results

| Workload | Before | After | Interpretation |
| --- | ---: | ---: | --- |
| Sky drawing CPU, 40 atlas frames | 164.78 ms | 63.48 ms | 61% less time |
| Insert a batch of 100 sky layers | 876.79 ms | 71.09 ms | 92% less time |
| Managed allocations for that layer batch | 49.34 MB | 14.21 MB | 71% fewer allocated bytes |
| Atlas projection, 100 frames of catalogue stars | 12.08 ms | 7.42 ms | 39% less time |
| Observer projection, same workload | 15.73 ms | 7.44 ms | 53% less time |
| Recapture native 33 × 33 astrometry grid, 100 times | 16.94 ms | 0.42 ms | Reuses immutable evaluated coordinates |
| Horsehead FITS, Automatic processing | 295.90 ms | 166.66 ms | 44% less time |
| Horsehead FITS, Dark sky + cleanup | 1,130.46 ms | 1,074.71 ms | Modest gain; native cosmetics dominate |
| Persistent preview hit, same image | 37.57 ms | 41.34 ms | No measured improvement; compression/I/O path was retained |

These are CPU/processing measurements, not physical screen-presentation or cold-disk guarantees. The image is `horsehead_164x2m_hyperstar_unfiltered_poseidon-cpro.fits` (6,365 × 4,266 pixels, approximately 326 MB). Core measurements and drawing use one warm-up followed by five measured iterations, reporting the median. Layer insertion is a single 100-layer batch with small synthetic textures; it excludes image decoding and corresponds to the batching now used for session replacement and multi-image documents. A folder containing separate files still displays images progressively.

Both Automatic and Finished preview SHA-256 hashes match their pre-optimization baselines exactly. Projection checksums and native-grid coordinate checksums also match. Managed allocation counters exclude native image/filter/GPU memory. Drawing allocation changed little, from 21.68 to 21.26 MB for 40 frames, despite the CPU improvement.

Raw reports: [core baseline](../artifacts/performance-before.json), [core final](../artifacts/performance-final.json), [drawing/batch baseline](../artifacts/performance-ui-before.json), [drawing/batch final](../artifacts/performance-ui-after.json). Reports are generated under ignored `artifacts/` and are local verification evidence.

## Findings addressed

1. **Repeated camera work in every projection.** `SkyCamera.Project` recalculated viewing vectors, observer up vectors, cross products, normalization and the field-of-view tangent for each point. The camera now retains its basis until the center or observer changes, and updates the tangent with zoom. Position, zoom, observer replacement and returning to atlas view are covered by regressions.

2. **Repeated static geometry and text work during panning.** `SkyViewport.SkyDrawing` rebuilt constellation arc samples, converted star coordinates and formatted label text on each draw. Star directions and sampled constellation arcs now survive redraws. Label layouts are cached by text, style and DPI with a 1,024-entry bound per drawing surface; their brushes are frozen. The WPF perspective camera is reused. Grid sampling density, target visibility and drawing rules are preserved.

3. **Repeated astrometry evaluation.** Validation, persistent-cache storage, mesh construction, spherical footprints and image outlines reevaluated the same solution points. Each immutable `ImagePlacement` now retains its evaluated 33 × 33 grid, while `CaptureSampleGrid` still returns a defensive copy. Meshes and footprint validation reuse it; outline drawing reuses existing mesh positions. Arbitrary `At(u,v)` calls retain their original exact mapping behavior. The retained grid is small geometry data, not an image-sized sample cache.

4. **Full scene reconstruction during each bulk insertion/removal.** Session restoration and multi-image documents now use nested, disposable UI-thread update scopes and rebuild once at the end of each batch. Folder replacement clears the old collection in one operation. Reset notifications detach layer subscriptions, so later changes to cleared layers no longer rebuild an unrelated scene. Mesh UV coordinates and triangle topology are constructed once and shared as frozen WPF collections; per-image positions are assembled before attaching them to the mesh.

5. **Sequential independent preview rows and repeated parameter lookups.** Full-frame box averaging and stretch application now use up to eight workers for sufficiently large previews, with a sequential path for small images. Each worker writes separate rows and preserves the original per-pixel accumulation order. Immutable display-function parameters are captured once per channel. Progress is serialized and monotonic within each row-processing step, cancellation remains checked per row, and parallel exceptions retain their original exception type. Statistics, stretch formulae, clipping, alpha, orientation and cosmetic settings are unchanged.

## Verification

- Release solution build succeeded with zero warnings/errors.
- All **107 tests passed**, including the supplied FITS/XISF examples, real WPF rendering, observer navigation, blending/hit testing, sessions, persistent cache recovery and progress/cancellation.
- New regressions cover camera invalidation, defensive/concurrent astrometry-grid access, nested scene batching, detached reset subscriptions and immutable shared mesh topology.
- A separate process with `DOTNET_PROCESSOR_COUNT=1` verifies pixel-for-pixel equality with the parallel implementation for both display modes on a large RGBA, high-pedestal fixture. A separate check cancels during row processing.

## Remaining costs and limits

The retained SQLite/Brotli read path still takes roughly 38–41 ms for this preview in these runs; no cache-hit speedup is claimed. Original sample decoding still materializes the scientific image, and managed allocations for the Horsehead processing remain about 388–397 MB. Full cleanup remains dominated by the native filters. Those operations were retained to preserve validation, numerical behavior and image quality.

Loaded previews and WPF textures continue to scale with the number and resolution of loaded images. The batching benchmark establishes scene CPU/allocation improvements, not a memory limit for arbitrarily large collections. Texture residency, codec streaming and cache decompression/LRU I/O would need separate representative workloads before changing those contracts. Checksum/signature verification, cache eviction, sample precision, image resolution and source preservation were not relaxed.

## Reproduce

```powershell
dotnet build SkyBrowser.slnx -c Release --artifacts-path .\artifacts
dotnet artifacts\bin\SkyBrowser.CacheProbe\release\SkyBrowser.CacheProbe.dll --performance examples\horsehead_164x2m_hyperstar_unfiltered_poseidon-cpro.fits artifacts\performance-local.json
$env:SKYBROWSER_PERFORMANCE_REPORT = 'D:\Code\SkyBrowser\artifacts\performance-ui-local.json'
dotnet test tests\SkyBrowser.Tests -c Release --artifacts-path .\artifacts --filter FullyQualifiedName~DesktopTests
```

The UI probe invokes the drawing code directly on the UI thread and measures scene insertion with synthetic layers. It is opt-in and does not run in normal application startup. Use a different report name when comparing versions to retain each run's reports and preview-cache directory. Filesystem caches and other system activity affect timings.
