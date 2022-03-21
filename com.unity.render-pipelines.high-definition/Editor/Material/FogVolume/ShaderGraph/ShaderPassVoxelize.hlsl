#if SHADERPASS != SHADERPASS_FOGVOLUME_VOXELIZATION
#error SHADERPASS_is_not_correctly_define
#endif

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VertMesh.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GeometricTools.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Core/Utilities/GeometryUtils.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/VolumetricLighting/HDRenderPipeline.VolumetricLighting.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"

RW_TEXTURE3D(float4, _VBufferDensity) : register(u1); // RGB = sqrt(scattering), A = sqrt(extinction)
RW_TEXTURE2D_X(float, _FogVolumeDepth) : register(u2);

float4 _ViewSpaceBounds;
uint _SliceOffset;
uint _VolumeIndex;
uint _SliceCount;
StructuredBuffer<OrientedBBox>                  _VolumeBounds;
StructuredBuffer<LocalVolumetricFogEngineData>  _VolumeData;

struct VertexToFragment
{
    float4 positionCS : SV_POSITION;
    float3 viewDirectionWS : TEXCOORD0;
    float3 positionOS : TEXCOORD1;
    float depth : TEXCOORD2; // TODO: packing
    uint depthSlice : SV_RenderTargetArrayIndex;
    uint xrViewIndex : BLENDINDICES0;
};

float ComputeSliceDepth(uint sliceIndex)
{
    float t0 = DecodeLogarithmicDepthGeneralized(0, _VBufferDistanceDecodingParams);
    float de = _VBufferRcpSliceCount; // Log-encoded distance between slices

    float e1 = ((float)sliceIndex + 0.5) * de + de;
    return DecodeLogarithmicDepthGeneralized(e1, _VBufferDistanceDecodingParams);
}

float EyeDepthToLinear(float linearDepth, float4 zBufferParam)
{
    linearDepth = rcp(linearDepth);
    linearDepth -= zBufferParam.w;

    return linearDepth / zBufferParam.z;
}

// TODO: instance id and vertex id in Attributes
VertexToFragment Vert(uint instanceId : INSTANCEID_SEMANTIC, uint vertexId : VERTEXID_SEMANTIC)
{
    VertexToFragment output;

    output.xrViewIndex = instanceId / _SliceCount;

    output.depthSlice = _SliceOffset + (instanceId % _SliceCount) + output.xrViewIndex * _VBufferSliceCount;

    float sliceDistance = ComputeSliceDepth(output.depthSlice);
    float depthViewSpace = sliceDistance;

    output.positionCS = GetQuadVertexPosition(vertexId); // TODO: replace this by cube slicing algorithm to avoid overdraw
    output.positionCS.xy *= _ViewSpaceBounds.zw;
    output.positionCS.xy += _ViewSpaceBounds.xy;
    output.positionCS.z = EyeDepthToLinear(depthViewSpace, _ZBufferParams);
    output.positionCS.w = 1;

    float3 positionWS = ComputeWorldSpacePosition(output.positionCS, UNITY_MATRIX_I_VP);
    output.viewDirectionWS = GetWorldSpaceViewDir(positionWS);

    // Calculate object space position
    OrientedBBox obb = _VolumeBounds[_VolumeIndex];
    float3x3 obbFrame   = float3x3(obb.right, obb.up, cross(obb.right, obb.up));
    float3   obbExtents = float3(obb.extentX, obb.extentY, obb.extentZ);
    output.positionOS = mul(positionWS - obb.center, transpose(obbFrame));

    output.depth = depthViewSpace;

    return output;
}

FragInputs BuildFragInputs(VertexToFragment v2f, float3 voxelClipSpace)
{
    FragInputs output;
    ZERO_INITIALIZE(FragInputs, output);

    float3 positionWS = mul(UNITY_MATRIX_M, float4(v2f.positionOS, 1));
    output.positionSS = v2f.positionCS;
    output.positionRWS = output.positionPredisplacementRWS = positionWS;
    output.positionPixel = uint2(v2f.positionCS.xy);
    output.texCoord0 = float4(saturate(voxelClipSpace * 0.5 + 0.5), 0);
    output.tangentToWorld = k_identity3x3;

    return output;
}

float ComputeFadeFactor(float3 coordNDC, float distance)
{
    bool exponential = _VolumeData[_VolumeIndex].falloffMode == LOCALVOLUMETRICFOGFALLOFFMODE_EXPONENTIAL;

    return ComputeVolumeFadeFactor(
        coordNDC, distance,
        _VolumeData[_VolumeIndex].rcpPosFaceFade,
        _VolumeData[_VolumeIndex].rcpNegFaceFade,
        _VolumeData[_VolumeIndex].invertFade,
        _VolumeData[_VolumeIndex].rcpDistFadeLen,
        _VolumeData[_VolumeIndex].endTimesRcpDistFadeLen,
        exponential
    );
}

void Frag(VertexToFragment v2f, out float4 outColor : SV_Target0)
{
    // Setup VR storeo eye index manually because we use the SV_RenderTargetArrayIndex semantic which conflicts with XR macros
#if defined(UNITY_SINGLE_PASS_STEREO)
    unity_StereoEyeIndex = v2f.xrViewIndex;
#endif

    float3 albedo;
    float extinction;
    

    float sliceDistance = ComputeSliceDepth(v2f.depthSlice);

    // Compute vocel center position and test against volume OBB
    // TODO: adjust cell distance to match the texture volumes
    float3 raycenterDirWS = normalize(-v2f.viewDirectionWS); // Normalize
    float3 rayoriginWS    = GetCurrentViewPosition();
    float3 voxelCenterWS = rayoriginWS + sliceDistance * raycenterDirWS;

    OrientedBBox obb = _VolumeBounds[_VolumeIndex];
    float3x3 obbFrame   = float3x3(obb.right, obb.up, cross(obb.right, obb.up));
    float3   obbExtents = float3(obb.extentX, obb.extentY, obb.extentZ);

    float3 voxelCenterBS = mul(voxelCenterWS - obb.center, transpose(obbFrame));
    float3 voxelCenterCS = (voxelCenterBS * rcp(obbExtents));
    bool overlap = Max3(abs(voxelCenterCS.x), abs(voxelCenterCS.y), abs(voxelCenterCS.z)) <= 1;

    if (!overlap)
        clip(-1);

    FragInputs fragInputs = BuildFragInputs(v2f, voxelCenterCS);
    GetVolumeData(fragInputs, v2f.viewDirectionWS, albedo, extinction);

    extinction *= _VolumeData[_VolumeIndex].extinction;

    float3 voxelCenterNDC = saturate(voxelCenterCS * 0.5 + 0.5);
    float fade = ComputeFadeFactor(voxelCenterNDC, sliceDistance);
    extinction *= fade;

    float3 scatteringColor = (albedo * _VolumeData[_VolumeIndex].albedo) * extinction;

    // Apply volume blending
    outColor = max(0, float4(scatteringColor, extinction));
}
