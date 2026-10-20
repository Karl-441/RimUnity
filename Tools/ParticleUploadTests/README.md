# Particle upload regression and host benchmark

Build the current runtime, then run (.NET 10, Windows, installed 1.7 host):

```powershell
dotnet run --project Tools/ParticleUploadTests -c Release "-p:RimWorldDir=$env:RIMWORLD_DIR" -- $env:RIMWORLD_DIR
```

Creates a hidden 64x64 graphics window and a real Vulkan device. No game, mods or
save is loaded. The production Core mesh check compares vertex bytes, triangle
order/offsets and per-range bounds between independent meshes and a shared mesh,
checks the legacy overload and Clear, and requires zero allocation for 1000 warmed
CPU rebuilds. Mesh upload and final geometry resolution use the installed host.

The benchmark alternates three runs of 60 separate dynamic meshes and one shared
dynamic mesh containing 60 ordered submeshes: both have 800 quads, the same layout,
and 60 draw ranges. After 30 warm frames it measures 100 frames. CPU storage setup,
GPU resource retirement and frame setup are outside the upload/resolve timings.
This does not time GPU execution or render pixels, nor prove all in-game overhead
belongs to these methods. Game visual checks must still cover transparent order,
HDR/Bloom layers, bounds, multipass/property-block fallback and offscreen cameras.

It also compares production Core append/CPU packing for both layouts, requiring
zero managed allocation after warmup. For stable comparisons set
`$env:DOTNET_TieredCompilation='0'` in the benchmark shell only; default tiered JIT
can transition during these short runs. This does not change game configuration.
