# Trail and refraction regression checks

Build the runtime first, then run this console harness against that build and
your installed RimWorld 1.7 assemblies. It exercises the actual runtime code
through reflection without starting the game or allocating GPU resources.

```powershell
dotnet run --project Tools/TrailGeometryTests -- "<runtime-assemblies>" "<RimWorld-1.7-directory>" "<0Harmony.dll>"
```

Alternatively, set `RIMWORLD_DIR` and `HARMONY_PATH`. The runtime assemblies
default to this repository's `1.7/Assemblies` folder.

Checks cover strip/corner/cap winding for straight and left/right paths,
refraction exclusion from Bloom while retaining the scene pass, layer masks,
and canonical scene-grab texture resolution through material creation, clones,
property copies, Shader changes and additional-pass synchronization.
Particle trail checks also cover lifetime as a fraction of particle starting
lifetime, separate aging of existing/new samples, and expired-point removal.

The harness checks CPU geometry and resource bindings. GPU appearance still
requires an in-game check.
