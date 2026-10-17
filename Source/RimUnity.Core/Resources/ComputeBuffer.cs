using System;
using System.Runtime.InteropServices;
using Waylith.Rendering;
using Waylith.Rendering.Compute;

namespace RimUnity;

//结构化缓冲保留提交时的环形快照，销毁包装不会提前修改已提交数据。
public sealed class ComputeBuffer : IDisposable
{
    private readonly InstanceDataBuffer data = new();
    private bool disposed;
    private GpuComputeBuffer immutable;
    private int generation = -1;
    public int count { get; }
    public int stride { get; }
    public ComputeBuffer(int count, int stride)
    {
        if (count <= 0 || stride <= 0 || stride % 4 != 0) throw new ArgumentOutOfRangeException(nameof(count), "数量必须为正数，步长必须是四的正整数倍。");
        this.count = count; this.stride = stride;
    }
    //不变的网格权重只上传一次，使用宿主延迟释放的独立 GPU 缓冲。
    public static ComputeBuffer CreateImmutable<T>(T[] source) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(source);
        Runtime.NativeRuntime.AssertThread();
        var result = new ComputeBuffer(source.Length, Marshal.SizeOf<T>());
        result.SetData(source);
        result.immutable = GpuTextureService.CreateComputeBuffer("RimUnity.Immutable", result.Bytes.Length)
            ?? throw new InvalidOperationException("宿主尚未提供 GPU 缓冲服务。");
        try { result.immutable.Upload<byte>(result.Bytes); }
        catch { result.Dispose(); throw; }
        return result;
    }

    public void SetData<T>(T[] source) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (immutable != null) throw new InvalidOperationException("不可变缓冲不能重新写入。");
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(source.AsSpan());
        if (Marshal.SizeOf<T>() != stride || source.Length > count) throw new ArgumentException("缓冲数据的步长或数量与声明不符。", nameof(source));
        generation = data.Write(bytes, stride, source.Length);
    }
    internal ReadOnlySpan<byte> Bytes { get { ObjectDisposedException.ThrowIf(disposed, this); return data.GetBytes(generation); } }
    internal InstanceStorageBinding Binding(int id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (generation < 0) throw new InvalidOperationException("ComputeBuffer 尚未写入数据。");
        return immutable != null ? new(id, immutable) : new(id, data, generation);
    }
    public void Release() => Dispose();
    //宿主等待在途帧完成后释放原生缓冲，已提交的绘制仍持有有效资源。
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        immutable?.Dispose();
    }
}
