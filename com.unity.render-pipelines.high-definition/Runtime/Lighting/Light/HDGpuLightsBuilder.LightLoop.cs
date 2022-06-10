using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine.Rendering.HighDefinition
{
    internal partial class HDGpuLightsBuilder
    {
        #region internal HDRP API

        //Preallocates number of lights for bounds arrays and resets all internal counters. Must be called once per frame per view always.
        public void NewFrame(HDCamera hdCamera, int maxLightCount)
        {
            int viewCounts = hdCamera.viewCount;
            if (viewCounts > m_LighsPerViewCapacity)
            {
                m_LighsPerViewCapacity = viewCounts;
                m_LightsPerView.ResizeArray(m_LighsPerViewCapacity);
            }

            m_LightsPerViewCount = viewCounts;

            int totalBoundsCount = maxLightCount * viewCounts;
            int requestedBoundsCount = Math.Max(totalBoundsCount, 1);
            if (requestedBoundsCount > m_LightBoundsCapacity)
            {
                m_LightBoundsCapacity = Math.Max(Math.Max(m_LightBoundsCapacity * 2, requestedBoundsCount), ArrayCapacity);
                m_LightBounds.ResizeArray(m_LightBoundsCapacity);
                m_LightVolumes.ResizeArray(m_LightBoundsCapacity);
            }
            m_LightBoundsCount = totalBoundsCount;

            m_BoundsEyeDataOffset = maxLightCount;

            for (int viewId = 0; viewId < viewCounts; ++viewId)
            {
                m_LightsPerView[viewId] = new LightsPerView()
                {
                    worldToView = HDRenderPipeline.GetWorldToViewMatrix(hdCamera, viewId),
                    boundsOffset = viewId * m_BoundsEyeDataOffset,
                    boundsCount = 0
                };
            }

            int numLightTypes = Enum.GetValues(typeof(GPULightTypeCountSlots)).Length;
            if (!m_LightTypeCounters.IsCreated)
                m_LightTypeCounters.ResizeArray(numLightTypes);
            if (!m_DGILightTypeCounters.IsCreated)
                m_DGILightTypeCounters.ResizeArray(numLightTypes);

            m_LightCount = 0;
            m_DirectionalLightCount = 0;
            m_DGILightCount = 0;
            m_LightsBuffer = null;
            m_DirectionalLightsBuffer = null;
            m_DGILightsBuffer = null;
            m_ContactShadowIndex = 0;
            m_ScreenSpaceShadowIndex = 0;
            m_ScreenSpaceShadowChannelSlot = 0;
            m_ScreenSpaceShadowsUnion.Clear();

            m_CurrentShadowSortedSunLightIndex = -1;
            m_CurrentSunLightAdditionalLightData = null;
            m_CurrentSunShadowMapFlags = HDProcessedVisibleLightsBuilder.ShadowMapFlags.None;

            m_DebugSelectedLightShadowIndex = -1;
            m_DebugSelectedLightShadowCount = 0;

            for (int i = 0; i < m_Asset.currentPlatformRenderPipelineSettings.hdShadowInitParams.maxScreenSpaceShadowSlots; ++i)
            {
                m_CurrentScreenSpaceShadowData[i].additionalLightData = null;
                m_CurrentScreenSpaceShadowData[i].lightDataIndex = -1;
                m_CurrentScreenSpaceShadowData[i].valid = false;
            }

            for (int i = 0; i < numLightTypes; ++i)
            {
                m_LightTypeCounters[i] = 0;
                m_DGILightTypeCounters[i] = 0;
            }
        }

        //Builds the GPU light list.
        public void Build(
            CommandBuffer cmd,
            HDCamera hdCamera,
            in CullingResults cullingResult,
            HDProcessedVisibleLightsBuilder visibleLights,
            HDLightRenderDatabase lightEntities,
            in HDShadowInitParameters shadowInitParams,
            DebugDisplaySettings debugDisplaySettings,
            HDRenderPipeline.HierarchicalVarianceScreenSpaceShadowsData hierarchicalVarianceScreenSpaceShadowsData,
            bool processVisibleLights,
            bool processDynamicGI)
        {
            int totalVisibleLightsCount = processVisibleLights ? visibleLights.sortedLightCounts : 0;
            int visibleLightsCount = processVisibleLights ? visibleLights.sortedNonDirectionalLightCounts : 0;
            int visibleDirectionalCount = processVisibleLights ? visibleLights.sortedDirectionalLightCounts : 0;

            int dgiLightsCount = processDynamicGI ? visibleLights.sortedDGILightCounts : 0;

            AllocateLightData(visibleLightsCount, visibleDirectionalCount, dgiLightsCount);

            using var lightHandle = m_LightsData.BeginWrite(m_LightCount);
            using var directionalLightHandle = m_DirectionalLightsData.BeginWrite(m_DirectionalLightCount);
            using var dgiHandle = m_DGILightsData.BeginWrite(dgiLightsCount);

            // TODO: Refactor shadow management
            // The good way of managing shadow:
            // Here we sort everyone and we decide which light is important or not (this is the responsibility of the lightloop)
            // we allocate shadow slot based on maximum shadow allowed on screen and attribute slot by bigger solid angle
            // THEN we ask to the ShadowRender to render the shadow, not the reverse as it is today (i.e render shadow than expect they
            // will be use...)
            // The lightLoop is in charge, not the shadow pass.
            // For now we will still apply the maximum of shadow here but we don't apply the sorting by priority + slot allocation yet

            if (totalVisibleLightsCount > 0 || dgiLightsCount > 0)
            {
                for (int viewId = 0; viewId < hdCamera.viewCount; ++viewId)
                {
                    var viewInfo = m_LightsPerView[viewId];
                    viewInfo.boundsCount += lightsCount;
                    m_LightsPerView[viewId] = viewInfo;
                }

                var hdShadowSettings = hdCamera.volumeStack.GetComponent<HDShadowSettings>();
                StartCreateGpuLightDataJob(hdCamera, cullingResult, hdShadowSettings, visibleLights, lightEntities, lightHandle.Data, directionalLightHandle.Data, dgiHandle.Data);
                CompleteGpuLightDataJob();
                CalculateAllLightDataTextureInfo(
                    cmd, hdCamera,
                    cullingResult, visibleLights, lightEntities,
                    hdShadowSettings, shadowInitParams, debugDisplaySettings,
                    lightHandle.Data,
                    directionalLightHandle.Data,
                    dgiHandle.Data,
                    hierarchicalVarianceScreenSpaceShadowsData);
            }

            m_LightsBuffer = lightHandle.EndWrite(m_LightCount);
            m_DirectionalLightsBuffer = directionalLightHandle.EndWrite(m_DirectionalLightCount);
            m_DGILightsBuffer = dgiHandle.EndWrite(m_DGILightCount);

            //Sanity check
            Debug.Assert(m_DirectionalLightCount == visibleDirectionalCount, "Mismatch in Directional gpu lights processed. Lights should not be culled in this loop.");
            Debug.Assert(m_LightCount == areaLightCount + punctualLightCount, "Mismatch in Area and Punctual gpu Visible lights processed. Lights should not be culled in this loop.");
            Debug.Assert(m_DGILightCount == dgiAreaLightCount + dgiPunctualLightCount, "Mismatch in Area and Punctual gpu Dynamic GI lights processed. Lights should not be culled in this loop.");
        }

        //Calculates a shadow type for a light and sets the shadow index information into the LightData.
        public void ProcessLightDataShadowIndex(
            CommandBuffer cmd,
            in HDShadowInitParameters shadowInitParams,
            HDLightType lightType,
            Light lightComponent,
            HDAdditionalLightData additionalLightData,
            int shadowIndex,
            ref LightDataCpuSubset cpuLightData,
            ref LightData gpuLightData)
        {
            if (cpuLightData.lightType == GPULightType.ProjectorBox && shadowIndex >= 0)
            {
                // We subtract a bit from the safe extent depending on shadow resolution
                float shadowRes = additionalLightData.shadowResolution.Value(shadowInitParams.shadowResolutionPunctual);
                shadowRes = Mathf.Clamp(shadowRes, 128.0f, 2048.0f); // Clamp in a somewhat plausible range.
                // The idea is to subtract as much as 0.05 for small resolutions.
                float shadowResFactor = Mathf.Lerp(0.05f, 0.01f, Mathf.Max(shadowRes / 2048.0f, 0.0f));
                gpuLightData.boxLightSafeExtent = 1.0f - shadowResFactor;
            }

            if (lightComponent != null &&
                (
                    (lightType == HDLightType.Spot && (lightComponent.cookie != null || additionalLightData.IESPoint != null)) ||
                    ((lightType == HDLightType.Area && cpuLightData.lightType == GPULightType.Rectangle) && (lightComponent.cookie != null || additionalLightData.IESSpot != null)) ||
                    (lightType == HDLightType.Point && (lightComponent.cookie != null || additionalLightData.IESPoint != null))
                )
            )
            {
                switch (lightType)
                {
                    case HDLightType.Spot:
                        gpuLightData.cookieMode = (lightComponent.cookie?.wrapMode == TextureWrapMode.Repeat) ? CookieMode.Repeat : CookieMode.Clamp;
                        if (additionalLightData.IESSpot != null && lightComponent.cookie != null && additionalLightData.IESSpot != lightComponent.cookie)
                            gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.Fetch2DCookie(cmd, lightComponent.cookie, additionalLightData.IESSpot);
                        else if (lightComponent.cookie != null)
                            gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.Fetch2DCookie(cmd, lightComponent.cookie);
                        else if (additionalLightData.IESSpot != null)
                            gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.Fetch2DCookie(cmd, additionalLightData.IESSpot);
                        else
                            gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.Fetch2DCookie(cmd, Texture2D.whiteTexture);
                        break;
                    case HDLightType.Point:
                        gpuLightData.cookieMode = CookieMode.Repeat;
                        if (additionalLightData.IESPoint != null && lightComponent.cookie != null && additionalLightData.IESPoint != lightComponent.cookie)
                            gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.FetchCubeCookie(cmd, lightComponent.cookie, additionalLightData.IESPoint);
                        else if (lightComponent.cookie != null)
                            gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.FetchCubeCookie(cmd, lightComponent.cookie);
                        else if (additionalLightData.IESPoint != null)
                            gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.FetchCubeCookie(cmd, additionalLightData.IESPoint);
                        break;
                    case HDLightType.Area:
                        gpuLightData.cookieMode = CookieMode.Clamp;
                        if (additionalLightData.areaLightCookie != null && additionalLightData.IESSpot != null && additionalLightData.areaLightCookie != additionalLightData.IESSpot)
                            gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.FetchAreaCookie(cmd, additionalLightData.areaLightCookie, additionalLightData.IESSpot);
                        else if (additionalLightData.IESSpot != null)
                            gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.FetchAreaCookie(cmd, additionalLightData.IESSpot);
                        else if (additionalLightData.areaLightCookie != null)
                            gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.FetchAreaCookie(cmd, additionalLightData.areaLightCookie);
                        break;
                }
            }
            else if (lightType == HDLightType.Spot && additionalLightData.spotLightShape != SpotLightShape.Cone)
            {
                // Projectors lights must always have a cookie texture.
                // As long as the cache is a texture array and not an atlas, the 4x4 white texture will be rescaled to 128
                gpuLightData.cookieMode = CookieMode.Clamp;
                gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.Fetch2DCookie(cmd, Texture2D.whiteTexture);
            }
            else if (cpuLightData.lightType == GPULightType.Rectangle)
            {
                if (additionalLightData.areaLightCookie != null || additionalLightData.IESPoint != null)
                {
                    gpuLightData.cookieMode = CookieMode.Clamp;
                    if (additionalLightData.areaLightCookie != null && additionalLightData.IESSpot != null && additionalLightData.areaLightCookie != additionalLightData.IESSpot)
                        gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.FetchAreaCookie(cmd, additionalLightData.areaLightCookie, additionalLightData.IESSpot);
                    else if (additionalLightData.IESSpot != null)
                        gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.FetchAreaCookie(cmd, additionalLightData.IESSpot);
                    else if (additionalLightData.areaLightCookie != null)
                        gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.FetchAreaCookie(cmd, additionalLightData.areaLightCookie);
                }
            }

            gpuLightData.shadowIndex = shadowIndex;
            additionalLightData.shadowIndex = shadowIndex;
        }


        #endregion


        // The first rendered 24 lights that have contact shadow enabled have a mask used to select the bit that contains
        // the contact shadow shadowed information (occluded or not). Otherwise -1 is written
        private void GetContactShadowMask(HDAdditionalLightData hdAdditionalLightData, BoolScalableSetting contactShadowEnabled, HDCamera hdCamera, ref int contactShadowMask, ref float rayTracingShadowFlag)
        {
            contactShadowMask = 0;
            rayTracingShadowFlag = 0.0f;
            // If contact shadows are not enabled or we already reached the manimal number of contact shadows
            // or this is not rasterization
            if ((!hdAdditionalLightData.useContactShadow.Value(contactShadowEnabled))
                || m_ContactShadowIndex >= LightDefinitions.s_LightListMaxPrunedEntries)
                return;

            // Evaluate the contact shadow index of this light
            contactShadowMask = 1 << m_ContactShadowIndex++;

            // If this light has ray traced contact shadow
            if (hdCamera.frameSettings.IsEnabled(FrameSettingsField.RayTracing) && hdAdditionalLightData.rayTraceContactShadow)
                rayTracingShadowFlag = 1.0f;
        }

        private bool EnoughScreenSpaceShadowSlots(GPULightType gpuLightType, int screenSpaceChannelSlot)
        {
            if (gpuLightType == GPULightType.Rectangle)
            {
                // Area lights require two shadow slots
                return (screenSpaceChannelSlot + 1) < m_Asset.currentPlatformRenderPipelineSettings.hdShadowInitParams.maxScreenSpaceShadowSlots;
            }
            else
            {
                return screenSpaceChannelSlot < m_Asset.currentPlatformRenderPipelineSettings.hdShadowInitParams.maxScreenSpaceShadowSlots;
            }
        }

        private void CalculateDirectionalLightDataTextureInfo(
            ref DirectionalLightDataCpuSubset cpuLightData, ref DirectionalLightData gpuLightData,
            CommandBuffer cmd, in VisibleLight light, in Light lightComponent, in HDAdditionalLightData additionalLightData,
            HDCamera hdCamera, HDProcessedVisibleLightsBuilder.ShadowMapFlags shadowFlags, int lightDataIndex, int shadowIndex)
        {
            if (shadowIndex != -1)
            {
                if ((shadowFlags & HDProcessedVisibleLightsBuilder.ShadowMapFlags.WillRenderScreenSpaceShadow) != 0)
                {
                    int screenSpaceShadowIndex = m_ScreenSpaceShadowChannelSlot;
                    bool willRenderRtShadows = (shadowFlags & HDProcessedVisibleLightsBuilder.ShadowMapFlags.WillRenderRayTracedShadow) != 0;
                    if (additionalLightData.colorShadow && willRenderRtShadows)
                    {
                        m_ScreenSpaceShadowChannelSlot += 3;
                        screenSpaceShadowIndex |= (int)LightDefinitions.s_ScreenSpaceColorShadowFlag;
                    }
                    else
                    {
                        m_ScreenSpaceShadowChannelSlot++;
                    }

                    // Raise the ray tracing flag in case the light is ray traced
                    if (willRenderRtShadows)
                        screenSpaceShadowIndex |= (int)LightDefinitions.s_RayTracedScreenSpaceShadowFlag;

                    cpuLightData.screenSpaceShadowIndex = gpuLightData.screenSpaceShadowIndex = screenSpaceShadowIndex;

                    m_ScreenSpaceShadowChannelSlot++;
                    m_ScreenSpaceShadowsUnion.Add(additionalLightData);
                }
                m_CurrentSunLightAdditionalLightData = additionalLightData;
                m_CurrentSunLightDirectionalLightData = cpuLightData;
                m_CurrentShadowSortedSunLightIndex = lightDataIndex;
                m_CurrentSunShadowMapFlags = shadowFlags;
            }

            if (lightComponent != null && lightComponent.cookie != null)
            {
                gpuLightData.cookieMode = lightComponent.cookie.wrapMode == TextureWrapMode.Repeat ? CookieMode.Repeat : CookieMode.Clamp;
                gpuLightData.cookieScaleOffset = m_TextureCaches.lightCookieManager.Fetch2DCookie(cmd, lightComponent.cookie);
            }
            else
            {
                gpuLightData.cookieMode = CookieMode.None;
            }

            if (additionalLightData.surfaceTexture == null)
            {
                gpuLightData.surfaceTextureScaleOffset = Vector4.zero;
            }
            else
            {
                gpuLightData.surfaceTextureScaleOffset = m_TextureCaches.lightCookieManager.Fetch2DCookie(cmd, additionalLightData.surfaceTexture);
            }

            GetContactShadowMask(additionalLightData, HDAdditionalLightData.ScalableSettings.UseContactShadow(m_Asset), hdCamera, ref gpuLightData.contactShadowMask, ref gpuLightData.isRayTracedContactShadow);

            gpuLightData.shadowIndex = shadowIndex;
        }

        private void CalculateLightDataTextureInfo(
            ref LightDataCpuSubset cpuLightData, ref LightData gpuLightData,
            CommandBuffer cmd, in Light lightComponent, HDAdditionalLightData additionalLightData, in HDShadowInitParameters shadowInitParams,
            in HDCamera hdCamera, BoolScalableSetting contactShadowScalableSetting,
            HDLightType lightType, HDProcessedVisibleLightsBuilder.ShadowMapFlags shadowFlags, bool rayTracingEnabled, int lightDataIndex, int shadowIndex, GPULightType gpuLightType, HDRenderPipeline.HierarchicalVarianceScreenSpaceShadowsData hierarchicalVarianceScreenSpaceShadowsData)
        {
            ProcessLightDataShadowIndex(
                cmd,
                shadowInitParams,
                lightType,
                lightComponent,
                additionalLightData,
                shadowIndex,
                ref cpuLightData,
                ref gpuLightData);

            GetContactShadowMask(additionalLightData, contactShadowScalableSetting, hdCamera, ref gpuLightData.contactShadowMask, ref gpuLightData.isRayTracedContactShadow);

            // If there is still a free slot in the screen space shadow array and this needs to render a screen space shadow
            if (rayTracingEnabled
                && EnoughScreenSpaceShadowSlots(cpuLightData.lightType, m_ScreenSpaceShadowChannelSlot)
                && (shadowFlags & HDProcessedVisibleLightsBuilder.ShadowMapFlags.WillRenderScreenSpaceShadow) != 0)
            {
                if (cpuLightData.lightType == GPULightType.Rectangle)
                {
                    // Rectangle area lights require 2 consecutive slots.
                    // Meaning if (screenSpaceChannelSlot % 4 ==3), we'll need to skip a slot
                    // so that the area shadow gets the first two slots of the next following texture
                    if (m_ScreenSpaceShadowChannelSlot % 4 == 3)
                    {
                        m_ScreenSpaceShadowChannelSlot++;
                    }
                }

                // Bind the next available slot to the light
               cpuLightData.screenSpaceShadowIndex = gpuLightData.screenSpaceShadowIndex = m_ScreenSpaceShadowChannelSlot;

                // Keep track of the screen space shadow data
                m_CurrentScreenSpaceShadowData[m_ScreenSpaceShadowIndex].additionalLightData = additionalLightData;
                m_CurrentScreenSpaceShadowData[m_ScreenSpaceShadowIndex].lightDataIndex = lightDataIndex;
                m_CurrentScreenSpaceShadowData[m_ScreenSpaceShadowIndex].valid = true;
                m_ScreenSpaceShadowsUnion.Add(additionalLightData);

                // increment the number of screen space shadows
                m_ScreenSpaceShadowIndex++;

                // Based on the light type, increment the slot usage
                if (cpuLightData.lightType == GPULightType.Rectangle)
                    m_ScreenSpaceShadowChannelSlot += 2;
                else
                    m_ScreenSpaceShadowChannelSlot++;
            }

            gpuLightData.hierarchicalVarianceScreenSpaceShadowsIndex = -1;
            if ((gpuLightType == GPULightType.Point) || (gpuLightType == GPULightType.Spot))
            {
                if (hdCamera.frameSettings.IsEnabled(FrameSettingsField.HierarchicalVarianceScreenSpaceShadows)
                    && additionalLightData.useHierarchicalVarianceScreenSpaceShadows
                    && hierarchicalVarianceScreenSpaceShadowsData != null)
                {
                    float lightDepthVS = Vector3.Dot(hdCamera.camera.transform.forward, cpuLightData.positionRWS);
                    gpuLightData.hierarchicalVarianceScreenSpaceShadowsIndex = hierarchicalVarianceScreenSpaceShadowsData.Push(cpuLightData.positionRWS, lightDepthVS, cpuLightData.range);
                }
            }
        }

        private unsafe void CalculateAllLightDataTextureInfo(
            CommandBuffer cmd,
            HDCamera hdCamera,
            in CullingResults cullResults,
            HDProcessedVisibleLightsBuilder visibleLights,
            HDLightRenderDatabase lightEntities,
            HDShadowSettings hdShadowSettings,
            in HDShadowInitParameters shadowInitParams,
            DebugDisplaySettings debugDisplaySettings,
            NativeArray<LightData> gpuLightArray,
            NativeArray<DirectionalLightData> gpuDirectionalLightArray,
            NativeArray<LightData> gpuDgiLightArray,
            HDRenderPipeline.HierarchicalVarianceScreenSpaceShadowsData hierarchicalVarianceScreenSpaceShadowsData)
        {
            BoolScalableSetting contactShadowScalableSetting = HDAdditionalLightData.ScalableSettings.UseContactShadow(m_Asset);
            bool rayTracingEnabled = hdCamera.frameSettings.IsEnabled(FrameSettingsField.RayTracing);
            HDProcessedVisibleLight* processedLightArrayPtr = (HDProcessedVisibleLight*)visibleLights.processedEntities.GetUnsafePtr<HDProcessedVisibleLight>();
            LightDataCpuSubset* cpuLightArrayPtr = (LightDataCpuSubset*)m_LightsCpuSubset.GetUnsafePtr<LightDataCpuSubset>();
            LightData* gpuLightArrayPtr = (LightData*)gpuLightArray.GetUnsafePtr<LightData>();
            DirectionalLightDataCpuSubset* cpuDirectionalLightArrayPtr = (DirectionalLightDataCpuSubset*)m_DirectionalLightsCpuSubset.GetUnsafePtr<DirectionalLightDataCpuSubset>();
            DirectionalLightData* gpuDirectionalLightArrayPtr = (DirectionalLightData*)gpuDirectionalLightArray.GetUnsafePtr<DirectionalLightData>();
            LightDataCpuSubset* cpuDgiLightArrayPtr = (LightDataCpuSubset*)m_DGILightsCpuSubset.GetUnsafePtr<LightDataCpuSubset>();
            LightData* gpuDgiLightArrayPtr = (LightData*)gpuDgiLightArray.GetUnsafePtr<LightData>();
            VisibleLight* visibleLightsArrayPtr = (VisibleLight*)cullResults.visibleLights.GetUnsafePtr<VisibleLight>();
            var shadowFilteringQuality = m_Asset.currentPlatformRenderPipelineSettings.hdShadowInitParams.shadowFilteringQuality;

            int directionalLightCount = visibleLights.sortedDirectionalLightCounts;
            int lightCounts = visibleLights.sortedLightCounts;
            for (int sortKeyIndex = 0; sortKeyIndex < lightCounts; ++sortKeyIndex)
            {
                uint sortKey = visibleLights.sortKeys[sortKeyIndex];
                LightCategory lightCategory = (LightCategory)((sortKey >> 27) & 0x1F);
                GPULightType gpuLightType = (GPULightType)((sortKey >> 22) & 0x1F);
                LightVolumeType lightVolumeType = (LightVolumeType)((sortKey >> 17) & 0x1F);
                int lightIndex = (int)(sortKey & 0xFFFF);

                int dataIndex = visibleLights.visibleLightEntityDataIndices[lightIndex];
                if (dataIndex == HDLightRenderDatabase.InvalidDataIndex)
                    continue;

                HDAdditionalLightData additionalLightData = lightEntities.hdAdditionalLightData[dataIndex];
                if (additionalLightData == null)
                    continue;

                //We utilize a raw light data pointer to avoid copying the entire structure
                HDProcessedVisibleLight* processedEntityPtr = processedLightArrayPtr + lightIndex;
                ref HDProcessedVisibleLight processedEntity = ref UnsafeUtility.AsRef<HDProcessedVisibleLight>(processedEntityPtr);
                HDLightType lightType = processedEntity.lightType;

                Light lightComponent = additionalLightData.legacyLight;

                int shadowIndex = -1;

                // Manage shadow requests
                if (lightComponent != null && (processedEntity.shadowMapFlags & HDProcessedVisibleLightsBuilder.ShadowMapFlags.WillRenderShadowMap) != 0)
                {
                    VisibleLight* visibleLightPtr = visibleLightsArrayPtr + lightIndex;
                    ref VisibleLight light = ref UnsafeUtility.AsRef<VisibleLight>(visibleLightPtr);
                    int shadowRequestCount;
                    shadowIndex = additionalLightData.UpdateShadowRequest(hdCamera, m_ShadowManager, hdShadowSettings, light, cullResults, lightIndex, debugDisplaySettings.data.lightingDebugSettings, shadowFilteringQuality, out shadowRequestCount);

#if UNITY_EDITOR
                    if ((debugDisplaySettings.data.lightingDebugSettings.shadowDebugUseSelection
                            || debugDisplaySettings.data.lightingDebugSettings.shadowDebugMode == ShadowMapDebugMode.SingleShadow)
                        && UnityEditor.Selection.activeGameObject == lightComponent.gameObject)
                    {
                        m_DebugSelectedLightShadowIndex = shadowIndex;
                        m_DebugSelectedLightShadowCount = shadowRequestCount;
                    }
#endif
                }

                if (gpuLightType == GPULightType.Directional)
                {
                    VisibleLight* visibleLightPtr = visibleLightsArrayPtr + lightIndex;
                    ref VisibleLight light = ref UnsafeUtility.AsRef<VisibleLight>(visibleLightPtr);
                    int directionalLightDataIndex = sortKeyIndex;
                    DirectionalLightDataCpuSubset* cpuLughtDataPtr = cpuDirectionalLightArrayPtr + directionalLightDataIndex;
                    ref DirectionalLightDataCpuSubset cpuLightData = ref UnsafeUtility.AsRef<DirectionalLightDataCpuSubset>(cpuLughtDataPtr);
                    DirectionalLightData* gpuLightDataPtr = gpuDirectionalLightArrayPtr + directionalLightDataIndex;
                    ref DirectionalLightData gpuLightData = ref UnsafeUtility.AsRef<DirectionalLightData>(gpuLightDataPtr);
                    CalculateDirectionalLightDataTextureInfo(
                        ref cpuLightData, ref gpuLightData, cmd, light, lightComponent, additionalLightData,
                        hdCamera, processedEntity.shadowMapFlags, directionalLightDataIndex, shadowIndex);
                }
                else
                {
                    int lightDataIndex = sortKeyIndex - directionalLightCount;
                    LightDataCpuSubset* cpuLightDataPtr = cpuLightArrayPtr + lightDataIndex;
                    ref LightDataCpuSubset cpuLightData = ref UnsafeUtility.AsRef<LightDataCpuSubset>(cpuLightDataPtr);
                    LightData* gpuLightDataPtr = gpuLightArrayPtr + lightDataIndex;
                    ref LightData gpuLightData = ref UnsafeUtility.AsRef<LightData>(gpuLightDataPtr);
                    CalculateLightDataTextureInfo(
                        ref cpuLightData, ref gpuLightData, cmd, lightComponent, additionalLightData, shadowInitParams,
                        hdCamera, contactShadowScalableSetting,
                        lightType, processedEntity.shadowMapFlags, rayTracingEnabled, lightDataIndex, shadowIndex, gpuLightType, hierarchicalVarianceScreenSpaceShadowsData);
                }
            }

            int dgiLightCounts = visibleLights.sortedDGILightCounts;
            for (int sortKeyIndex = 0; sortKeyIndex < dgiLightCounts; ++sortKeyIndex)
            {
                uint sortKey = visibleLights.sortKeysDGI[sortKeyIndex];
                HDGpuLightsBuilder.UnpackLightSortKey(sortKey, out var lightCategory, out var gpuLightType, out var lightVolumeType, out var lightIndex);

                int dataIndex = visibleLights.visibleLightEntityDataIndices[lightIndex];
                if (dataIndex == HDLightRenderDatabase.InvalidDataIndex)
                    continue;

                HDAdditionalLightData additionalLightData = lightEntities.hdAdditionalLightData[dataIndex];
                if (additionalLightData == null)
                    continue;

                //We utilize a raw light data pointer to avoid copying the entire structure
                HDProcessedVisibleLight* processedEntityPtr = processedLightArrayPtr + lightIndex;
                ref HDProcessedVisibleLight processedEntity = ref UnsafeUtility.AsRef<HDProcessedVisibleLight>(processedEntityPtr);
                HDLightType lightType = processedEntity.lightType;

                Light lightComponent = additionalLightData.legacyLight;

                // use the same shadow index from the previously computed one for visible lights
                int shadowIndex = additionalLightData.shadowIndex;

                if (gpuLightType != GPULightType.Directional)
                {
                    int lightDataIndex = sortKeyIndex;
                    LightDataCpuSubset* cpuLightDataPtr = cpuDgiLightArrayPtr + lightDataIndex;
                    ref LightDataCpuSubset cpuLightData = ref UnsafeUtility.AsRef<LightDataCpuSubset>(cpuLightDataPtr);
                    LightData* gpuLightDataPtr = gpuDgiLightArrayPtr + lightDataIndex;
                    ref LightData gpuLightData = ref UnsafeUtility.AsRef<LightData>(gpuLightDataPtr);
                    CalculateLightDataTextureInfo(
                        ref cpuLightData, ref gpuLightData, cmd, lightComponent, additionalLightData, shadowInitParams,
                        hdCamera, contactShadowScalableSetting,
                        lightType, processedEntity.shadowMapFlags, rayTracingEnabled, lightDataIndex, shadowIndex, gpuLightType, hierarchicalVarianceScreenSpaceShadowsData);
                }
            }
        }
    }
}
