using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SphericalHarmonicCompute : MonoBehaviour
{
    [SerializeReference] public ComputeShader shader;
    [SerializeReference] public int BlockSize;
    [SerializeReference] public Vector3Int Dimensions;
    [SerializeReference] public Texture3D OccupancyMap;
    private RenderTexture resultTexture;
    private Vector3Int Dmap;
    // Start is called before the first frame update

    void Start()
    {
        Dmap = Dimensions / BlockSize;
        computeSphericalHarmonic();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void computeSphericalHarmonic()
    {
        int kernelKey = shader.FindKernel("CSMain");
        ComputeBuffer resultBuffer = new ComputeBuffer(Dmap.x * Dmap.y * Dmap.z * 16, 4);
        shader.SetFloats("dM", Dmap.x, Dmap.y, Dmap.z);
        shader.SetTexture(kernelKey, "OccupancyMap", OccupancyMap);
        shader.SetBuffer(kernelKey, "Result", resultBuffer);

        shader.Dispatch(kernelKey, Dmap.x / 8, Dmap.y / 8, Dmap.z / 8);

        float[] bufferArray = new float[resultBuffer.count];
        resultBuffer.GetData(bufferArray);
        resultBuffer.Release();

        


        for (int i = 0; i < 4; i++)
        {
            float[] tempBuffer = bufferArray.Skip(Dmap.x * Dmap.y * Dmap.z * 4 * i).Take(Dmap.x * Dmap.y * Dmap.z * 4).ToArray();
            Texture3D texture3D = new Texture3D(Dmap.x, Dmap.y, Dmap.z, TextureFormat.RGBAFloat, false);
            texture3D.filterMode = FilterMode.Point;
            texture3D.wrapMode = TextureWrapMode.Clamp;
            texture3D.SetPixelData(tempBuffer, 0);

            string fileName = "Assets/Textures/" + "Spherical" + i + ".Asset";
            Debug.Log(fileName);
            AssetDatabase.DeleteAsset(fileName);
            AssetDatabase.CreateAsset(texture3D, fileName);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        


        return;


 
    }
}
