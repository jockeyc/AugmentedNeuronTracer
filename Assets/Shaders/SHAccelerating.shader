Shader "Unlit/SHAccelerating"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _VolumeScale("VolumeScale", Vector) = (1,1,1,1)
        _Dimensions("Dimensions", Vector) = (512,512,512)
        _BlockSize("BlockSize", int) = 8
        _Volume("Volume", 3D) = "white" {}
        _OccupancyMap("OccupancyMap", 3D) = "white" {}
        _SH0("SH0", 3D) = "white" {}
        _SH1("SH1", 3D) = "white" {}
        _SH2("SH2", 3D) = "white" {}
        _SH3("SH3", 3D) = "white" {}

    }
        SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100
        Cull Front

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "SHCommon.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 vray_dir : TEXCOORD0;
                float3 transformed_eye : float3;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _VolumeScale;
            float3 _Dimensions;
            int _BlockSize;
            sampler3D _Volume;
            sampler3D _OccupancyMap;
            sampler3D_float _SH0;
            sampler3D_float _SH1;
            sampler3D_float _SH2;
            sampler3D_float _SH3;

            v2f vert(appdata v)
            {
                v2f o;
                float3 volume_translation = float3(0.5,0.5,0.5) - _VolumeScale.xyz * 0.5;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.transformed_eye = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos,1)).xyz;
                o.vray_dir = v.vertex.xyz - o.transformed_eye;
                return o;
            }

            float2 intersect_box(float3 start, float3 dir) {
                float3 box_min = float3(-0.5f,-0.5f,-0.5f);
                float3 box_max = float3(0.5f,0.5f,0.5f);
                float3 inverse_dir = 1.0f / dir;
                float3 tmin_temp = (box_min - start) * inverse_dir;
                float3 tmax_temp = (box_max - start) * inverse_dir;
                float3 tmin = min(tmin_temp, tmax_temp);
                float3 tmax = max(tmin_temp, tmax_temp);
                float t0 = max(tmin.x, max(tmin.y, tmin.z));
                float t1 = min(tmax.x, min(tmax.y, tmax.z));
                return float2(t0, t1);
            }

            fixed4 frag(v2f i) : SV_Target
            {

                float3 ray_dir = normalize(i.vray_dir);
                float2 t_hit = intersect_box(i.transformed_eye, ray_dir);

                if (t_hit.x > t_hit.y) discard;

                t_hit.x = max(t_hit.x, 0);

                float3 dt_vec = 1 / float3(float3(512,512,512) * abs(ray_dir));
                float dt = min(dt_vec.x, min(dt_vec.y, dt_vec.z));
                float3 deltaT = ray_dir * dt;
                float3 deltaM = deltaT;
                float3 deltauM = deltaM * _Dimensions / _BlockSize;
                float3 deltauM_inv = 1 / deltauM;


                float col = 0;
                float alpha = 0;
                float3 start = i.transformed_eye + t_hit.x * ray_dir;
                float3 end = i.transformed_eye + t_hit.y * ray_dir;
                float dist = distance(start,end);
                float3 p = start;

                for (int t = 0; t < 1000; t++) {
                    float3 uv = p + 0.5;
                    float isOccupied = tex3Dlod(_OccupancyMap,float4(uv,0));
                    if (isOccupied > 0) {
                        float val = tex3Dlod(_Volume,float4(uv,0));
                        col = max(col,val);
                        alpha = max(alpha,val);
                        if (alpha >= 0.98 || distance(p,start) > dist) {
                            break;
                        }
                            p += deltaM;
                    }
                    else {
                        float4 C0 = tex3Dlod(_SH0, float4(uv, 0));
                        float4 C1 = tex3Dlod(_SH1, float4(uv, 0));
                        float4 C2 = tex3Dlod(_SH2, float4(uv, 0));
                        float4 C3 = tex3Dlod(_SH3, float4(uv, 0));
                        float D = C0.x * GetY00(ray_dir) + C0.y * GetY10(ray_dir) + C0.z * GetY1p1(ray_dir) + C0.w * GetY1n1(ray_dir)
                            + C1.x * GetY20(ray_dir) + C1.y * GetY2p1(ray_dir) + C1.z * GetY2n1(ray_dir) + C1.w * GetY2p2(ray_dir)
                            + C2.x * GetY2n2(ray_dir) + C2.y * GetY30(ray_dir) + C2.z * GetY3p1(ray_dir) + C2.w * GetY3n1(ray_dir)
                            + C3.x * GetY3p2(ray_dir) + C3.y * GetY3n2(ray_dir) + C3.z * GetY3p3(ray_dir) + C3.w * GetY3n3(ray_dir);
                        float3 uM = uv * _Dimensions / _BlockSize;
                        float3 rM = -frac(uM);
                        //float D = tex3D(_DistanceMap, uv) * 64;
                        //float D = 1;
                        float3 deltai = ceil((step(0,-deltauM) + sign(deltauM) * D + rM) / deltauM);
                        //float3 deltai = ceil((step(0,-deltauM)+sign(deltauM)*D+rM)*deltauM_inv);

                        float step = max(min(min(deltai.x,deltai.y),deltai.z),1);
                        p += step * deltaM;
                    }

                    if (alpha >= 0.98 || distance(p,start) > dist) {
                        break;
                    }
                }
                return fixed4(col,col,col,alpha);
            }
            ENDCG
        }
    }
}
