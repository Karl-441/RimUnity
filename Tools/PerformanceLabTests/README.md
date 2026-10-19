# Fixed-scene playback checks

Run `dotnet run --project Tools/PerformanceLabTests -c Release`.

The fixture links the production `PerformanceLab.Playback.cs` with fake prefab/lifecycle/sampler APIs. It checks bounded setup, fixed population without expiration/replacement, sampler start after population and warmup, reproducible positions/seeds, event subemitter preservation, source-template isolation, restart cleanup, manual sampling stop, invalid workload termination, construction failure, scene exit, legacy create/destroy playback, and invalid selection.

This does not start the game or render particles. Compile the real runtime separately and verify the debug window, temporary map cleanup, actual prefab cloning, child emission and camera-dependent performance in game. The fixture's prefab factory is a test double; source-template isolation in the actual engine also relies on PrefabLoader reconstructing each clone from serialized prefab data.
