#ifndef LED_MIN_SIZE_INCLUDED
#define LED_MIN_SIZE_INCLUDED

float3 LED_InflateOS(float3 positionOS)
{
    const float minPixelRadius = 2;

    if (minPixelRadius <= 0.0)
        return positionOS;

    float3 pivotWS  = UNITY_MATRIX_M._m03_m13_m23;
    float3 offsetWS = TransformObjectToWorld(positionOS) - pivotWS;
    float  dist     = length(offsetWS);
    if (dist < 1e-6)
        return positionOS;

    // clip.w of the pivot = view depth (perspective) or 1 (orthographic).
    // Both cases work with the same formula because P._m11 = 1/tan(fov/2) or 1/orthoSize.
    float w = mul(UNITY_MATRIX_VP, float4(pivotWS, 1.0)).w;
    if (w <= 1e-4)
        return positionOS;

    // abs(): render-texture projections can flip the sign of _m11.
    float pxPerUnit = abs(UNITY_MATRIX_P._m11) * 0.5 * _ScaledScreenParams.y / w;
    float pxRadius  = dist * pxPerUnit;

    if (pxRadius >= minPixelRadius)
        return positionOS;

    offsetWS *= minPixelRadius / pxRadius;
    return TransformWorldToObject(pivotWS + offsetWS);
}

// Generates a wrapper vertex function that inflates the position, then calls the original
// URP vertex function unchanged (normals, fog, shadow coords, GI, etc. all stay stock).
//   NAME     : new vertex function name (goes into #pragma vertex)
//   ORIGINAL : the function the pragma named before (LitPassVertex, DepthOnlyVertex, ...)
//   POSFIELD : the position member of that pass's Attributes struct (positionOS or position)
#define LED_WRAP_VERTEX(NAME, ORIGINAL, POSFIELD)                              \
    Varyings NAME(Attributes input)                                            \
    {                                                                          \
        UNITY_SETUP_INSTANCE_ID(input);                                        \
        input.POSFIELD.xyz = LED_InflateOS(input.POSFIELD.xyz);                \
        return ORIGINAL(input);                                                \
    }

#endif
