using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RippleEffectRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class RippleSettings
    {
        public Material rippleMaterial;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public RippleSettings settings = new RippleSettings();

    RippleRenderPass ripplePass;

    public override void Create()
    {
        ripplePass = new RippleRenderPass(settings.rippleMaterial);
        ripplePass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        ripplePass.SetTarget(renderer.cameraColorTarget); // For older Unity/URP
        renderer.EnqueuePass(ripplePass);
    }


    public void TriggerRipple(Vector2 screenUV, float rippleTime, float strength)
    {
        ripplePass?.SetRipple(screenUV, rippleTime, strength);
    }

    class RippleRenderPass : ScriptableRenderPass
    {
        private Material rippleMat;
        private Vector2 rippleCenter;
        private float rippleTime;
        private float rippleStrength;

        private RenderTargetIdentifier colorTarget;


        public RippleRenderPass(Material material)
        {
            rippleMat = material;
        }

        public void SetTarget(RenderTargetIdentifier colorTarget)
        {
            this.colorTarget = colorTarget;
        }

        public void SetRipple(Vector2 center, float time, float strength)
        {
            rippleCenter = center;
            rippleTime = time;
            rippleStrength = strength;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (rippleMat == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get("Ripple Effect");

            rippleMat.SetVector("_RippleCenter", rippleCenter);
            rippleMat.SetFloat("_RippleTime", rippleTime);
            rippleMat.SetFloat("_RippleStrength", rippleStrength);

            Blit(cmd, colorTarget, colorTarget, rippleMat);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
