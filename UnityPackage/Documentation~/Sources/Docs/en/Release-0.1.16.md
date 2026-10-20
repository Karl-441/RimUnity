# 0.1.16 Release Notes

October 21, 2026. Updated for the Waylith engine in RimWorld 1.7.4984 rev1242.

- Fixed a crash when entering a map after the game update changed the post-processing API.
- Fixed a null reference error when releasing screen-effect texture bindings.
- Optimized particle coordinate transforms and vertex data generation to reduce repeated calculations.
- Expanded effect performance diagnostics to distinguish lab particles from background particles and identify their rendering sources.

Update the game before updating RimUnity. Content mods using the embedded runtime must also replace their embedded package; C# content mods should republish through the included `.targets`. Both standalone and embedded runtimes require Harmony. The 1.6 compatibility placeholder remains available without runtime functionality.

Downloads:

- `RimUnity-0.1.16.zip`: standalone mod; extract into the game's `Mods` directory.
- `RimUnity.Embedded-0.1.16.zip`: embedded runtime, compile-time references and integration files for content mods.
- `com.chezhou.rimunity-0.1.16.tgz`: Unity exporter; install through Package Manager.
- `SHA256SUMS.txt`: SHA-256 checksums for the release packages.
