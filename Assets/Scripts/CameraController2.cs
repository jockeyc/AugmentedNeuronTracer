using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController2 : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        StartCoroutine(MoveCamera());
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    IEnumerator MoveCamera()
    {
        
        Vector3 basePos = new(-0.0812f, -0.0812f, -0.667f);
        Camera cam = Camera.main;
        GameObject cube = GameObject.Find("Cube");
        Material material = cube.GetComponent<MeshRenderer>().material;
        for(int i=0;i<9;i++)
            for(int j=0;j<9;j++)
            {
                cam.transform.position = basePos + new Vector3(0.0203f * i, 0.0203f * j, 0);
                cam.transform.LookAt(GameObject.Find("Cube").transform);
                material.SetFloat("_IsDepth", 0);
                ScreenCapture.CaptureScreenshot($"ScreenShot/Color_{i}_{j}.png");
                yield return new WaitForSeconds(0.1f);
                material.SetFloat("_IsDepth", 1);
                ScreenCapture.CaptureScreenshot($"ScreenShot/Depth_{i}_{j}.png");
                yield return new WaitForSeconds(0.1f);
            }
    }
}
