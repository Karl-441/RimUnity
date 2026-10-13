using System;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//选中层单独绘制到浮点颜色附件，借用地图深度测试遮挡，不读回 CPU。
internal sealed unsafe class BloomLayerTarget : IDisposable
{
    private DeviceEx device;
    private MeshBatchRenderer renderer;
    private VkRenderPass pass;
    private RenderTexture[] colors;
    private VkFramebuffer[] framebuffers;

    //与地图使用相同的顶点流、材质和投影，仅替换颜色输出附件。
    internal RenderTexture Record(in FrameContext ctx, RenderTexture scene, MeshDrawFrame draws)
    {
        if (colors == null) Initialize(in ctx, scene);
        int index = ctx.SubmissionFrameIndex;
        var color = colors[index];
        if (framebuffers[index].IsNull())
        {
            VkImageView* views = stackalloc VkImageView[2] { color.ColorView, scene.DepthView };
            framebuffers[index] = device.CreateFramebuffer(new VkFramebufferCreateInfo
            {
                RenderPass = pass, AttachmentCount = scene.HasDepth ? 2 : 1, Attachments = views,
                Width = scene.Width, Height = scene.Height, Layers = 1
            }, device.VkAllocator);
        }
        VkClearValue clear = default;
        var begin = new VkRenderPassBeginInfo
        {
            RenderPass = pass, Framebuffer = framebuffers[index], RenderArea = new VkRect2D(color.Extent),
            ClearValueCount = 1, ClearValues = &clear
        };
        var command = ctx.CommandBuffer;
        command.BeginRenderPass(in begin, VkSubpassContents.Inline);
        var viewport = new VkViewport { Width = scene.Width, Height = scene.Height, MinDepth = 0, MaxDepth = 1 };
        var scissor = new VkRect2D(color.Extent);
        command.SetViewport(0, new ReadOnlySpan<VkViewport>(in viewport));
        command.SetScissor(0, new ReadOnlySpan<VkRect2D>(in scissor));
        var target = new FrameContext(ctx.RenderContext, ctx.Resources, command, pass, framebuffers[index], color.Extent,
            VkSampleCountFlags._1Bit, color.ColorFormat, scene.DepthFormat);
        renderer.RecordPass(in target, draws, BloomLayerCapture.MapPass, diagnosticPrefix: "RimUnity.BloomLayers");
        command.EndRenderPass();
        color.Color.SetMipLayout(0, VkImageLayout.ShaderReadOnlyOptimal);
        return color;
    }

    //每个在途帧拥有独立颜色缓冲，场景尺寸由宿主重建入口统一失效。
    private void Initialize(in FrameContext ctx, RenderTexture scene)
    {
        device = ctx.Device;
        renderer = new MeshBatchRenderer(ctx.GraphicsDevice, ctx.RenderContext);
        pass = CreatePass(device, scene.DepthFormat);
        colors = new RenderTexture[ctx.RenderContext.FramesInFlight];
        framebuffers = new VkFramebuffer[colors.Length];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = new RenderTexture(scene.Width, scene.Height, VkFormat.R16G16B16A16SFloat, name: $"RimUnity.BloomLayers.{i}");
    }

    //清空独立颜色，但加载并保留地图深度；所有材质快照均已关闭深度和模板写入。
    private static VkRenderPass CreatePass(DeviceEx device, VkFormat depthFormat)
    {
        bool depth = depthFormat != VkFormat.Undefined;
        VkAttachmentDescription* attachments = stackalloc VkAttachmentDescription[2];
        attachments[0] = new VkAttachmentDescription
        {
            Format = VkFormat.R16G16B16A16SFloat, Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Clear, StoreOp = VkAttachmentStoreOp.Store,
            StencilLoadOp = VkAttachmentLoadOp.DontCare, StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.Undefined, FinalLayout = VkImageLayout.ShaderReadOnlyOptimal
        };
        attachments[1] = new VkAttachmentDescription
        {
            Format = depthFormat, Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Load, StoreOp = VkAttachmentStoreOp.Store,
            StencilLoadOp = VkAttachmentLoadOp.Load, StencilStoreOp = VkAttachmentStoreOp.Store,
            InitialLayout = VkImageLayout.DepthStencilAttachmentOptimal, FinalLayout = VkImageLayout.DepthStencilAttachmentOptimal
        };
        var colorRef = new VkAttachmentReference { Attachment = 0, Layout = VkImageLayout.ColorAttachmentOptimal };
        var depthRef = new VkAttachmentReference { Attachment = 1, Layout = VkImageLayout.DepthStencilAttachmentOptimal };
        var subpass = new VkSubpassDescription
        {
            PipelineBindPoint = VkPipelineBindPoint.Graphics, ColorAttachmentCount = 1,
            ColorAttachments = &colorRef, DepthStencilAttachment = depth ? &depthRef : null
        };
        VkSubpassDependency* dependencies = stackalloc VkSubpassDependency[2];
        dependencies[0] = new VkSubpassDependency
        {
            SrcSubpass = -1, DstSubpass = 0,
            SrcStageMask = VkPipelineStageFlags.FragmentShaderBit | VkPipelineStageFlags.EarlyFragmentTestsBit | VkPipelineStageFlags.LateFragmentTestsBit | VkPipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = VkPipelineStageFlags.EarlyFragmentTestsBit | VkPipelineStageFlags.LateFragmentTestsBit | VkPipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = VkAccessFlags.ShaderReadBit | VkAccessFlags.DepthStencilAttachmentWriteBit | VkAccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = VkAccessFlags.DepthStencilAttachmentReadBit | VkAccessFlags.ColorAttachmentWriteBit
        };
        dependencies[1] = new VkSubpassDependency
        {
            SrcSubpass = 0, DstSubpass = -1,
            SrcStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.LateFragmentTestsBit,
            DstStageMask = VkPipelineStageFlags.FragmentShaderBit | VkPipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit | VkAccessFlags.DepthStencilAttachmentReadBit,
            DstAccessMask = VkAccessFlags.ShaderReadBit | VkAccessFlags.DepthStencilAttachmentReadBit | VkAccessFlags.DepthStencilAttachmentWriteBit
        };
        return device.CreateRenderPass(new VkRenderPassCreateInfo
        {
            AttachmentCount = depth ? 2 : 1, Attachments = attachments, SubpassCount = 1, Subpasses = &subpass,
            DependencyCount = 2, Dependencies = dependencies
        }, device.VkAllocator);
    }

    //只由宿主等待在途帧后的重建／销毁入口调用。
    public void Dispose()
    {
        renderer?.Dispose(); renderer = null;
        if (framebuffers != null) foreach (var framebuffer in framebuffers)
            if (!framebuffer.IsNull()) device.DestroyFramebuffer(framebuffer, device.VkAllocator);
        if (colors != null) foreach (var color in colors) color.Dispose();
        if (!pass.IsNull()) device.DestroyRenderPass(pass, device.VkAllocator);
        pass = default; colors = null; framebuffers = null;
    }
}
