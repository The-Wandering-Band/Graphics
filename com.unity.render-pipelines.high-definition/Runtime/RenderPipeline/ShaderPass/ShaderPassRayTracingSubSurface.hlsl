#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/Raytracing/Shaders/RaytracingFragInputs.hlsl"

// Generic function that handles the reflection code
[shader("closesthit")]
void ClosestSubSurface(inout RayIntersectionSubSurface rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    UNITY_XR_ASSIGN_VIEW_INDEX(DispatchRaysIndex().z);

    // Always set the new t value
    rayIntersection.t = RayTCurrent();

    IntersectionVertex currentVertex;
    #ifdef HAVE_VFX_MODIFICATION
        ZERO_INITIALIZE(IntersectionVertex, currentVertex);
        FragInputs fragInput;
        BuildFragInputsFromVFXIntersection(attributeData, fragInput);
    #else
        GetCurrentIntersectionVertex(attributeData, currentVertex);
        // Build the Frag inputs from the intersection vertice
        FragInputs fragInput;
        BuildFragInputsFromIntersection(currentVertex, fragInput);
    #endif

    // Evaluate the incident direction
    const float3 incidentDirection = WorldRayDirection();

    PositionInputs posInput;
    posInput.positionWS = fragInput.positionRWS;
    posInput.positionSS = rayIntersection.pixelCoord;

    // Build the surfacedata and builtindata
    SurfaceData surfaceData;
    BuiltinData builtinData;
    bool isVisible;
    GetSurfaceAndBuiltinData(fragInput, -incidentDirection, posInput, surfaceData, builtinData, currentVertex, rayIntersection.cone, isVisible);

    // make sure we output the normal
    rayIntersection.outNormal = fragInput.tangentToWorld[2];
    // Make sure to output the indirect diffuse lighting value and the emissive value
    rayIntersection.outIndirectDiffuse = builtinData.bakeDiffuseLighting + builtinData.emissiveColor;
}
