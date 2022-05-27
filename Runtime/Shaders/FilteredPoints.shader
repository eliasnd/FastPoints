Shader "Custom/FilteredPoints" {
    Properties {
        _PointSize ("Point Size", float) = 0.05
    }
    Subshader {
        Tags { "RenderType"="Opaque" }
        Pass {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct Point {
                float3 pos;
                half4 col;
            };

            float _PointSize;
            float4x4 _Transform;
            int _ScreenWidth;

            StructuredBuffer<float4> _Positions;
            StructuredBuffer<half4> _Colors;

            struct v2f {
                float4 pos : SV_POSITION;
                half psize : PSIZE;
                half4 col : COLOR;
            };

            v2f vert(uint vid : SV_VertexID) {
                v2f o;
                o.pos = _Positions[vid];
                o.col = _Colors[vid];
                o.psize = _PointSize;
                return o;
            }

            half4 frag(v2f i) : SV_Target {
                return i.col;
            }

            ENDCG


        }
    }
}