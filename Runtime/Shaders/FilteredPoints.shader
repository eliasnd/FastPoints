Shader "Custom/FilteredPoints" {
    Properties {
        _PointSize ("Point Size", float) = 0.05
        [HideInInspector] _MainTex ("Texture", 2D) = "white" {}
        [HideInInspector] _CloudTex ("Texture", 2D) = "white" {}
        [HideInInspector] _CloudDepthTexture ("Texture", 2D) = "white" {}
        // [HideInInspector] _CameraDepthTexture ("Texture", 2D) = "white" {}
    }
    Subshader {
        Tags { "RenderType"="Opaque" }
        
        Pass {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;
            sampler2D _CloudTex;
            Texture2D<float> _CloudDepthTexture;

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                float sceneDepth = Linear01Depth(tex2D(_CameraDepthTexture, i.uv).r) * _ProjectionParams.z;
                float cloudDepth = _CloudDepthTexture[i.uv];
                return sampler2D(_CloudTex).r;
                // if (sceneDepth < cloudDepth)
                //     return tex2D(_MainTex, i.uv);
                // else
                //     return _CloudTex[i.uv];
            }

            ENDCG


        }
    }
}