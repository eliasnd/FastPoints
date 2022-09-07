Shader "Custom/DefaultPoint" {
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

            // StructuredBuffer<Point> _PointBuffer;
            StructuredBuffer<float3> _Positions;
            StructuredBuffer<uint> _Colors;

            struct v2f {
                float4 pos : SV_POSITION;
                half psize : PSIZE;
                half4 col : COLOR;
            };

            v2f vert(uint vid : SV_VertexID) {
                v2f o;
                // Point p = _PointBuffer[vid];
                float3 pos = _Positions[vid];
                uint icol = _Colors[vid];
                half4 col = half4(
                    ((icol      ) & 0xff) / 255.0, 
                    ((icol >>  8) & 0xff) / 255.0,
                    ((icol >> 16) & 0xff) / 255.0,
                    ((icol >> 24) & 0xff) / 255.0
                );

                o.pos = UnityObjectToClipPos(mul(_Transform, float4(pos, 1)));
                o.col = col;
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
