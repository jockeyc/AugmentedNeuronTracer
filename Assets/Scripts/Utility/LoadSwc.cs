using Microsoft.MixedReality.Toolkit.SpatialManipulation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class LoadSwc : MonoBehaviour
{
    public enum RadiusMode { line, origin, generate}
    private Dictionary<int, Vector3> swcList;
    private Dictionary<int, float> radiusList;
    private Dictionary<int, int> parentList;
    private Dictionary<int, int> typeList;
    private Dictionary<int, float> batchDict;
    private Vector3 swc_average;
    //private Dictionary<int, int> swc_map;
    float scale = 1 / 2048.0f;
    public string filePath;
    public string dictionaryPath;
    public Config config;
    public bool useBatch;
    private int batchMax = 0;
    public float updateTime = 0.18f;
    public RadiusMode radiusMode = RadiusMode.generate;

    public Texture3D[] textures = new Texture3D[10];
    public Vector3[] postions = new Vector3[10];
    //private BoundsControl boundsControl;

    //Start is called before the first frame update
    void Start()
    {

        //for (int i = 0; i < 10; i++)
        //{
        //    LoadPath(dictionaryPath + "\\" + i + ".swc");
        //    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        //    //cube.transform.position = postions[i];
        //    GameObject temp = new GameObject();
        //    temp.transform.SetParent(config._paintingBoard.transform);

        //    cube.transform.position = postions[i % 10];
        //    float maxDim = Math.Max(Math.Max(textures[i].width, textures[i].height), textures[i].depth);
        //    cube.transform.localScale = new Vector3(textures[i].width / maxDim, textures[i].height / maxDim, textures[i].depth / maxDim);
        //    cube.transform.SetParent(temp.transform);
        //    cube.AddComponent<BoxCollider>();
        //    cube.GetComponent<MeshRenderer>().enabled = false;
        //    temp.AddComponent<ObjectManipulator>();

        //    CreateNeuron(cube.transform, temp.transform, i ,new Vector3Int(textures[i].width, textures[i].height, textures[i].depth), textures[i]);
        //}
        LoadPath(filePath);
        CreateNeuron(config.cube.transform, config._originalDim, config.Origin);
    }

    [InspectorButton]
    private void Reset()
    {
        batch = 0;
    }

    public float deltaTime = 0;
    public int batch = 0;
    private void Update()
    {
        if(!useBatch) { return; }
        deltaTime += Time.deltaTime;
        if (deltaTime > updateTime)
        {
            deltaTime = 0;
            GameObject temp = GameObject.Find("Temp");
            for (int i = 0; i < temp.transform.childCount; i++)
            {
                GameObject oj = temp.transform.GetChild(i).gameObject;
                int node_batch = oj.GetComponent<NodeInformation>().batch;
                if (node_batch > batch)
                {
                    oj.SetActive(false);
                }
                else
                {
                    oj.SetActive(true);
                }

            }
            batch++;
        }
    }

    private void LoadPath(string path)
    {
        swc_average = new Vector3(0, 0, 0);
        if (!File.Exists(path))
        {
            Debug.Log("Error on reading swc file!");
            return;
        }
        string[] strs = File.ReadAllLines(path);
        swcList = new();
        radiusList = new();
        parentList = new();
        typeList = new();
        batchDict = new();
        for (int i = 0; i < strs.Length; ++i)
        {
            if (strs[i].StartsWith("#")) continue;
            string[] words = strs[i].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int index = int.Parse(words[0]);
            typeList[index] = int.Parse(words[1]);
            Vector3 swc = new(float.Parse(words[2]), float.Parse(words[3]), float.Parse(words[4]));
            //swcList[index] = swc.Div(config._originalDim).Mul(config._scaledDim);
            swcList[index] = swc;
            radiusList[index] = float.Parse(words[5]);
            parentList[index] = int.Parse(words[6]);
            if (useBatch)
            {
                batchDict[index] = int.Parse(words[7]);
                batchMax = (int)Math.Max(batchMax, batchDict[index]);
            }
            swc_average += swc;
        }
        swc_average /= swcList.Count;
        print("success");

        
    }

    private void CreateNeuron(Transform transform, Vector3Int dim, Texture3D volume)
    {
        byte[] data = volume.GetPixelData<byte>(0).ToArray();
        Dictionary<int ,Marker> markers = new();
        foreach (var pair in swcList)
        {
            Marker marker = new(pair.Value);
            switch(radiusMode)
            {
                case RadiusMode.origin:
                    {
                        marker.radius = radiusList[pair.Key];
                        break;
                    }
                case RadiusMode.generate:
                    {
                        marker.radius = Marker.markerRadius(data, dim.x, dim.y, dim.z, marker, config.BkgThresh);
                        marker.radius = Math.Min(2*marker.radius, 4);
                        break;
                    }
                case RadiusMode.line:
                    marker.radius = 1;
                    break;
            }
            if (pair.Key == 1) marker.radius = config._somaRadius;
            if (pair.Key == 1) marker.radius = Math.Max(marker.radius,Marker.markerRadius(data, dim.x, dim.y, dim.z, marker, config.BkgThresh));
            markers[pair.Key] = marker;
        }

        Debug.Log(swcList.Count);
        foreach (var pair in swcList)
        {
            int pid = parentList[pair.Key];
            if (pid >=0) markers[pair.Key].parent = markers[pid];
            markers[pair.Key].type = typeList[pair.Key];
            if (useBatch)
            {
                markers[pair.Key].batch = (int)batchDict[pair.Key];
                batchDict[pair.Key] = batchDict[pair.Key] / 50;
            }
        }

        if(useBatch)
        {
            Primitive.CreateTree(markers.Values.ToList(), transform, dim, batchDict.Values.ToList());
        }
        else
        {
            Primitive.CreateTree(markers.Values.ToList(), transform, dim);
        }
    }
    private void CreateNeuron(Transform transform, Transform temp, int colorTemp, Vector3Int dim, Texture3D volume)
    {
        byte[] data = volume.GetPixelData<byte>(0).ToArray();
        Dictionary<int ,Marker> markers = new();
        foreach (var pair in swcList)
        {
            Marker marker = new(pair.Value);
            switch(radiusMode)
            {
                case RadiusMode.origin:
                    {
                        marker.radius = radiusList[pair.Key];
                        break;
                    }
                case RadiusMode.generate:
                    {
                        marker.radius = Marker.markerRadius(data, dim.x, dim.y, dim.z, marker, config.BkgThresh);
                        marker.radius = Math.Max(marker.radius, 5);
                        break;
                    }
                case RadiusMode.line:
                    marker.radius = 1;
                    break;
            }
            if (pair.Key == 1) marker.radius = config._somaRadius;
            if (pair.Key == 1) marker.radius = Math.Max(marker.radius,Marker.markerRadius(data, dim.x, dim.y, dim.z, marker, config.BkgThresh));
            markers[pair.Key] = marker;
        }

        Debug.Log(swcList.Count);
        foreach (var pair in swcList)
        {
            int pid = parentList[pair.Key];
            if (pid >=0) markers[pair.Key].parent = markers[pid];
            markers[pair.Key].type = typeList[pair.Key];
            markers[pair.Key].type = 1;
            if (useBatch)
            {
                markers[pair.Key].batch = (int)batchDict[pair.Key];
                batchDict[pair.Key] = batchDict[pair.Key] / 50;
            }
        }

        if(useBatch)
        {
            Primitive.CreateTree(markers.Values.ToList(), transform, dim, batchDict.Values.ToList());
        }
        else
        {
            //Primitive.CreateTree(markers.Values.ToList(), transform, dim);
            Primitive.CreateTreeTemp(markers.Values.ToList(), transform,temp, colorTemp, dim);
        }
    }


}
