// FastPoints
// Copyright (C) 2023  Elias Neuman-Donihue

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
            float3 _Offset;
            float3 _Scale;

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
                float3 pos = (_Positions[vid] + _Offset) * _Scale;
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
