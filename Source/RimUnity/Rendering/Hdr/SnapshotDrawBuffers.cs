using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Waylith.Rendering;

namespace RimUnity.Rendering;

// Snapshot-owned commands never enter the host's concurrent command pool. Bind the exact
// known layout once; unknown host versions retain the original Rent/Return implementation.
internal static class SnapshotDrawBuffers
{
    internal static readonly Func<FinalDrawItem> Create;
    internal static readonly Action<FinalDrawItem, FinalDrawItem, MaterialBinding, int> Copy;
    internal static readonly Action<FinalDrawItem> Clear;
    internal static readonly string Status;
    internal static bool Available => Copy != null;
    static SnapshotDrawBuffers()
    {
        try
        {
            var type = typeof(FinalDrawItem);
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            string[] expected = { "mesh", "binding", "hasOverride", "overrideUniforms", "overrideResources",
                "cachedPipeline", "cachedPipelineLayout", "cachedPipelineVersion", "cachedInstancedMaterialSet",
                "cachedInstancedResolvedDataVersion", "cachedInstancedResourceGeneration", "<Instancing>k__BackingField",
                "<Storage>k__BackingField", "<Matrix>k__BackingField", "<PassId>k__BackingField", "<Layer>k__BackingField",
                "<RenderQueue>k__BackingField", "<SortKey>k__BackingField" };
            if (fields.Length != expected.Length || fields.Any(f => !expected.Contains(f.Name) || f.IsInitOnly))
                throw new NotSupportedException("FinalDrawItem field layout changed");
            var factory = new DynamicMethod("HdrCreateDraw", type, Type.EmptyTypes, typeof(SnapshotDrawBuffers).Module, true);
            var il = factory.GetILGenerator();
            il.Emit(OpCodes.Newobj, AccessTools.Constructor(type, Type.EmptyTypes)); il.Emit(OpCodes.Ret);
            var create = factory.CreateDelegate<Func<FinalDrawItem>>();
            var copy = new DynamicMethod("HdrCopyDraw", typeof(void), new[] { type, type, typeof(MaterialBinding), typeof(int) }, typeof(SnapshotDrawBuffers).Module, true);
            il = copy.GetILGenerator();
            foreach (var field in fields)
            {
                il.Emit(OpCodes.Ldarg_1);
                if (field.Name == "binding") il.Emit(OpCodes.Ldarg_2);
                else if (field.Name == "hasOverride") il.Emit(OpCodes.Ldc_I4_1);
                else if (field.Name == "overrideUniforms" || field.Name == "overrideResources")
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, type.GetProperty(field.Name == "overrideUniforms" ? "Uniforms" : "Resources").GetMethod);
                    il.Emit(OpCodes.Ldobj, field.FieldType);
                }
                else if (field.Name == "<RenderQueue>k__BackingField")
                {
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ldarg_3);
                    il.Emit(OpCodes.Call, typeof(Math).GetMethod(nameof(Math.Max), new[] { typeof(int), typeof(int) }));
                }
                else if (field.Name.StartsWith("cached", StringComparison.Ordinal))
                {
                    if (field.FieldType == typeof(int)) il.Emit(OpCodes.Ldc_I4_M1);
                    else if (field.FieldType == typeof(long)) { il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Conv_I8); }
                    else
                    {
                        il.Emit(OpCodes.Ldflda, field); il.Emit(OpCodes.Initobj, field.FieldType); continue;
                    }
                }
                else { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); }
                il.Emit(OpCodes.Stfld, field);
            }
            il.Emit(OpCodes.Ret);
            var copier = copy.CreateDelegate<Action<FinalDrawItem, FinalDrawItem, MaterialBinding, int>>();
            var clear = new DynamicMethod("HdrClearDraw", typeof(void), new[] { type }, typeof(SnapshotDrawBuffers).Module, true);
            il = clear.GetILGenerator();
            foreach (var field in fields)
            {
                //Numeric matrix/uniform/cache values are overwritten by Copy. Only references
                //need retirement work; do not zero large reference-free uniform blocks twice.
                bool references = !field.FieldType.IsValueType || (bool)typeof(RuntimeHelpers)
                    .GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences)).MakeGenericMethod(field.FieldType).Invoke(null, null);
                if (!references) continue;
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldflda, field); il.Emit(OpCodes.Initobj, field.FieldType);
            }
            il.Emit(OpCodes.Ret);
            var cleaner = clear.CreateDelegate<Action<FinalDrawItem>>();
            RuntimeHelpers.PrepareDelegate(create); RuntimeHelpers.PrepareDelegate(copier); RuntimeHelpers.PrepareDelegate(cleaner);
            Create = create; Clear = cleaner; Copy = copier;
            Status = "snapshot-owned commands; exact host layout verified";
        }
        catch (Exception error) { Status = "host Rent fallback: " + error.GetBaseException().Message; }
    }
}
