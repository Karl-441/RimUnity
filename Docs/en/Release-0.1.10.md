# 0.1.10 Stable · Changes and validation scope

2026-10-16, targeting RimWorld 1.7.4980 rev60 and .NET 10. This release retains the embedded runtime and Inspector-style settings from 0.1.9 Preview and adds rendering-warmup progress during map loading.

## Rendering warmup

- The native loading background remains visible, with a Chinese or English warmup message and processed / total task counts.
- The native black fade overlay does not cover the prompt while warmup is waiting. Other phases keep the original behavior.
- Map pipelines run in batches of at most 16 tasks or about 8 ms. The prompt is presented before plan construction. Screen effects use the existing warmup entry point on a separate frame.
- A timeout cancels remaining work at a batch boundary. Render-target rebuilding resets the warmup plan; completion or failure releases the request.

A single graphics-driver call cannot be interrupted. Plan construction and screen-effect warmup are also outside the per-task time budget. This change makes loading status visible; it does not guarantee a frame-time limit or a shorter total load. See [resource loading](ResourceLoading.md).

## Distribution and compatibility

The standalone mod, embedded runtime and Unity exporter all use version 0.1.10, with SHA-256 checksums. Assembly identities remain 1.0.0.0 and the embedded protocol remains 1. The targets file keeps its default minimum API version of 0.1.9.

An enabled standalone dependency always wins; update the selected runtime to receive this warmup change. Without standalone, the newest compatible embedded package is selected and identical copies still run once. References are compilation-only and must not be copied as extra runtime DLLs. The framework bundles no real Unity engine, game assemblies or Harmony and performs no online update checks or downloads.

## Checks for this release

- Core, runtime, Spine, Embedded, ShaderTool and Unity exporter source compiled successfully.
- Warmup batch, cancellation and timeout verification source compiled; these runtime tests were not executed for this release.
- Static review covered progress reads, render-thread calls, the native loading-window and fade patches, and bilingual text.
- Package versions, assembly identities, embedded resources and licenses, compilation references, the offline handbook and SHA-256 were checked.
- Three existing third-party Spine CA2022 warnings and five deprecated exporter particle-enum warnings remain.

Neither RimWorld nor Unity was launched for this release. The new warmup prompt has not been visually verified in-game. Long-running map rendering and all UI scales have not received a full regression pass.

## Earlier validation

Version 0.1.9 Preview passed .NET 10 offline selection/loading checks and actual game startup with three test mods, both with and without standalone. Those checks covered runtime and Def singletons, content ownership and derived-script instances. The 5,002-entry registry overhead check was offline. Temporary test files were removed.

Those are previous-version results and were not rerun for this release; they are not in-game verification of 0.1.10. See the [0.1.9 Preview validation record](Release-0.1.9.md).
