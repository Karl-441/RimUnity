using System.Collections.Concurrent;
using Waylith.Rendering;

//No game or graphics device is started. Use real host draw/binding/frame/pool types,
//with a deterministic shader-variant registry instead of compiling native shaders.
namespace RimWorldWaylith.Rendering
{
    internal static class RenderPassIds { internal const byte MapMain = 1; }
}
namespace RimUnity.Rendering
{
    internal static class LinearShaderVariants
    {
        internal static readonly ConcurrentDictionary<Shader, Shader> Variants = new();
        internal static int FailOnCall;
        internal static bool TryGet(Shader source, out Shader linear)
        {
            if (FailOnCall > 0 && --FailOnCall == 0) throw new InvalidOperationException("Injected shader lookup failure");
            return Variants.TryGetValue(source, out linear);
        }
    }
}
