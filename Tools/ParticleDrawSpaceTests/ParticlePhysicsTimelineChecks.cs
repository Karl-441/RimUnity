using System.Diagnostics;
using System.Runtime.CompilerServices;
using RimUnity;

internal static class ParticlePhysicsTimelineChecks
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        CheckFirstBurstCollision();
        CheckSaturatedPhysics();
        CheckPhysicsCapacityRelease();
        CheckBurstCheckpoint();
        Console.WriteLine("PASS first-Burst world collision, saturated collision/trigger budgets and within-step physics capacity release.");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }

    private static ParticleSystem Create(GameObject root)
    {
        var system = root.AddComponent<ParticleSystem>();
        system.main.maxParticles = 1;
        system.main.startLifetime = 100;
        system.main.startSpeed = 0;
        system.main.startSize = .01f;
        system.shape.enabled = false;
        system.emission.rateOverTime = 0;
        return system;
    }

    private static void CheckFirstBurstCollision()
    {
        foreach (bool kill in new[] { false, true })
        {
            var root = new GameObject("first Burst physics");
            var obstacle = new GameObject("first Burst obstacle");
            try
            {
                obstacle.transform.position = new Vector3(0, 0, 1);
                var collider = obstacle.AddComponent<BoxCollider>();
                collider.size = new Vector3(1, 1, .1f);
                var system = Create(root);
                system.main.startSpeed = 100;
                system.collision.enabled = true;
                system.collision.type = ParticleSystemCollisionType.World;
                system.collision.sendCollisionMessages = true;
                system.collision.lifetimeLoss = kill ? 1 : 0;
                system.emission.bursts = new[] { new ParticleSystem.Burst { time = 0, count = 1, cycleCount = 1, probability = 1 } };
                system.Simulate(.02f, false, true, false);
                var events = new List<ParticleCollisionEvent>();
                system.GetCollisionEvents(obstacle, events);
                Check(events.Count > 0, "First-step Burst missed its world collision");
                if (kill) Check(system.particleCount == 0, "First-step lifetime-loss kill did not occur");
                else
                {
                    var particles = new ParticleSystem.Particle[1];
                    Check(system.GetParticles(particles) == 1 && particles[0].velocity.z < 0, "First-step bounce did not reverse velocity");
                }
            }
            finally { RimUnity.Object.DestroyImmediate(root); RimUnity.Object.DestroyImmediate(obstacle); }
        }
    }

    private static void CheckSaturatedPhysics()
    {
        foreach (bool trigger in new[] { false, true })
        foreach (int capacity in new[] { 1, 100 })
        {
            var root = new GameObject("saturated physics budget");
            try
            {
                var system = Create(root);
                system.main.maxParticles = capacity;
                system.collision.enabled = !trigger;
                system.collision.type = ParticleSystemCollisionType.World;
                system.trigger.enabled = trigger;
                system.Simulate(0, false, true, false);
                system.Emit(capacity);
                system.emission.rateOverTime = 1000;
                system.Simulate(.02f, false, false, false); //warm module/JIT paths
                system.emission.rateOverTime = 1e9f;
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                system.Simulate(.02f, false, false, false);
                timer.Stop(); bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                Check(timer.Elapsed.TotalSeconds < 3, "Rejected emissions exceeded the physics time budget");
                Check(bytes < 8_000_000, "Rejected emissions caused rate-proportional allocation");
                Check(system.particleCount == capacity, "Saturated physics changed population without a death");
                Console.WriteLine($"Physics saturation: trigger={trigger}, capacity={capacity}, {timer.Elapsed.TotalMilliseconds:F2} ms, {bytes} B");
            }
            finally { RimUnity.Object.DestroyImmediate(root); }
        }
    }

    private static void CheckPhysicsCapacityRelease()
    {
        foreach (bool trigger in new[] { false, true })
        {
            var root = new GameObject("physics release and refill");
            var obstacle = new GameObject("physics release volume");
            try
            {
                obstacle.transform.position = new Vector3(0, 0, 1);
                var collider = obstacle.AddComponent<BoxCollider>();
                collider.size = new Vector3(1, 1, .5f);
                var system = Create(root);
                if (trigger)
                {
                    system.trigger.enabled = true;
                    system.trigger.inside = ParticleSystemOverlapAction.Kill;
                    system.trigger.AddCollider(collider);
                }
                else
                {
                    system.collision.enabled = true;
                    system.collision.type = ParticleSystemCollisionType.World;
                    system.collision.lifetimeLoss = 1;
                }
                system.Simulate(0, false, true, false);
                system.Emit(new ParticleSystem.EmitParams { velocity = new Vector3(0, 0, 60) }, 1);
                system.emission.rateOverTime = 1000;
                system.Simulate(.04f, false, false, false);
                var particles = new ParticleSystem.Particle[1];
                Check(system.GetParticles(particles) == 1 && particles[0].velocity.z == 0,
                    "Physics-killed particle was not replaced within the same step");
            }
            finally { RimUnity.Object.DestroyImmediate(root); RimUnity.Object.DestroyImmediate(obstacle); }
        }
    }

    private static void CheckBurstCheckpoint()
    {
        var root = new GameObject("Burst capacity checkpoint");
        var obstacle = new GameObject("checkpoint obstacle");
        try
        {
            obstacle.transform.position = new Vector3(0, 0, .3f);
            var collider = obstacle.AddComponent<BoxCollider>();
            collider.size = new Vector3(1, 1, .1f);
            var system = Create(root);
            system.main.startLifetime = .001f;
            system.collision.enabled = true;
            system.collision.type = ParticleSystemCollisionType.World;
            system.collision.lifetimeLoss = 1;
            system.emission.rateOverTime = 1500;
            system.emission.bursts = new[] { new ParticleSystem.Burst { time = .005f, count = 1, cycleCount = 1, probability = 1 } };
            system.Simulate(0, false, true, false);
            system.Emit(new ParticleSystem.EmitParams { velocity = new Vector3(0, 0, 100), startLifetime = 100 }, 1);
            system.Simulate(.01f, false, false, false);
            Check(system.particleCount == 1, "Continuous attempts after a capacity-releasing Burst checkpoint were discarded");
        }
        finally { RimUnity.Object.DestroyImmediate(root); RimUnity.Object.DestroyImmediate(obstacle); }
    }
}
