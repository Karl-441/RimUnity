using System;
using System.Collections.Generic;
using System.Text;

namespace RimUnity.Diagnostics;

// Numeric observations only. Scratch ownership roots are cleared even on failure.
internal static class LabPopulation
{
    internal readonly record struct State(int ActiveSystems, long ActiveParticles, int OwnedSystems, long OwnedParticles,
        int RendererSystems, int MainCameraCandidates, int OwnedMainCameraCandidates,
        int BackgroundRendererSystems, int EmptyRendererSystems, int BackgroundEmptyRendererSystems)
    {
        public int BackgroundSystems => ActiveSystems - OwnedSystems;
        public long BackgroundParticles => ActiveParticles - OwnedParticles;
        public int BackgroundMainCameraCandidates => MainCameraCandidates - OwnedMainCameraCandidates;
    }
    internal sealed record Entry(int Id, string Name, string Root, string Hierarchy, int Particles,
        bool RendererEnabled, bool MainCameraCandidate, bool HasCanvas, int Layer);
    internal sealed record Boundary(State Counts, Entry[] Background, bool Truncated, int ActiveCameras, int OffscreenCameras,
        Entry[] OwnedEmptyRenderers, bool OwnedEmptyTruncated);
    internal const int DetailLimit = 128;

    internal static State Read(PerformanceLab lab, HashSet<GameObject> roots)
    {
        try { lab.CollectPopulationRoots(roots); return ReadCore(roots); }
        finally { roots.Clear(); }
    }
    internal static Boundary Capture(PerformanceLab lab, HashSet<GameObject> roots)
    {
        try
        {
            lab.CollectPopulationRoots(roots);
            var counts = ReadCore(roots); var entries = new List<Entry>(); var ownedEmpty = new List<Entry>();
            bool truncated = false, ownedTruncated = false;
            foreach (var system in ParticleSystem.Active)
            {
                if (system == null || !system.isActiveAndEnabled) continue;
                bool ours = Owned(system.transform, roots);
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                bool enabled = renderer != null && renderer.isActiveAndEnabled;
                if (ours && (!enabled || system.particleCount != 0)) continue;
                var list = ours ? ownedEmpty : entries;
                if (list.Count == DetailLimit) { if (ours) ownedTruncated = true; else truncated = true; continue; }
                bool canvas = system.GetComponentInParent<Canvas>() != null;
                list.Add(new(system.GetInstanceID(), Clip(system.name), Clip(system.transform.root.name), Path(system.transform),
                    system.particleCount, enabled, MainCandidate(system, enabled, canvas), canvas, system.gameObject.layer));
            }
            int cameras = 0, offscreen = 0;
            foreach (var camera in Camera.Active)
                if (camera != null && camera.isActiveAndEnabled) { cameras++; if (camera.targetTexture != null) offscreen++; }
            return new(counts, entries.ToArray(), truncated, cameras, offscreen, ownedEmpty.ToArray(), ownedTruncated);
        }
        finally { roots.Clear(); }
    }
    private static State ReadCore(HashSet<GameObject> roots)
    {
        int active = 0, owned = 0, renderers = 0, candidates = 0, ownedCandidates = 0, backgroundRenderers = 0, empty = 0, backgroundEmpty = 0;
        long particles = 0, ownedParticles = 0;
        foreach (var system in ParticleSystem.Active)
        {
            if (system == null || !system.isActiveAndEnabled) continue;
            active++; particles += system.particleCount;
            bool ours = Owned(system.transform, roots);
            if (ours) { owned++; ownedParticles += system.particleCount; }
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            bool enabled = renderer != null && renderer.isActiveAndEnabled;
            if (!enabled) continue;
            renderers++; if (!ours) backgroundRenderers++;
            if (system.particleCount == 0) { empty++; if (!ours) backgroundEmpty++; }
            if (MainCandidate(system, true, system.GetComponentInParent<Canvas>() != null))
            { candidates++; if (ours) ownedCandidates++; }
        }
        return new(active, particles, owned, ownedParticles, renderers, candidates, ownedCandidates, backgroundRenderers, empty, backgroundEmpty);
    }
    private static bool Owned(Transform node, HashSet<GameObject> roots)
    {
        for (; node != null; node = node.parent) if (roots.Contains(node.gameObject)) return true;
        return false;
    }
    private static bool MainCandidate(ParticleSystem system, bool enabled, bool canvas)
        => enabled && !canvas && Camera.main != null && (Camera.main.cullingMask & (1 << system.gameObject.layer)) != 0;
    private static string Clip(string text)
    {
        string value = (text ?? "").Replace('\r', ' ').Replace('\n', ' ');
        return value.Length > 80 ? value[..80] : value;
    }
    private static string Path(Transform node)
    {
        var text = new StringBuilder(); int depth = 0;
        for (; node != null && depth < 8; node = node.parent, depth++)
        { if (depth != 0) text.Insert(0, '/'); text.Insert(0, Clip(node.name)); }
        if (node != null) text.Insert(0, ".../");
        return text.ToString();
    }
}
