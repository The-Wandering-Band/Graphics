#ifndef UNITY_COMMON_LIGHTING_INCLUDED
#define UNITY_COMMON_LIGHTING_INCLUDED

// These clamping function to max of floating point 16 bit are use to prevent INF in code in case of extreme value
real ClampToFloat16Max(real value)
{
    return min(value, HALF_MAX);
}

real2 ClampToFloat16Max(real2 value)
{
    return min(value, HALF_MAX);
}

real3 ClampToFloat16Max(real3 value)
{
    return min(value, HALF_MAX);
}

real4 ClampToFloat16Max(real4 value)
{
    return min(value, HALF_MAX);
}

// Ligthing convention
// Light direction is oriented backward (-Z). i.e in shader code, light direction is -lightData.forward

//-----------------------------------------------------------------------------
// Helper functions
//-----------------------------------------------------------------------------

// Performs the mapping of the vector 'v' centered within the axis-aligned cube
// of dimensions [-1, 1]^3 to a vector centered within the unit sphere.
// The function expects 'v' to be within the cube (possibly unexpected results otherwise).
// Ref: http://mathproofs.blogspot.com/2005/07/mapping-cube-to-sphere.html
real3 MapCubeToSphere(real3 v)
{
    real3 v2 = v * v;
    real2 vr3 = v2.xy * rcp(3.0);
    return v * sqrt((real3)1.0 - 0.5 * v2.yzx - 0.5 * v2.zxy + vr3.yxx * v2.zzy);
}

// Computes the squared magnitude of the vector computed by MapCubeToSphere().
real ComputeCubeToSphereMapSqMagnitude(real3 v)
{
    real3 v2 = v * v;
    // Note: dot(v, v) is often computed before this function is called,
    // so the compiler should optimize and use the precomputed result here.
    return dot(v, v) - v2.x * v2.y - v2.y * v2.z - v2.z * v2.x + v2.x * v2.y * v2.z;
}

// texelArea = 4.0 / (resolution * resolution).
// Ref: http://bpeers.com/blog/?itemid=1017
real ComputeCubemapTexelSolidAngle(real3 L, real texelArea)
{
    // Stretch 'L' by (1/d) so that it points at a side of a [-1, 1]^2 cube.
    real d = Max3(abs(L.x), abs(L.y), abs(L.z));
    // Since 'L' is a unit vector, we can directly compute its
    // new (inverse) length without dividing 'L' by 'd' first.
    real invDist = d;

    // dw = dA * cosTheta / (dist * dist), cosTheta = 1.0 / dist,
    // where 'dA' is the area of the cube map texel.
    return texelArea * invDist * invDist * invDist;
}

//-----------------------------------------------------------------------------
// Attenuation functions
//-----------------------------------------------------------------------------

// Ref: Moving Frostbite to PBR
real SmoothDistanceAttenuation(real squaredDistance, real invSqrAttenuationRadius)
{
    real factor = squaredDistance * invSqrAttenuationRadius;
    real smoothFactor = saturate(1.0 - factor * factor);
    return smoothFactor * smoothFactor;
}

#define PUNCTUAL_LIGHT_THRESHOLD 0.01 // 1cm (in Unity 1 is 1m)

real GetDistanceAttenuation(real sqrDist, real invSqrAttenuationRadius)
{
    real attenuation = 1.0 / (max(PUNCTUAL_LIGHT_THRESHOLD * PUNCTUAL_LIGHT_THRESHOLD, sqrDist));
    // Non physically based hack to limit light influence to attenuationRadius.
    attenuation *= SmoothDistanceAttenuation(sqrDist, invSqrAttenuationRadius);
    return attenuation;
}

real GetDistanceAttenuation(real3 unL, real invSqrAttenuationRadius)
{
    real sqrDist = dot(unL, unL);
    return GetDistanceAttenuation(sqrDist, invSqrAttenuationRadius);
}

real GetAngleAttenuation(real3 L, real3 lightDir, real lightAngleScale, real lightAngleOffset)
{
    real cd = dot(lightDir, L);
    real attenuation = saturate(cd * lightAngleScale + lightAngleOffset);
    // smooth the transition
    attenuation *= attenuation;

    return attenuation;
}

