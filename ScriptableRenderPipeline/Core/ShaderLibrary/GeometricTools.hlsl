#ifndef UNITY_GEOMETRICTOOLS_INCLUDED
#define UNITY_GEOMETRICTOOLS_INCLUDED

//-----------------------------------------------------------------------------
// Intersection functions
//-----------------------------------------------------------------------------

// return furthest near intersection in x and closest far intersection in y
// if (intersections.y > intersections.x) the ray hit the box, else it miss it
// Assume dir is normalize
float2 BoxRayIntersect(float3 start, float3 dir, float3 boxMin, float3 boxMax)
{
    float3 invDir = 1.0 / dir;

    // Find the ray intersection with box plane
    float3 firstPlaneIntersect = (boxMin - start) * invDir;
    float3 secondPlaneIntersect = (boxMax - start) * invDir;

    // Get the closest/furthest of these intersections along the ray (Ok because x/0 give +inf and -x/0 give �inf )
    float3 closestPlane = min(firstPlaneIntersect, secondPlaneIntersect);
    float3 furthestPlane = max(firstPlaneIntersect, secondPlaneIntersect);

    float2 intersections;
    // Find the furthest near intersection
    intersections.x = max(closestPlane.x, max(closestPlane.y, closestPlane.z));
    // Find the closest far intersection
    intersections.y = min(min(furthestPlane.x, furthestPlane.y), furthestPlane.z);

    return intersections;
}

// This simplified version assume that we care about the result only when we are inside the box
// Assume dir is normalize
float BoxRayIntersectSimple(float3 start, float3 dir, float3 boxMin, float3 boxMax)
{
    float3 invDir = 1.0 / dir;

    // Find the ray intersection with box plane
    float3 rbmin = (boxMin - start) * invDir;
    float3 rbmax = (boxMax - start) * invDir;

    float3 rbminmax = (dir > 0.0) ? rbmax : rbmin;

    return min(min(rbminmax.x, rbminmax.y), rbminmax.z);
}

// Assume Sphere is at the origin (i.e start = position - spherePosition)
float2 SphereRayIntersect(float3 start, float3 dir, float radius, out bool intersect)
{
    float a = dot(dir, dir);
    float b = dot(dir, start) * 2.0;
    float c = dot(start, start) - radius * radius;
    float discriminant = b * b - 4.0 * a * c;

    float2 intersections = float2(0.0, 0.0);
    intersect = false;
    if (discriminant < 0.0 || a == 0.0)
    {
        intersections.x = 0.0;
        intersections.y = 0.0;
    }
    else
    {
        float sqrtDiscriminant = sqrt(discriminant);
        intersections.x = (-b - sqrtDiscriminant) / (2.0 * a);
        intersections.y = (-b + sqrtDiscriminant) / (2.0 * a);
        intersect = true;
    }

    return intersections;
}

// This simplified version assume that we care about the result only when we are inside the sphere
// Assume Sphere is at the origin (i.e start = position - spherePosition) and dir is normalized
// Ref: http://http.developer.nvidia.com/GPUGems/gpugems_ch19.html
float SphereRayIntersectSimple(float3 start, float3 dir, float radius)
{
    float b = dot(dir, start) * 2.0;
    float c = dot(start, start) - radius * radius;
    float discriminant = b * b - 4.0 * c;

    return abs(sqrt(discriminant) - b) * 0.5;
}

float3 RayPlaneIntersect(in float3 rayOrigin, in float3 rayDirection, in float3 planeOrigin, in float3 planeNormal)
{
    float dist = dot(planeNormal, planeOrigin - rayOrigin) / dot(planeNormal, rayDirection);
    return rayOrigin + rayDirection * dist;
}

// Solves the quadratic equation of the form: a*t^2 + b*t + c = 0.
// Returns 'false' if there are no real roots, 'true' otherwise.
// Ref: Numerical Recipes in C++ (3rd Edition)
bool SolveQuadraticEquation(float a, float b, float c, out float2 roots)
{
    float d = b * b - 4 * a * c;
    float q = -0.5 * (b + FastSign(b) * sqrt(d));
    roots   = float2(q / a, c / q);

    return (d >= 0);
}

