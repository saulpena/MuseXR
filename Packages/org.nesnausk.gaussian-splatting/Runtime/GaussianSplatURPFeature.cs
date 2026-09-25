// SPDX-License-Identifier: MIT
#if GS_ENABLE_URP

#if !UNITY_6000_0_OR_NEWER
#error Unity Gaussian Splatting URP support only works in Unity 6 or later
#endif

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace GaussianSplatting.Runtime
{
    // Note: I have no idea what is the purpose of ScriptableRendererFeature vs ScriptableRenderPass, which one of those
    // is supposed to do resource management vs logic, etc. etc. Code below "seems to work" but I'm just fumbling along,
    // without understanding any of it.
    //
    // ReSharper disable once InconsistentNaming
    class GaussianSplatURPFeature : ScriptableRendererFeature
    {
        class GSRenderPass : ScriptableRenderPass
        {
            const string GaussianSplatRTName = "_GaussianSplatRT";

            const string ProfilerTag = "GaussianSplatRenderGraph";
            static readonly ProfilingSampler s_profilingSampler = new(ProfilerTag);
            static readonly int s_gaussianSplatRT = Shader.PropertyToID(GaussianSplatRTName);
            // MuseXR reduced-resolution splat layer (GaussianSplatSettings.ResolutionScale)
            static readonly int s_scaled = Shader.PropertyToID("_GaussianSplatScaled");
            static readonly int s_screenSize = Shader.PropertyToID("_GaussianSplatScreenSize");
            static readonly int s_outputSize = Shader.PropertyToID("_GaussianSplatOutputSize");
            static readonly int s_srcDepth = Shader.PropertyToID("_GaussianSplatSrcDepth");
            static readonly int s_srcDepthSize = Shader.PropertyToID("_GaussianSplatSrcDepthSize");

            class PassData
            {
                internal UniversalCameraData CameraData;
                internal TextureHandle SourceTexture;
                internal TextureHandle SourceDepth;
                internal TextureHandle GaussianSplatRT;
                internal bool Scaled;
                internal TextureHandle LowResDepth;
                internal Vector4 ScreenSize, OutputSize, SrcDepthSize;
            }

            static int s_DiagPasses;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using var builder = renderGraph.AddUnsafePass(ProfilerTag, out PassData passData);

                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();

                RenderTextureDescriptor rtDesc = cameraData.cameraTargetDescriptor;
                rtDesc.depthBufferBits = 0;
                rtDesc.msaaSamples = 1;
                rtDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                var full = cameraData.cameraTargetDescriptor;
                // MuseXR: only Multi Pass (plain 2D targets) is handled; anything else keeps the original path.
                bool scaled = GaussianSplatSettings.IsScaled && full.dimension == TextureDimension.Tex2D;
                if (scaled)
                {
                    float s = GaussianSplatSettings.ResolutionScale;
                    rtDesc.width = Mathf.Max(1, Mathf.RoundToInt(full.width * s));
                    rtDesc.height = Mathf.Max(1, Mathf.RoundToInt(full.height * s));
                }
                var textureHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, rtDesc, GaussianSplatRTName, true,
                    scaled ? FilterMode.Bilinear : FilterMode.Point);
                // MuseXR diagnostic, render-neutral. CalcViewData sizes every splat from
                // XRSettings.eyeTextureWidth, but the splats are drawn into THIS target, whose
                // width is scaled by renderScale. If the two differ, every splat covers
                // (eyeW / rtW)^2 times the pixels it should. Logged on passes 1, 100, 1000.
                s_DiagPasses++;
                if (s_DiagPasses == 1 || s_DiagPasses == 100 || s_DiagPasses == 1000)
                    Debug.Log($"[SplatDiag] pass {s_DiagPasses}: splatRT {rtDesc.width}x{rtDesc.height} " +
                              $"eyeTexture {UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight} " +
                              $"camPixel {cameraData.camera.pixelWidth}x{cameraData.camera.pixelHeight} " +
                              $"renderScale {cameraData.renderScale} splatScale {(scaled ? GaussianSplatSettings.ResolutionScale : 1f)} xrEnabled {cameraData.xrRendering}");
                passData.Scaled = scaled;
                if (scaled)
                {
                    var depthDesc = rtDesc;
                    depthDesc.graphicsFormat = GraphicsFormat.None;
                    depthDesc.depthStencilFormat = full.depthStencilFormat != GraphicsFormat.None
                        ? full.depthStencilFormat : GraphicsFormat.D32_SFloat;
                    passData.LowResDepth = UniversalRenderer.CreateRenderGraphTexture(renderGraph, depthDesc, "_GaussianSplatLowResDepth", false);
                    builder.UseTexture(passData.LowResDepth, AccessFlags.Write);
                    // Same numbers CalcViewData sizes the splats with, so footprints stay in full-res units.
                    int eyeW = UnityEngine.XR.XRSettings.eyeTextureWidth, eyeH = UnityEngine.XR.XRSettings.eyeTextureHeight;
                    passData.ScreenSize = new Vector4(eyeW != 0 ? eyeW : cameraData.camera.pixelWidth,
                                                      eyeH != 0 ? eyeH : cameraData.camera.pixelHeight, 0, 0);
                    passData.OutputSize = new Vector4(full.width, full.height, 0, 0);
                    passData.SrcDepthSize = new Vector4(full.width, full.height, (float)full.width / rtDesc.width, 0);
                }

                passData.CameraData = cameraData;
                passData.SourceTexture = resourceData.activeColorTexture;
                passData.SourceDepth = resourceData.activeDepthTexture;
                passData.GaussianSplatRT = textureHandle;

                builder.UseTexture(resourceData.activeColorTexture, AccessFlags.ReadWrite);
                builder.UseTexture(resourceData.activeDepthTexture);
                builder.UseTexture(textureHandle, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    using var _ = new ProfilingScope(commandBuffer, s_profilingSampler);
                    commandBuffer.SetGlobalTexture(s_gaussianSplatRT, data.GaussianSplatRT);
                    commandBuffer.SetGlobalFloat(s_scaled, data.Scaled ? 1f : 0f);
                    Material depthMat = data.Scaled ? GaussianSplatRenderSystem.instance.CompositeMaterialForActiveSplats() : null;
                    if (depthMat != null)
                    {
                        commandBuffer.SetGlobalVector(s_screenSize, data.ScreenSize);
                        commandBuffer.SetGlobalVector(s_outputSize, data.OutputSize);
                        commandBuffer.SetGlobalVector(s_srcDepthSize, data.SrcDepthSize);
                        commandBuffer.SetGlobalTexture(s_srcDepth, data.SourceDepth);
                        CoreUtils.SetRenderTarget(commandBuffer, data.LowResDepth, ClearFlag.None);
                        commandBuffer.DrawProcedural(Matrix4x4.identity, depthMat, 1, MeshTopology.Triangles, 3, 1);
                        CoreUtils.SetRenderTarget(commandBuffer, data.GaussianSplatRT, data.LowResDepth, ClearFlag.Color, Color.clear);
                    }
                    else if (data.Scaled)
                    {
                        // No material to copy depth with (no active splat): an empty low-res depth
                        // keeps the targets the same size; nothing is drawn into it anyway.
                        commandBuffer.SetGlobalVector(s_screenSize, data.ScreenSize);
                        commandBuffer.SetGlobalVector(s_outputSize, data.OutputSize);
                        CoreUtils.SetRenderTarget(commandBuffer, data.GaussianSplatRT, data.LowResDepth, ClearFlag.All, Color.clear);
                    }
                    else
                    {
                        CoreUtils.SetRenderTarget(commandBuffer, data.GaussianSplatRT, data.SourceDepth, ClearFlag.Color, Color.clear);
                    }
                    Material matComposite = GaussianSplatRenderSystem.instance.SortAndRenderSplats(data.CameraData.camera, commandBuffer);
                    commandBuffer.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                    Blitter.BlitCameraTexture(commandBuffer, data.GaussianSplatRT, data.SourceTexture, matComposite, 0);
                    commandBuffer.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                });
            }
        }

        GSRenderPass m_Pass;
        bool m_HasCamera;

        public override void Create()
        {
            m_Pass = new GSRenderPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            m_HasCamera = false;
            var system = GaussianSplatRenderSystem.instance;
            if (!system.GatherSplatsForCamera(cameraData.camera))
                return;

            m_HasCamera = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!m_HasCamera)
                return;
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass = null;
        }
    }
}

#endif // #if GS_ENABLE_URP
