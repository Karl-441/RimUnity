# HDR snapshot checks

This console harness links the production HDR snapshot code and uses the installed host's real `MeshDrawFrame`, `FinalDrawItem`, `MaterialBinding`, shader-variant identity registry and draw pools. It does not start RimWorld or create a graphics device. A small deterministic shader-variant shim replaces shader compilation, and the map pass ID is fixed for the fixture.

`BaselineHdrDrawFrame.cs` freezes the pre-optimization implementation for differential behavior and allocation measurements. Keep it unchanged when updating the production snapshot implementation.

Run against the same host/Harmony used by the game, supplying your local paths:

```powershell
dotnet run --project Tools/HdrSnapshotTests/HdrSnapshotTests.csproj -c Release `
  -p:RimWorldDir=D:/SteamLibrary/steamapps/common/RimWorld `
  -p:HarmonyPath=D:/SteamLibrary/steamapps/workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll `
  -- D:/SteamLibrary/steamapps/common/RimWorld
```

Coverage:

- Original-versus-optimized order, filtering, shader/variant identity, depth/stencil, material parameters, resource/storage data, metadata and queue-sliced linear flags.
- Empty scenes; normal and read-only-depth captures; mixed linear/display/refraction materials; selected and empty layer masks.
- Independent map/Bloom/preview leases, source mutation while old snapshots remain in flight, binding-buffer refresh and immutable identity replacement.
- Early filtering failure and failure after partial cloning; resource-reference clearing; oversize buffer rejection; pool cap; shutdown epochs; concurrent return/rent and duplicate disposal.
- Warm allocation comparison of two captures per synthetic frame, with a required reduction of at least 90%. Timing includes construction and disposal on both paths; it is an offline CPU benchmark, not an FPS/TPS/GPU measurement.
- CPU comparison against frozen `Round2HdrDrawFrame.cs` (the previous deployed identity-based binding implementation), using three alternating matched runs and per-frame P95. Keep both frozen implementations unchanged.
- Snapshot-owned draw buffers, source commands without overrides, independent in-flight commands, pipeline/instanced-cache invalidation, and resource-reference clearing before reuse. Known host layouts use direct field copies and retain original sort keys; unknown layouts fall back to the host's Rent/Return path.

The pool also reuses snapshot-owned `MaterialBinding` buffers. All values are repopulated on capture and all resource references are cleared before pooling; there is no cross-frame material-result cache. The host's readonly ID/material/variant identity must match before a buffer can be reused, and every capture receives a unique derived data generation for host upload invalidation.

Game acceptance remains separate: use the same map, camera, effects and DPA mode for at least 60 seconds after warmup, compare constructor allocations/CPU and GC, then check frame times with DPA Cleanup. Check shadows, transparency, refraction, Bloom masks, preview windows, resize and scene exit visually. Do not infer visual equivalence or in-game speedup solely from this harness.
