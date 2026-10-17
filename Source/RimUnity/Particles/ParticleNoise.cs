namespace RimUnity;

//以解析 Perlin 梯度构造连续 Curl 噪声；不依赖 Unity 原生噪声表。
internal static class ParticleNoise
{
    private const float InverseSqrtTwo = .7071067811865475f;

    //低、中质量复用势场，仍保留三个空间维度，分别只需求一组、两组梯度。
    internal static Vector3 Curl(Vector3 point, uint seed, ParticleSystemNoiseQuality quality)
    {
        Vector3 x = PerlinGradient(point, seed);
        Vector3 y = quality == ParticleSystemNoiseQuality.Low ? x : PerlinGradient(point, seed ^ 0x68bc21ebu);
        Vector3 z = quality == ParticleSystemNoiseQuality.High ? PerlinGradient(point, seed ^ 0x02e5be93u) : x;
        //每个分量由两项导数相减，固定缩放保持强度量级，不按位置归一化旋涡场。
        return new Vector3(z.y - y.z, x.z - z.x, y.x - x.y) * InverseSqrtTwo;
    }

    //对八角点梯度插值直接求导，避免为 Curl 在六个邻近位置重复采样。
    private static Vector3 PerlinGradient(Vector3 point, uint seed)
    {
        int ix = Mathf.FloorToInt(point.x), iy = Mathf.FloorToInt(point.y), iz = Mathf.FloorToInt(point.z);
        Vector3 offset = new(point.x - ix, point.y - iy, point.z - iz);
        Vector3 fade = new(Fade(offset.x), Fade(offset.y), Fade(offset.z));
        Vector3 derivative = new(FadeDerivative(offset.x), FadeDerivative(offset.y), FadeDerivative(offset.z));
        Vector3 result = Vector3.zero;
        for (int z = 0; z < 2; z++)
        {
            float wz = z == 0 ? 1 - fade.z : fade.z, dz = z == 0 ? -derivative.z : derivative.z;
            for (int y = 0; y < 2; y++)
            {
                float wy = y == 0 ? 1 - fade.y : fade.y, dy = y == 0 ? -derivative.y : derivative.y;
                for (int x = 0; x < 2; x++)
                {
                    float wx = x == 0 ? 1 - fade.x : fade.x, dx = x == 0 ? -derivative.x : derivative.x;
                    Vector3 gradient = LatticeGradient(ix + x, iy + y, iz + z, seed);
                    float value = gradient.x * (offset.x - x) + gradient.y * (offset.y - y) + gradient.z * (offset.z - z);
                    float weight = wx * wy * wz;
                    result.x += gradient.x * weight + value * dx * wy * wz;
                    result.y += gradient.y * weight + value * wx * dy * wz;
                    result.z += gradient.z * weight + value * wx * wy * dz;
                }
            }
        }
        return result;
    }

    //五次插值让格点边界的一阶、二阶导数连续。
    private static float Fade(float value) => value * value * value * (value * (value * 6 - 15) + 10);

    //五次插值的解析导数。
    private static float FadeDerivative(float value) => 30 * value * value * (value - 1) * (value - 1);

    //使用现有确定性哈希选择十二个单位梯度，不推进发射器随机序列。
    private static Vector3 LatticeGradient(int x, int y, int z, uint seed)
    {
        uint salt = unchecked((uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791));
        int index = (int)(ParticleSystem.Random(seed, salt) * 12);
        float g = InverseSqrtTwo;
        return index switch
        {
            0 => new(g, g, 0), 1 => new(-g, g, 0), 2 => new(g, -g, 0), 3 => new(-g, -g, 0),
            4 => new(g, 0, g), 5 => new(-g, 0, g), 6 => new(g, 0, -g), 7 => new(-g, 0, -g),
            8 => new(0, g, g), 9 => new(0, -g, g), 10 => new(0, g, -g), _ => new(0, -g, -g)
        };
    }
}
