using System.Runtime.CompilerServices;
using RimUnity;

internal static class ParticleTurnoverChecks
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        foreach (int fps in new[] { 30, 60, 120, 144 })
        foreach (bool fixedStep in new[] { false, true })
        {
            var root = new GameObject("capacity turnover");
            try
            {
                var system = root.AddComponent<ParticleSystem>();
                system.main.maxParticles = 1;
                system.main.startLifetime = 5;
                system.main.startSpeed = 0;
                system.main.loop = true;
                system.emission.rateOverTime = 999;
                system.shape.enabled = false;
                int gaps = 0;
                for (int frame = 0; frame < fps * 21; frame++)
                {
                    system.Simulate(1f / fps, false, frame == 0, fixedStep);
                    if (frame > fps && system.particleCount == 0) gaps++;
                    if (system.particleCount > 1) throw new Exception("Exceeded maxParticles");
                }
                Console.WriteLine($"{fps} FPS, fixedStep={fixedStep}: {gaps} empty frames after warmup");
                if (gaps != 0) throw new Exception("Particle turnover leaves empty frames");
            }
            finally { RimUnity.Object.DestroyImmediate(root); }
        }
        CheckAgesAndEmissionGaps();
        CheckRingReplacement();
        CheckDeathEmitter();
        CheckBurstCapacityTimeline();
        CheckMixedEmissionTimeline();
        Console.WriteLine("PASS particle turnover, birth offsets, existing ages, emission gaps, ring replacement, death subemitters, Burst capacity boundaries, split-step equivalence and mixed/high-rate emission timelines.");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void CheckBurstCapacityTimeline()
    {
        foreach (float burstTime in new[] { .005f, .015f, .018f })
        foreach (bool split in new[] { false, true })
        foreach (int physics in new[] { 0, 1, 2 })
        {
            var root = new GameObject("burst capacity timeline");
            try
            {
                var system = root.AddComponent<ParticleSystem>();
                system.main.maxParticles = 1;
                system.main.startLifetime = 1;
                system.main.startSpeed = 0;
                system.shape.enabled = false;
                system.collision.enabled = physics == 1;
                system.collision.type = ParticleSystemCollisionType.World;
                system.trigger.enabled = physics == 2;
                system.emission.rateOverTime = 0;
                system.emission.bursts = new[] { new ParticleSystem.Burst { time = burstTime, count = 1, cycleCount = 1, probability = 1 } };
                system.Simulate(0, false, true, false);
                system.Emit(new ParticleSystem.EmitParams { startLifetime = .015f }, 1);
                if (split) { system.Simulate(.01f, false, false, false); system.Simulate(.01f, false, false, false); }
                else system.Simulate(.02f, false, false, false);
                int expected = burstTime < .015f ? 0 : 1;
                Check(system.particleCount == expected, $"Burst {burstTime}, split={split}, physics={physics}: expected {expected}, got {system.particleCount}");
                if (expected != 0)
                {
                    var particles = new ParticleSystem.Particle[1]; system.GetParticles(particles);
                    Check(Math.Abs(particles[0].remainingLifetime - (1 - (.02f - burstTime))) < .00001f, "Burst newborn age differs from its birth timestamp");
                }
            }
            finally { RimUnity.Object.DestroyImmediate(root); }
        }
    }

    private static void CheckMixedEmissionTimeline()
    {
        foreach (bool split in new[] { false, true })
        foreach (bool distance in new[] { false, true })
        {
            var root = new GameObject("mixed emission order");
            try
            {
                var system = root.AddComponent<ParticleSystem>();
                system.main.maxParticles = 1;
                system.main.startLifetime = 1;
                system.main.startSpeed = 0;
                system.shape.enabled = false;
                system.emission.rateOverTime = distance ? 0 : 50; //next event at .02 must not precede the Burst
                system.emission.rateOverDistance = distance ? 50 : 0;
                system.emission.bursts = new[] { new ParticleSystem.Burst { time = .005f, count = 1, cycleCount = 1, probability = 1 } };
                system.Simulate(0, false, true, false);
                if (split)
                {
                    root.transform.position = new Vector3(.01f, 0, 0);
                    system.Simulate(.01f, false, false, false);
                    root.transform.position = new Vector3(.02f, 0, 0);
                    system.Simulate(.01f, false, false, false);
                }
                else { root.transform.position = new Vector3(.02f, 0, 0); system.Simulate(.02f, false, false, false); }
                var particles = new ParticleSystem.Particle[1]; system.GetParticles(particles);
                Check(system.particleCount == 1 && Math.Abs(particles[0].remainingLifetime - .985f) < .00001f,
                    "Continuous emission took capacity before an earlier Burst");
                system.emission.rateOverTime = 0;
                system.emission.rateOverDistance = 0;
                system.main.startLifetime = .004f;
                system.emission.bursts = new[] {
                    new ParticleSystem.Burst { time = .01f, count = 1, cycleCount = 1, probability = 1 },
                    new ParticleSystem.Burst { time = .002f, count = 1, cycleCount = 1, probability = 1 } };
                system.Simulate(.012f, false, true, false);
                Check(system.GetParticles(particles) == 1 && Math.Abs(particles[0].remainingLifetime - .002f) < .00001f,
                    "Unsorted Bursts or within-step newborn death did not respect the timeline");
                system.emission.rateOverTime = 1e9f;
                system.emission.bursts = Array.Empty<ParticleSystem.Burst>();
                system.main.startLifetime = 100;
                system.Simulate(.02f, false, true, false);
                Check(system.particleCount == 1, "High-rate saturated source did not stay bounded");
            }
            finally { RimUnity.Object.DestroyImmediate(root); }
        }
    }

    private static void CheckAgesAndEmissionGaps()
    {
        var root = new GameObject("birth offset checks");
        try
        {
            var system = root.AddComponent<ParticleSystem>();
            system.shape.enabled = false;
            system.main.startLifetime = 1;
            system.main.startSpeed = 0;
            system.emission.rateOverTime = 10;
            system.Simulate(0, false, true, false);
            system.Emit(1);
            system.Simulate(.15f, false, false, false);
            var particles = new ParticleSystem.Particle[4];
            int count = system.GetParticles(particles);
            Check(count == 2, "Expected one existing and one newborn particle");
            var ages = particles.Take(count).Select(p => p.remainingLifetime).Order().ToArray();
            Check(Math.Abs(ages[0] - .85f) < .0001f, "Existing particle advanced more than once");
            Check(Math.Abs(ages[1] - .95f) < .0001f, "Newborn did not respect its birth offset");
            system.emission.enabled = false;
            system.Simulate(2, false, false, false);
            Check(system.particleCount == 0, "Disabled emission replenished particles");
            system.emission.enabled = true;
            system.emission.rateOverTime = .5f;
            system.main.startLifetime = .1f;
            system.Simulate(2, false, true, false);
            Check(system.particleCount == 1, "Low-rate emitter did not emit at its scheduled time");
            system.Simulate(.2f, false, false, false);
            Check(system.particleCount == 0, "Intentional gap incorrectly filled without scheduled emission");
        }
        finally { RimUnity.Object.DestroyImmediate(root); }
    }

    private static void CheckRingReplacement()
    {
        var root = new GameObject("ring replacement checks");
        try
        {
            var system = root.AddComponent<ParticleSystem>();
            system.shape.enabled = false;
            system.main.maxParticles = 1;
            system.main.startLifetime = .1f;
            system.main.ringBufferMode = ParticleSystemRingBufferMode.PauseUntilReplaced;
            system.emission.rateOverTime = 0;
            system.Simulate(0, false, true, false);
            system.Emit(1);
            system.Simulate(.2f, false, false, false);
            var particles = new ParticleSystem.Particle[1];
            Check(system.GetParticles(particles) == 1 && particles[0].remainingLifetime == 0, "Paused ring particle was removed");
            system.emission.rateOverTime = 20;
            system.Simulate(.1f, false, false, false);
            Check(system.GetParticles(particles) == 1 && Math.Abs(particles[0].remainingLifetime - .1f) < .0001f,
                "Ring replacement at the step end was advanced for a full step or skipped");
        }
        finally { RimUnity.Object.DestroyImmediate(root); }
    }

    private static void CheckDeathEmitter()
    {
        var root = new GameObject("death emitter checks");
        var childRoot = new GameObject("death child");
        childRoot.transform.SetParent(root.transform, false);
        try
        {
            var parent = root.AddComponent<ParticleSystem>();
            var child = childRoot.AddComponent<ParticleSystem>();
            parent.shape.enabled = child.shape.enabled = false;
            parent.main.startLifetime = .05f;
            parent.emission.enabled = false;
            child.main.startLifetime = 1;
            child.emission.rateOverTime = 0;
            child.emission.bursts = new[] { new ParticleSystem.Burst { time = 0, count = 1, cycleCount = 1, probability = 1 } };
            parent.subEmitters.enabled = true;
            parent.subEmitters.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            parent.Simulate(0, true, true, false);
            parent.Emit(1);
            parent.Simulate(.1f, true, false, false);
            var particles = new ParticleSystem.Particle[4];
            Check(parent.particleCount == 0 && child.GetParticles(particles) == 1, "Death subemitter missing or duplicated");
            Check(Math.Abs(particles[0].remainingLifetime - .95f) < .0001f, "Death child birth offset changed");
            parent.Simulate(.1f, true, false, false);
            Check(child.GetParticles(particles) == 1 && Math.Abs(particles[0].remainingLifetime - .85f) < .0001f,
                "Death child advanced twice or death event repeated");
        }
        finally { RimUnity.Object.DestroyImmediate(root); }
    }
}
