using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SobelOutlineFeature : ScriptableRendererFeature
{
    class SobelPass : ScriptableRenderPass
    {
        private Material material;
        private RTHandle source;
        private RTHandle temp;

        public SobelPass(Material mat)
        {
            material = mat;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            source = renderingData.cameraData.renderer.cameraColorTargetHandle;

            var desc = renderingData.cameraData.cameraTargetDescriptor;
            RenderingUtils.ReAllocateIfNeeded(ref temp, desc, name: "_TempSobelTex");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get("SobelOutline");

            Blitter.BlitCameraTexture(cmd, source, temp, material, 0);
            Blitter.BlitCameraTexture(cmd, temp, source);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            temp?.Release();
        }
    }


    public Material sobelMaterial;
    SobelPass pass;

    public override void Create()
    {
        pass = new SobelPass(sobelMaterial);
        pass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(pass);
    }

}
