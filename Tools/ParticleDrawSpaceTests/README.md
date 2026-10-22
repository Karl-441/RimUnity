# Per-draw coordinate snapshot checks

Build the current runtime, then run `Tools/Test-ParticleDrawSpace.ps1` with
`-GameRoot` and `-HarmonyPath`. Requires .NET 10 and the installed host assemblies.
The script extracts the frozen pre-optimization DLL from commit
`a5abd1ee84d32a88981fade591217c245a0a7ea8` without converting binary output to text.

Old and new actual runtime DLLs load in separate assembly contexts sharing the
same real Core math/Transform implementation. Geometry comparisons cover 675
combinations of Local/World/Custom simulation, all scaling modes, null/present
custom transforms, nested rotated/nonuniform/negative/zero scale, per-particle
event spaces, all five render modes and alignments, two cameras plus no camera,
roll/3D rotation, flip/pivot, size clamp and explicit/default vertex streams.
Vertices, indices, normals, tangents, colors, UV and custom outputs must be equal.
The fixture invokes geometry building without material submission or GPU work;
it does not test pixel appearance, scripts, collisions or full rendering.

Benchmarks compare the unchanged per-particle coordinate helpers with the actual
new snapshot helpers, requiring zero warmed managed allocation. A second kernel
includes snapshot setup for 100 systems of 40 particles. For steady comparisons,
disable tiered JIT only in the benchmark process (`DOTNET_TieredCompilation=0`).
These are coordinate-kernel timings, not whole-renderer, game, FPS or GPU gains.
Transform mutation after a call and a fresh next-call snapshot are also checked.

The same actual-runtime fixture checks schema 8 population scanning against live
root ownership and a separate background object, requiring zero warmed allocation
and cleared temporary root references. Fake-host tests separately cover the broader
classification and export matrix. These tests do not identify objects in the user's
running game; the next report's boundary inventories are required for that.


Optional `-VertexBaseline <path>` compares a separately frozen DLL built immediately
before the change being measured (it must contain DrawSpace). Preserve its SHA256 with benchmark results; do not regenerate it from the
optimized sources. This adds every supported individual stream, mixed/duplicate
layouts, reordered layouts, default and zero-width layouts, nonzero UV3/UV4,
animated UV2, changing particle age/seed/angular velocity, rejected over-capacity
layouts, and ClearMesh followed by the unprepared Vertex path used by trails.
All nine output channels are compared exactly against that DLL.

The optional CPU geometry benchmark builds 4000 particles in 300 renderer chunks
(100 x 14 + 200 x 13), alternating old/new order for three rounds after warmup.
It includes ClearMesh, camera state and DrawSpace setup, AppendParticle and stream
packing; it requires zero warmed allocation. Billboard and three-vertex Mesh use
default, UV-only, transformed-value and random-value layouts. No Submit, DrawMesh,
GPU, real scene composition or full-game timing is covered. The baseline retained
for this round is `Artifacts/verify/vertex-reuse-baseline.dll`, SHA256
`ED65547E55D5601450C2F9C824BE484807021D0D30A54FAD5CD0D20B3603EF66`.


The UV-only fast-path round uses `Artifacts/verify/simple-stream-baseline.dll`,
SHA256 `DDE48B379D219EED967B92E7FAC75F48BF006146CAD54FD2ADC5CEE0DB008899`.
It is the deployed runtime from the 073824/074018 reports. The added cold-fire
layout is Position/Normal/Color/UV, selected by stream semantics, not asset name.
The suite also compares reordered zero-width attributes, duplicate UV (general
path), layout replacement, explicit zero channels versus default custom passthrough,
and the unprepared trail vertex path. Cold-fire layout benchmarks still use the
fixture's generic module configuration, not the full cold-fire asset or game.


`--mixed-stress` (fourth runtime argument, instead of a baseline path) checks all
12 production LabStressScenario.Configure recipes using the actual runtime.
It verifies explicit layouts/modules, clone-local Custom space, Birth graph,
repeatable seeds, sustained bounded manual simulation and actual child emission.
It supplies CPU fixture mesh/material inputs and does not validate shader creation,
GPU appearance, window geometry or real map/camera transitions.


`--trail-reuse <旧版RimUnity.dll>`：对照拖尾端点复用前后的真实运行时，覆盖 1620 种模式/空间/点数组合，逐项比较 9 个网格通道。随后用编译委托测量 256 条、每条 17 点的拖尾几何生成；预热后检查零分配。该测试不访问 GPU，也不代表整个游戏帧耗时。


`--noise-reuse <优化前RimUnity.dll>`：逐位对照 Curl 噪声（各质量/种子/边界/六层累积），运行 72 种连续模拟及尺寸噪声对照，再测量 Curl 和四个噪声系统的显式推进。Curl 预热后零分配；完整显式推进允许 API 原有分配，但新旧必须一致。无 GPU 和整帧性能承诺。


`--sort-reuse <优化前RimUnity.dll>`：比较空间/年龄排序的原始索引顺序、成功/异常清理，以及复用世界速度前后的所有网格输出通道和可见性轴。覆盖 675 种几何/可见性场景、810 种排序配置，各配置连续三次移动相机/变换。微基准计入坐标快照、索引填充和排序，预热后零分配；不测 GPU。
