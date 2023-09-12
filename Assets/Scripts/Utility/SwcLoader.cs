using ANT;
using Cysharp.Threading.Tasks;
using GLTFast.FakeSchema;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.XR.CoreUtils;
using UnityEngine;
using static Fusion.Allocator;
using Random = UnityEngine.Random;

public class SwcLoader : Singleton<SwcLoader>
{
    public enum RadiusMode { line, origin, generate}
    private Dictionary<int, Vector3> swcDict;
    private Dictionary<int, float> radiusDict;
    private Dictionary<int, int> parentDict;
    private Dictionary<int, int> typeDict;
    private Dictionary<int, float> batchDict;
    //private Dictionary<int, int> swc_map;
    public string filePath;
    public string directoryPath;
    public bool useBatch;
    private int batchMax = 0;
    public float updateTime = 0.18f;
    public RadiusMode radiusMode = RadiusMode.generate;

    public Texture3D[] textures = new Texture3D[10];
    public Vector3[] postions = new Vector3[10];
    public int annotateNumber = 0;

    public bool loadNextSwc = false;
    string[] swcFiles;

    //Start is called before the first frame update
    async void Start()
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


        //CreateNeuron(config.cube.transform, config.originalDim, config.Origin);
        LoadDirectory(directoryPath);
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
            GameObject reconstruction = GameObject.Find("Reconstruction");
            for (int i = 0; i < reconstruction.transform.childCount; i++)
            {
                GameObject oj = reconstruction.transform.GetChild(i).gameObject;
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

    private async Task LoadSwc(string path)
    {
        if (!File.Exists(path))
        {
            Debug.Log("Error on reading swc file!");
            return;
        }
        string[] strs = File.ReadAllLines(path);
        swcDict = new();
        radiusDict = new();
        parentDict = new();
        typeDict = new();
        batchDict = new();
        for (int i = 0; i < strs.Length; ++i)
        {
            if (strs[i].StartsWith("#")) continue;
            string[] words = strs[i].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int index = int.Parse(words[0]);
            typeDict[index] = int.Parse(words[1]);
            Vector3 swc = new(float.Parse(words[2]), float.Parse(words[3]), float.Parse(words[4]));
            //swcList[index] = swc.Div(config._originalDim).Mul(config._scaledDim);
            swcDict[index] = swc;
            radiusDict[index] = float.Parse(words[5]);
            parentDict[index] = int.Parse(words[6]);
            if (useBatch)
            {
                batchDict[index] = int.Parse(words[7]);
                batchMax = (int)Math.Max(batchMax, batchDict[index]);
            }
        }

        print($"load {Path.GetFileName(path)} success");

        await Config.Instance.ReplaceTexture(Path.GetFileNameWithoutExtension(path));
    }

    private void LoadDirectory(string path)
    {
        swcFiles = Directory.GetFiles(path);
        RandomGenerate();
    }

    private void CreateNeuron(Transform transform, Vector3Int dim, Texture3D volume)
    {
        byte[] data = volume.GetPixelData<byte>(0).ToArray();
        Dictionary<int ,Marker> markers = new();
        foreach (var pair in swcDict)
        {
            Marker marker = new(pair.Value);
            switch(radiusMode)
            {
                case RadiusMode.origin:
                    {
                        marker.radius = radiusDict[pair.Key];
                        break;
                    }
                case RadiusMode.generate:
                    {
                        marker.radius = Marker.markerRadius(data, dim.x, dim.y, dim.z, marker, Config.Instance.BkgThresh);
                        marker.radius = Math.Min(2*marker.radius, 4);
                        break;
                    }
                case RadiusMode.line:
                    marker.radius = 1;
                    break;
            }
            if (pair.Key == 1) marker.radius = Config.Instance.somaRadius;
            if (pair.Key == 1) marker.radius = Math.Max(marker.radius,Marker.markerRadius(data, dim.x, dim.y, dim.z, marker, Config.Instance.BkgThresh));
            markers[pair.Key] = marker;
        }

        Debug.Log(swcDict.Count);
        foreach (var pair in swcDict)
        {
            int pid = parentDict[pair.Key];
            if (pid >=0) markers[pair.Key].parent = markers[pid];
            markers[pair.Key].type = typeDict[pair.Key];
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
    private void CreateNeuron(Transform transform, Transform reconstruction, int colorTemp, Vector3Int dim, Texture3D volume)
    {
        byte[] data = volume.GetPixelData<byte>(0).ToArray();
        Dictionary<int ,Marker> markers = new();
        foreach (var pair in swcDict)
        {
            Marker marker = new(pair.Value);
            switch(radiusMode)
            {
                case RadiusMode.origin:
                    {
                        marker.radius = radiusDict[pair.Key];
                        break;
                    }
                case RadiusMode.generate:
                    {
                        marker.radius = Marker.markerRadius(data, dim.x, dim.y, dim.z, marker, Config.Instance.BkgThresh);
                        marker.radius = Math.Max(marker.radius, 5);
                        break;
                    }
                case RadiusMode.line:
                    marker.radius = 1;
                    break;
            }
            if (pair.Key == 1) marker.radius = Config.Instance.somaRadius;
            if (pair.Key == 1) marker.radius = Math.Max(marker.radius,Marker.markerRadius(data, dim.x, dim.y, dim.z, marker, Config.Instance.BkgThresh));
            markers[pair.Key] = marker;
        }

        Debug.Log(swcDict.Count);
        foreach (var pair in swcDict)
        {
            int pid = parentDict[pair.Key];
            if (pid >=0) markers[pair.Key].parent = markers[pid];
            markers[pair.Key].type = typeDict[pair.Key];
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
            Primitive.CreateTreeTemp(markers.Values.ToList(), transform,reconstruction, colorTemp, dim);
        }
    }

    private async void  RandomGenerate()
    {
        Random.InitState(1802);
        List<int> seedList = new();

        for(int i=0;i< swcFiles.Length; i++)
        {
            seedList.Add(Random.Range(1,10000));
        }

        for(int i=0;i< swcFiles.Length;i++)
        {
            Random.InitState(seedList[i]);
            Debug.Log(seedList[i]);
            await LoadSwc(swcFiles[i]);

            BoardManager.Instance.ClearTargets();
            BoardManager.Instance.ClearReconstruction();

            Config.Instance.gazeController.enabled = true;
            Config.Instance.gazeController.interactionType = GazeController.EyeInteractionType.LabelRefine;

            CreateNeuron(Config.Instance.cube.transform, Config.Instance.originalDim, Config.Instance.Origin);

            List<Vector3> filtered = FilterBranchAndLeaf();
            for (int j=0; j < 10; j++)
            {
                int index = Random.Range(0, filtered.Count - j);
                BoardManager.Instance.CreatePoint(filtered[index], Config.Instance.originalDim, Color.blue);
                filtered.SwapAtIndices(index, filtered.Count - 1 - j);
            }
            await UniTask.WaitUntil(() => loadNextSwc);
            loadNextSwc = false;
        }
    }

    private List<Vector3> FilterBranchAndLeaf()
    {
        Vector3 rootCoordinate = swcDict[0].Divide(Config.Instance.originalDim).Multiply(Config.Instance.scaledDim);
        List<Vector3> nodeList = new();
        Dictionary<int,int> childCount = new();
        foreach(var pair in parentDict)
        {
            if (pair.Value >= 0)
            {
                if (!childCount.ContainsKey(pair.Key)) childCount[pair.Key] = 0;
                if (childCount.ContainsKey(pair.Value)) childCount[pair.Value]++;
                else childCount[pair.Value] = 0;
            }
        }
        foreach(var pair in childCount)
        {
            if (pair.Value == 0 || pair.Value == 2)
            {
                nodeList.Add(swcDict[pair.Key]);
            }
        }
        List<Vector3> result = new();
        foreach(Vector3 swc in nodeList)
        {
            Vector3 cubicCoordinate = swc.Divide(Config.Instance.originalDim);
            Vector3 scaledCoordinate = cubicCoordinate.Multiply(Config.Instance.scaledDim);
            int index = Utils.CoordinateToIndex(scaledCoordinate, Config.Instance.scaledDim);
            Debug.Log(Vector3.Distance(rootCoordinate, scaledCoordinate));
            if (Config.Instance.VolumeData[index]>=25 && Vector3.Distance(rootCoordinate,scaledCoordinate)>=40) result.Add(swc);
        }
        Debug.Log(nodeList.Count);
        Debug.Log(result.Count);
        return result;
    }
}
