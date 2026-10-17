using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Waylith.Rendering;

namespace RimUnity.ShaderTool;

//把 GPU 实际使用的成员偏移与宿主材质上传布局核对，防止编译成功但参数串位。
internal static class ShaderUniformLayout
{
    //检查普通材质块及扩展存储块；完全未使用而被优化掉的块无需上传。
    internal static void Verify(byte[] code, MaterialLayout layout, string name)
    {
        var words = new uint[code.Length / 4];
        Buffer.BlockCopy(code, 0, words, 0, code.Length);
        var sets = new Dictionary<uint, uint>();
        var bindings = new Dictionary<uint, uint>();
        var pointers = new Dictionary<uint, uint>();
        var variables = new Dictionary<uint, uint>();
        var names = new Dictionary<(uint, uint), string>();
        var offsets = new Dictionary<(uint, uint), uint>();
        var major = new Dictionary<(uint, uint), uint>();
        var matrixStrides = new Dictionary<(uint, uint), uint>();
        var arrayStrides = new Dictionary<uint, uint>();
        var structures = new Dictionary<uint, uint[]>();
        for (int i = 5; i < words.Length;)
        {
            int count = (int)(words[i] >> 16), op = (int)(words[i] & 0xffff);
            if (count == 0 || i + count > words.Length) throw new InvalidDataException("SPIR-V 指令长度不合法。");
            if (op == 6) names[(words[i + 1], words[i + 2])] = Encoding.UTF8.GetString(code, (i + 3) * 4, (count - 3) * 4).TrimEnd('\0');
            else if (op == 30) structures[words[i + 1]] = words.Skip(i + 2).Take(count - 2).ToArray();
            else if (op == 32) pointers[words[i + 1]] = words[i + 3];
            else if (op == 59) variables[words[i + 2]] = words[i + 1];
            else if (op == 71 && count >= 4)
            {
                if (words[i + 2] == 6) arrayStrides[words[i + 1]] = words[i + 3];
                if (words[i + 2] == 33) bindings[words[i + 1]] = words[i + 3];
                if (words[i + 2] == 34) sets[words[i + 1]] = words[i + 3];
            }
            else if (op == 72 && count >= 4)
            {
                var member = (words[i + 1], words[i + 2]);
                if (words[i + 3] is 4 or 5) major[member] = words[i + 3];
                if (count >= 5 && words[i + 3] == 7) matrixStrides[member] = words[i + 4];
                if (count >= 5 && words[i + 3] == 35) offsets[member] = words[i + 4];
            }
            i += count;
        }
        foreach (var variable in variables)
        {
            if (!sets.TryGetValue(variable.Key, out uint set) || set != 2 ||
                !bindings.TryGetValue(variable.Key, out uint binding)) continue;
            MaterialUniformSlot[] slots;
            if (binding == 0) slots = layout.UniformSlots;
            else
            {
                var storage = layout.StorageBufferSlots.FirstOrDefault(s => s.Binding == binding);
                var block = layout.UniformBlocks.FirstOrDefault(b => b.PropertyId == storage.PropertyId);
                if (storage.PropertyName == "_Bones")
                {
                    uint boneStructure = pointers[variable.Value];
                    var member = (boneStructure, 0u);
                    uint arrayType = structures[boneStructure][0];
                    if (!major.TryGetValue(member, out uint order) || order != 4
                        || !matrixStrides.TryGetValue(member, out uint stride) || stride != 16
                        || !arrayStrides.TryGetValue(arrayType, out uint arrayStride) || arrayStride != 64
                        || !offsets.TryGetValue(member, out uint start) || start != 0)
                        throw new InvalidDataException(name + "：骨骼缓冲必须匹配宿主逐行 Matrix4x4、16 字节行步长和 64 字节矩阵步长。");
                    Console.WriteLine(name + ": _Bones RowMajor / MatrixStride=16 / ArrayStride=64 / Offset=0");
                }
                if (block == null) continue;
                slots = block.Members;
            }
            uint structure = pointers[variable.Value];
            for (uint member = 0; member < slots.Length; member++)
            {
                var slot = slots[member];
                if (!offsets.TryGetValue((structure, member), out uint offset))
                    throw new InvalidDataException($"{name}：GPU 材质块缺少成员 {slot.PropertyName}。");
                if (offset != slot.Offset)
                    throw new InvalidDataException($"{name}：{slot.PropertyName} 材质偏移不一致，宿主={slot.Offset}，GPU={offset}。");
                if (names.TryGetValue((structure, member), out string memberName) && memberName != slot.PropertyName)
                    throw new InvalidDataException($"{name}：材质成员顺序不一致，宿主={slot.PropertyName}，GPU={memberName}。");
            }
        }
    }
}
