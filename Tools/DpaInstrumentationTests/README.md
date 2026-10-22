# DPA instrumentation checks

Set `RIMWORLD_DIR` to the game directory and `HARMONY_PATH` to `0Harmony.dll`, then run `dotnet run --project Tools/DpaInstrumentationTests -c Release` after updating DPA's source. The default source location is `Mods/Dubs Performance Analyzer/Source/Profiling` under that game directory; override it with `-p:DpaRoot=<path-to-Source/Profiling>`. A flat staging directory can instead be supplied with `-p:DpaSource=<path>`. Override the Harmony reference with `-p:HarmonyPath=<path-to-0Harmony.dll>`.

The harness tests real Harmony prefix/finalizer behavior, constructor and private-method call wrappers, return values, exceptions, nesting, main-thread guards, reset, bounded spikes, warm allocation overhead, clipboard filtering and diagnostic pause/Cleanup. It links the runtime's numeric diagnostic bridge and uses small game/UI stubs; it does not create a GPU device.

For actual runtime metadata and patch-compilation checks, first build RimUnity, set `DPA_METADATA_ONLY=1`, then run the same command. This mode loads the repository's built runtime, verifies original IL call sites and installs/removes the three Submit/TryAppend/Flush transpilers without executing game rendering. Set `RIMUNITY_DIR` to override the runtime repository location. Run metadata checks in a separate process from stub tests.

Game acceptance is separate: compare the same warmed scene for three 60-second runs before/after, export DPA's detailed report, and check the `RIMUNITY DETAIL v3`, `HDR BINDING REUSE`, `SLOWEST PARTICLE SUBMITS` and stage coverage sections. `MISSING`/`FAILED` coverage is not zero cost. Compare Cleanup frame times and inspect HDR/Bloom/refraction/previews visually; CPU submission timing does not measure GPU execution.


Detail v4 adds completed-frame root totals and a bounded 8192-frame quantile ring.
Tests inject known frame values to verify mean, max, allocation totals, rollover,
nearest-rank quantiles, first/invalid/current exclusions, nested root accounting,
and runtime reset with late submit rejection. Quantiles are retained-window values,
not whole-session quantiles; mean/max use the valid completed session.
The installed DPA source contains the matching RimUnityTiming.cs and ClipboardReport.cs
changes. Back up those files together with PerformanceAnalyzer.dll before replacing them.
HDR source stage tests live in Tools/HdrSnapshotTests and include failure cleanup,
per-role counts, reset generation rejection and zero additional warmed allocation.
