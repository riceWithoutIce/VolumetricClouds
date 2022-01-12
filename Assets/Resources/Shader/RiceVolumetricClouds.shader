// ref: https://zhuanlan.zhihu.com/p/248406797
// 体积云

Shader "Hidden/PostProcessing/RiceVolumetricClouds"
{
    HLSLINCLUDE

        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        #pragma multi_compile __ _VC_DOWNSAMPLE

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        float4 _MainTex_TexelSize;

        // unity depth texture
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        half4 _CameraDepthTexture_TexelSize;
        // down sample 
        TEXTURE2D_SAMPLER2D(_CloudsDownSampleDepthTex, sampler_CloudsDownSampleDepthTex);
        TEXTURE2D_SAMPLER2D(_CloudsDownSampleColor, sampler_CloudsDownSampleColor);

        float3 _CloudsBoundsMin, _CloudsBoundsMax;

        half _CloudsRayStepCount;
        
        TEXTURE3D_SAMPLER3D(_CloudsNoiseTex, sampler_CloudsNoiseTex);
        half _CloudsNoiseTexScale;
        half3 _CloudsNoiseSpeed;

        // shade
        // light
        half _LightAbsorptionTowardSun, _LightAbsorptionThroughCloud;
        // color
        half4 _ColA, _ColB;

        // hg phase
        half _MieScatter;

        // light step
        half _LightingSteps;

        // shape
        sampler2D _WeatherMap;
        half _WeatherMapSize;
        half _HeightWeights;
        half _ShapeNoiseWeight;
        half _DensityOffset;
        half _EdgeFadeDst;
        half _DensityMultiplier;
        half4 _CloudsSpeed;

        // detail
        TEXTURE3D_SAMPLER3D(_CloudsNoiseTexDetail, sampler_CloudsNoiseTexDetail);
        half _CloudsNoiseDetailTexScale;
        half _CloudsNoiseTexStrength;
        half3 _CloudsNoiseDetailSpeed;
        half _ShapeNoiseDetailWeights;
        half _NoiseDetailWeight;

        // blue noids
        sampler2D _CloudsBlueNoise;
        float4 _CloudsBlueNoiseCoords;
        half _CloudsBlueNoiseScale;
        half _CloudsBlueNoiseStrength;

        // history
        TEXTURE2D_SAMPLER2D(_CloudsHistoryTex, sampler_CloudsHistoryTex);
        half _CloudsSample;

        half4 _WorldSpaceLightPos0;
        half4 _LightColor0;
        float4x4 unity_CameraToWorld;

        //--------------------------------------------------------------------------
        // 深度图重建世界坐标
        inline float4 GetWorldPositionFromDepthValue(half2 uv, half linearDepth) 
        {
            float camPosZ = _ProjectionParams.y + (_ProjectionParams.z - _ProjectionParams.y) * linearDepth;

            // unity_CameraProjection._m11 = near / t，其中t是视锥体near平面的高度的一半。
            // 投影矩阵的推导见：http://www.songho.ca/opengl/gl_projectionmatrix.html。
            // 这里求的height和width是坐标点所在的视锥体截面（与摄像机方向垂直）的高和宽，并且
            // 假设相机投影区域的宽高比和屏幕一致。
            float height = 2 * camPosZ / unity_CameraProjection._m11;
            float width = _ScreenParams.x / _ScreenParams.y * height;

            float camPosX = width * uv.x - width / 2;
            float camPosY = height * uv.y - height / 2;
            float4 camPos = float4(camPosX, camPosY, camPosZ, 1.0);
            return mul(unity_CameraToWorld, camPos);
        }

        // 使用 HG phase function 来代替复杂的米氏散射
        half hg(half a, half g) 
        {
            float g2 = g * g;
            return (1 - g2) / (4 * 3.1415 * pow(1 + g2 - 2 * g * (a), 1.5));
        }

        half phase(half a) 
        {
            // half blend = 0.5;
            half hgBlend = saturate(hg(a, _MieScatter));// * (1 - blend) + hg(a, -_PhaseParams.y) * blend;
            // return _PhaseParams.z + hgBlend * _PhaseParams.w;
            return hgBlend;
        }

        // remap
        float remap(float original_value, float original_min, float original_max, float new_min, float new_max)
        {
            return new_min + (((original_value - original_min) / (original_max - original_min)) * (new_max - new_min));
        }

        // 包围盒计算
        // nvdia改进算法: http://jcgt.org/published/0007/03/04/
        // bounds min value
        // bounds max value
        // camera position
        // inv ray dir
        // 返回 x: 相机到box的距离 y: box内的距离
        float2 rayBoxDst(float3 boundsMin, float3 boundsMax, float3 rayOri, float3 invRayDir)
        {
            float3 t0 = (boundsMin - rayOri) * invRayDir;
            float3 t1 = (boundsMax - rayOri) * invRayDir;
            float3 tmin = min(t0, t1);
            float3 tmax = max(t0, t1);

            float dstA = max(max(tmin.x, tmin.y), tmin.z); //进入点
            float dstB = min(tmax.x, min(tmax.y, tmax.z)); //出去点

            float dstToBox = max(0, dstA);
            float dstInsideBox = max(0, dstB - dstToBox);

            return float2(dstToBox, dstInsideBox);
        }

        // sample 3d tex
        half sampleDensity(float3 rayPos, float3 boundsCenter, float3 size)
        {
            // noise 1
            float3 uvw = rayPos * _CloudsNoiseTexScale;
            uvw += _Time.x * _CloudsNoiseSpeed;
            half4 noise = SAMPLE_TEXTURE3D(_CloudsNoiseTex, sampler_CloudsNoiseTex, uvw);

            // uv
            float2 uv = (size.xz * 0.5f + (rayPos.xz - boundsCenter.xz)) / max(size.x, size.z);
        
            // weather map
            half weatherMap = tex2D(_WeatherMap, uv * _WeatherMapSize + _Time.x * _CloudsSpeed.xy + (noise.x * 2 - 1) * _CloudsNoiseTexStrength).x;

            // soft bottom
            half gMin = remap(weatherMap, 0, 1, 0.1, 0.6);
            float heightPercent = (rayPos.y - _CloudsBoundsMin.y) / size.y;

            float heightGradient = saturate(remap(heightPercent, 0.0, weatherMap.r, 1, 0)) * saturate(remap(heightPercent, 0.0, gMin, 0, 1));

            // edge fade
            float dstEdgeX = min(_EdgeFadeDst, min(rayPos.x - _CloudsBoundsMin.x, _CloudsBoundsMax.x - rayPos.x));
            float dstEdgeZ = min(_EdgeFadeDst, min(rayPos.z - _CloudsBoundsMin.z, _CloudsBoundsMax.z - rayPos.z));
            float edgeWeight = min(dstEdgeZ, dstEdgeX) / _EdgeFadeDst;
            heightGradient *= edgeWeight;

            // float4 normalizeShapeWeights = _ShapeNoiseWeight / dot(_ShapeNoiseWeight, 1);
            // 根据weather map 插值 noise.xyzw
            // half weight = saturate(pow(weatherMap, _ShapeNoiseWeight));
            // half4 normalizeShapeWeights = normalize(half4((1- weight), saturate(weight - 2 / 3), saturate(weight - 1 / 3), weight));
            // half shapeFBM = dot(noise, normalizeShapeWeights) * heightGradient;
            noise = lerp(1, noise, saturate(_ShapeNoiseWeight * weatherMap));
            half shapeFBM = noise * heightGradient;
            half baseShapeDensity = saturate(shapeFBM + _DensityOffset);

            // detail
            if (baseShapeDensity > 0)
            {
                float3 uvwDetail = rayPos * _CloudsNoiseDetailTexScale;
                uvwDetail += _Time.x * _CloudsNoiseDetailSpeed;
                half4 detailNoise = SAMPLE_TEXTURE3D(_CloudsNoiseTexDetail, sampler_CloudsNoiseTexDetail, uvwDetail);
                float detailFBM = pow(detailNoise.r, _ShapeNoiseDetailWeights);
                float oneMinusShape = saturate(1 - baseShapeDensity);
                float cloudDensity = baseShapeDensity - detailFBM * oneMinusShape * _NoiseDetailWeight;

                return saturate(cloudDensity * _DensityMultiplier);
            }

            return 0;
        }

        // light march
        half3 lightmarch(float3 position, float3 boundsCenter, float3 size, half blueNoise)
        {
            position += blueNoise;

            half3 dir2Light = _WorldSpaceLightPos0.xyz;
            float dstInsideBox = rayBoxDst(_CloudsBoundsMin, _CloudsBoundsMax, position, 1 / dir2Light).y;
            float stepSize = dstInsideBox / _LightingSteps;
            float sum = 0;

            // march
            [unroll(16)]
            for (int i = 0; i < _LightingSteps; i++)
            {
                position += dir2Light * stepSize;
                sum += max(0, sampleDensity(position, boundsCenter, size) * stepSize);
            }

            // sum = max(0, sampleDensity(position, boundsCenter, size));
            // sum += max(0, sampleDensity(position + dir2Light * dstInsideBox, boundsCenter, size));

            half transmittance = saturate(exp(-sum * _LightAbsorptionTowardSun));

            half3 cloudColor = lerp(_ColB, _ColA, transmittance) * _LightColor0;
            return cloudColor;

            // 将亮->暗映射为 3段颜色, 亮->灯光颜色 中->ColorA 暗->ColorB
            // float3 cloudColor = lerp(_ColA, _LightColor0, saturate(transmittance * _ColorOffset1));
            // cloudColor = lerp(_ColB, cloudColor, saturate(pow(transmittance * _ColorOffset2, 3)));
            
            // float3 lightTransmittance = lerp(_DarknessThreshold, cloudColor, (1 - _DarknessThreshold));// _DarknessThreshold + transmittance * (1 - _DarknessThreshold) * cloudColor;
            // return lightTransmittance;
        }

        // dstLimit: raymarch, entryPoint: 入射坐标, rayDir: 射线方向, phaseVal: 米氏散射, blueNoise: blue noise
        float4 CloudRayMarching(float dstLimit, float3 entryPoint, half3 rayDir, half phaseVal, half blueNoise)
        {
            // float dstInsideBox = rayBoxDst(_CloudsBoundsMin, _CloudsBoundsMax, entryPoint, 1 / rayDir).y;
            float dstInsideBox = dstLimit;
            float stepSize = dstInsideBox / _CloudsRayStepCount;
            float stepLength = exp(1 / _CloudsRayStepCount) * stepSize;

            half sum = 1;
            float dstTravelled = blueNoise;
            half3 lightEnergy = 0;
            float3 boundsCenter = (_CloudsBoundsMin + _CloudsBoundsMax) * 0.5;
            float3 size = _CloudsBoundsMax - _CloudsBoundsMin;

            [unroll(32)]
            for (int i = 0; i < _CloudsRayStepCount; i++)
            {
                if (dstTravelled < dstLimit)
                {
                    float3 rayPos = entryPoint + (rayDir * dstTravelled);
                    half density = sampleDensity(rayPos, boundsCenter, size);
                    if (density > 0)
                    {
                        half3 lightTransmittance = lightmarch(rayPos, boundsCenter, size, blueNoise);
                        lightEnergy += density * stepSize * sum * lightTransmittance * phaseVal;
                        sum *= exp(-density * stepSize * _LightAbsorptionThroughCloud);
                        if (sum < 0.01f)
                            break;

                    }
                }
                dstTravelled += stepSize;
            }

            sum = 1 - saturate(sum);
            lightEnergy = saturate(lightEnergy);
            return float4(lightEnergy, sum);
        }

        half4 Frag(VaryingsDefault i) : SV_Target
        {   
            // down sample 
            // #if _VC_DOWNSAMPLE
                // half depth = SAMPLE_DEPTH_TEXTURE(_CloudsDownSampleDepthTex, sampler_CloudsDownSampleDepthTex, i.texcoordStereo);
            // #else
                half depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoordStereo);
            // #endif
            
            half linearDepth = LinearEyeDepth(depth);
            depth = Linear01Depth(depth);

            half4 color = SAMPLE_TEXTURE2D(_CloudsDownSampleColor, sampler_CloudsDownSampleColor, i.texcoord);

            // world pos
            float3 wsPos = GetWorldPositionFromDepthValue(i.texcoord, depth);
            // ray dir
            half3 rayDir = wsPos - _WorldSpaceCameraPos.xyz;
            float depthEyeLinear = length(rayDir);
            rayDir = normalize(rayDir);

            float2 rayInfo = rayBoxDst(_CloudsBoundsMin, _CloudsBoundsMax, _WorldSpaceCameraPos.xyz, 1 / rayDir);
            float dst2Box = rayInfo.x;
            float dstInsideBox = rayInfo.y;

            // 获得实际raymarch的距离
            // 最大raymarch距离为 dstInsideBox 
            float dstLimit = min(depthEyeLinear - dst2Box, dstInsideBox);

            // 射线入点
            float3 entryPoint = _WorldSpaceCameraPos.xyz + rayDir * dst2Box;

            // 散射
            half rdotl = dot(rayDir, _WorldSpaceLightPos0.xyz);
            rdotl = saturate(rdotl);
            half phaseVal = phase(rdotl);

            // blue noise
            half blueNoise = tex2D(_CloudsBlueNoise, i.texcoord * _CloudsBlueNoiseCoords.xy + _CloudsBlueNoiseCoords.zw).a;
            // return blueNoise;
            blueNoise = blueNoise * 2 - 1;
            blueNoise *= _CloudsBlueNoiseStrength;

            // ray marching
            half4 cloud = CloudRayMarching(dstLimit, entryPoint, rayDir, phaseVal, blueNoise);

            // 时间混合
            // half4 history = SAMPLE_TEXTURE2D(_CloudsHistoryTex, sampler_CloudsHistoryTex, i.texcoord);
            half4 history = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            half blend = 1.0f / (_CloudsSample + 1.0f);
            cloud = lerp(history, cloud, blend);

            return cloud;
        }

        // 深度降采样，用于优化降采样导致的边缘锯齿
        // ref: https://developer.nvidia.com/gpugems/gpugems3/part-iv-image-effects/chapter-23-high-speed-screen-particles
        float4 DownsampleDepth(VaryingsDefault i) : SV_Target
        {
            float2 texSize = 0.5f * _CameraDepthTexture_TexelSize.xy;

            float2 taps[4] = { 	
                float2(i.texcoord + float2(-1,-1) * texSize),
                float2(i.texcoord + float2(-1,1) * texSize),
                float2(i.texcoord + float2(1,-1) * texSize),
                float2(i.texcoord + float2(1,1) * texSize)};


            float depth1 = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, taps[0]);
            float depth2 = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, taps[1]);
            float depth3 = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, taps[2]);
            float depth4 = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, taps[3]);

            float result = min(depth1, min(depth2, min(depth3, depth4)));

            return result;
        }

        half4 FragCombine(VaryingsDefault i) : SV_Target
        {
            half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            half4 cloudsColor = SAMPLE_TEXTURE2D(_CloudsDownSampleColor, sampler_CloudsDownSampleColor, i.texcoord);

            color.rgb = lerp(color.rgb, cloudsColor.rgb, cloudsColor.a);

            return color;
        }

    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            // Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM

                #pragma vertex VertDefault
                #pragma fragment Frag

            ENDHLSL
        }

        // down sample
        Pass
        {
            HLSLPROGRAM

                #pragma vertex VertDefault
                #pragma fragment DownsampleDepth

            ENDHLSL
        }

        // combine
        Pass
        {
            HLSLPROGRAM

                #pragma vertex VertDefault
                #pragma fragment FragCombine

            ENDHLSL
        }
    }
}