// 'coneAxisX' and 'coneAxisY' should be pre-scaled by by cot(halfAngle).
// Returns parametric distances 'tEntr' and 'tExit' along the ray,
// subject to constraints 'tMin' and 'tMax'.
bool ConeRayIntersect(float3 rayOrigin,  float3 rayDirection,
                      float3 coneOrigin, float3 coneDirection,
                      float3 coneAxisX,  float3 coneAxisY,
                      float tMin, float tMax,
                      inout float tEntr, inout float tExit)
{
    // Inverse transform the ray into a coordinate system with the cone at the origin facing along the Z axis.
    float3x3 rotMat = float3x3(coneAxisX, coneAxisY, coneDirection);

    float3 o = mul(rotMat, rayOrigin - coneOrigin);
    float3 d = mul(rotMat, rayDirection);

    // Cone equation (facing along Z): (h/r*x)^2 + (h/r*y)^2 - z^2 = 0.
    // Cone axes are premultiplied with (h/r).
    // Set up the quadratic equation: a*t^2 + b*t + c = 0.
    float a = d.x * d.x + d.y * d.y - d.z * d.z;
    float b = o.x * d.x + o.y * d.y - o.z * d.z;
    float c = o.x * o.x + o.y * o.y - o.z * o.z;

    float2 roots;

    // Check whether we have at least 1 root.
    bool hit = SolveQuadraticEquation(a, 2 * b, c, roots);

    tEntr = min(roots.x, roots.y);
    tExit = max(roots.x, roots.y);
    float3 pEntr = o + tEntr * d;
    float3 pExit = o + tExit * d;

    // Clip the negative cone.
    if (max(pEntr.z, pExit.z) < 0) { hit = false; }
    if (pEntr.z < 0) { tEntr = tExit; tExit = tMax; }
    if (pExit.z < 0) { tExit = tEntr; tEntr = tMin; }

    // Clamp using the values passed into the function.
    tEntr = clamp(tEntr, tMin, tMax);
    tExit = clamp(tExit, tMin, tMax);

    // Check for grazing intersections.
    if (tEntr == tExit) { hit = false; }

    return hit;
}

//-----------------------------------------------------------------------------
// Miscellaneous functions
//-----------------------------------------------------------------------------

// Box is AABB
float DistancePointBox(float3 position, float3 boxMin, float3 boxMax)
{
    return length(max(max(position - boxMax, boxMin - position), float3(0.0, 0.0, 0.0)));
}

float3 ProjectPointOnPlane(float3 position, float3 planePosition, float3 planeNormal)
{
    return position - (dot(position - planePosition, planeNormal) * planeNormal);
}

// Plane equation: {(a, b, c) = N, d = -dot(N, P)}.
// Returns the distance from the plane to the point 'p' along the normal.
// Positive -> in front (above), negative -> behind (below).
float DistanceFromPlane(float3 p, float4 plane)
{
    return dot(float4(p, 1.0), plane);
}

// Returns 'true' if the triangle is outside of the frustum.
// 'epsilon' is the (negative) distance to (outside of) the frustum below which we cull the triangle.
bool CullTriangleFrustum(float3 p0, float3 p1, float3 p2, float epsilon, float4 frustumPlanes[6], int numPlanes)
{
    bool outside = false;

    for (int i = 0; i < numPlanes; i++)
    {
        // If all 3 points are behind any of the planes, we cull.
        outside = outside || Max3(DistanceFromPlane(p0, frustumPlanes[i]),
                                  DistanceFromPlane(p1, frustumPlanes[i]),
                                  DistanceFromPlane(p2, frustumPlanes[i])) < epsilon;
    }

    return outside;
}

// Returns 'true' if the edge of the triangle is outside of the frustum.
// The edges are defined s.t. they are on the opposite side of the point with the given index.
// 'epsilon' is the (negative) distance to (outside of) the frustum below which we cull the triangle.
bool3 CullTriangleEdgesFrustum(float3 p0, float3 p1, float3 p2, float epsilon, float4 frustumPlanes[6], int numPlanes)
{
    bool3 edgesOutside = false;

    for (int i = 0; i < numPlanes; i++)
    {
        bool3 pointsOutside = bool3(DistanceFromPlane(p0, frustumPlanes[i]) < epsilon,
                                    DistanceFromPlane(p1, frustumPlanes[i]) < epsilon,
                                    DistanceFromPlane(p2, frustumPlanes[i]) < epsilon);

        // If both points of the edge are behind any of the planes, we cull.
        edgesOutside.x = edgesOutside.x || (pointsOutside.y && pointsOutside.z);
        edgesOutside.y = edgesOutside.y || (pointsOutside.x && pointsOutside.z);
        edgesOutside.z = edgesOutside.z || (pointsOutside.x && pointsOutside.y);
    }

    return edgesOutside;
}

// Returns 'true' if a triangle defined by 3 vertices is back-facing.
// 'epsilon' is the (negative) value of dot(N, V) below which we cull the triangle.
// 'winding' can be used to change the order: pass 1 for (p0 -> p1 -> p2), or -1 for (p0 -> p2 -> p1).
bool CullTriangleBackFace(float3 p0, float3 p1, float3 p2, float epsilon, float3 viewPos, float winding)
{
    float3 edge1 = p1 - p0;
    float3 edge2 = p2 - p0;

    float3 N     = cross(edge1, edge2);
    float3 V     = viewPos - p0;
    float  NdotV = dot(N, V) * winding;

    // Optimize:
    // NdotV / (length(N) * length(V)) < Epsilon
    // NdotV < Epsilon * length(N) * length(V)
    // NdotV < Epsilon * sqrt(dot(N, N)) * sqrt(dot(V, V))
    // NdotV < Epsilon * sqrt(dot(N, N) * dot(V, V))
    return NdotV < epsilon * sqrt(dot(N, N) * dot(V, V));
}

#endif // UNITY_GEOMETRICTOOLS_INCLUDED
