// Real desktop pixels, separate from WPF foreground text and controls.
sampler2D Background : register(s0);
float2 SurfaceSize : register(c0);
float Radius : register(c1);
float2 CaptureOrigin : register(c2);
float2 CaptureScale : register(c3);
float Amount : register(c4);
float Dark : register(c5);
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 p = (uv - 0.5) * SurfaceSize;
    float2 q = abs(p) - SurfaceSize * 0.5 + Radius;
    float2 outside = max(q, 0);
    float distance = length(outside) + min(max(q.x, q.y), 0) - Radius;
    float depth = -distance;
    float2 normal = outside / max(length(outside), 0.001);
    // Normal on straight edges, with a smooth transition around corners.
    float2 straight = q.x > q.y ? float2(1, 0) : float2(0, 1);
    normal = (length(outside) > 0.001 ? normal : straight) * sign(p);
    float lens = smoothstep(0, 1.5, depth) * (1 - smoothstep(1.5, 18, depth));
    float2 shift = -normal * (Amount * lens) / SurfaceSize * CaptureScale;
    float2 source = CaptureOrigin + uv * CaptureScale;
    float2 step = CaptureScale / SurfaceSize;
    // A clear centre with a small blur; dispersion affects only displaced edge pixels.
    float3 colour;
    colour.r = tex2D(Background, source + shift * 1.035).r;
    colour.g = tex2D(Background, source + shift).g;
    colour.b = tex2D(Background, source + shift * 0.965).b;
    float3 blur = (tex2D(Background, source + shift + step * .8).rgb
                 +tex2D(Background, source + shift - step * .8).rgb) * .5;
    colour = lerp(colour, blur, .32);
    float3 tint = lerp(float3(.96, .97, .99), float3(.055, .068, .085), Dark);
    colour = lerp(colour, tint, lerp(.16, .25, Dark));
    return float4(colour, 1);
}