// Applies SmoothDistanceAttenuation() after transforming the attenuation ellipsoid into a sphere.
// If r = rsqrt(invSqRadius), then the ellipsoid is defined s.t. r1 = r / invAspectRatio, r2 = r3 = r.
// The transformation is performed along the major axis of the ellipsoid (corresponding to 'r1').
// Both the ellipsoid (e.i. 'axis') and 'unL' should be in the same coordinate system.
// 'unL' should be computed from the center of the ellipsoid.
real GetEllipsoidalDistanceAttenuation(real3 unL,  real invSqRadius,
                                        real3 axis, real invAspectRatio)
{
    // Project the unnormalized light vector onto the axis.
    real projL = dot(unL, axis);

    // Transform the light vector instead of transforming the ellipsoid.
    real diff = projL - projL * invAspectRatio;
    unL -= diff * axis;

    real sqDist = dot(unL, unL);
    return SmoothDistanceAttenuation(sqDist, invSqRadius);
}

// Applies SmoothDistanceAttenuation() using the axis-aligned ellipsoid of the given dimensions.
// Both the ellipsoid and 'unL' should be in the same coordinate system.
// 'unL' should be computed from the center of the ellipsoid.
real GetEllipsoidalDistanceAttenuation(real3 unL, real3 invHalfDim)
{
    // Transform the light vector so that we can work with
    // with the ellipsoid as if it was a unit sphere.
    unL *= invHalfDim;

    real sqDist = dot(unL, unL);
    return SmoothDistanceAttenuation(sqDist, 1.0);
}

// Applies SmoothDistanceAttenuation() after mapping the axis-aligned box to a sphere.
// If the diagonal of the box is 'd', invHalfDim = rcp(0.5 * d).
// Both the box and 'unL' should be in the same coordinate system.
// 'unL' should be computed from the center of the box.
real GetBoxDistanceAttenuation(real3 unL, real3 invHalfDim)
{
    // Transform the light vector so that we can work with
    // with the box as if it was a [-1, 1]^2 cube.
    unL *= invHalfDim;

    // Our algorithm expects the input vector to be within the cube.
    if (Max3(abs(unL.x), abs(unL.y), abs(unL.z)) > 1.0) return 0.0;

    real sqDist = ComputeCubeToSphereMapSqMagnitude(unL);
    return SmoothDistanceAttenuation(sqDist, 1.0);
}

//-----------------------------------------------------------------------------
// IES Helper
//-----------------------------------------------------------------------------

real2 GetIESTextureCoordinate(real3x3 lightToWord, real3 L)
{
    // IES need to be sample in light space
    real3 dir = mul(lightToWord, -L); // Using matrix on left side do a transpose

    // convert to spherical coordinate
    real2 sphericalCoord; // .x is theta, .y is phi
    // Texture is encoded with cos(phi), scale from -1..1 to 0..1
    sphericalCoord.y = (dir.z * 0.5) + 0.5;
    real theta = atan2(dir.y, dir.x);
    sphericalCoord.x = theta * INV_TWO_PI;

    return sphericalCoord;
}

//-----------------------------------------------------------------------------
// Lighting functions
//-----------------------------------------------------------------------------

// Ref: Horizon Occlusion for Normal Mapped Reflections: http://marmosetco.tumblr.com/post/81245981087
real GetHorizonOcclusion(real3 V, real3 normalWS, real3 vertexNormal, real horizonFade)
{
    real3 R = reflect(-V, normalWS);
    real specularOcclusion = saturate(1.0 + horizonFade * dot(R, vertexNormal));
    // smooth it
    return specularOcclusion * specularOcclusion;
}

// Ref: Moving Frostbite to PBR - Gotanda siggraph 2011
// Return specular occlusion based on ambient occlusion (usually get from SSAO) and view/roughness info
real GetSpecularOcclusionFromAmbientOcclusion(real NdotV, real ambientOcclusion, real roughness)
{
	return saturate(PositivePow(NdotV + ambientOcclusion, exp2(-16.0 * roughness - 1.0)) - 1.0 + ambientOcclusion);
}

// ref: Practical Realtime Strategies for Accurate Indirect Occlusion
// Update ambient occlusion to colored ambient occlusion based on statitics of how light is bouncing in an object and with the albedo of the object
real3 GTAOMultiBounce(real visibility, real3 albedo)
{
    real3 a =  2.0404 * albedo - 0.3324;
    real3 b = -4.7951 * albedo + 0.6417;
    real3 c =  2.7552 * albedo + 0.6903;

    real x = visibility;
    return max(x, ((x * a + b) * x + c) * x);
}

