using RimUnity.Serialization;
using UnityEngine;

namespace RimUnity.Editor
{
    //物理模块引用真实导出的碰撞体和平面，不根据名字猜测目标。
    internal static class ParticlePhysicsWriter
    {
        //高度场按 z*N+x 保存，固定三角划分保留表面与孔洞，不导出原生 PhysX 缓存。
        internal static void Terrain(TerrainCollider collider, ComponentData data, ValueWriter writer, string path)
        {
            TerrainData terrain = collider.terrainData;
            if (terrain == null) throw new System.NotSupportedException(path + "：TerrainCollider 缺少 TerrainData。");
            if (terrain.treeInstanceCount != 0) throw new System.NotSupportedException(path + "：地形树实例的隐式碰撞体尚未导出，请将碰撞体显式放入预制体。");
            if (collider.isTrigger) throw new System.NotSupportedException(path + "：TerrainCollider 不作为封闭 Trigger 体积。");
            if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic)
                throw new System.NotSupportedException(path + "：TerrainCollider 不支持动态刚体。");
            int n = terrain.heightmapResolution, h = terrain.holesResolution;
            float[,] source = terrain.GetHeights(0, 0, n, n); bool[,] holes = terrain.GetHoles(0, 0, h, h);
            var heights = new float[n * n]; var surfaces = new bool[h * h];
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) heights[z * n + x] = source[z, x];
            for (int z = 0; z < h; z++) for (int x = 0; x < h; x++) surfaces[z * h + x] = holes[z, x];
            var value = new ValueData { kind = "data", fields = new System.Collections.Generic.List<MemberData>() };
            void Field(string name, object content) => value.fields.Add(new MemberData { name = name, value = writer.Write(content, path + ".terrainData." + name) });
            Field("heightmapResolution", n); Field("size", terrain.size); Field("heights", heights); Field("holes", surfaces);
            data.members.Add(new MemberData { name = "terrainData", value = value });
            Debug.LogWarning(path + "：高度场使用固定三角对角线，接触细节可能与 Unity 原生地形碰撞不同。", collider);
        }
        internal static void Write(ParticleSystem system, ComponentData data, ValueWriter writer, string path)
        {
            var module = system.collision;
            var collision = MathValueWriter.Properties(module, writer, path + ".collision", "enabled", "type", "mode", "quality", "dampen", "bounce", "lifetimeLoss", "minKillSpeed", "maxKillSpeed", "radiusScale", "sendCollisionMessages", "maxCollisionShapes", "enableDynamicColliders", "colliderForce", "multiplyColliderForceByCollisionAngle", "multiplyColliderForceByParticleSpeed", "multiplyColliderForceByParticleSize");
            collision.fields.Add(new MemberData { name = "collidesWith", value = writer.Write(module.collidesWith.value, path + ".collision.collidesWith") });
            var planes = new Transform[module.planeCount];
            for (int i = 0; i < planes.Length; i++) planes[i] = module.GetPlane(i);
            collision.fields.Add(new MemberData { name = "planes", value = writer.Write(planes, path + ".collision.planes") });
            data.members.Add(new MemberData { name = "collision", value = collision });
            var trigger = MathValueWriter.Properties(system.trigger, writer, path + ".trigger", "enabled", "inside", "outside", "enter", "exit", "radiusScale", "colliderQueryMode");
            var colliders = new Component[system.trigger.colliderCount];
            for (int i = 0; i < colliders.Length; i++) colliders[i] = system.trigger.GetCollider(i);
            trigger.fields.Add(new MemberData { name = "colliders", value = writer.Write(colliders, path + ".trigger.colliders") });
            data.members.Add(new MemberData { name = "trigger", value = trigger });
        }
    }
}
