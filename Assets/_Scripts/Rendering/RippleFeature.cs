using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// This is the ScriptableRendererFeature that you’ll add in the Renderer Features list
public class RippleFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class RippleSettings
    {
        public RenderPassEvent    renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        public Material           rippleMaterial  = null;
    }

    public RippleSettings settings = new RippleSettings();

    RipplePass ripplePass;

    public override void Create()
    {
        // Instantiate the pass and give it the correct injection point + material
        ripplePass = new RipplePass(settings.rippleMaterial, settings.renderPassEvent);
    }

    // Called by the Renderer each frame; queue up your pass if the Material is assigned
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.rippleMaterial == null) return;
        ripplePass.Setup(renderer.cameraColorTarget);
        renderer.EnqueuePass(ripplePass);
    }

    // The actual rendering pass
    class RipplePass : ScriptableRenderPass
    {
        static readonly string k_CommandBufferTag = "Render 2D Ripple";
        readonly Material rippleMat;
        RenderTargetIdentifier source { get; set; }
        RenderTargetHandle tempTexture;

        public RipplePass(Material mat, RenderPassEvent evt)
        {
            rippleMat = mat;
            renderPassEvent = evt;
            tempTexture.Init("_TempRippleTexture");
        }

        public void Setup(RenderTargetIdentifier src)
        {
            source = src;
        }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (rippleMat == null) return;

            var cmd = CommandBufferPool.Get(k_CommandBufferTag);

            // Allocate a temporary RT, blit source into it
            cmd.GetTemporaryRT(tempTexture.id, data.cameraData.cameraTargetDescriptor);
            cmd.Blit(source, tempTexture.Identifier());

            // Blit back with your ripple material (it reads _MainTex by default)
            cmd.Blit(tempTexture.Identifier(), source, rippleMat);

            // Release
            cmd.ReleaseTemporaryRT(tempTexture.id);

            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
