# 0.1.11 Stable · Release notes

2026-10-16, for RimWorld 1.7.4980 rev60 / .NET 10.

- Export and support particle Renderer `minParticleSize` / `maxParticleSize`. A zero maximum hides particle billboards while preserving simulation and trails, fixing white dots in effects that use this setting. Mesh particles are unaffected.
- Correct particle trail facing to prevent disappearance through backface culling.
- Generate rectangular rendering geometry for runtime-created FullRect sprites. Exported Tight sprites retain their original geometry and report missing data explicitly.

Re-export existing effects with the 0.1.11 exporter to include particle size settings, and update the selected standalone or embedded runtime. Enabled standalone still takes precedence. Assembly identities remain 1.0.0.0 and the embedded protocol remains 1.

Downloads: standalone `RimUnity-0.1.11.zip`, embedded runtime `RimUnity.Embedded-0.1.11.zip`, Unity exporter `com.chezhou.rimunity-0.1.11.tgz`, and `SHA256SUMS.txt`.

Validation: runtime and exporter compilation and static asset checks; no in-game visual acceptance test. Existing warnings remain: 3 CA2022 warnings in Spine and 5 CS0618 warnings for legacy particle enums in the exporter.
