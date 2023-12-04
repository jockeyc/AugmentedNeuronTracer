using IntraMR;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static Unity.ProjectAuditor.Editor.UI.Draw2D;


public class FIM : MonoBehaviour
{
    private Config config;
    public RenderTexture volume;
    public RenderTexture gwdt;
    public RenderTexture state;
    public RenderTexture parent;
    public RenderTexture phi;
    public RenderTexture visualize;
    public RenderTexture mask;
    public RenderTexture bias;
    public RenderTexture threshold;
    public ComputeShader computeShader;
    public RenderTexture selection;
    public Vector3Int dim;


    private int[] seed = new int[3];
    private int seedIndex;
    private float maxIntensity;
    private Vector3Int numthreads = new(8, 8, 4);

    public HashSet<uint> trunk;
    public HashSet<uint> oldTrunk;
    uint[] parentBufferData;
    public Dictionary<int, Marker> markers;

    // Start is called before the first frame update
    public void Start()
    {
        this.config = GetComponent<Config>();
        computeShader = Resources.Load("ComputeShaders/FIM") as ComputeShader;
        PrepareDatas();
        threshold = InitThreshold();
        //StartCoroutine(FIMDTCoroutine());
    }

    public void PrepareDatas()
    {
        dim = config.scaledDim;
        gwdt = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RFloat, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat);
        state = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UInt);
        parent = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RInt, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt);
        phi = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RFloat, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat);
        mask = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RFloat, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat);
        visualize = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        threshold = InitRenderTexture3D(dim.x , dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        selection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RFloat, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat);
        bias = InitBias();
        volume = CopyData(config.ScaledVolume);
    }

    //Return the part connected to soma according to the threshold
    public RenderTexture ConnectedPart(bool view) {
        int bkgThreshold = view ? config.ViewThresh : config.BkgThresh;
        var connection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);

        int kernel = computeShader.FindKernel("InitConnectionSeed");
        computeShader.SetTexture(kernel, "connection", connection);
        computeShader.SetInt("seedIndex", VectorToIndex(config._rootPos, dim));
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        int[] dimsArray = new int[3] { dim.x, dim.y, dim.z };
        uint activeCount;

        computeShader.SetInts("dims", dimsArray);
        computeShader.SetInt("bkgThreshold", bkgThreshold);
        //Update Step
        int updateTime = 0;
        var activeSet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        do
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateConnection");
            computeShader.SetTexture(kernel, "connection", connection);
            computeShader.SetTexture(kernel, "mask", mask);
            computeShader.SetTexture(kernel, "volume", volume);
            computeShader.SetBuffer(kernel, "activeSet", activeSet);
            computeShader.SetTexture(kernel, "threshold", threshold);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
            //Debug.Log($"active buffer count: {activeCount}");
        } while (activeCount > 0);
        activeSet.Release();
        return connection;
    }

    //distance transform with FIM
    public void DistanceTransform()
    {
        int bkgThreshold = config.BkgThresh;

        int kernel = computeShader.FindKernel("ApplyBias");
        computeShader.SetTexture(kernel, "volume", volume);
        computeShader.SetTexture(kernel, "origin", config.ScaledVolume);
        computeShader.SetTexture(kernel, "bias", bias);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        kernel = computeShader.FindKernel("InitBound");
        computeShader.SetTexture(kernel, "state", state);
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetTexture(kernel, "volume", volume);
        computeShader.SetTexture(kernel, "mask", mask);
        computeShader.SetTexture(kernel, "threshold", threshold);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        int[] dimsArray = new int[3] { dim.x, dim.y, dim.z };
        uint activeCount;
        computeShader.SetInts("dims", dimsArray);

        //Update Step
        int updateTime = 0;
        ComputeBuffer activeSet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        do
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateFarState");
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "visualize", visualize);
            computeShader.SetBuffer(kernel, "activeSet", activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateActiveValue");
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "volume", volume);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateActiveState");
            computeShader.SetTexture(kernel, "state", state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
        } while (activeCount > 0);
        activeSet.Release();

        uint remedyCount;
        //Remedy Step
        ComputeBuffer remedySet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedy");
        computeShader.SetBuffer(kernel, "remedySet", remedySet);
        computeShader.SetTexture(kernel, "volume", volume);
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetTexture(kernel, "state", state);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        remedyCount = GetAppendBufferSize(remedySet);

        int remedyTime = 0;
        while (remedyCount > 0)
        {
            remedyTime++;
            remedySet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateRemedy");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "volume", volume);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            remedyCount = GetAppendBufferSize(remedySet);

            kernel = computeShader.FindKernel("UpdateRemedyNeighbor");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));


            //Get Remedy Set Count
            remedyCount = GetAppendBufferSize(remedySet);
        }
        remedySet.Release();

        Debug.Log($"DT update times:{updateTime} remedy times:{remedyTime}");

        ComputeBuffer gwdtBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        ComputeBuffer gwdtBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        kernel = computeShader.FindKernel("VisualizeTexture");
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetBuffer(kernel, "gwdtBuffer1", gwdtBuffer1);
        computeShader.SetBuffer(kernel, "gwdtBuffer2", gwdtBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        float[] gwdtBufferData = new float[gwdtBuffer1.count + gwdtBuffer2.count];

        float time = Time.realtimeSinceStartup;
        gwdtBuffer1.GetData(gwdtBufferData, 0, 0, gwdtBuffer1.count);
        gwdtBuffer2.GetData(gwdtBufferData, gwdtBuffer1.count, 0, gwdtBuffer2.count);

        Debug.Log("DT GET Data cost " + (Time.realtimeSinceStartup - time));
        int maxIndex = 0;
        for (int i = 0; i < gwdtBufferData.Length; i++)
        {
            if (gwdtBufferData[i] > gwdtBufferData[maxIndex])
                maxIndex = i;
        }
        seed[0] = maxIndex % dim.x;
        seed[1] = (maxIndex / dim.x) % dim.y;
        seed[2] = (maxIndex / dim.x / dim.y) % dim.z;
        maxIntensity = gwdtBufferData[maxIndex];
        seedIndex = maxIndex;

        Debug.Log($"Max Intexsity:{maxIntensity}");


        config._rootPos = new Vector3Int(seed[0], seed[1], seed[2]);

        if (config.forceRootCenter)
        {
            config._rootPos = config.scaledDim / 2;
            seedIndex = VectorToIndex(config._rootPos, dim);
        }


        Debug.Log($"{seed[0]} {seed[1]} {seed[2]}");

        gwdtBuffer1.Release();
        gwdtBuffer2.Release();

        //var visualization = InitRenderTexture3D(dims.x, dims.y, dims.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        //kernel = computeShader.FindKernel("Visualization");
        //computeShader.SetTexture(kernel, "gwdt", gwdt);
        //computeShader.SetTexture(kernel, "visualization", visualization);
        //computeShader.SetFloat("max_intensity", maxIntensity);
        //computeShader.Dispatch(kernel, dims.x / numthreads.x, dims.y / numthreads.y, dims.z / numthreads.z);


        //AssetDatabase.DeleteAsset("Assets/Textures/FIMFI/gwdt.Asset");
        //AssetDatabase.CreateAsset(visualization, "Assets/Textures/FIMFI/gwdt.Asset");
        //AssetDatabase.SaveAssets();
        //AssetDatabase.Refresh();


    }

    // coroutine version of  distance transform with FIM
    public IEnumerator DistanceTransformAsync()
    {
        int kernel = computeShader.FindKernel("ApplyBias");
        computeShader.SetTexture(kernel, "volume", volume);
        computeShader.SetTexture(kernel, "origin", config.ScaledVolume);
        computeShader.SetTexture(kernel, "bias", bias);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        kernel = computeShader.FindKernel("InitBound");
        computeShader.SetTexture(kernel, "state", state);
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetTexture(kernel, "volume", volume);
        computeShader.SetTexture(kernel, "mask", mask);
        computeShader.SetTexture(kernel, "threshold", threshold);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        int[] dimsArray = new int[3] { dim.x, dim.y, dim.z };
        uint activeCount = 1;
        computeShader.SetInts("dims", dimsArray);

        //Update Step
        int updateTime = 0;
        ComputeBuffer activeSet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        while (activeCount > 0)
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateFarState");
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "visualize", visualize);
            computeShader.SetBuffer(kernel, "activeSet", activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateActiveValue");
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "volume", volume);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateActiveState");
            computeShader.SetTexture(kernel, "state", state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            if (updateTime % 10 == 0)
            {
                uint[] countBufferData = new uint[1];
                yield return StartCoroutine(GetAppendBufferSizeAsync(activeSet, countBufferData));
                activeCount = countBufferData[0];
            }
        }
        activeSet.Release();

        uint remedyCount;
        //Remedy Step
        ComputeBuffer remedySet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedy");
        computeShader.SetBuffer(kernel, "remedySet", remedySet);
        computeShader.SetTexture(kernel, "volume", volume);
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetTexture(kernel, "state", state);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        remedyCount = GetAppendBufferSize(remedySet);

        int remedyTime = 0;
        while (remedyCount > 0)
        {
            remedyTime++;
            remedySet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateRemedy");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "volume", volume);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateRemedyNeighbor");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));


            //Get Remedy Set Count
            if (remedyTime % 10 == 0)
            {
                uint[] countBufferData = new uint[1];
                yield return StartCoroutine(GetAppendBufferSizeAsync(remedySet, countBufferData));
                remedyCount = countBufferData[0];
            }
        }
        remedySet.Release();

        Debug.Log($"DT update times:{updateTime} remedy times:{remedyTime}");

        ComputeBuffer gwdtBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        ComputeBuffer gwdtBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        kernel = computeShader.FindKernel("VisualizeTexture");
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetBuffer(kernel, "gwdtBuffer1", gwdtBuffer1);
        computeShader.SetBuffer(kernel, "gwdtBuffer2", gwdtBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        float[] gwdtBufferData = new float[gwdtBuffer1.count + gwdtBuffer2.count];

        float time = Time.realtimeSinceStartup;
        gwdtBuffer1.GetData(gwdtBufferData, 0, 0, gwdtBuffer1.count);
        gwdtBuffer2.GetData(gwdtBufferData, gwdtBuffer1.count, 0, gwdtBuffer2.count);

        Debug.Log("DT GET Data cost: " + (Time.realtimeSinceStartup - time));
        time = Time.realtimeSinceStartup;
        int maxIndex = 0;

        for (int i = 0; i < gwdtBufferData.Length; i++)
        {
            if (gwdtBufferData[i] > gwdtBufferData[maxIndex])
                maxIndex = i;
        }
        seed[0] = maxIndex % dim.x;
        seed[1] = (maxIndex / dim.x) % dim.y;
        seed[2] = (maxIndex / dim.x / dim.y) % dim.z;
        maxIntensity = gwdtBufferData[maxIndex];
        seedIndex = maxIndex;
        Debug.Log("DT iteration cost: " + (Time.realtimeSinceStartup - time));
        Debug.Log($"Max Intexsity:{maxIntensity}");


        config._rootPos = new Vector3Int(seed[0], seed[1], seed[2]);

        if (config.forceRootCenter)
        {
            config._rootPos = config.scaledDim / 2;
            seedIndex = VectorToIndex(config._rootPos, dim);
        }


        Debug.Log($"{seed[0]} {seed[1]} {seed[2]}");

        gwdtBuffer1.Release();
        gwdtBuffer2.Release();

        //var visualization = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        //kernel = computeShader.FindKernel("Visualization");
        //computeShader.SetTexture(kernel, "gwdt", gwdt);
        //computeShader.SetTexture(kernel, "visualization", visualization);
        //computeShader.SetFloat("maxIntensity", maxIntensity);
        //computeShader.Dispatch(kernel, dim.x / numthreads.x, dim.y / numthreads.y, dim.z / numthreads.z);

        //visualization.filterMode = FilterMode.Bilinear;
        //Config.Instance.postProcessVolume.profile.GetSetting<BaseVolumeRendering>().volume.value = visualization;

        //AssetDatabase.DeleteAsset("Assets/Textures/FIM/gwdt.Asset");
        //AssetDatabase.CreateAsset(visualization, "Assets/Textures/FIM/gwdt.Asset");
        //AssetDatabase.SaveAssets();
        //AssetDatabase.Refresh();
    }

    // calculate the geodesic distance wthin the trunk part with FIM
    public List<Marker> InitialReconstruction()
    {
        float computationTime = Time.realtimeSinceStartup;
        int bkgThreshold = config.BkgThresh;
        int kernel = computeShader.FindKernel("InitSeed");
        computeShader.SetTexture(kernel, "state", state);
        computeShader.SetTexture(kernel, "parent", parent);
        computeShader.SetTexture(kernel, "phi", phi);
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetTexture(kernel, "threshold", threshold);
        computeShader.SetFloat("maxIntensity", maxIntensity);
        computeShader.SetInt("seedIndex", seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        uint activeCount = 1;
        trunk = new HashSet<uint>();
        trunk.Add((uint)seedIndex);
        //Update Steps
        var activeSet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        activeSet.SetCounterValue(0);

        float time = Time.realtimeSinceStartup;
        int updateTime = 0;
        do
        {
            updateTime++;

            kernel = computeShader.FindKernel("UpdateFarStateTree");
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "mask", mask);
            computeShader.SetBuffer(kernel, "activeSet", activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateActiveValueTree");
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "phi", phi);
            computeShader.SetTexture(kernel, "parent", parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateActiveStateTree");
            computeShader.SetTexture(kernel, "state", state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            if (updateTime % 10 == 0)
            {
                activeCount = GetAppendBufferSize(activeSet);
                uint[] activeData = new uint[activeCount];
                activeSet.GetData(activeData);
                trunk.UnionWith(activeData);
                activeSet.SetCounterValue(0);
            }
            //Debug.Log($"active buffer count: {activeCount}");
        } while (activeCount > 0);
        Debug.Log("trunk Count" + trunk.Count);
        Debug.Log($"generate inital result cost: {Time.realtimeSinceStartup - time}");
        time = Time.realtimeSinceStartup;

        activeSet.Release();

        //Remedy Step
        uint remedyCount;
        var remedySet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedyTree");
        computeShader.SetBuffer(kernel, "remedySet", remedySet);
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetTexture(kernel, "phi", phi);
        computeShader.SetTexture(kernel, "state", state);
        computeShader.SetTexture(kernel, "parent", parent);
        computeShader.SetTexture(kernel, "threshold", threshold);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));
        remedyCount = GetAppendBufferSize(remedySet);
        remedySet.SetCounterValue(0);
        Debug.Log("first traceTime remedy count:" + remedyCount);
        //remedySet.Release();
        int remedyTime = 0;
        int maxCount = 0;
        //remedyCount = 0;
        while (remedyCount > 0)
        {
            remedyTime++;
            //remedySet = new ComputeBuffer(dims.x * dims.y * dims.z, sizeof(uint), ComputeBufferType.Append);

            kernel = computeShader.FindKernel("UpdateRemedyTree");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "phi", phi);
            computeShader.SetTexture(kernel, "threshold", threshold);
            computeShader.SetTexture(kernel, "parent", parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateRemedyNeighborTree");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            if (remedyTime % 10 == 0)
            {
                remedyCount = GetAppendBufferSize(remedySet);
                maxCount = (int)Math.Max(maxCount, remedyCount);
                remedySet.SetCounterValue(0);
            }

        }
        Debug.Log(maxCount);
        Debug.Log("remedy cost:" + (Time.realtimeSinceStartup - time));
        time = Time.realtimeSinceStartup;
        Debug.Log($"update times:{updateTime} remedy times:{remedyTime}");
        remedySet.Release();


        ComputeBuffer parentBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(uint), ComputeBufferType.Default);
        ComputeBuffer parentBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(uint), ComputeBufferType.Default);

        kernel = computeShader.FindKernel("GetParent");
        computeShader.SetTexture(kernel, "parent", parent);
        computeShader.SetBuffer(kernel, "parentBuffer1", parentBuffer1);
        computeShader.SetBuffer(kernel, "parentBuffer2", parentBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        parentBufferData = new uint[parentBuffer1.count + parentBuffer2.count];

        parentBuffer1.GetData(parentBufferData, 0, 0, parentBuffer1.count);
        parentBuffer2.GetData(parentBufferData, parentBuffer1.count, 0, parentBuffer2.count);

        parentBuffer1.Release();
        parentBuffer2.Release();

        Debug.Log("GetParentData cost:" + (Time.realtimeSinceStartup - time));
        time = Time.realtimeSinceStartup;

        Debug.Log($"FIM Build Tree cost: {Time.realtimeSinceStartup - computationTime}");

        markers = new Dictionary<int, Marker>(trunk.Count);
        var completeTree = new List<Marker>(trunk.Count);
        Queue<uint> queue = new(trunk);


        while (queue.Count > 0)
        {
            uint peek = queue.Dequeue();
            if (peek > parentBufferData.Length) Debug.Log(peek);
            if (!trunk.Contains(parentBufferData[peek]))
            {
                trunk.Add(parentBufferData[peek]);
                queue.Enqueue(parentBufferData[peek]);
            }
        }

        foreach (var index in trunk)
        {
            int i = (int)(index % dim.x);
            int j = (int)((index / dim.x) % dim.y);
            int k = (int)((index / dim.x / dim.y) % dim.z);
            //createSphere(new Vector3(i, j, k), dims, Color.blue);
            Marker marker = new(new Vector3(i, j, k));
            markers[(int)index] = marker;
            completeTree.Add(marker);
        }

        foreach (var index in trunk)
        {
            uint index2 = parentBufferData[index];
            Marker marker1 = markers[(int)index];
            Marker marker2 = markers[(int)index2];
            if (marker1 == marker2) marker1.parent = null;
            else marker1.parent = marker2;
        }

        return completeTree;
    }

    // coroutine version of calculating the geodesic distance wthin the trunk part with FIM 
    public IEnumerator InitialReconstructionAsync(List<Marker> completeTree)
    {
        float computationTime = Time.realtimeSinceStartup;
        int bkgThreshold = config.BkgThresh;
        int kernel = computeShader.FindKernel("InitSeed");
        computeShader.SetTexture(kernel, "state", state);
        computeShader.SetTexture(kernel, "parent", parent);
        computeShader.SetTexture(kernel, "phi", phi);
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetTexture(kernel, "threshold", threshold);
        computeShader.SetFloat("maxIntensity", maxIntensity);
        computeShader.SetInt("seedIndex", seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        uint activeCount = 1;
        trunk = new HashSet<uint>();
        trunk.Add((uint)seedIndex);
        //Update Steps
        var activeSet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        activeSet.SetCounterValue(0);
        yield return 0;
        float time = Time.realtimeSinceStartup;
        int updateTime = 0;
        while (activeCount > 0)
        {
            updateTime++;

            kernel = computeShader.FindKernel("UpdateFarStateTree");
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "mask", mask);
            computeShader.SetBuffer(kernel, "activeSet", activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateActiveValueTree");
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "phi", phi);
            computeShader.SetTexture(kernel, "parent", parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateActiveStateTree");
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "phi", phi);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            if (updateTime % 20 == 0)
            {
                uint[] countBufferData = new uint[1];
                yield return StartCoroutine(GetAppendBufferSizeAsync(activeSet, countBufferData));
                activeCount = countBufferData[0];
                //Debug.Log($"active buffer count: {activeCount}");

                uint[] activeData = new uint[activeCount];
                //var request = AsyncGPUReadback.Request(activeSet);
                //yield return new WaitUntil(() => request.done);
                //Debug.Log(request.GetData<uint>().Count());
                activeSet.GetData(activeData);
                trunk.UnionWith(activeData);
                //trunk.UnionWith(activeData);
                activeSet.SetCounterValue(0);
            }
            else
            {

            }
        }
        Debug.Log("trunk Count" + trunk.Count);
        Debug.Log($"generate inital result cost: {Time.realtimeSinceStartup - time}");
        time = Time.realtimeSinceStartup;

        activeSet.Release();

        //Remedy Step
        uint remedyCount;
        var remedySet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedyTree");
        computeShader.SetBuffer(kernel, "remedySet", remedySet);
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetTexture(kernel, "phi", phi);
        computeShader.SetTexture(kernel, "state", state);
        computeShader.SetTexture(kernel, "parent", parent);
        computeShader.SetTexture(kernel, "threshold", threshold);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));
        remedyCount = GetAppendBufferSize(remedySet);
        remedySet.SetCounterValue(0);
        Debug.Log("first traceTime remedy count:" + remedyCount);

        uint[] remedyData = new uint[remedyCount];
        remedySet.GetData(remedyData);
        firstRemedy = remedyData.ToList();

        //remedySet.Release();
        int remedyTime = 0;
        int maxCount = 0;
        //remedyCount = 0;
        while (remedyCount > 0)
        {
            remedyTime++;
            //remedySet = new ComputeBuffer(dims.x * dims.y * dims.z, sizeof(uint), ComputeBufferType.Append);

            kernel = computeShader.FindKernel("UpdateRemedyTree");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "phi", phi);
            computeShader.SetTexture(kernel, "threshold", threshold);
            computeShader.SetTexture(kernel, "parent", parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateRemedyNeighborTree");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            if (remedyTime % 20 == 0)
            {
                uint[] countBufferData = new uint[1];
                yield return StartCoroutine(GetAppendBufferSizeAsync(remedySet, countBufferData));
                remedyCount = countBufferData[0];
                remedySet.SetCounterValue(0);
            }

        }
        Debug.Log("remedy cost:" + (Time.realtimeSinceStartup - time));
        time = Time.realtimeSinceStartup;
        Debug.Log($"update times:{updateTime} remedy times:{remedyTime}");
        remedySet.Release();


        ComputeBuffer parentBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(uint), ComputeBufferType.Default);
        ComputeBuffer parentBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(uint), ComputeBufferType.Default);

        kernel = computeShader.FindKernel("GetParent");
        computeShader.SetTexture(kernel, "parent", parent);
        computeShader.SetBuffer(kernel, "parentBuffer1", parentBuffer1);
        computeShader.SetBuffer(kernel, "parentBuffer2", parentBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        parentBufferData = new uint[parentBuffer1.count + parentBuffer2.count];

        parentBuffer1.GetData(parentBufferData, 0, 0, parentBuffer1.count);
        parentBuffer2.GetData(parentBufferData, parentBuffer1.count, 0, parentBuffer2.count);

        parentBuffer1.Release();
        parentBuffer2.Release();

        Debug.Log("GetParentData cost:" + (Time.realtimeSinceStartup - time));
        time = Time.realtimeSinceStartup;

        Debug.Log($"FIM Build Tree cost: {Time.realtimeSinceStartup - computationTime}");

        markers = new Dictionary<int, Marker>(trunk.Count);
        //completeTree = new List<Marker>(trunk.Count);
        completeTree.Capacity = trunk.Count;
        Queue<uint> queue = new(trunk);


        while (queue.Count > 0)
        {
            uint peek = queue.Dequeue();
            if (peek > parentBufferData.Length)
            {
                BoardManager.Instance.CreatePoint(peek, Config.Instance.scaledDim, Color.green, 1.6f);
                Debug.Log(Utils.IndexToCoordinate(peek, Config.Instance.scaledDim));
            }
                if (!trunk.Contains(parentBufferData[peek]))
            {
                trunk.Add(parentBufferData[peek]);
                queue.Enqueue(parentBufferData[peek]);
            }
        }

        foreach (var index in trunk)
        {
            int i = (int)(index % dim.x);
            int j = (int)((index / dim.x) % dim.y);
            int k = (int)((index / dim.x / dim.y) % dim.z);
            //createSphere(new Vector3(i, j, k), dims, Color.blue);
            Marker marker = new(new Vector3(i, j, k));
            markers[(int)index] = marker;
            completeTree.Add(marker);
        }

        foreach (var index in trunk)
        {
            uint index2 = parentBufferData[index];
            Marker marker1 = markers[(int)index];
            Marker marker2 = markers[(int)index2];
            if (marker1 == marker2) marker1.parent = null;
            else marker1.parent = marker2;
        }
    }

    /// <summary>
    /// calculate the geodesic distance wthin full image range with FIM
    /// </summary>
    /// <returns>markers of connected part</returns>
    public List<Marker> FIMFI()
    {
        int[] dimsArray = new int[3] { dim.x, dim.y, dim.z };
        int kernel = computeShader.FindKernel("InitSeed");
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Threshold, threshold);
        computeShader.SetFloat(MaxIntensity, maxIntensity);
        computeShader.SetInt(SeedIndex, seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        uint activeCount;
        trunk = new HashSet<uint>();
        trunk.Add((uint)seedIndex);
        //Update Steps
        ComputeBuffer activeSet = new(134217728, sizeof(uint), ComputeBufferType.Append);
        activeSet.SetCounterValue(0);

        float time = Time.realtimeSinceStartup;
        int updateTime = 0;
        do
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateFarStateTree");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Mask, mask);
            computeShader.SetBuffer(kernel, ActiveSet, activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateActiveValueTree");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.SetTexture(kernel, Parent, parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateActiveStateTree");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
            //Debug.Log($"active buffer count: {activeCount}");
            uint[] activeData = new uint[activeCount];
            activeSet.GetData(activeData);
            trunk.UnionWith(activeData);
        } while (activeCount > 0);
        Debug.Log($"results count: {trunk.Count}");

        Debug.Log($"generate initial result cost: {Time.realtimeSinceStartup - time}");
        time = Time.realtimeSinceStartup;

        float calculationTime = Time.realtimeSinceStartup;

        activeSet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitSeedFI");
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetFloat(MaxIntensity, maxIntensity);
        computeShader.SetTexture(kernel, Threshold, threshold);
        computeShader.SetInt(SeedIndex, seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));



        //results.Add((uint)seedIndex);
        Debug.Log(IndexToVector((uint)seedIndex, dim));
        //Update Steps

        time = Time.realtimeSinceStartup;
        updateTime = 0;
        int sum = 0;
        do
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateFarStateFI");
            computeShader.SetInts(Dims, dimsArray);
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Mask, mask);
            computeShader.SetTexture(kernel, Visualize, visualize);
            computeShader.SetBuffer(kernel, ActiveSet, activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateActiveValueFI");
            computeShader.SetInts(Dims, dimsArray);
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.SetTexture(kernel, Parent, parent);
            computeShader.SetTexture(kernel, Threshold, threshold);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            kernel = computeShader.FindKernel("UpdateActiveStateFI");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
            sum += (int)activeCount;
        } while (activeCount > 0);
        activeSet.Release();
        Debug.Log($"active count: {sum}");

        Debug.Log($"update cost :" + (Time.realtimeSinceStartup - time));
        time = Time.realtimeSinceStartup;

        //afterUpdate = GetBuffer();
        //SaveTexture(afterUpdate, $"Assets/Textures/FIM/afterUpdate.Asset");

        //Remedy Step
        uint remedyCount = 0;
        var remedySet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedyFI");
        computeShader.SetBuffer(kernel, RemedySet, remedySet);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));
        remedyCount = GetAppendBufferSize(remedySet);
        Debug.Log("first traceTime remedy count:" + remedyCount);

        //uint[] remedyData = new uint[remedyCount];
        //remedySet.GetData(remedyData);
        //firstRemedy = remedyData.ToList();

        //remedySet.Release();
        int remedyTime = 0;
        while (remedyCount > 0)
        {
            remedyTime++;
            //remedySet = new ComputeBuffer(dims.x * dims.y * dims.z, sizeof(uint), ComputeBufferType.Append);
            remedySet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateRemedyFI");
            computeShader.SetBuffer(kernel, RemedySet, remedySet);
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.SetTexture(kernel, Threshold, threshold);
            computeShader.SetTexture(kernel, Parent, parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            remedyCount = GetAppendBufferSize(remedySet);

            kernel = computeShader.FindKernel("UpdateRemedyNeighborFI");
            computeShader.SetBuffer(kernel, RemedySet, remedySet);
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Remedy Set Count
            remedyCount = GetAppendBufferSize(remedySet);
            //bug.Log($"remedy buffer count: {remedyCount}");
            //remedySet.Release();
        }
        Debug.Log("remedy cost:" + (Time.realtimeSinceStartup - time));
        Debug.Log($"update times:{updateTime} remedy times:{remedyTime}");
        time = Time.realtimeSinceStartup;
        remedySet.Release();


        ComputeBuffer parentBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        ComputeBuffer parentBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        kernel = computeShader.FindKernel("GetParent");
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetBuffer(kernel, ParentBuffer1, parentBuffer1);
        computeShader.SetBuffer(kernel, ParentBuffer2, parentBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));
        
        parentBufferData = new uint[parentBuffer1.count + parentBuffer2.count];
        parentBuffer1.GetData(parentBufferData, 0, 0, parentBuffer1.count);
        parentBuffer2.GetData(parentBufferData, parentBuffer1.count, 0, parentBuffer2.count);

        parentBuffer1.Release();
        parentBuffer2.Release();

        //afterRemedy = GetBuffer();
        //SaveTexture(afterRemedy, $"Assets/Textures/FIM/afterRemedy.Asset");

        //var diff = GetDiff(afterUpdate,afterRemedy);
        //SaveTexture(diff, $"Assets/Textures/FIM/diff.Asset");


        Debug.Log($"FIM GD Cal cost: {Time.realtimeSinceStartup - calculationTime}");
        time = Time.realtimeSinceStartup;

        markers = new Dictionary<int, Marker>(trunk.Count);
        var completeTree = new List<Marker>(trunk.Count);
        Queue<uint> queue = new(trunk);

        while (queue.Count > 0)
        {
            uint peek = queue.Dequeue();
            if (!trunk.Contains(parentBufferData[peek]))
            {
                Debug.Log(Utils.IndexToCoordinate(peek, Config.Instance.scaledDim));
                trunk.Add(parentBufferData[peek]);
                queue.Enqueue(parentBufferData[peek]);

            }
        }

        foreach (var index in trunk)
        {
            int i = (int)(index % dim.x);
            int j = (int)((index / dim.x) % dim.y);
            int k = (int)((index / dim.x / dim.y) % dim.z);
            //createSphere(new Vector3(i, j, k), dims, Color.blue);
            Marker marker = new Marker(new Vector3(i, j, k));
            markers[(int)index] = marker;
            completeTree.Add(marker);
        }

        foreach (var index in trunk)
        {
            uint index2 = parentBufferData[index];
            if (!trunk.Contains(index2))
            {

                int i = (int)(index2 % dim.x);
                int j = (int)((index2 / dim.x) % dim.y);
                int k = (int)((index2 / dim.x / dim.y) % dim.z);

                //createSphere(new Vector3(i, j, k), config._scaledDim, Color.yellow);
                //Marker marker = new Marker(new Vector3(i, j, k));
                //markers[(int)index2] = marker;
            }
            Marker marker1 = markers[(int)index];
            Marker marker2 = markers[(int)index2];
            if (marker1 == marker2) marker1.parent = null;
            else marker1.parent = marker2;
        }


        //int loopCount = 0;
        //for (int i = 0; i < parentBufferData.Length; i++)
        //{
        //    if (parentBufferData[i] == i)
        //    {
        //        loopCount++;
        //        int x = (int)(i % dim.x);
        //        int y = (int)((i / dim.x) % dim.y);
        //        int z = (int)((i / dim.x / dim.y) % dim.z);
        //        Debug.Log(x + " " + y + " " + z);
        //    }
        //}
        //Debug.LogWarning("LoopCount" + loopCount);

        return completeTree;
    }

    RenderTexture afterUpdate;
    RenderTexture afterRemedy;
    RenderTexture afterTracing;

    /// <summary>
    /// incrementally calculate the tracing part
    /// </summary>
    /// <returns>markers of connected part</returns>
    public List<Marker> FIMRemedy()
    {
        float time = Time.realtimeSinceStartup;
        uint activeCount;
        trunk = new();
        trunk.Add((uint)seedIndex);
        //Update Steps
        ComputeBuffer activeSet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        activeSet.SetCounterValue(0);

        var connection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("InitTrunk");
        computeShader.SetTexture(kernel, "connection", connection);
        computeShader.SetInt("seedIndex", seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        int[] dimsArray = new int[3] { dim.x, dim.y, dim.z };
        computeShader.SetInts("dims", dimsArray);
        //Update Step
        int updateTime = 0;
        activeSet.SetCounterValue(0);
        do
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateTrunk");
            computeShader.SetTexture(kernel, "connection", connection);
            computeShader.SetTexture(kernel, "origin", volume);
            computeShader.SetTexture(kernel, "threshold", threshold);
            computeShader.SetTexture(kernel, "mask", mask);
            computeShader.SetBuffer(kernel, "activeSet", activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
            uint[] activeData = new uint[activeCount];
            activeSet.GetData(activeData);
            trunk.UnionWith(activeData);
            //Debug.Log($"active buffer count: {activeCount}");
        } while (activeCount > 0);
        activeSet.Release();
        Debug.Log("update cost:" + (Time.realtimeSinceStartup - time));
        time = Time.realtimeSinceStartup;

        float calculationTime = Time.realtimeSinceStartup;
        //Remedy Step
        HashSet<uint> modified = new();
        uint remedyCount;
        ComputeBuffer remedySet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedyFI");
        computeShader.SetBuffer(kernel, "remedySet", remedySet);
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetTexture(kernel, "phi", phi);
        computeShader.SetTexture(kernel, "state", state);
        computeShader.SetTexture(kernel, "parent", parent);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        remedyCount = GetAppendBufferSize(remedySet);
        uint[] remedyData = new uint[remedyCount];
        remedySet.GetData(remedyData);
        modified.UnionWith(remedyData);
        //Debug.Log("first traceTime remedy count:" + remedyCount);
        //remedySet.Release();
        int remedyTime = 0;
        while (remedyCount > 0)
        {
            remedyTime++;
            //remedySet = new ComputeBuffer(dims.x * dims.y * dims.z, sizeof(uint), ComputeBufferType.Append);
            remedySet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateRemedyFI");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "phi", phi);
            computeShader.SetTexture(kernel, "threshold", threshold);
            computeShader.SetTexture(kernel, "parent", parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            remedyCount = GetAppendBufferSize(remedySet);

            kernel = computeShader.FindKernel("UpdateRemedyNeighborFI");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Remedy Set Count
            remedyCount = GetAppendBufferSize(remedySet);
            //remedyData = new uint[remedyCount];
            //remedySet.GetData(remedyData);
            //modified.UnionWith(remedyData);
            //bug.Log($"remedy buffer count: {remedyCount}");
            //remedySet.Release();
        }
        Debug.Log("remedy cost:" + (Time.realtimeSinceStartup - time));
        Debug.Log($"update times:{updateTime} remedy times:{remedyTime}");
        remedySet.Release();


        ComputeBuffer parentBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        ComputeBuffer parentBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        kernel = computeShader.FindKernel("GetParent");
        computeShader.SetTexture(kernel, "parent", parent);
        computeShader.SetBuffer(kernel, "parentBuffer1", parentBuffer1);
        computeShader.SetBuffer(kernel, "parentBuffer2", parentBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        parentBufferData = new uint[parentBuffer1.count + parentBuffer2.count];
        parentBuffer1.GetData(parentBufferData, 0, 0, parentBuffer1.count);
        parentBuffer2.GetData(parentBufferData, parentBuffer1.count, 0, parentBuffer2.count);

        parentBuffer1.Release();
        parentBuffer2.Release();

        Debug.Log($"Incremental Calculation cost: {Time.realtimeSinceStartup - calculationTime}");

        markers = new Dictionary<int, Marker>();
        var completeTree = new List<Marker>();
        Queue<uint> queue = new(trunk);

        while (queue.Count > 0)
        {
            uint peek = queue.Dequeue();
            if (!trunk.Contains(parentBufferData[peek]))
            {
                trunk.Add(parentBufferData[peek]);
                queue.Enqueue(parentBufferData[peek]);
            }
        }

        foreach (var index in trunk)
        {
            int i = (int)(index % dim.x);
            int j = (int)((index / dim.x) % dim.y);
            int k = (int)((index / dim.x / dim.y) % dim.z);
            //createSphere(new Vector3(i, j, k), dims, Color.blue);
            Marker marker = new Marker(new Vector3(i, j, k));
            markers[(int)index] = marker;
            completeTree.Add(marker);
        }

        foreach (var index in trunk)
        {
            uint index2 = parentBufferData[index];
            if (!trunk.Contains(index2))
            {

                int i = (int)(index2 % dim.x);
                int j = (int)((index2 / dim.x) % dim.y);
                int k = (int)((index2 / dim.x / dim.y) % dim.z);

                //createSphere(new Vector3(i, j, k), config._scaledDim, Color.yellow);
                //Marker marker = new Marker(new Vector3(i, j, k));
                //markers[(int)index2] = marker;
            }
            Marker marker1 = markers[(int)index];
            Marker marker2 = markers[(int)index2];
            if (marker1 == marker2) marker1.parent = null;
            else marker1.parent = marker2;
        }
        Debug.Log(trunk.Count);

        int loopCount = 0;
        for (int i = 0; i < parentBufferData.Length; i++)
        {
            if (parentBufferData[i] == i) loopCount++;
        }
        Debug.LogWarning("LoopCount" + loopCount);

        return completeTree;
    }

    /// <summary>
    /// incrementally calculate the tracing part in way of coroutine
    /// </summary>
    /// <returns>markers of connected part</returns>
    public IEnumerator TraceBranch(List<Marker> completeTree)
    {
        float time = Time.realtimeSinceStartup;
        trunk = new();
        trunk.Add((uint)seedIndex);
        //Update Steps
        ComputeBuffer activeSet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        activeSet.SetCounterValue(0);

        var connection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("InitTrunk");
        computeShader.SetTexture(kernel, "connection", connection);
        computeShader.SetInt("seedIndex", seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        int[] dimsArray = new int[3] { dim.x, dim.y, dim.z };
        computeShader.SetInts("dims", dimsArray);
        //Update Step
        uint activeCount = 1;
        int updateTime = 0;
        activeSet.SetCounterValue(0);
        while (activeCount > 0)
        {
            updateTime++;

            kernel = computeShader.FindKernel("UpdateTrunk");
            computeShader.SetTexture(kernel, "connection", connection);
            computeShader.SetTexture(kernel, "origin", volume);
            computeShader.SetTexture(kernel, "threshold", threshold);
            computeShader.SetTexture(kernel, "mask", mask);
            computeShader.SetBuffer(kernel, "activeSet", activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            if (updateTime % 50 == 0)
            {
                uint[] countBufferData = new uint[1];
                yield return StartCoroutine(GetAppendBufferSizeAsync(activeSet, countBufferData));
                activeCount = countBufferData[0];

                uint[] activeData = new uint[activeCount];
                activeSet.GetData(activeData);
                trunk.UnionWith(activeData);
                activeSet.SetCounterValue(0);
            }
        }
        activeSet.Release();
        Debug.Log("update cost:" + (Time.realtimeSinceStartup - time));
        time = Time.realtimeSinceStartup;

        float calculationTime = Time.realtimeSinceStartup;
        //Remedy Step
        uint remedyCount;
        ComputeBuffer remedySet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedyFI");
        computeShader.SetBuffer(kernel, "remedySet", remedySet);
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetTexture(kernel, "phi", phi);
        computeShader.SetTexture(kernel, "state", state);
        computeShader.SetTexture(kernel, "parent", parent);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        remedyCount = GetAppendBufferSize(remedySet);
        uint[] remedyData = new uint[remedyCount];
        remedySet.GetData(remedyData);
        Debug.Log("first traceTime remedy count:" + remedyCount);

        remedySet.GetData(remedyData);
        tracingRemedy.AddRange(remedyData.ToList());

        //remedySet.Release();
        int remedyTime = 0;
        while (remedyCount > 0)
        {
            remedyTime++;
            remedySet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateRemedyFI");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "phi", phi);
            computeShader.SetTexture(kernel, "threshold", threshold);
            computeShader.SetTexture(kernel, "parent", parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //remedyCount = GetAppendBufferSize(remedySet);

            kernel = computeShader.FindKernel("UpdateRemedyNeighborFI");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Remedy Set Count
            if (remedyTime % 50 == 0)
            {
                uint[] countBufferData = new uint[1];
                yield return StartCoroutine(GetAppendBufferSizeAsync(remedySet, countBufferData));
                remedyCount = GetAppendBufferSize(remedySet);
                remedySet.SetCounterValue(0);
            }
        }
        Debug.Log("remedy cost:" + (Time.realtimeSinceStartup - time));
        Debug.Log($"update times:{updateTime} remedy times:{remedyTime}");
        remedySet.Release();

        ComputeBuffer parentBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        ComputeBuffer parentBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        kernel = computeShader.FindKernel("GetParent");
        computeShader.SetTexture(kernel, "parent", parent);
        computeShader.SetBuffer(kernel, "parentBuffer1", parentBuffer1);
        computeShader.SetBuffer(kernel, "parentBuffer2", parentBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        parentBufferData = new uint[parentBuffer1.count + parentBuffer2.count];
        parentBuffer1.GetData(parentBufferData, 0, 0, parentBuffer1.count);
        parentBuffer2.GetData(parentBufferData, parentBuffer1.count, 0, parentBuffer2.count);

        parentBuffer1.Release();
        parentBuffer2.Release();

        //afterTracing = GetBuffer();
        //SaveTexture(afterTracing, "Assets/Textures/FIM/afterTracing.Asset");

        //var tracingDiff = GetDiff(afterRemedy,afterTracing);
        //SaveTexture(tracingDiff, "Assets/Textures/FIM/tracingDiff.Asset");

        Debug.Log($"Incremental Calculation cost: {Time.realtimeSinceStartup - calculationTime}");
        calculationTime = Time.realtimeSinceStartup;

        markers = new Dictionary<int, Marker>(trunk.Count);
        completeTree.Capacity = (trunk.Count);
        Queue<uint> queue = new(trunk);

        while (queue.Count > 0)
        {
            uint peek = queue.Dequeue();
            if (!trunk.Contains(parentBufferData[peek]))
            {
                trunk.Add(parentBufferData[peek]);
                queue.Enqueue(parentBufferData[peek]);
            }
        }

        foreach (var index in trunk)
        {
            int i = (int)(index % dim.x);
            int j = (int)((index / dim.x) % dim.y);
            int k = (int)((index / dim.x / dim.y) % dim.z);
            //createSphere(new Vector3(i, j, k), dims, Color.blue);
            Marker marker = new Marker(new Vector3(i, j, k));
            markers[(int)index] = marker;
            completeTree.Add(marker);
        }

        foreach (var index in trunk)
        {
            uint index2 = parentBufferData[index];
            Marker marker1 = markers[(int)index];
            Marker marker2 = markers[(int)index2];
            if (marker1 == marker2) marker1.parent = null;
            else marker1.parent = marker2;
        }
        Debug.Log(trunk.Count);

        Debug.Log($"Incremental Calculation cost: {Time.realtimeSinceStartup - calculationTime}");
    }

    public List<Marker> FIMErase(HashSet<uint> modified)
    {
        float time = Time.realtimeSinceStartup;
        uint activeCount;
        trunk = new HashSet<uint>();
        trunk.Add((uint)seedIndex);
        //Update Steps
        ComputeBuffer activeSet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        activeSet.SetCounterValue(0);

        var connection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("InitTrunk");
        computeShader.SetTexture(kernel, "connection", connection);
        computeShader.SetInt("seedIndex", seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        float calculationTime = Time.realtimeSinceStartup;

        int[] dimsArray = new int[3] { dim.x, dim.y, dim.z };
        computeShader.SetInts("dims", dimsArray);
        //Update Step
        int updateTime = 0;
        activeSet.SetCounterValue(0);
        do
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateTrunk");
            computeShader.SetTexture(kernel, "connection", connection);
            computeShader.SetTexture(kernel, "origin", volume);
            computeShader.SetTexture(kernel, "threshold", threshold);
            computeShader.SetTexture(kernel, "mask", mask);
            computeShader.SetBuffer(kernel, "activeSet", activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
            uint[] activeData = new uint[activeCount];
            activeSet.GetData(activeData);
            trunk.UnionWith(activeData);
            //Debug.Log($"active buffer count: {activeCount}");
        } while (activeCount > 0);
        activeSet.Release();

        ComputeBuffer eraseTarget = new(modified.Count, sizeof(uint), ComputeBufferType.Default);
        eraseTarget.SetData(modified.ToArray());
        kernel = computeShader.FindKernel("InitErase");
        computeShader.SetBuffer(kernel, "eraseTargetBuffer", eraseTarget);
        computeShader.SetTexture(kernel, "phi", phi);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));
        eraseTarget.Release();

        //Remedy Step
        uint remedyCount;
        ComputeBuffer remedySet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedyFI");
        computeShader.SetBuffer(kernel, "remedySet", remedySet);
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetTexture(kernel, "phi", phi);
        computeShader.SetTexture(kernel, "state", state);
        computeShader.SetTexture(kernel, "parent", parent);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        remedyCount = GetAppendBufferSize(remedySet);
        //Debug.Log("first traceTime remedy count:" + remedyCount);
        //remedySet.Release();
        int remedyTime = 0;
        while (remedyCount > 0)
        {
            remedyTime++;
            //remedySet = new ComputeBuffer(dims.x * dims.y * dims.z, sizeof(uint), ComputeBufferType.Append);
            remedySet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateRemedyFI");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.SetTexture(kernel, "gwdt", gwdt);
            computeShader.SetTexture(kernel, "phi", phi);
            computeShader.SetTexture(kernel, "threshold", threshold);
            computeShader.SetTexture(kernel, "parent", parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            remedyCount = GetAppendBufferSize(remedySet);

            kernel = computeShader.FindKernel("UpdateRemedyNeighborFI");
            computeShader.SetBuffer(kernel, "remedySet", remedySet);
            computeShader.SetTexture(kernel, "state", state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Remedy Set Count
            remedyCount = GetAppendBufferSize(remedySet);
        }
        remedySet.Release();
        Debug.Log("remedy cost:" + (Time.realtimeSinceStartup - time));
        Debug.Log($"update times:{updateTime} remedy times:{remedyTime}");


        ComputeBuffer parentBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        ComputeBuffer parentBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        kernel = computeShader.FindKernel("GetParent");
        computeShader.SetTexture(kernel, "parent", parent);
        computeShader.SetBuffer(kernel, "parentBuffer1", parentBuffer1);
        computeShader.SetBuffer(kernel, "parentBuffer2", parentBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        parentBufferData = new uint[parentBuffer1.count + parentBuffer2.count];
        parentBuffer1.GetData(parentBufferData, 0, 0, parentBuffer1.count);
        parentBuffer2.GetData(parentBufferData, parentBuffer1.count, 0, parentBuffer2.count);

        parentBuffer1.Release();
        parentBuffer2.Release();

        Debug.Log($"Incremental Calculation cost: {Time.realtimeSinceStartup - calculationTime}");

        markers = new Dictionary<int, Marker>();
        var completeTree = new List<Marker>();
        Queue<uint> queue = new(trunk);

        while (queue.Count > 0)
        {
            uint peek = queue.Dequeue();
            if (!trunk.Contains(parentBufferData[peek]))
            {
                trunk.Add(parentBufferData[peek]);
                queue.Enqueue(parentBufferData[peek]);
            }
        }

        foreach (var index in trunk)
        {
            int i = (int)(index % dim.x);
            int j = (int)((index / dim.x) % dim.y);
            int k = (int)((index / dim.x / dim.y) % dim.z);
            //createSphere(new Vector3(i, j, k), dims, Color.blue);
            Marker marker = new Marker(new Vector3(i, j, k));
            markers[(int)index] = marker;
            completeTree.Add(marker);
        }

        foreach (var index in trunk)
        {
            uint index2 = parentBufferData[index];
            if (!trunk.Contains(index2))
            {

                int i = (int)(index2 % dim.x);
                int j = (int)((index2 / dim.x) % dim.y);
                int k = (int)((index2 / dim.x / dim.y) % dim.z);

                //createSphere(new Vector3(i, j, k), config._scaledDim, Color.yellow);
                //Marker marker = new Marker(new Vector3(i, j, k));
                //markers[(int)index2] = marker;
            }
            Marker marker1 = markers[(int)index];
            Marker marker2 = markers[(int)index2];
            if (marker1 == marker2) marker1.parent = null;
            else marker1.parent = marker2;
        }
        Debug.Log(trunk.Count);

        return completeTree;
    }

    /// <summary>
    /// get the indexes of the branch from the target to the trunk
    /// </summary>
    /// <param name="targetIndex"></param>
    /// <returns></returns>
    public List<uint> GetBranch(uint targetIndex)
    {
        HashSet<uint> ret = new();
        uint iter = targetIndex;
        while (!trunk.Contains(iter))
        {
            //createSphere(IndexToVector(iter, config._scaledDim), config._scaledDim, iter == targetIndex ? Color.green : Color.yellow);
            if (ret.Contains(iter))
            {
                Debug.Log("there is a Loop����id: " + iter);
                foreach (uint index in ret) Debug.Log(index);
                break;
            }
            ret.Add(iter);
            iter = parentBufferData[iter];
        }
        return ret.ToList();
    }

    /// <summary>
    /// adjust the intensity of targets
    /// </summary>
    /// <param name="targets"></param>
    /// <param name="undo"></param>
    public void AdjustIntensity(List<uint> targets, bool undo)
    {
        if (targets.Count == 0)
        {
            Debug.LogWarning("adjust targets' count is zero");
            return;
        }
        uint[] targetData = targets.ToArray();
        ComputeBuffer targetBuffer = new ComputeBuffer(targets.Count, sizeof(uint), ComputeBufferType.Default);
        targetBuffer.SetData(targetData);
        int kernel = computeShader.FindKernel("AdjustIntensity");
        computeShader.SetBuffer(kernel, "targetBuffer", targetBuffer);
        computeShader.SetTexture(kernel, "bias", bias);
        computeShader.SetTexture(kernel, "threshold", threshold);
        computeShader.SetInt("undo", undo ? 1 : 0);
        computeShader.SetInt("targetNum", targets.Count);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(targets.Count / 128.0f), 1, 1);
        targetBuffer.Release();
    }

    /// <summary>
    /// adjust the intensity of targets with a bias intensity
    /// </summary>
    /// <param name="targetIndexes"></param>
    /// <param name="intensity"></param>
    public void AdjustIntensity(List<uint> targetIndexes, float intensity)
    {
        uint[] targetData = targetIndexes.ToArray();
        ComputeBuffer targetBuffer = new ComputeBuffer(targetIndexes.Count, sizeof(uint), ComputeBufferType.Default);
        targetBuffer.SetData(targetData);
        int kernel = computeShader.FindKernel("AdjustIntensityWithValue");
        computeShader.SetBuffer(kernel, "targetBuffer", targetBuffer);
        computeShader.SetTexture(kernel, "bias", bias);
        computeShader.SetFloat("intensity", intensity);
        computeShader.SetInt("targetNum", targetIndexes.Count);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(targetIndexes.Count / 128.0f), 1, 1);
        targetBuffer.Release();
    }

    /// <summary>
    /// mask the target voxels
    /// </summary>
    /// <param name="targetindexes"></param>
    /// <param name="undo"></param>
    /// <returns>render texture and byte data of the masked volume</returns>
    public (RenderTexture, byte[]) ModifyMask(List<uint> targetindexes, bool undo)
    {
        foreach (uint index in targetindexes)
        {
            var pos = IndexToVector(index, dim);
            //createSphere(pos, dims, Color.blue);
        }
        Debug.Log("undo: " + undo);
        Debug.Log("target count: " + targetindexes.Count);
        int kernel;
        if (targetindexes.Count > 0)
        {
            ComputeBuffer maskTargetBuffer = new(targetindexes.Count, sizeof(uint), ComputeBufferType.Default);
            maskTargetBuffer.SetData(targetindexes.ToArray());
            kernel = computeShader.FindKernel("ModifyMask");
            computeShader.SetBuffer(kernel, "maskTargetBuffer", maskTargetBuffer);
            computeShader.SetTexture(kernel, "mask", mask);
            computeShader.SetInt("targetNum", targetindexes.Count);
            computeShader.SetBool("undo", undo);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(targetindexes.Count / 128.0f), 1, 1);
            maskTargetBuffer.Release();
        }

        ComputeBuffer maskedVolumeBuffer = new(dim.x * dim.y * dim.z, sizeof(float), ComputeBufferType.Default);
        kernel = computeShader.FindKernel("GetMaskedVolumeData");
        computeShader.SetBuffer(kernel, "maskedVolumeBuffer", maskedVolumeBuffer);
        computeShader.SetTexture(kernel, "origin", config.ScaledVolume);
        computeShader.SetTexture(kernel, "mask", mask);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        float[] bufferData = new float[dim.x * dim.y * dim.z];
        byte[] data = new byte[dim.x * dim.y * dim.z];
        maskedVolumeBuffer.GetData(bufferData);
        maskedVolumeBuffer.Release();
        Parallel.For(0, dim.x * dim.y * dim.z, (i) =>
        {
            data[i] = (byte)(bufferData[i] * 255.0f);
        });

        return (mask, data);
    }

    public RenderTexture ModifySelection(List<uint> targetindexes)
    {
        selection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RFloat, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat);
        Debug.Log("target count: " + targetindexes.Count);
        int kernel;
        if (targetindexes.Count > 0)
        {
            ComputeBuffer targetBuffer = new(targetindexes.Count, sizeof(uint), ComputeBufferType.Default);
            targetBuffer.SetData(targetindexes.ToArray());
            kernel = computeShader.FindKernel("ModifySelection");
            computeShader.SetBuffer(kernel, "selectionTargetBuffer", targetBuffer);
            computeShader.SetTexture(kernel, "selection", selection);
            computeShader.SetInt("targetNum", targetindexes.Count);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(targetindexes.Count / 128.0f), 1, 1);
            targetBuffer.Release();
        }

        return selection;
    }

    public RenderTexture ClearSelection()
    {
        selection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RFloat, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat);
        return selection;
    }

    RenderTexture InitRenderTexture3D(int width, int height, int depth, RenderTextureFormat format, UnityEngine.Experimental.Rendering.GraphicsFormat graphicsFormat)
    {
        RenderTexture renderTexture = new(width, height, 0, format);
        renderTexture.graphicsFormat = graphicsFormat;
        renderTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        renderTexture.volumeDepth = depth;
        renderTexture.enableRandomWrite = true;
        renderTexture.filterMode = FilterMode.Point;
        renderTexture.wrapMode = TextureWrapMode.Clamp;
        //renderTexture.Create();
        return renderTexture;
    }

    /// <summary>
    /// get the count of buffer
    /// </summary>
    /// <param name="appendBuffer"></param>
    /// <returns></returns>
    uint GetAppendBufferSize(ComputeBuffer appendBuffer)
    {
        uint[] countBufferData = new uint[1];
        var countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.IndirectArguments);
        ComputeBuffer.CopyCount(appendBuffer, countBuffer, 0);
        countBuffer.GetData(countBufferData);
        uint count = countBufferData[0];
        countBuffer.Release();

        return count;
    }

    /// <summary>
    /// coroutine version
    /// </summary>
    /// <param name="appendBuffer"></param>
    /// <param name="countBufferData"></param>
    /// <returns></returns>
    IEnumerator GetAppendBufferSizeAsync(ComputeBuffer appendBuffer, uint[] countBufferData)
    {
        var countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.IndirectArguments);
        ComputeBuffer.CopyCount(appendBuffer, countBuffer, 0);
        var request = AsyncGPUReadback.Request(countBuffer);
        yield return new WaitUntil(() => request.done);
        request.GetData<uint>().CopyTo(countBufferData);
        countBuffer.Release();
    }

    private Vector3 IndexToVector(uint index, Vector3Int dim)
    {
        int x = (int)(index % dim.x);
        int y = (int)((index / dim.x) % dim.y);
        int z = (int)((index / dim.x / dim.y) % dim.z);
        return new Vector3(x, y, z);
    }

    private Vector3 IndexToVector(int index, Vector3Int dim)
    {
        int x = (index % dim.x);
        int y = ((index / dim.x) % dim.y);
        int z = ((index / dim.x / dim.y) % dim.z);
        return new Vector3(x, y, z);
    }

    private int VectorToIndex(Vector3 pos, Vector3Int dim)
    {
        int index = ((int)pos.x + (int)pos.y * dim.x + (int)pos.z * dim.x * dim.y);
        return index;
    }

    private void createSphere(Vector3 pos, Vector3Int Dim, Color color, float scale = 0.002f)
    {
        Vector3 position = pos.Div(Dim) - new Vector3(0.5f, 0.5f, 0.5f);
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var trans = GameObject.Find("Cube").transform;
        sphere.transform.position = trans.TransformPoint(position);
        sphere.transform.localScale = new Vector3(scale, scale, scale);
        sphere.transform.parent = GameObject.Find("SearchPoints").transform;
        sphere.GetComponent<MeshRenderer>().material.color = color;
    }

    private RenderTexture CopyData(Texture3D src)
    {
        var dst = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("CopyData");
        computeShader.SetTexture(kernel, "volume", dst);
        computeShader.SetTexture(kernel, "origin", src);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        return dst;
    }

    private RenderTexture InitBias()
    {
        var bias = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("InitBias");
        computeShader.SetTexture(kernel, "bias", bias);
        Debug.Log(numthreads);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        return bias;
    }

    /// <summary>
    /// return the connected part of the target index
    /// </summary>
    /// <param name="targetIndex"></param>
    /// <returns></returns>
    public List<uint> GetCluster(uint targetIndex)
    {
        int bkgThreshold = config.ViewThresh;
        var connection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("InitClusterSeed");
        computeShader.SetTexture(kernel, "connection", connection);
        computeShader.SetInt("seedIndex", (int)targetIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        int[] dimsArray = new int[3] { dim.x, dim.y, dim.z };
        uint activeCount;

        HashSet<uint> cluster = new();
        cluster.Add(targetIndex);
        computeShader.SetInts("dims", dimsArray);
        computeShader.SetFloat("viewThreshold", bkgThreshold / 255.0f);
        //Update Step
        int updateTime = 0;
        var activeSet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        do
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateCluster");
            computeShader.SetTexture(kernel, "connection", connection);
            computeShader.SetTexture(kernel, "origin", config.ScaledVolume);
            computeShader.SetBuffer(kernel, "activeSet", activeSet);
            computeShader.SetFloat("viewThreshold", bkgThreshold / 255.0f);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
            uint[] activeData = new uint[activeCount];
            activeSet.GetData(activeData);
            cluster.UnionWith(activeData);
            //Debug.Log($"active buffer count: {activeCount}");
        } while (activeCount > 0);
        activeSet.Release();
        Debug.Log($"cluster size: {cluster.Count}");
        return cluster.ToList();
    }

    public List<uint> GetForegroundExtension()
    {
        int bkgThreshold = config.BkgThresh;
        var connection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        var computeShader = Resources.Load<ComputeShader>("ComputeShaders/Utility");
        int kernel = computeShader.FindKernel("InitForegroundBoundary");
        computeShader.SetTexture(kernel, "connection", connection);
        computeShader.SetTexture(kernel, "origin", config.ScaledVolume);
        computeShader.SetTexture(kernel, "threshold", threshold);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        int[] dimsArray = new int[3] { dim.x, dim.y, dim.z };
        uint activeCount;

        HashSet<uint> foreground = new();
        computeShader.SetInts("dims", dimsArray);
        computeShader.SetTexture(kernel, "threshold", threshold);
        //Update Step
        int updateTime = 0;
        var activeSet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        do
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateForeground");
            computeShader.SetTexture(kernel, "connection", connection);
            computeShader.SetTexture(kernel, "origin", config.ScaledVolume);
            computeShader.SetBuffer(kernel, "activeSet", activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
            uint[] activeData = new uint[activeCount];
            activeSet.GetData(activeData);
            foreground.UnionWith(activeData);
            //Debug.Log($"active buffer count: {activeCount}");
        } while (activeCount > 0);

        Debug.Log($"cluster size: {foreground.Count}");

        int extend_width = 3;
        do
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("ExtendForeground");
            computeShader.SetTexture(kernel, "connection", connection);
            computeShader.SetTexture(kernel, "origin", config.ScaledVolume);
            computeShader.SetBuffer(kernel, "activeSet", activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
            uint[] activeData = new uint[activeCount];
            activeSet.GetData(activeData);
            foreground.UnionWith(activeData);
            //Debug.Log($"active buffer count: {activeCount}");
        } while (activeCount > 0 && extend_width-- > 0);
        activeSet.Release();

        Debug.Log($"cluster size: {foreground.Count}");
        return foreground.ToList();

    }

    internal void AdjustThreshold(Vector3 hitPos, Vector3 direction)
    {
        int defaultThreshold = config.BkgThresh;
        float viewRadius = config.viewRadius;
        var computeShader = Resources.Load<ComputeShader>("ComputeShaders/Utility");

        int kernel = computeShader.FindKernel("ModifyThreshold");
        computeShader.SetTexture(kernel, "threshold", threshold);
        computeShader.SetVector("hitPos", hitPos);
        computeShader.SetVector("direction", direction);
        computeShader.SetFloat("viewRadius", viewRadius);
        computeShader.SetInt("defaultThreshold", defaultThreshold);
        computeShader.SetInt("thresholdOffset", config.thresholdOffset);
        computeShader.Dispatch(kernel, dim.x / config.thresholdBlockSize / numthreads.x, dim.y / config.thresholdBlockSize / numthreads.y, dim.z / config.thresholdBlockSize / numthreads.z);
    }

    internal RenderTexture InitThreshold()
    {
        int defaultThreshold = config.BkgThresh;
        var computeShader = Resources.Load<ComputeShader>("ComputeShaders/Utility");

        int kernel = computeShader.FindKernel("InitThreshold");
        computeShader.SetTexture(kernel, "threshold", threshold);
        computeShader.SetInt("defaultThreshold", defaultThreshold);
        computeShader.Dispatch(kernel, dim.x / config.thresholdBlockSize / numthreads.x, dim.y / config.thresholdBlockSize / numthreads.y, dim.z / config.thresholdBlockSize / numthreads.z);

        return threshold;
    }

    public Vector3Int cubesDim = new(11, 11, 5);
    public Color cubeColor = new Color(1, 1, 0, 0.25f);
    public Color wireColor = new Color(1, 1, 0, 0.8f);
    public int distThreshold = 3;
    public List<uint> firstRemedy = new();
    public List<uint> tracingRemedy = new();
    public float RemedyRate = 0.5f;
    private static readonly int State = Shader.PropertyToID("state");
    private static readonly int Parent = Shader.PropertyToID("parent");
    private static readonly int Phi = Shader.PropertyToID("phi");
    private static readonly int Gwdt = Shader.PropertyToID("gwdt");
    private static readonly int Threshold = Shader.PropertyToID("threshold");
    private static readonly int MaxIntensity = Shader.PropertyToID("maxIntensity");
    private static readonly int SeedIndex = Shader.PropertyToID("seedIndex");
    private static readonly int Mask = Shader.PropertyToID("mask");
    private static readonly int ActiveSet = Shader.PropertyToID("activeSet");
    private static readonly int Dims = Shader.PropertyToID("dims");
    private static readonly int Visualize = Shader.PropertyToID("visualize");
    private static readonly int RemedySet = Shader.PropertyToID("remedySet");
    private static readonly int ParentBuffer1 = Shader.PropertyToID("parentBuffer1");
    private static readonly int ParentBuffer2 = Shader.PropertyToID("parentBuffer2");

    void OnDrawGizmosSelected()
    {
        int[] cubeStatus = new int[cubesDim.x * cubesDim.y * cubesDim.z];
        for (int i = 0; i < tracingRemedy.Count; i++)
        {
            var pos = IndexToVector(tracingRemedy[i], dim);
            pos = pos.Div(dim).Mul(cubesDim);
            int index = VectorToIndex(pos, cubesDim);
            cubeStatus[index] = 1;
        }
        if(tracingRemedy.Count==0)
        {
            for (int i=0;i<firstRemedy.Count;i++)
            {
                var pos = IndexToVector(firstRemedy[i], dim);
                pos = pos.Div(dim).Mul(cubesDim);
                int index = VectorToIndex(pos, cubesDim);
                cubeStatus[index] += 1;
            }
        }
        Vector3 center = cubesDim / 2;
        //cubeStatus[VectorToIndex(center, new Vector3Int((int)cubesDim.x, (int)cubesDim.y, (int)cubesDim.z))] = 1;
        List<int> indexes = new List<int>();
        for (int i = 0; i < cubeStatus.Length; i++)
        {
            indexes.Add(i);
        }
        indexes.Sort((a, b) =>
        {
            Vector3 coordA = IndexToVector(a, new Vector3Int((int)cubesDim.x, (int)cubesDim.y, (int)cubesDim.z));
            Vector3 posA = coordA.Div(cubesDim) - 0.5f * Vector3.one;
            posA = config.cube.transform.TransformPoint(posA);
            float distToCameraA = Vector3.Distance(posA, Camera.current.transform.position);

            Vector3 coordB = IndexToVector(b, new Vector3Int((int)cubesDim.x, (int)cubesDim.y, (int)cubesDim.z));
            Vector3 posB = coordB.Div(cubesDim) - 0.5f * Vector3.one;
            posB = config.cube.transform.TransformPoint(posB);
            float distToCameraB = Vector3.Distance(posB, Camera.current.transform.position);
            if (distToCameraA < distToCameraB) return 1;
            else if (distToCameraA > distToCameraB) return -1;
            else return 0;
        });

        for (int i = 0; i < cubeStatus.Length; i++)
        {
            int index = indexes[i];
            Vector3 coord = IndexToVector(index, new Vector3Int((int)cubesDim.x, (int)cubesDim.y, (int)cubesDim.z));
            Vector3 offset = coord - center;


            float dist = Math.Abs(offset.x) + Math.Abs(offset.y) + Math.Abs(offset.z);
            if (dist < 0)
            {
                Gizmos.color = new Color(1, 0, 0, 0.1F);
                Vector3 pos = coord.Div(cubesDim) - 0.5f * Vector3.one;
                pos = config.cube.transform.TransformPoint(pos);
                //Gizmos.DrawCube(pos, new Vector3(1 / cubesDim.x, 1 / cubesDim.y, 0.5f / cubesDim.z));
                //Gizmos.color = new Color(1, 1, 1, 1.0F);
                //Gizmos.DrawWireCube(pos, new Vector3(1, 1, 0.5f));
            }
            //else if(dist == 3)
            else if (dist < distThreshold)
            {
                Gizmos.color = cubeColor;
                Vector3 pos = coord.Div(cubesDim) - 0.5f * Vector3.one;
                pos = config.cube.transform.TransformPoint(pos);
                pos += 0.5f * new Vector3(1.0f / cubesDim.x, 1.0f / cubesDim.y, 0.5f / cubesDim.z);
                Gizmos.DrawCube(pos, new Vector3(1.0f / cubesDim.x, 1.0f / cubesDim.y, 0.5f / cubesDim.z));
                Gizmos.color = wireColor;
                Gizmos.DrawWireCube(pos, new Vector3(1.0f / cubesDim.x, 1.0f / cubesDim.y, 0.5f / cubesDim.z));
            }

            if (cubeStatus[index]> 1733.183471* RemedyRate)
            {
                Gizmos.color = cubeColor;
                Vector3 pos = coord.Div(cubesDim) - 0.5f * Vector3.one;
                pos = config.cube.transform.TransformPoint(pos);
                pos += 0.5f * new Vector3(1.0f / cubesDim.x, 1.0f / cubesDim.y, 0.5f / cubesDim.z);
                Gizmos.DrawCube(pos, new Vector3(1.0f / cubesDim.x, 1.0f / cubesDim.y, 0.5f / cubesDim.z));
                Gizmos.color = wireColor;
                Gizmos.DrawWireCube(pos, new Vector3(1.0f / cubesDim.x, 1.0f / cubesDim.y, 0.5f / cubesDim.z));
            }
        }
    }

    RenderTexture GetBuffer()
    {
        var phiBuffer = new ComputeBuffer(dim.x * dim.y * dim.z, sizeof(float));
        int kernel = computeShader.FindKernel("GetPhi");
        computeShader.SetTexture(kernel, "phi", phi);
        computeShader.SetBuffer(kernel, "phiBuffer", phiBuffer);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));

        float[] phiData = new float[phiBuffer.count];
        phiBuffer.GetData(phiData);
        float phiMax = 0;
        for (int i = 0; i < phiData.Length; i++)
        {

            phiMax = Math.Max(phiMax, phiData[i]);
        }
        Debug.Log($"phiMax: {phiMax}");
        phiBuffer.Release();

        var buff = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        kernel = computeShader.FindKernel("GetBuff");
        computeShader.SetTexture(kernel, "buff", buff);
        computeShader.SetTexture(kernel, "phi", phi);
        computeShader.SetTexture(kernel, "gwdt", gwdt);
        computeShader.SetFloat("phiMax", phiMax);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));
        return buff;
    }

    RenderTexture GetDiff(RenderTexture before, RenderTexture after)
    {
        var diff = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("GetDiff");
        computeShader.SetTexture(kernel, "diff", diff);
        computeShader.SetTexture(kernel, "before", before);
        computeShader.SetTexture(kernel, "after", after);
        computeShader.Dispatch(kernel, Mathf.CeilToInt((float)dim.x / (float)numthreads.x), Mathf.CeilToInt((float)dim.y / (float)numthreads.y), Mathf.CeilToInt((float)dim.z / (float)numthreads.z));
        return diff;
    }

    void SaveTexture(Texture texture, string path)
    {
#if UNITY_EDITOR
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(texture, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
#endif
    }
}