// Based on Oat and Sander's 2008 technique
// Area/solidAngle of intersection of two cone
real SphericalCapIntersectionSolidArea(real cosC1, real cosC2, real cosB)
{
    real r1 = FastACos(cosC1);
    real r2 = FastACos(cosC2);
    real rd = FastACos(cosB);
    real area = 0.0;

    if (rd <= max(r1, r2) - min(r1, r2))
    {
        // One cap is completely inside the other
        area = TWO_PI - TWO_PI * max(cosC1, cosC2);
    }
    else if (rd >= r1 + r2)
    {
        // No intersection exists
        area = 0.0;
    }
    else
    {
        real diff = abs(r1 - r2);
        real den = r1 + r2 - diff;
        real x = 1.0 - saturate((rd - diff) / den);
        area = smoothstep(0.0, 1.0, x);
        area *= TWO_PI - TWO_PI * max(cosC1, cosC2);
    }

    return area;
}

// Ref: Steve McAuley - Energy-Conserving Wrapped Diffuse
real ComputeWrappedDiffuseLighting(real NdotL, real w)
{
    return saturate((NdotL + w) / ((1 + w) * (1 + w)));
}

//-----------------------------------------------------------------------------
// Helper functions
//-----------------------------------------------------------------------------

// Inputs:    normalized normal and view vectors.
// Outputs:   front-facing normal, and the new non-negative value of the cosine of the view angle.
// Important: call Orthonormalize() on the tangent and recompute the bitangent afterwards.
real3 GetViewReflectedNormal(real3 N, real3 V, out real NdotV)
{
    // Fragments of front-facing geometry can have back-facing normals due to interpolation,
    // normal mapping and decals. This can cause visible artifacts from both direct (negative or
    // extremely high values) and indirect (incorrect lookup direction) lighting.
    // There are several ways to avoid this problem. To list a few:
    //
    // 1. Setting { NdotV = max(<N,V>, SMALL_VALUE) }. This effectively removes normal mapping
    // from the affected fragments, making the surface appear flat.
    //
    // 2. Setting { NdotV = abs(<N,V>) }. This effectively reverses the convexity of the surface.
    // It also reduces light leaking from non-shadow-casting lights. Note that 'NdotV' can still
    // be 0 in this case.
    //
    // It's important to understand that simply changing the value of the cosine is insufficient.
    // For one, it does not solve the incorrect lookup direction problem, since the normal itself
    // is not modified. There is a more insidious issue, however. 'NdotV' is a constituent element
    // of the mathematical system describing the relationships between different vectors - and
    // not just normal and view vectors, but also light vectors, real vectors, tangent vectors, etc.
    // Changing only one angle (or its cosine) leaves the system in an inconsistent state, where
    // certain relationships can take on different values depending on whether 'NdotV' is used
    // in the calculation or not. Therefore, it is important to change the normal (or another
    // vector) in order to leave the system in a consistent state.
    //
    // We choose to follow the conceptual approach (2) by reflecting the normal around the
    // (<N,V> = 0) boundary if necessary, as it allows us to preserve some normal mapping details.

    NdotV = dot(N, V);

    // N = (NdotV >= 0.0) ? N : (N - 2.0 * NdotV * V);
    N += (2.0 * saturate(-NdotV)) * V;
    NdotV = abs(NdotV);

    return N;
}

// Generates an orthonormal right-handed basis from a unit vector.
// Ref: http://marc-b-reynolds.github.io/quaternions/2016/07/06/Orthonormal.html
real3x3 GetLocalFrame(real3 localZ)
{
    real x  = localZ.x;
    real y  = localZ.y;
    real z  = localZ.z;
    real sz = FastSign(z);
    real a  = 1 / (sz + z);
    real ya = y * a;
    real b  = x * ya;
    real c  = x * sz;

    real3 localX = real3(c * x * a - 1, sz * b, c);
    real3 localY = real3(b, y * ya - sz, y);

    return real3x3(localX, localY, localZ);
}

real3x3 GetLocalFrame(real3 localZ, real3 localX)
{
    real3 localY = cross(localZ, localX);

    return real3x3(localX, localY, localZ);
}

// ior is a value between 1.0 and 2.5
real IORToFresnel0(real ior)
{
    return Sq((ior - 1.0) / (ior + 1.0));
}

#endif // UNITY_COMMON_LIGHTING_INCLUDED
