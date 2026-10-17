# 0.1.14 release notes

2026-10-18. For RimWorld 1.7 / .NET 10; author assets with Unity 2022.3.

- Improve Generic Avatar animation bindings and support renderer-based Legacy Animation culling. Reduce repeated bone uploads and Transform matrix calculations.
- Adapt custom material vertex programs, multiple passes and shadows for skinned meshes. Correct bone matrix layout and apply submesh rules to additional materials.
- Use continuous 3D curl noise for particles, reuse camera projection and visibility calculations, and avoid unnecessary collision preparation. Correct trail facing and GrabPass texture bindings.
- Reduce repeated UGUI projection, masking, clipping and hierarchy work. Improve camera draw ordering and allocation of R8/R32 single-channel render textures.
- Export standalone shaders with multiple passes and defaults. Improve ForwardBase lighting, shadows and normal matrix conversion. Preserve original compressed blocks and mip chains for eligible readable DXT1/DXT5 textures.
- Load the standalone runtime early for ordinary C# mod compatibility and identify its provider by assembly ownership. Without standalone, select the highest compatible embedded runtime satisfying content requirements.
- Improve build and CI entry points, pin the .NET 10 SDK, and improve mod backup recovery during game verification. Fix dot-sourcing overwriting the game path and erroneous cleanup failures when no mod was previously installed.

Update the exporter and the runtime together. Re-export resources that need the animation binding, shader or texture export improvements. Both integration modes require Harmony. Enabled standalone takes priority. C# content mods using the embedded package must publish deferred scripts through its `.targets`; `References` are compile-only and must not be copied into mod assembly directories. Assembly identity remains `1.0.0.0`; embedded protocol remains `1`.

Downloads:

- `RimUnity-0.1.14.zip`: standalone mod; extract into the game's `Mods` directory.
- `RimUnity.Embedded-0.1.14.zip`: embedded runtime, compile references and integration files for content mods.
- `com.chezhou.rimunity-0.1.14.tgz`: Unity exporter; install through Package Manager.
- `SHA256SUMS.txt`: SHA-256 checksums for all three packages.
