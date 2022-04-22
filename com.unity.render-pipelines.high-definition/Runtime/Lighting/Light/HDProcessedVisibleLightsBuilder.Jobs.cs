using System;
using UnityEngine.Jobs;
using Unity.Collections;
using Unity.Jobs;
using System.Threading;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine.Rendering.HighDefinition
{
    internal partial class HDProcessedVisibleLightsBuilder
    {
        JobHandle m_ProcessVisibleLightJobHandle;

#if ENABLE_BURST_1_5_0_OR_NEWER
        [Unity.Burst.BurstCompile]
#endif
        protected struct ProcessVisibleLightJob : IJobParallelFor
        {
            #region Light entity data
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<HDLightRenderData> lightData;
            #endregion

            #region Visible light data
            [ReadOnly]
            public NativeArray<VisibleLight> visibleLights;
            [ReadOnly]
            public NativeArray<VisibleLight> visibleOffscreenVertexLights;
            [ReadOnly]
            public NativeArray<int> visibleLightEntityDataIndices;
            [ReadOnly]
            public NativeArray<LightBakingOutput> visibleLightBakingOutput;
            [ReadOnly]
            public NativeArray<LightShadows> visibleLightShadows;
            [ReadOnly]
            public NativeArray<bool> visibleLightIsFromVisibleList; // GG: Instead of this array we could store the cutoff index such that any idx < cutoff is from the visible list
            #endregion

            #region Parameters
            [ReadOnly]
            public int totalLightCounts;
            [ReadOnly]
            public Vector3 cameraPosition;
            [ReadOnly]
            public int pixelCount;
            [ReadOnly]
            public bool enableAreaLights;
            [ReadOnly]
            public bool onlyDynamicGI;
            [ReadOnly]
            public bool dynamicGIUseRealtimeLights;
            [ReadOnly]
            public bool dynamicGIUseMixedLights;
#if UNITY_EDITOR
            [ReadOnly]
            public bool dynamicGIPreparingMixedLights;
#endif
            [ReadOnly]
            public bool evaluateShadowStates;
            [ReadOnly]
            public bool alwaysContributeToLightingWhenAffectsDynamicGI;
            [ReadOnly]
            public bool enableRayTracing;
            [ReadOnly]
            public bool showDirectionalLight;
            [ReadOnly]
            public bool showPunctualLight;
            [ReadOnly]
            public bool showAreaLight;
            [ReadOnly]
            public bool enableShadowMaps;
            [ReadOnly]
            public bool enableScreenSpaceShadows;
            [ReadOnly]
            public int maxDirectionalLightsOnScreen;
            [ReadOnly]
            public int maxPunctualLightsOnScreen;
            [ReadOnly]
            public int maxAreaLightsOnScreen;
            [ReadOnly]
            public DebugLightFilterMode debugFilterMode;
            #endregion

            #region output processed lights
            [WriteOnly]
            public NativeArray<int> processedVisibleLightCountsPtr;
            [WriteOnly]
            public NativeArray<LightVolumeType> processedLightVolumeType;
            [WriteOnly]
            public NativeArray<HDProcessedVisibleLight> processedEntities;
            [WriteOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<uint> sortKeys;
            [WriteOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<int> shadowLightsDataIndices;
            #endregion

            private bool TrivialRejectLight(in VisibleLight light, int dataIndex, bool isFromVisibleList)
            {
                if (dataIndex < 0)
                    return true;

                // We can skip the processing of lights that are so small to not affect at least a pixel on screen.
                // TODO: The minimum pixel size on screen should really be exposed as parameter, to allow small lights to be culled to user's taste.
                const int minimumPixelAreaOnScreen = 1;
                if (isFromVisibleList && (light.screenRect.height * light.screenRect.width * pixelCount) < minimumPixelAreaOnScreen)
                    return true;

                return false;
            }

            private int IncrementCounter(HDProcessedVisibleLightsBuilder.ProcessLightsCountSlots counterSlot)
            {
                int outputIndex = 0;
                unsafe
                {
                    int* ptr = (int*)processedVisibleLightCountsPtr.GetUnsafePtr<int>() + (int)counterSlot;
                    outputIndex = Interlocked.Increment(ref UnsafeUtility.AsRef<int>(ptr));
                }
                return outputIndex;
            }

            private int DecrementCounter(HDProcessedVisibleLightsBuilder.ProcessLightsCountSlots counterSlot)
            {
                int outputIndex = 0;
                unsafe
                {
                    int* ptr = (int*)processedVisibleLightCountsPtr.GetUnsafePtr<int>() + (int)counterSlot;
                    outputIndex = Interlocked.Decrement(ref UnsafeUtility.AsRef<int>(ptr));
                }
                return outputIndex;
            }

            private int NextOutputIndex() => IncrementCounter(HDProcessedVisibleLightsBuilder.ProcessLightsCountSlots.ProcessedLights) - 1;

            private bool IncrementLightCounterAndTestLimit(LightCategory lightCategory, GPULightType gpuLightType)
            {
                // Do NOT process lights beyond the specified limit!
                switch (lightCategory)
                {
                    case LightCategory.Punctual:
                        if (gpuLightType == GPULightType.Directional) // Our directional lights are "punctual"...
                        {
                            var directionalLightcount = IncrementCounter(HDProcessedVisibleLightsBuilder.ProcessLightsCountSlots.DirectionalLights) - 1;
                            if (!showDirectionalLight || directionalLightcount >= maxDirectionalLightsOnScreen)
                            {
                                DecrementCounter(HDProcessedVisibleLightsBuilder.ProcessLightsCountSlots.DirectionalLights);
                                return false;
                            }
                            break;
                        }
                        var punctualLightcount = IncrementCounter(HDProcessedVisibleLightsBuilder.ProcessLightsCountSlots.PunctualLights) - 1;
                        if (!showPunctualLight || punctualLightcount >= maxPunctualLightsOnScreen)
                        {
                            DecrementCounter(HDProcessedVisibleLightsBuilder.ProcessLightsCountSlots.PunctualLights);
                            return false;
                        }
                        break;
                    case LightCategory.Area:
                        var areaLightCount = IncrementCounter(HDProcessedVisibleLightsBuilder.ProcessLightsCountSlots.AreaLightCounts) - 1;
                        if (!showAreaLight || areaLightCount >= maxAreaLightsOnScreen)
                        {
                            DecrementCounter(HDProcessedVisibleLightsBuilder.ProcessLightsCountSlots.AreaLightCounts);
                            return false;
                        }
                        break;
                    default:
                        break;
                }

                return true;
            }

            private HDProcessedVisibleLightsBuilder.ShadowMapFlags EvaluateShadowState(
                LightShadows shadows,
                HDLightType lightType,
                GPULightType gpuLightType,
                AreaLightShape areaLightShape,
                bool useScreenSpaceShadowsVal,
                bool useRayTracingShadowsVal,
                float shadowDimmerVal,
                float shadowFadeDistanceVal,
                float distanceToCamera,
                LightVolumeType lightVolumeType)
            {
                var flags = HDProcessedVisibleLightsBuilder.ShadowMapFlags.None;
                bool willRenderShadowMap = shadows != LightShadows.None && enableShadowMaps;
                if (!willRenderShadowMap)
                    return flags;

                // When creating a new light, at the first frame, there is no AdditionalShadowData so we can't really render shadows
                if (shadowDimmerVal <= 0)
                    return flags;

                // If the shadow is too far away, we don't render it
                bool isShadowInRange = lightType == HDLightType.Directional || distanceToCamera < shadowFadeDistanceVal;
#if UNITY_EDITOR
                isShadowInRange |= dynamicGIPreparingMixedLights;
#endif
                if (!isShadowInRange)
                    return flags;

                if (lightType == HDLightType.Area && areaLightShape != AreaLightShape.Rectangle)
                    return flags;

                // First we reset the ray tracing and screen space shadow data
                flags |= HDProcessedVisibleLightsBuilder.ShadowMapFlags.WillRenderShadowMap;

                // If this camera does not allow screen space shadows we are done, set the target parameters to false and leave the function
                if (!enableScreenSpaceShadows)
                    return flags;

                // Flag the ray tracing only shadows
                if (enableRayTracing && useRayTracingShadowsVal)
                {
                    bool validShadow = false;
                    if (gpuLightType == GPULightType.Point
                        || gpuLightType == GPULightType.Rectangle
                        || (gpuLightType == GPULightType.Spot && lightVolumeType == LightVolumeType.Cone))
                        validShadow = true;

                    if (validShadow)
                        flags |= HDProcessedVisibleLightsBuilder.ShadowMapFlags.WillRenderScreenSpaceShadow
                            | HDProcessedVisibleLightsBuilder.ShadowMapFlags.WillRenderRayTracedShadow;
                }

                // Flag the directional shadow
                if (useScreenSpaceShadowsVal && gpuLightType == GPULightType.Directional)
                {
                    flags |= HDProcessedVisibleLightsBuilder.ShadowMapFlags.WillRenderScreenSpaceShadow;
                    if (enableRayTracing && useRayTracingShadowsVal)
                        flags |= HDProcessedVisibleLightsBuilder.ShadowMapFlags.WillRenderRayTracedShadow;
                }

                return flags;
            }

            private ref HDLightRenderData GetLightData(int dataIndex)
            {
#if DEBUG
                if (dataIndex < 0 || dataIndex >= totalLightCounts)
                    throw new Exception("Trying to access a light from the DB out of bounds. The index requested is: " + dataIndex + " and the length is " + totalLightCounts);
#endif
                unsafe
                {
                    HDLightRenderData* data = (HDLightRenderData*)lightData.GetUnsafePtr<HDLightRenderData>() + dataIndex;
                    return ref UnsafeUtility.AsRef<HDLightRenderData>(data);
                }
            }

            public void Execute(int index)
            {
                bool isFromVisibleList = visibleLightIsFromVisibleList[index];
                VisibleLight visibleLight = isFromVisibleList ? visibleLights[index] : visibleOffscreenVertexLights[index - visibleLights.Length];
                int dataIndex = visibleLightEntityDataIndices[index];
                LightBakingOutput bakingOutput = visibleLightBakingOutput[index];
                LightShadows shadows = visibleLightShadows[index];
                if (TrivialRejectLight(visibleLight, dataIndex, isFromVisibleList))
                    return;

                ref HDLightRenderData lightRenderData = ref GetLightData(dataIndex);

                if (onlyDynamicGI)
                {
                    if (!lightRenderData.affectDynamicGI)
                        return;

                    if (lightRenderData.mixedDynamicGI)
                    {
                        if (!dynamicGIUseMixedLights)
                            return;
                    }
                    else
                    {
                        if (!dynamicGIUseRealtimeLights)
                            return;
                    }
                }

                if (enableRayTracing && !lightRenderData.includeForRayTracing)
                    return;

                Vector3 lightPosition = visibleLight.GetPosition();
                float distanceToCamera = (lightPosition - cameraPosition).magnitude;
                var lightType = HDAdditionalLightData.TranslateLightType(visibleLight.lightType, lightRenderData.pointLightType);
                var lightCategory = LightCategory.Count;
                var gpuLightType = GPULightType.Point;
                var areaLightShape = lightRenderData.areaLightShape;

                if (!enableAreaLights && (lightType == HDLightType.Area && (areaLightShape == AreaLightShape.Rectangle || areaLightShape == AreaLightShape.Tube)))
                    return;

                var spotLightShape = lightRenderData.spotLightShape;
                var lightVolumeType = LightVolumeType.Count;
                var isBakedShadowMaskLight =
                    bakingOutput.lightmapBakeType == LightmapBakeType.Mixed &&
                    bakingOutput.mixedLightingMode == MixedLightingMode.Shadowmask &&
                    bakingOutput.occlusionMaskChannel != -1;    // We need to have an occlusion mask channel assign, else we have no shadow mask
                HDRenderPipeline.EvaluateGPULightType(lightType, spotLightShape, areaLightShape,
                    ref lightCategory, ref gpuLightType, ref lightVolumeType);

                if (debugFilterMode != DebugLightFilterMode.None && debugFilterMode.IsEnabledFor(gpuLightType, spotLightShape))
                    return;

                float lightDistanceFade;
#if UNITY_EDITOR
                if (dynamicGIPreparingMixedLights)
                {
                    lightDistanceFade = 1.0f;
                }
                else
#endif
                {
                    lightDistanceFade = gpuLightType == GPULightType.Directional ? 1.0f : HDUtils.ComputeLinearDistanceFade(distanceToCamera, lightRenderData.fadeDistance);
                }
                float volumetricDistanceFade = gpuLightType == GPULightType.Directional ? 1.0f : HDUtils.ComputeLinearDistanceFade(distanceToCamera, lightRenderData.volumetricFadeDistance);

                bool contributesToLighting = ((lightRenderData.lightDimmer > 0) && (lightRenderData.affectDiffuse || lightRenderData.affectSpecular)) || ((lightRenderData.affectVolumetric ? lightRenderData.volumetricDimmer : 0.0f) > 0);
                contributesToLighting = contributesToLighting || (alwaysContributeToLightingWhenAffectsDynamicGI && lightRenderData.affectDynamicGI);
                contributesToLighting = contributesToLighting && (lightDistanceFade > 0);

                var shadowMapFlags = HDProcessedVisibleLightsBuilder.ShadowMapFlags.None;
                if (evaluateShadowStates && isFromVisibleList)
                {
                    shadowMapFlags = EvaluateShadowState(
                        shadows, lightType, gpuLightType, areaLightShape,
                        lightRenderData.useScreenSpaceShadows, lightRenderData.useRayTracedShadows,
                        lightRenderData.shadowDimmer, lightRenderData.shadowFadeDistance, distanceToCamera, lightVolumeType);
                }

                if (!contributesToLighting)
                    return;

                if (!IncrementLightCounterAndTestLimit(lightCategory, gpuLightType))
                    return;

                int outputIndex = NextOutputIndex();
#if DEBUG
                if (outputIndex < 0 || outputIndex >= sortKeys.Length)
                    throw new Exception("Trying to access an output index out of bounds. Output index is " + outputIndex + "and max length is " + sortKeys.Length);
#endif
                sortKeys[outputIndex] = HDGpuLightsBuilder.PackLightSortKey(lightCategory, gpuLightType, lightVolumeType, index);

                processedLightVolumeType[index] = lightVolumeType;
                processedEntities[index] = new HDProcessedVisibleLight()
                {
                    dataIndex = dataIndex,
                    gpuLightType = gpuLightType,
                    lightType = lightType,
                    lightDistanceFade = lightDistanceFade,
                    lightVolumetricDistanceFade = volumetricDistanceFade,
                    distanceToCamera = distanceToCamera,
                    shadowMapFlags = shadowMapFlags,
                    isBakedShadowMask = isBakedShadowMaskLight
                };

                if (isBakedShadowMaskLight)
                    IncrementCounter(HDProcessedVisibleLightsBuilder.ProcessLightsCountSlots.BakedShadows);

                if ((shadowMapFlags & HDProcessedVisibleLightsBuilder.ShadowMapFlags.WillRenderShadowMap) != 0)
                {
                    int shadowOutputIndex = IncrementCounter(HDProcessedVisibleLightsBuilder.ProcessLightsCountSlots.ShadowLights) - 1;
                    shadowLightsDataIndices[shadowOutputIndex] = index;
                }
            }
        }

        public void StartProcessVisibleLightJob(
            HDCamera hdCamera,
            NativeArray<VisibleLight> visibleLights,
            NativeArray<VisibleLight> visibleOffscreenVertexLights,
            in GlobalLightLoopSettings lightLoopSettings,
            DebugDisplaySettings debugDisplaySettings)
        {
            if (m_Size == 0)
                return;

            var lightEntityCollection = HDLightRenderDatabase.instance;
            var processVisibleLightJob = new ProcessVisibleLightJob()
            {
                //Parameters.
                totalLightCounts = lightEntityCollection.lightCount,
                cameraPosition = hdCamera.camera.transform.position,
                pixelCount = hdCamera.actualWidth * hdCamera.actualHeight,
                enableAreaLights = ShaderConfig.s_AreaLights != 0,
                enableRayTracing = hdCamera.frameSettings.IsEnabled(FrameSettingsField.RayTracing),
                showDirectionalLight = debugDisplaySettings.data.lightingDebugSettings.showDirectionalLight,
                showPunctualLight = debugDisplaySettings.data.lightingDebugSettings.showPunctualLight,
                showAreaLight = debugDisplaySettings.data.lightingDebugSettings.showAreaLight,
                enableShadowMaps = hdCamera.frameSettings.IsEnabled(FrameSettingsField.ShadowMaps),
                enableScreenSpaceShadows = hdCamera.frameSettings.IsEnabled(FrameSettingsField.ScreenSpaceShadows),
                maxDirectionalLightsOnScreen = lightLoopSettings.maxDirectionalLightsOnScreen,
                maxPunctualLightsOnScreen = lightLoopSettings.maxPunctualLightsOnScreen,
                maxAreaLightsOnScreen = lightLoopSettings.maxAreaLightsOnScreen,
                debugFilterMode = debugDisplaySettings.GetDebugLightFilterMode(),

#if UNITY_EDITOR
                dynamicGIPreparingMixedLights = ProbeVolume.preparingMixedLights,
#endif

                //render light entities.
                lightData = lightEntityCollection.lightData,

                //data of all visible light entities.
                visibleLights = visibleLights,
                visibleOffscreenVertexLights = visibleOffscreenVertexLights,
                visibleLightEntityDataIndices = m_VisibleLightEntityDataIndices,
                visibleLightBakingOutput = m_VisibleLightBakingOutput,
                visibleLightShadows = m_VisibleLightShadows,
                visibleLightIsFromVisibleList = m_VisibleLightIsFromVisibleList,

                //Output processed lights.
                processedVisibleLightCountsPtr = m_ProcessVisibleLightCounts,
                processedLightVolumeType = m_ProcessedLightVolumeType,
                processedEntities = m_ProcessedEntities,
                sortKeys = m_SortKeys,
                shadowLightsDataIndices = m_ShadowLightsDataIndices
            };
            OverrideProcessVisibleLightJobParameters(hdCamera, ref processVisibleLightJob);

            m_ProcessVisibleLightJobHandle = processVisibleLightJob.Schedule(m_Size, 32);
        }

        public void CompleteProcessVisibleLightJob()
        {
            if (m_Size == 0)
                return;

            m_ProcessVisibleLightJobHandle.Complete();
        }

        protected abstract void OverrideProcessVisibleLightJobParameters(HDCamera hdCamera, ref ProcessVisibleLightJob job);
    }

    internal partial class HDProcessedVisibleLightsRegularBuilder : HDProcessedVisibleLightsBuilder
    {
        protected override void OverrideProcessVisibleLightJobParameters(HDCamera hdCamera, ref ProcessVisibleLightJob job)
        {
            job.onlyDynamicGI = false;
            job.alwaysContributeToLightingWhenAffectsDynamicGI = false;
            job.evaluateShadowStates = true;
        }
    }

    internal partial class HDProcessedVisibleLightsDynamicBuilder : HDProcessedVisibleLightsBuilder
    { 
        protected override void OverrideProcessVisibleLightJobParameters(HDCamera hdCamera, ref ProcessVisibleLightJob job)
        {
            job.onlyDynamicGI = true;
            job.alwaysContributeToLightingWhenAffectsDynamicGI = true;
            job.evaluateShadowStates = false;

#if UNITY_EDITOR
            if (ProbeVolume.preparingMixedLights)
            {
                job.dynamicGIUseRealtimeLights = false;
                job.dynamicGIUseMixedLights = true;
            }
            else
#endif
            {
                var dynamicGIMixedLightMode = hdCamera.frameSettings.probeVolumeDynamicGIMixedLightMode;
                job.dynamicGIUseRealtimeLights = dynamicGIMixedLightMode != ProbeVolumeDynamicGIMixedLightMode.MixedOnly;
                job.dynamicGIUseMixedLights = dynamicGIMixedLightMode == ProbeVolumeDynamicGIMixedLightMode.ForceRealtime;
            }
        }
    }
}
