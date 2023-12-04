// using System;
// using System.IO.Compression;
// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;
// using Material = GLTFast.Schema.Material;
//
// public class CameraSetting : MonoBehaviour {
//  
//     Camera cam = null;
//     public RenderTexture colorRT;
//     public RenderTexture depthRT;
//     public RenderTexture m_DepthTex;
//
//     public GameObject Cube;
//     // Use this for initialization
//     void Start () {
//         cam = this.GetComponent<Camera>();
//         //cam.depthTextureMode = DepthTextureMode.Depth;
//         colorRT = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default);
//         colorRT.name = "ColorRT";
//         depthRT = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.Depth);
//         depthRT.name = "DepthRT";
//
//        // Cube.GetComponent<MeshRenderer>().material.mainTexture = colorRT;
//     }
// 	
//     // Update is called once per frame
//     void Update () {
//  
//     }
//     private void OnPreRender() {
//         cam.SetTargetBuffers(colorRT.colorBuffer,depthRT.depthBuffer);
//     }
//     //
//     private void OnRenderImage(RenderTexture src, RenderTexture dest)
//     {
//         Graphics.Blit(colorRT,dest);
//         // if (Config.Instance != null)
//         // {
//         //     Config.Instance.volumeRendering.depth.value = depthRT;
//         //     Debug.Log("changed");
//         // }
//     }
//     //
//     // private void OnPostRender()
//     // {
//     //     if (Config.Instance != null)
//     //     {
//     //         Debug.Log(depthRT); 
//     //         Config.Instance.volumeRendering.depth.value = depthRT;
//     //         Debug.Log("changed");
//     //     }
//     // }
// }