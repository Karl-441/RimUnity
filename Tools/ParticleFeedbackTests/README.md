# Full-pool particle feedback regression

Run with runtime directory, game directory, Harmony DLL and `--verify` as arguments.
Pass `-p:HarmonyPath=<Harmony DLL>` when building. Tests use the actual runtime;
no graphics device or game save is needed.

Each system has 1000 particles, 200 births/sec, a five-second lifetime and medium
quality noise. Initial remaining lifetimes are staggered as in a warmed continuous
emitter. Test 1 and 10 systems at 1/60, .05, .235 and 1/3 second frame intervals.
Harmony counts actual Integrate calls. Verification rejects repeated integration
of survivors during capacity turnover and checks steady-state particle counts.
Omit `--verify` to measure a frozen pre-fix runtime using the same executable.

Baseline at ten systems and .235 seconds: 440826 integrations/frame.
After deferred natural-death reclamation: 149975 integrations/frame.
At 1/60 second, including the substep-rounding fix: 36665 -> 10000 calls/frame.
Timing includes headless simulation and counting instrumentation, not map rendering
or player-device FPS. Fixed input frame lengths do not themselves reproduce the
host's whole-frame timing feedback; actual gameplay still requires verification.

Existing ParticleDrawSpaceTests `--particle-turnover` and
`--particle-physics-timeline` cover observer-sensitive fallback behavior.
