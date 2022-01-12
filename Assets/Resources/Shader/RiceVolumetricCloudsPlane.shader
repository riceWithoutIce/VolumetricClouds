Shader "Rice/RiceVolumetricCloudsPlane"
{
    Properties
    {
        _Color0 ("Color0", Color) = (1, 1, 1, 1)
        _Color1 ("Color1", Color) = (0.5, 0.5, 0.5, 1)
        _CloudsTex ("R: right, G: top, B: left, A: bottom", 2D) = "white" {}
        _CloudsAlpha ("R: alpha", 2D) = "white" {}
        _ColorScale ("Color Scale", Range(0, 5)) = 1
        
        _Inscatter ("Inscatter", Range(0, 10)) = 1
        _InscatterExponent ("Inscatter exponent", Range(0, 64)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IngoreProjector"="True" }
        LOD 100

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            Cull Back
			Blend SrcAlpha OneMinusSrcAlpha 

            CGPROGRAM
            // #pragma multi_compile_fwdbase

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
            };

            sampler2D _CloudsTex;
            sampler2D _CloudsAlpha;
            float4 _CloudsTex_ST;
            half4 _Color0, _Color1;
            half _ColorScale;

            half _Inscatter;
            half _InscatterExponent;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _CloudsTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                half3 lightDir = normalize(UnityWorldSpaceLightDir(i.worldPos));

                fixed4 cloud0 = tex2D(_CloudsTex, i.uv);
                fixed alpha = tex2D(_CloudsAlpha, i.uv).r;
                // fixed4 cloud1 = tex2D(_CloudsTex1, i.uv);
                fixed front = (cloud0.r + cloud0.g + cloud0.b + cloud0.a) * 0.25f;
                front = pow(front, 0.625);
                fixed back = 1 - front;
                back = saturate(0.25 * (1.0 - i.worldNormal.x) + 0.5 * (cloud0.r + cloud0.g + cloud0.b + cloud0.a));

                float hMap = (lightDir.x > 0.0f) ? cloud0.x : cloud0.z;   // Picks the correct horizontal side.
                float vMap = (lightDir.y > 0.0f) ? cloud0.y : cloud0.w;   // Picks the correct Vertical side.
                float dMap = (lightDir.z > 0.0f) ? back : front;          // Picks the correct Front/back Pseudo Map
                float lightMap = hMap * lightDir.x * lightDir.x + vMap * lightDir.y * lightDir.y + dMap * lightDir.z * lightDir.z; // Pythagoras!
                lightMap = pow(lightMap, _ColorScale);

                // ray dir
                float3 rayDir = i.worldPos - _WorldSpaceCameraPos;
                rayDir = normalize(rayDir);
                half inscatter = pow(saturate(dot(rayDir, lightDir)), _InscatterExponent * (1 - back)) * _Inscatter;
                
                fixed4 col;
                col = lerp(_Color1, _Color0, lightMap);
                col.rgb *= _LightColor0.rgb;
                col.rgb = lerp(col.rgb, _LightColor0.rgb, inscatter);
                col.a *= alpha;

                return col;
            }
            ENDCG
        }
    }

    FallBack "Transparent/VertexLit"
}
