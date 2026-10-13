using System;

namespace RimUnity.Serialization
{
    //Unity 2022.3 顶点流枚举值固定；导出与运行时共用支持范围和通道数。
    public static class ParticleVertexLayout
    {
        public static int Width(int stream)
        {
            switch (stream)
            {
                case 0: case 1: case 2: case 3: return 0;
                case 4: case 5: case 6: case 7: case 13: case 24: case 28: case 32: case 36: return 2;
                case 10: case 14: case 16: case 18: case 19: case 25: case 29: case 33: case 37: return 3;
                case 26: case 30: case 34: case 38: return 4;
                case 8: case 9: case 11: case 12: case 15: case 17: case 20: case 21: case 22: case 23: case 27: case 31: case 35: case 45: case 46: return 1;
                default: throw new NotSupportedException("粒子顶点流尚未接通，枚举值：" + stream);
            }
        }
    }
}
