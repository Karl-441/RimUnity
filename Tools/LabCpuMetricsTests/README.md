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
Shared-page checks retain ABA material order, verify index offsets, drain before
incompatible inputs, isolate same-frame cameras, enforce page/range bounds and
discard unfinished pages.
Pool checks additionally cover next-frame reuse, exact vertex/index boundaries,
130-page burst recovery, the 128 idle-tail threshold, 120-frame retirement,
idempotent scene clear, allocation-free diagnostics, and distinct report start/peak/end snapshots.
Population checks use the production scanner to classify global/owned/background
systems, empty renderers, camera layers and Canvas paths. They cover reparenting,
duplicate names, bounded identity lists, ownership cleanup, warmed zero allocation,
schema 8 JSON and the separate 14-column population CSV. Fake scene objects do not
replace the actual runtime scan check in ParticleDrawSpaceTests.
The fake host does not validate actual Core mesh packing; that check lives in
ParticleUploadTests and uses real installed host storage.
It does not validate actual Shader eligibility, GPU work, game
UI layout or in-game performance.

The microbenchmark checks zero managed allocation after warmup and prints active
scope/counter and inactive scope costs. These numbers do not replace an in-game
comparison with CPU stages disabled; no timing threshold is imposed on shared CI.
