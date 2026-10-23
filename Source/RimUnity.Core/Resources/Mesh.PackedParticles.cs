using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RimUnity;

public sealed partial class Mesh
{
    // Alternate canonical storage for owned particle pages. General mesh access
    // materializes the public channels once; Clear discards either representation.
    private readonly List<ParticleVertex> particleVertices = new();

    private void MaterializeParticleChannels()
    {
        if (particleVertices.Count == 0) return;
        AppendParticleChannels(CollectionsMarshal.AsSpan(particleVertices));
        particleVertices.Clear();
    }

    private T[] ReadChannel<T>(List<T> channel)
    {
        MaterializeParticleChannels();
        return channel.ToArray();
    }

    private void AppendParticleChannels(ReadOnlySpan<ParticleVertex> data)
    {
        int count = checked(positions.Count + data.Length);
        positions.EnsureCapacity(count); normalData.EnsureCapacity(count);
        tangentData.EnsureCapacity(count); colorData.EnsureCapacity(count);
        for (int i = 0; i < 3; i++) (texcoords[i] ??= new()).EnsureCapacity(count);
        foreach (ref readonly var vertex in data)
        {
            positions.Add(vertex.Position); normalData.Add(vertex.Normal);
            tangentData.Add(vertex.Tangent); colorData.Add(vertex.Color);
            texcoords[0].Add(vertex.UV0); texcoords[1].Add(vertex.UV1); texcoords[2].Add(vertex.UV2);
        }
    }

    internal void AppendParticleGeometry(List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents,
        List<Color32> colors, List<Vector4> uv0, List<Vector4> uv1, List<Vector4> uv2, List<int> indices,
        int submesh, List<ParticleVertex> packed)
        => AppendParticleGeometryCore(vertices, normals, tangents, colors, uv0, uv1, uv2, indices, submesh, false, packed);
}
