# Lab CPU diagnostics checks

Run `dotnet run --project Tools/LabCpuMetricsTests -c Release` (.NET 10, no game).

Links the production collector, CPU report, sampler/exporter and particle batch
implementation. Uses fake frame events, mesh, material eligibility and lab context.
Checks nesting/self allocation conservation, exceptional scope closure, worker
rejection, late scopes after shutdown, bounded storage, report schema/CSV alignment,
disabled baseline, scene exit, batch order and the first decisive batch rejection
or flush boundary. Also covers complete timeline windows, missing-boundary omission,
empty-scene sampling, before/after process memory snapshots, and forces the periodic
GC/UI update 60 times to report Observer cost without waiting a minute.
It does not validate actual Shader eligibility, GPU work, game
UI layout or in-game performance.

The microbenchmark checks zero managed allocation after warmup and prints active
scope/counter and inactive scope costs. These numbers do not replace an in-game
comparison with CPU stages disabled; no timing threshold is imposed on shared CI.
