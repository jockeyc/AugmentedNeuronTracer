Shader "VolumeRendering/FixedThreshold"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertDefault
            #pragma fragment Frag
            #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"
            float _BlendMultiply;
            float4 _Color;
            float4x4 _InverseProjectionMatrix;
            float4x4 _InverseViewMatrix;
            float4x4 _WorldToLocalMatrix;
            float4x4 _TransposeObjectToWorldMatrix;

            TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
			TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
			sampler3D _Volume; 
			sampler3D _Mask; 
            float _viewThreshold;

            float4 _CameraDepthTexture_TexelSize;

            float4 GetWorldSpacePosition(float depth, float2 uv)
            {
                // 屏幕空间 --> 视锥空间
                float4 view_vector = mul(_InverseProjectionMatrix, float4(2.0 * uv - 1.0, depth, 1.0));
                view_vector.xyz /= view_vector.w;
                //视锥空间 --> 世界空间
                float4x4 l_matViewInv = _InverseViewMatrix;
                float4 world_vector = mul(l_matViewInv, float4(view_vector.xyz, 1));
                return world_vector;
            }

            float4 RayMarching(float3 startPoint, float3 direction, float dstToBox, float dstLimit) 
            {
                float3 p = startPoint + direction * dstToBox;
                float alpha = 0;
                float3 col = 0;
                float dt = 0.001;
              //  for(int t = 0; t< 500000; t ++){
              //      float4 uv = float4(p + 0.5, 1);
              //      float val = tex3Dlod(_Volume, uv);
              //      float masked = tex3Dlod(_Mask, uv);
              //      if(masked) val = 0;
              //      if(val>=_viewThreshold){
              //          col.b = max(val,col.b);
              //      }
              //      else{
              //          col.r = max(val,col.r);
              //      }
              //      alpha = max(alpha,val);
                    
              //      if(alpha >= 0.98 || t * dt > dstLimit)
              //      {
              //          break;
              //      }

		            //p += direction * dt;
              //  }                
                for(int t = 0; t< 500000; t ++){
                    float4 uv = float4(p + 0.5, 1);
                    float val = tex3Dlod(_Volume, uv);
                    float masked = tex3Dlod(_Mask, uv);
                    if(masked) {
                        //val = 0;
                        col.g = 1;
                    }
                    if(val>=_viewThreshold){
                        col.b = max(val,col.b);
                    }
                    else{
                        col.r = max(val,col.r);
                    }
                    alpha = max(alpha,val);
                    
                    if(alpha >= 0.98 || t * dt > dstLimit)
                    {
                        break;
                    }

		            p += direction * dt;
                }
                return float4(col.r,col.g,col.b,alpha);
            }

            float2 rayBoxDst(float3 boundsMin, float3 boundsMax, 
                            //世界相机位置      光线方向倒数
                            float3 rayOrigin, float3 invRaydir) 
            {
                float3 t0 = (boundsMin - rayOrigin) * invRaydir;
                float3 t1 = (boundsMax - rayOrigin) * invRaydir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);

                float dstA = max(max(tmin.x, tmin.y), tmin.z); //进入点
                float dstB = min(tmax.x, min(tmax.y, tmax.z)); //出去点

                float dstToBox = max(0, dstA);
                float dstInsideBox = max(0, dstB - dstToBox);
                return float2(dstToBox, dstInsideBox);
            }

            float4 Frag(VaryingsDefault i) : SV_Target
            {
                //screen depth
                float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoordStereo);

                float4 worldPos = GetWorldSpacePosition(depth, i.texcoord);
                float4 localPos = mul(_WorldToLocalMatrix, float4(worldPos.xyz,1));
                float3 rayPos = _WorldSpaceCameraPos;
                //相机到每个像素的世界方向
                float3 worldViewDir = normalize(worldPos.xyz - rayPos.xyz);

                float3 localRayPos = mul(_WorldToLocalMatrix,float4(rayPos,1)).xyz;
                float3 localViewDir = normalize(localPos.xyz - localRayPos.xyz);

                float3 boundsMin = -0.5f * float3(1,1,1);
                float3 boundsMax = 0.5f * float3(1,1,1);

                float depthEyeLinear = distance(localPos.xyz,localRayPos);
                
                float2 rayToContainerInfo = rayBoxDst(boundsMin, boundsMax, localRayPos, (1 / localViewDir));
                float dstToBox = rayToContainerInfo.x; //相机到容器的距离
                float dstInsideBox = rayToContainerInfo.y; //返回光线是否在容器中
                //相机到物体的距离 - 相机到容器的距离，这里跟 光线是否在容器中 取最小，过滤掉一些无效值
                float dstLimit = min(depthEyeLinear - dstToBox, dstInsideBox);
                float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
                if(dstLimit>0)
                {
                    float4 result =  RayMarching(localRayPos,localViewDir,dstToBox,dstLimit);
                    result.a = min(0.85,result.a);
                    result.a = max(0.1,result.a);
                    if(result.g ==1) result = float4(0,result.b,0,result.a);
                    else if(result.b >= result.r) result = float4(result.b,result.b,result.b,result.a);
                    else result = float4(result.r,0,0,result.a);
                    //if(color.r>0||color.b>0||color.g>0)
                    //{
                    //    color = float4(result.rgb * result.a + color.rgb * (1-result.a),1);
                    //}
                    //else
                    //{
                    //    color = result;
                    //}
                    if(result.a>_viewThreshold)
                    {                    
                        if(result.a<0.1)
                        {
                            float3 base = lerp(float3(1,1,1),float3(0.5,0.7,0.9),(result.a)/0.1);
                            color = float4(result.a * base + (1-result.a)*color.rgb,1);
                        }
                        else if(result.a>0.1 && result.a<0.5)
                        {
                            float3 base = lerp(float3(0.5,0.7,0.9),float3(0.6,0.7,1),(result.a-0.1)/0.4);
                            color = float4(result.a * base + (1-result.a)*color.rgb,1);
                        }
                        else
                        {
                            float3 base = lerp(float3(0.6,0.7,1),float3(1,1,1),(result.a-0.5)/0.5);
                            color = float4(result.a * base + (1-result.a)*color.rgb,1);
                        }
                    }
                    else{
                        color = float4(result.a * float3(1,1,1) + (1-result.a)*color.rgb,1);
                    }
                }
                return color;
            }
            ENDHLSL
        }
    }
}