// Paper.cginc — the field's one lighting model, shared by every surface the map
// draws: ground, cliff, water, body, prop.
//
// .art/STYLE.md froze the rendering technique for this game as 2.5D cut paper,
// and names the parts: three value bands per material (base, one broad
// upper-left highlight, one broad lower-right shade), no outer contour line,
// forms separated by value rather than by stroke, one soft light from the upper
// left. That document was written for authored sprites. This is the same
// technique stated as a lighting function, so that a mesh lit at runtime lands
// in the same three bands an artist would have painted, and the field in three
// dimensions is still the game the sprites were drawn for.
//
// Written as a surface shader lighting model on purpose. Unity's built-in
// pipeline gives a surface shader cast and received shadows, light probes and
// the forward add pass for free, and a hand-written vertex/fragment pair gives
// none of that without several hundred lines that exist only to re-earn it.
//
// Every shader that includes this must:
//   - put `float3 worldPos;` in its Input struct,
//   - name WcPaper as its lighting model: `#pragma surface surf WcPaper`,
//   - pass `nofog` and run WcAtmosphere from a `finalcolor:` function, because
//     the fog of war is a texture over the map and not a distance ramp, and
//     Unity's own fog would be applied on top of a colour this has already
//     decided.

#ifndef WORDCRAFT_PAPER_INCLUDED
#define WORDCRAFT_PAPER_INCLUDED

// ---------------------------------------------------------------------------
// Globals. Set once a frame from the view (Sky.cs, Fog.cs) with
// Shader.SetGlobalColor / SetGlobalTexture / SetGlobalVector, so every material
// on the field agrees about the light and the weather without holding its own
// copy of either.
// ---------------------------------------------------------------------------

// The fog of war, one texel per cell, sampled by world xz. r is what the local
// peer knows about that cell: 1 seen now, something in between remembered, 0
// never seen. Bilinear, so the edge of knowledge is a soft boundary rather than
// a staircase of cells.
sampler2D _WcFog;

// x: 1 / cells on a side, to turn a world x or z into a uv.
// y: 1 when the fog is on, 0 when it is off — a solo match reveals everything
//    and multiplying by a texture full of ones is a texture read for nothing.
float4 _WcFogParams;

// The value unseen ground falls to. Not black: docs/UI-STYLE.md gives the fog
// its own token and the map keeps its shape under it.
float4 _WcFogTint;

// Hemisphere ambient. rgb of each, lit from above by the sky colour and from
// below by what the ground bounces, which is most of what makes a low-poly
// surface read as rounded rather than as a flat chart.
float4 _WcSkyLight;
float4 _WcEarthLight;

// x: the n·l below which a surface is in its shade band.
// y: the n·l above which it is in its highlight band.
// z: how dark the shade band is, as a fraction of base.
// w: how light the highlight band is, as a multiple of base.
float4 _WcBands;

// rgb: the lit paper edge. a: how strong.
float4 _WcRim;

// rgb: what the distance fades toward — the sky, so the far side of the map
// recedes into it. a: how much of it the furthest ground takes.
float4 _WcHaze;

// x: where the haze starts, in world units from the camera.
// y: 1 / (end - start).
float4 _WcHazeRange;

// ---------------------------------------------------------------------------
// The lighting model.
// ---------------------------------------------------------------------------

/// Quantises a lit fraction into the three bands the style allows. The edges are
/// soft by a hair rather than a hard compare: a hard one aliases along every
/// terrain triangle, and a sliver of gradient at the boundary is the "very
/// subtle gradient inside a large shape" the style document already permits.
inline half WcBand(half lit)
{
    half edge = 0.035h;
    half shade = smoothstep(_WcBands.x - edge, _WcBands.x + edge, lit);
    half light = smoothstep(_WcBands.y - edge, _WcBands.y + edge, lit);
    half band = lerp(_WcBands.z, 1.0h, shade);
    return lerp(band, _WcBands.w, light);
}

inline fixed4 LightingWcPaper(SurfaceOutput s, fixed3 lightDir, fixed3 viewDir, fixed atten)
{
    half ndl = dot(s.Normal, lightDir);

    // A cast shadow lands in the shade band and goes no lower. A shadow that
    // reached black would separate shapes by stroke, which is the one thing the
    // style forbids, and on a dark palette it reads as a hole in the map.
    half lit = max(ndl, 0.0h) * lerp(_WcBands.z, 1.0h, atten);
    half3 direct = s.Albedo * _LightColor0.rgb * WcBand(lit);

    half3 colour = direct;

#ifdef UNITY_PASS_FORWARDBASE
    // Hemisphere ambient, added once. In the add pass this would be counted
    // again for every light, and the field has one.
    half up = s.Normal.y * 0.5h + 0.5h;
    colour += s.Albedo * lerp(_WcEarthLight.rgb, _WcSkyLight.rgb, up);

    // The rim is the cut edge of the paper catching the light: strongest where
    // the surface turns away from the camera, and only on the side the light is
    // actually on, so it reads as one light source and not as a glow.
    half rim = 1.0h - saturate(dot(s.Normal, viewDir));
    colour += _WcRim.rgb * (pow(rim, 3.0h) * _WcRim.a * saturate(ndl));
#endif

    return fixed4(colour, s.Alpha);
}

// ---------------------------------------------------------------------------
// Atmosphere: the fog of war, then the distance haze. Run from every shader's
// finalcolor function, after the colour is otherwise final.
// ---------------------------------------------------------------------------

inline half WcSeen(float3 worldPos)
{
    float2 uv = worldPos.xz * _WcFogParams.x;
    half seen = tex2D(_WcFog, uv).r;
    return lerp(1.0h, seen, _WcFogParams.y);
}

inline void WcAtmosphere(float3 worldPos, inout fixed4 colour)
{
    colour.rgb = lerp(_WcFogTint.rgb, colour.rgb, saturate(WcSeen(worldPos)));

    float distance = length(worldPos - _WorldSpaceCameraPos);
    half haze = saturate((distance - _WcHazeRange.x) * _WcHazeRange.y) * _WcHaze.a;
    colour.rgb = lerp(colour.rgb, _WcHaze.rgb, haze);
}

#endif
