using System;
using System.IO;
using System.Text;

namespace RimUnity.Diagnostics;

internal sealed partial class LabSampler
{
    private const string PopulationCaveats = "Global active systems versus descendants of the lab's live roots, including dynamic children absent from the initial Systems list. Main-camera candidates match layer/Canvas/renderer eligibility, including empty systems; they are not measured DrawMesh calls. CPU Renderer scopes also include UI and offscreen calls. Background and owned-empty identities are captured only at start/end, each capped at 128 per boundary; transient objects may be absent. Frame scans allocate no steady garbage and are charged to Observer. No background objects are disabled or deleted.";
    private string PopulationText()
    {
        var text = new StringBuilder("全局粒子来源（采样结束边界）\n");
        var c = endPopulation.Counts;
        text.AppendLine($"活动系统：全局 {c.ActiveSystems}；测试场 {c.OwnedSystems}；背景 {c.BackgroundSystems}；全局粒子 {c.ActiveParticles}；背景粒子 {c.BackgroundParticles}");
        text.AppendLine($"启用 Renderer 的系统 {c.RendererSystems}；背景 Renderer 系统 {c.BackgroundRendererSystems}；其中背景空系统 {c.BackgroundEmptyRendererSystems}");
        text.AppendLine($"主相机候选系统 {c.MainCameraCandidates}（测试场 {c.OwnedMainCameraCandidates}，背景 {c.BackgroundMainCameraCandidates}）；此数包含空系统，不等于实际提交次数。");
        foreach (var item in new[] { ("开始", startPopulation), ("结束", endPopulation) })
        {
            text.AppendLine($"{item.Item1}背景清单：全局活动系统 {item.Item2.Counts.ActiveSystems}；活动相机 {item.Item2.ActiveCameras}；离屏相机 {item.Item2.OffscreenCameras}；截断 {item.Item2.Truncated}");
            foreach (var entry in item.Item2.Background)
                text.AppendLine($"id={entry.Id} | name={entry.Name} | root={entry.Root} | path={entry.Hierarchy} | particles={entry.Particles} | renderer={entry.RendererEnabled} | mainCandidate={entry.MainCameraCandidate} | canvas={entry.HasCanvas} | layer={entry.Layer}");
            text.AppendLine($"{item.Item1}测试场空 Renderer 系统清单：{item.Item2.OwnedEmptyRenderers.Length} 条；截断 {item.Item2.OwnedEmptyTruncated}");
            foreach (var entry in item.Item2.OwnedEmptyRenderers)
                text.AppendLine($"id={entry.Id} | name={entry.Name} | root={entry.Root} | path={entry.Hierarchy} | mainCandidate={entry.MainCameraCandidate} | canvas={entry.HasCanvas} | layer={entry.Layer}");
        }
        return text.AppendLine(PopulationCaveats).ToString();
    }
    private void WritePopulationFrames(string prefix, Encoding encoding)
    {
        using var csv = new StreamWriter(prefix + ".population.csv", false, encoding);
        csv.WriteLine("frame,active_systems,active_particles,owned_systems,owned_particles,background_systems,background_particles,renderer_systems,main_camera_candidates,owned_main_camera_candidates,background_main_camera_candidates,background_renderer_systems,empty_renderer_systems,background_empty_renderer_systems");
        for (int i = 0; i < count; i++)
        {
            var c = samples[i].ParticlePopulation;
            csv.WriteLine(FormattableString.Invariant($"{i + 1},{c.ActiveSystems},{c.ActiveParticles},{c.OwnedSystems},{c.OwnedParticles},{c.BackgroundSystems},{c.BackgroundParticles},{c.RendererSystems},{c.MainCameraCandidates},{c.OwnedMainCameraCandidates},{c.BackgroundMainCameraCandidates},{c.BackgroundRendererSystems},{c.EmptyRendererSystems},{c.BackgroundEmptyRendererSystems}"));
        }
    }
}
