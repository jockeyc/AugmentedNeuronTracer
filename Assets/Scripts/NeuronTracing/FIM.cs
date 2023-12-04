using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IntraMR;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

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
    private Vector3Int numThreads = new(8, 8, 4);

    public HashSet<uint> trunk;
    public HashSet<uint> oldTrunk;
    uint[] parentBufferData;
    public Dictionary<int, Marker> markers;

    // Start is called before the first frame update
    public void Start()
    {
        config = GetComponent<Config>();
        computeShader = Resources.Load("ComputeShaders/FIM") as ComputeShader;
        PrepareData();
        threshold = InitThreshold();
        //StartCoroutine(FIMDTCoroutine());
    }

    public void PrepareData()
    {
        dim = config.scaledDim;
        gwdt = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RFloat, GraphicsFormat.R32_SFloat);
        state = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UInt);
        parent = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RInt, GraphicsFormat.R32_UInt);
        phi = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RFloat, GraphicsFormat.R32_SFloat);
        mask = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RFloat, GraphicsFormat.R32_SFloat);
        visualize = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UNorm);
        threshold = InitRenderTexture3D(dim.x , dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UNorm);
        selection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RFloat, GraphicsFormat.R32_SFloat);
        bias = InitBias();
        volume = CopyData(config.ScaledVolume);
    }

    //Return the part connected to soma according to the threshold
    public RenderTexture ConnectedPart(bool view) {
        int bkgThreshold = view ? config.ViewThresh : config.BkgThresh;
        var connection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UNorm);

        int kernel = computeShader.FindKernel("InitConnectionSeed");
        computeShader.SetTexture(kernel, Connection, connection);
        computeShader.SetInt(SeedIndex, VectorToIndex(config._rootPos, dim));
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        int[] dimsArray = { dim.x, dim.y, dim.z };
        uint activeCount;

        computeShader.SetInts(Dims, dimsArray);
        computeShader.SetInt(BkgThreshold, bkgThreshold);
        //Update Step
        var activeSet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        do
        {
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateConnection");
            computeShader.SetTexture(kernel, Connection, connection);
            computeShader.SetTexture(kernel, Mask, mask);
            computeShader.SetTexture(kernel, Volume, volume);
            computeShader.SetBuffer(kernel, ActiveSet, activeSet);
            computeShader.SetTexture(kernel, Threshold, threshold);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
        int kernel = computeShader.FindKernel("ApplyBias");
        computeShader.SetTexture(kernel, Volume, volume);
        computeShader.SetTexture(kernel, Origin, config.ScaledVolume);
        computeShader.SetTexture(kernel, Bias, bias);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        kernel = computeShader.FindKernel("InitBound");
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Volume, volume);
        computeShader.SetTexture(kernel, Mask, mask);
        computeShader.SetTexture(kernel, Threshold, threshold);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        int[] dimsArray = { dim.x, dim.y, dim.z };
        uint activeCount;
        computeShader.SetInts(Dims, dimsArray);

        //Update Step
        int updateTime = 0;
        ComputeBuffer activeSet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        do
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateFarState");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Visualize, visualize);
            computeShader.SetBuffer(kernel, ActiveSet, activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateActiveValue");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Volume, volume);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateActiveState");
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
        } while (activeCount > 0);
        activeSet.Release();

        //Remedy Step
        ComputeBuffer remedySet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedy");
        computeShader.SetBuffer(kernel, RemedySet, remedySet);
        computeShader.SetTexture(kernel, Volume, volume);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, State, state);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        uint remedyCount = GetAppendBufferSize(remedySet);

        int remedyTime = 0;
        while (remedyCount > 0)
        {
            remedyTime++;
            remedySet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateRemedy");
            computeShader.SetBuffer(kernel, RemedySet, remedySet);
            computeShader.SetTexture(kernel, Volume, volume);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));
            
            kernel = computeShader.FindKernel("UpdateRemedyNeighbor");
            computeShader.SetBuffer(kernel, RemedySet, remedySet);
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));


            //Get Remedy Set Count
            remedyCount = GetAppendBufferSize(remedySet);
        }
        remedySet.Release();

        Debug.Log($"DT update times:{updateTime} remedy times:{remedyTime}");

        ComputeBuffer gwdtBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        ComputeBuffer gwdtBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        kernel = computeShader.FindKernel("VisualizeTexture");
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetBuffer(kernel, GwdtBuffer1, gwdtBuffer1);
        computeShader.SetBuffer(kernel, GwdtBuffer2, gwdtBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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

        Debug.Log($"Max Intensity:{maxIntensity}");


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
        //computeShader.SetTexture(kernel, Gwdt, gwdt);
        //computeShader.SetTexture(kernel, "visualization", visualization);
        //computeShader.SetFloat("max_intensity", maxIntensity);
        //computeShader.Dispatch(kernel, dims.x / numThreads.x, dims.y / numThreads.y, dims.z / numThreads.z);


        //AssetDatabase.DeleteAsset("Assets/Textures/FIMFI/gwdt.Asset");
        //AssetDatabase.CreateAsset(visualization, "Assets/Textures/FIMFI/gwdt.Asset");
        //AssetDatabase.SaveAssets();
        //AssetDatabase.Refresh();


    }

    // coroutine version of  distance transform with FIM
    public IEnumerator DistanceTransformAsync()
    {
        int kernel = computeShader.FindKernel("ApplyBias");
        computeShader.SetTexture(kernel, Volume, volume);
        computeShader.SetTexture(kernel, Origin, config.ScaledVolume);
        computeShader.SetTexture(kernel, Bias, bias);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        kernel = computeShader.FindKernel("InitBound");
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Volume, volume);
        computeShader.SetTexture(kernel, Mask, mask);
        computeShader.SetTexture(kernel, Threshold, threshold);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        int[] dimsArray = { dim.x, dim.y, dim.z };
        uint activeCount = 1;
        computeShader.SetInts(Dims, dimsArray);

        //Update Step
        int updateTime = 0;
        ComputeBuffer activeSet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        while (activeCount > 0)
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateFarState");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Visualize, visualize);
            computeShader.SetBuffer(kernel, ActiveSet, activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateActiveValue");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Volume, volume);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateActiveState");
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            //Get Active Set Count
            if (updateTime % 10 == 0)
            {
                uint[] countBufferData = new uint[1];
                yield return StartCoroutine(GetAppendBufferSizeAsync(activeSet, countBufferData));
                activeCount = countBufferData[0];
            }
        }
        activeSet.Release();
        
        //Remedy Step
        int[] dispatchFlag = {0};
        ComputeBuffer dispatchBuffer = new(1, sizeof(int));
        dispatchBuffer.SetData(dispatchFlag);
        
        kernel = computeShader.FindKernel("InitRemedy");
        computeShader.SetBuffer(kernel, DispatchBuffer, dispatchBuffer);
        computeShader.SetTexture(kernel, Volume, volume);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, State, state);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        bool continueDispatch = NeedDispatch(dispatchBuffer);

        int remedyTime = 0;
        while (continueDispatch)
        {
            remedyTime++;
            dispatchFlag[0] = 0;
            dispatchBuffer.SetData(dispatchFlag);
            
            kernel = computeShader.FindKernel("UpdateRemedy");
            computeShader.SetBuffer(kernel, DispatchBuffer, dispatchBuffer);
            computeShader.SetTexture(kernel, Volume, volume);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateRemedyNeighbor");
            computeShader.SetBuffer(kernel, DispatchBuffer, dispatchBuffer);
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));


            //Get Remedy Set Count
            // if (remedyTime % 10 == 0)
            // {
            //     uint[] countBufferData = new uint[1];
            //     yield return StartCoroutine(GetAppendBufferSizeAsync(remedySet, countBufferData));
            //     remedyCount = countBufferData[0];
            // }
            continueDispatch = NeedDispatch(dispatchBuffer);
        }
        
        dispatchBuffer.Release();

        Debug.Log($"DT update times:{updateTime} remedy times:{remedyTime}");

        ComputeBuffer gwdtBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        ComputeBuffer gwdtBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        kernel = computeShader.FindKernel("VisualizeTexture");
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetBuffer(kernel, GwdtBuffer1, gwdtBuffer1);
        computeShader.SetBuffer(kernel, GwdtBuffer2, gwdtBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
        Debug.Log($"Max Intensity:{maxIntensity}");


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
        //computeShader.SetTexture(kernel, Gwdt, gwdt);
        //computeShader.SetTexture(kernel, "visualization", visualization);
        //computeShader.SetFloat(MaxIntensity, maxIntensity);
        //computeShader.Dispatch(kernel, dim.x / numThreads.x, dim.y / numThreads.y, dim.z / numThreads.z);

        //visualization.filterMode = FilterMode.Bilinear;
        //Config.Instance.postProcessVolume.profile.GetSetting<BaseVolumeRendering>().volume.value = visualization;

        //AssetDatabase.DeleteAsset("Assets/Textures/FIM/gwdt.Asset");
        //AssetDatabase.CreateAsset(visualization, "Assets/Textures/FIM/gwdt.Asset");
        //AssetDatabase.SaveAssets();
        //AssetDatabase.Refresh();
    }

    // calculate the geodesic distance within the trunk part with FIM
    public List<Marker> InitialReconstruction()
    {
        float computationTime = Time.realtimeSinceStartup;
        int kernel = computeShader.FindKernel("InitSeed");
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Threshold, threshold);
        computeShader.SetFloat(MaxIntensity, maxIntensity);
        computeShader.SetInt(SeedIndex, seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        uint activeCount = 1;
        trunk = new() { (uint)seedIndex };
        //Update Steps
        var activeSet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        activeSet.SetCounterValue(0);

        float time = Time.realtimeSinceStartup;
        int updateTime = 0;
        do
        {
            updateTime++;

            kernel = computeShader.FindKernel("UpdateFarStateTree");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Mask, mask);
            computeShader.SetBuffer(kernel, ActiveSet, activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateActiveValueTree");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.SetTexture(kernel, Parent, parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateActiveStateTree");
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
        Debug.Log($"generate initial result cost: {Time.realtimeSinceStartup - time}");
        time = Time.realtimeSinceStartup;

        activeSet.Release();

        //Remedy Step
        var remedySet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedyTree");
        computeShader.SetBuffer(kernel, RemedySet, remedySet);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetTexture(kernel, Threshold, threshold);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));
        uint remedyCount = GetAppendBufferSize(remedySet);
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
            computeShader.SetBuffer(kernel, RemedySet, remedySet);
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.SetTexture(kernel, Threshold, threshold);
            computeShader.SetTexture(kernel, Parent, parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateRemedyNeighborTree");
            computeShader.SetBuffer(kernel, RemedySet, remedySet);
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetBuffer(kernel, ParentBuffer1, parentBuffer1);
        computeShader.SetBuffer(kernel, ParentBuffer2, parentBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        parentBufferData = new uint[parentBuffer1.count + parentBuffer2.count];

        parentBuffer1.GetData(parentBufferData, 0, 0, parentBuffer1.count);
        parentBuffer2.GetData(parentBufferData, parentBuffer1.count, 0, parentBuffer2.count);

        parentBuffer1.Release();
        parentBuffer2.Release();

        Debug.Log("GetParentData cost:" + (Time.realtimeSinceStartup - time));
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
            marker1.parent = marker1 == marker2 ? null : marker2;
        }

        return completeTree;
    }

    // coroutine version of calculating the geodesic distance within the trunk part with FIM 
    public IEnumerator InitialReconstructionAsync(List<Marker> completeTree)
    {
        float computationTime = Time.realtimeSinceStartup;
        int kernel = computeShader.FindKernel("InitSeed");
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Threshold, threshold);
        computeShader.SetFloat(MaxIntensity, maxIntensity);
        computeShader.SetInt(SeedIndex, seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        uint activeCount = 1;
        trunk = new(){ (uint)seedIndex };
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
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Mask, mask);
            computeShader.SetBuffer(kernel, ActiveSet, activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateActiveValueTree");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.SetTexture(kernel, Parent, parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateActiveStateTree");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
        }
        Debug.Log("trunk Count" + trunk.Count);
        Debug.Log($"generate initial result cost: {Time.realtimeSinceStartup - time}");
        time = Time.realtimeSinceStartup;

        activeSet.Release();

        //Remedy Step
        
        int[] dispatchFlag = {0};
        ComputeBuffer dispatchBuffer = new(1, sizeof(int));
        dispatchBuffer.SetData(dispatchFlag);
        
        kernel = computeShader.FindKernel("InitRemedyTree");
        computeShader.SetBuffer(kernel, DispatchBuffer, dispatchBuffer);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetTexture(kernel, Threshold, threshold);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        bool continueDispatch = NeedDispatch(dispatchBuffer);
        
        int remedyTime = 0;
        while (continueDispatch)
        {
            remedyTime++;
            //remedySet = new ComputeBuffer(dims.x * dims.y * dims.z, sizeof(uint), ComputeBufferType.Append);
            dispatchFlag[0] = 0;
            dispatchBuffer.SetData(dispatchFlag);
                
            kernel = computeShader.FindKernel("UpdateRemedyTree");
            computeShader.SetBuffer(kernel, DispatchBuffer, dispatchBuffer);
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.SetTexture(kernel, Threshold, threshold);
            computeShader.SetTexture(kernel, Parent, parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateRemedyNeighborTree");
            computeShader.SetBuffer(kernel, DispatchBuffer, dispatchBuffer);
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            // if (remedyTime % 20 == 0)
            // {
            //     uint[] countBufferData = new uint[1];
            //     yield return StartCoroutine(GetAppendBufferSizeAsync(remedySet, countBufferData));
            //     remedyCount = countBufferData[0];
            //     remedySet.SetCounterValue(0);
            // }
            continueDispatch = NeedDispatch(dispatchBuffer);
        }
        Debug.Log("remedy cost:" + (Time.realtimeSinceStartup - time));
        time = Time.realtimeSinceStartup;
        Debug.Log($"update times:{updateTime} remedy times:{remedyTime}");
        dispatchBuffer.Release();


        ComputeBuffer parentBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(uint), ComputeBufferType.Default);
        ComputeBuffer parentBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(uint), ComputeBufferType.Default);

        kernel = computeShader.FindKernel("GetParent");
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetBuffer(kernel, ParentBuffer1, parentBuffer1);
        computeShader.SetBuffer(kernel, ParentBuffer2, parentBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        parentBufferData = new uint[parentBuffer1.count + parentBuffer2.count];

        parentBuffer1.GetData(parentBufferData, 0, 0, parentBuffer1.count);
        parentBuffer2.GetData(parentBufferData, parentBuffer1.count, 0, parentBuffer2.count);

        parentBuffer1.Release();
        parentBuffer2.Release();

        Debug.Log("GetParentData cost:" + (Time.realtimeSinceStartup - time));
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

            if (trunk.Contains(parentBufferData[peek])) continue;
            trunk.Add(parentBufferData[peek]);
            queue.Enqueue(parentBufferData[peek]);
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
            marker1.parent = marker1 == marker2 ? null : marker2;
        }
    }

    /// <summary>
    /// calculate the geodesic distance within full image range with FIM
    /// </summary>
    /// <returns>markers of connected part</returns>
    public List<Marker> FIMFI()
    {
        int[] dimsArray = { dim.x, dim.y, dim.z };
        int kernel = computeShader.FindKernel("InitSeed");
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Threshold, threshold);
        computeShader.SetFloat(MaxIntensity, maxIntensity);
        computeShader.SetInt(SeedIndex, seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        uint activeCount;
        trunk = new HashSet<uint> { (uint)seedIndex };
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
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateActiveValueTree");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.SetTexture(kernel, Parent, parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateActiveStateTree");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
            //Debug.Log($"active buffer count: {activeCount}");
            uint[] activeData = new uint[activeCount];
            activeSet.GetData(activeData);
            trunk.UnionWith(activeData);
        } while (activeCount > 0);
        Debug.Log($"results count: {trunk.Count}");

        Debug.Log($"generate initial result cost: {Time.realtimeSinceStartup - time}");


        float calculationTime = Time.realtimeSinceStartup;

        activeSet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitSeedFI");
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetFloat(MaxIntensity, maxIntensity);
        computeShader.SetTexture(kernel, Threshold, threshold);
        computeShader.SetInt(SeedIndex, seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));



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
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateActiveValueFI");
            computeShader.SetInts(Dims, dimsArray);
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.SetTexture(kernel, Parent, parent);
            computeShader.SetTexture(kernel, Threshold, threshold);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            kernel = computeShader.FindKernel("UpdateActiveStateFI");
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
            sum += (int)activeCount;
        } while (activeCount > 0);
        activeSet.Release();
        Debug.Log($"active count: {sum}");

        Debug.Log("update cost :" + (Time.realtimeSinceStartup - time));
        time = Time.realtimeSinceStartup;

        //afterUpdate = GetBuffer();
        //SaveTexture(afterUpdate, $"Assets/Textures/FIM/afterUpdate.Asset");

        //Remedy Step
        uint remedyCount = 0;
        int[] dispatchFlag = {0};
        ComputeBuffer dispatchBuffer = new(1, sizeof(int));
        dispatchBuffer.SetData(dispatchFlag);

        
        kernel = computeShader.FindKernel("InitRemedyFI");
        computeShader.SetBuffer(kernel, DispatchBuffer, dispatchBuffer);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));
        bool continueDispatch = NeedDispatch(dispatchBuffer); 
        Debug.Log("first traceTime remedy count:" + remedyCount);
        
        int remedyTime = 0;
        while (continueDispatch)
        {
            remedyTime++;

            kernel = computeShader.FindKernel($"UpdateRemedyFI");
            computeShader.SetBuffer(kernel, DispatchBuffer, dispatchBuffer);
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.SetTexture(kernel, Threshold, threshold);
            computeShader.SetTexture(kernel, Parent, parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));
            
            kernel = computeShader.FindKernel("UpdateRemedyNeighborFI");
            computeShader.SetBuffer(kernel, DispatchBuffer, dispatchBuffer);
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            //Get Remedy Set Count
            if (remedyTime % 20 == 0)
            {
                continueDispatch = NeedDispatch(dispatchBuffer);
                dispatchFlag[0] = 0;
                dispatchBuffer.SetData(dispatchFlag);
            }
        }
        Debug.Log("remedy cost:" + (Time.realtimeSinceStartup - time));
        Debug.Log($"update times:{updateTime} remedy times:{remedyTime}");
        dispatchBuffer.Release();


        ComputeBuffer parentBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        ComputeBuffer parentBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        kernel = computeShader.FindKernel("GetParent");
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetBuffer(kernel, ParentBuffer1, parentBuffer1);
        computeShader.SetBuffer(kernel, ParentBuffer2, parentBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));
        
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

                // int i = (int)(index2 % dim.x);
                // int j = (int)((index2 / dim.x) % dim.y);
                // int k = (int)((index2 / dim.x / dim.y) % dim.z);

                //createSphere(new Vector3(i, j, k), config._scaledDim, Color.yellow);
                //Marker marker = new Marker(new Vector3(i, j, k));
                //markers[(int)index2] = marker;
            }
            Marker marker1 = markers[(int)index];
            Marker marker2 = markers[(int)index2];
            marker1.parent = marker1 == marker2 ? null : marker2;
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
        trunk = new() { (uint)seedIndex };
        //Update Steps
        ComputeBuffer activeSet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        activeSet.SetCounterValue(0);

        var connection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("InitTrunk");
        computeShader.SetTexture(kernel, Connection, connection);
        computeShader.SetInt(SeedIndex, seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        int[] dimsArray = { dim.x, dim.y, dim.z };
        computeShader.SetInts(Dims, dimsArray);
        //Update Step
        int updateTime = 0;
        activeSet.SetCounterValue(0);
        do
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateTrunk");
            computeShader.SetTexture(kernel, Connection, connection);
            computeShader.SetTexture(kernel, Origin, volume);
            computeShader.SetTexture(kernel, Threshold, threshold);
            computeShader.SetTexture(kernel, Mask, mask);
            computeShader.SetBuffer(kernel, ActiveSet, activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
        ComputeBuffer remedySet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedyFI");
        computeShader.SetBuffer(kernel, RemedySet, remedySet);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        uint remedyCount = GetAppendBufferSize(remedySet);
        uint[] remedyData = new uint[remedyCount];
        remedySet.GetData(remedyData);
        //Debug.Log("first traceTime remedy count:" + remedyCount);
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
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));
            
            kernel = computeShader.FindKernel("UpdateRemedyNeighborFI");
            computeShader.SetBuffer(kernel, RemedySet, remedySet);
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetBuffer(kernel, ParentBuffer1, parentBuffer1);
        computeShader.SetBuffer(kernel, ParentBuffer2, parentBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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

                // int i = (int)(index2 % dim.x);
                // int j = (int)((index2 / dim.x) % dim.y);
                // int k = (int)((index2 / dim.x / dim.y) % dim.z);

                //createSphere(new Vector3(i, j, k), config._scaledDim, Color.yellow);
                //Marker marker = new Marker(new Vector3(i, j, k));
                //markers[(int)index2] = marker;
            }
            Marker marker1 = markers[(int)index];
            Marker marker2 = markers[(int)index2];
            marker1.parent = marker1 == marker2 ? null : marker2;
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
        trunk = new() { (uint)seedIndex };
        //Update Steps
        ComputeBuffer activeSet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        activeSet.SetCounterValue(0);

        var connection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("InitTrunk");
        computeShader.SetTexture(kernel, Connection, connection);
        computeShader.SetInt(SeedIndex, seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        int[] dimsArray = { dim.x, dim.y, dim.z };
        computeShader.SetInts(Dims, dimsArray);
        //Update Step
        uint activeCount = 1;
        int updateTime = 0;
        activeSet.SetCounterValue(0);
        while (activeCount > 0)
        {
            updateTime++;

            kernel = computeShader.FindKernel("UpdateTrunk");
            computeShader.SetTexture(kernel, Connection, connection);
            computeShader.SetTexture(kernel, Origin, volume);
            computeShader.SetTexture(kernel, Threshold, threshold);
            computeShader.SetTexture(kernel, Mask, mask);
            computeShader.SetBuffer(kernel, ActiveSet, activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
        ComputeBuffer remedySet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedyFI");
        computeShader.SetBuffer(kernel, RemedySet, remedySet);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        var remedyCount = GetAppendBufferSize(remedySet);
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
            computeShader.SetBuffer(kernel, RemedySet, remedySet);
            computeShader.SetTexture(kernel, State, state);
            computeShader.SetTexture(kernel, Gwdt, gwdt);
            computeShader.SetTexture(kernel, Phi, phi);
            computeShader.SetTexture(kernel, Threshold, threshold);
            computeShader.SetTexture(kernel, Parent, parent);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            //remedyCount = GetAppendBufferSize(remedySet);

            kernel = computeShader.FindKernel("UpdateRemedyNeighborFI");
            computeShader.SetBuffer(kernel, RemedySet, remedySet);
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetBuffer(kernel, ParentBuffer1, parentBuffer1);
        computeShader.SetBuffer(kernel, ParentBuffer2, parentBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
            marker1.parent = marker1 == marker2 ? null : marker2;
        }
        Debug.Log(trunk.Count);

        Debug.Log($"Incremental Calculation cost: {Time.realtimeSinceStartup - calculationTime}");
    }

    public List<Marker> FIMErase(HashSet<uint> modified)
    {
        float time = Time.realtimeSinceStartup;
        uint activeCount;
        trunk = new HashSet<uint> { (uint)seedIndex };
        //Update Steps
        ComputeBuffer activeSet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        activeSet.SetCounterValue(0);

        var connection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("InitTrunk");
        computeShader.SetTexture(kernel, Connection, connection);
        computeShader.SetInt(SeedIndex, seedIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        float calculationTime = Time.realtimeSinceStartup;

        int[] dimsArray = { dim.x, dim.y, dim.z };
        computeShader.SetInts(Dims, dimsArray);
        //Update Step
        int updateTime = 0;
        activeSet.SetCounterValue(0);
        do
        {
            updateTime++;
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateTrunk");
            computeShader.SetTexture(kernel, Connection, connection);
            computeShader.SetTexture(kernel, Origin, volume);
            computeShader.SetTexture(kernel, Threshold, threshold);
            computeShader.SetTexture(kernel, Mask, mask);
            computeShader.SetBuffer(kernel, ActiveSet, activeSet);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
        computeShader.SetBuffer(kernel, EraseTargetBuffer, eraseTarget);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));
        eraseTarget.Release();

        //Remedy Step
        ComputeBuffer remedySet = new(16777216, sizeof(uint), ComputeBufferType.Append);
        remedySet.SetCounterValue(0);
        kernel = computeShader.FindKernel("InitRemedyFI");
        computeShader.SetBuffer(kernel, RemedySet, remedySet);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetTexture(kernel, State, state);
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        var remedyCount = GetAppendBufferSize(remedySet);
        //Debug.Log("first traceTime remedy count:" + remedyCount);
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
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));
            
            kernel = computeShader.FindKernel("UpdateRemedyNeighborFI");
            computeShader.SetBuffer(kernel, RemedySet, remedySet);
            computeShader.SetTexture(kernel, State, state);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            //Get Remedy Set Count
            remedyCount = GetAppendBufferSize(remedySet);
        }
        remedySet.Release();
        Debug.Log("remedy cost:" + (Time.realtimeSinceStartup - time));
        Debug.Log($"update times:{updateTime} remedy times:{remedyTime}");


        ComputeBuffer parentBuffer1 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        ComputeBuffer parentBuffer2 = new(dim.x * dim.y * dim.z / 2, sizeof(float), ComputeBufferType.Default);
        kernel = computeShader.FindKernel("GetParent");
        computeShader.SetTexture(kernel, Parent, parent);
        computeShader.SetBuffer(kernel, ParentBuffer1, parentBuffer1);
        computeShader.SetBuffer(kernel, ParentBuffer2, parentBuffer2);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
                //
                // int i = (int)(index2 % dim.x);
                // int j = (int)((index2 / dim.x) % dim.y);
                // int k = (int)((index2 / dim.x / dim.y) % dim.z);

                //createSphere(new Vector3(i, j, k), config._scaledDim, Color.yellow);
                //Marker marker = new Marker(new Vector3(i, j, k));
                //markers[(int)index2] = marker;
            }
            Marker marker1 = markers[(int)index];
            Marker marker2 = markers[(int)index2];
            marker1.parent = marker1 == marker2 ? null : marker2;
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
        computeShader.SetBuffer(kernel, TargetBuffer, targetBuffer);
        computeShader.SetTexture(kernel, Bias, bias);
        computeShader.SetTexture(kernel, Threshold, threshold);
        computeShader.SetInt(Undo, undo ? 1 : 0);
        computeShader.SetInt(TargetNum, targets.Count);
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
        computeShader.SetBuffer(kernel, TargetBuffer, targetBuffer);
        computeShader.SetTexture(kernel, Bias, bias);
        computeShader.SetFloat(Intensity, intensity);
        computeShader.SetInt(TargetNum, targetIndexes.Count);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(targetIndexes.Count / 128.0f), 1, 1);
        targetBuffer.Release();
    }

    /// <summary>
    /// mask the target voxels
    /// </summary>
    /// <param name="targetIndexes"></param>
    /// <param name="undo"></param>
    /// <returns>render texture and byte data of the masked volume</returns>
    public (RenderTexture, byte[]) ModifyMask(List<uint> targetIndexes, bool undo)
    {
        Debug.Log("undo: " + undo);
        Debug.Log("target count: " + targetIndexes.Count);
        int kernel;
        if (targetIndexes.Count > 0)
        {
            ComputeBuffer maskTargetBuffer = new(targetIndexes.Count, sizeof(uint), ComputeBufferType.Default);
            maskTargetBuffer.SetData(targetIndexes.ToArray());
            kernel = computeShader.FindKernel("ModifyMask");
            computeShader.SetBuffer(kernel, MaskTargetBuffer, maskTargetBuffer);
            computeShader.SetTexture(kernel, Mask, mask);
            computeShader.SetInt(TargetNum, targetIndexes.Count);
            computeShader.SetBool(Undo, undo);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(targetIndexes.Count / 128.0f), 1, 1);
            maskTargetBuffer.Release();
        }

        ComputeBuffer maskedVolumeBuffer = new(dim.x * dim.y * dim.z, sizeof(float), ComputeBufferType.Default);
        kernel = computeShader.FindKernel("GetMaskedVolumeData");
        computeShader.SetBuffer(kernel, MaskedVolumeBuffer, maskedVolumeBuffer);
        computeShader.SetTexture(kernel, Origin, config.ScaledVolume);
        computeShader.SetTexture(kernel, Mask, mask);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        float[] bufferData = new float[dim.x * dim.y * dim.z];
        byte[] data = new byte[dim.x * dim.y * dim.z];
        maskedVolumeBuffer.GetData(bufferData);
        maskedVolumeBuffer.Release();
        Parallel.For(0, dim.x * dim.y * dim.z, i =>
        {
            data[i] = (byte)(bufferData[i] * 255.0f);
        });

        return (mask, data);
    }

    public RenderTexture ModifySelection(List<uint> targetIndexes)
    {
        selection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RFloat, GraphicsFormat.R32_SFloat);
        Debug.Log("target count: " + targetIndexes.Count);
        if (targetIndexes.Count > 0)
        {
            ComputeBuffer targetBuffer = new(targetIndexes.Count, sizeof(uint), ComputeBufferType.Default);
            targetBuffer.SetData(targetIndexes.ToArray());
            var kernel = computeShader.FindKernel("ModifySelection");
            computeShader.SetBuffer(kernel, SelectionTargetBuffer, targetBuffer);
            computeShader.SetTexture(kernel, Selection1, selection);
            computeShader.SetInt(TargetNum, targetIndexes.Count);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(targetIndexes.Count / 128.0f), 1, 1);
            targetBuffer.Release();
        }

        return selection;
    }

    public RenderTexture ClearSelection()
    {
        selection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.RFloat, GraphicsFormat.R32_SFloat);
        return selection;
    }

    RenderTexture InitRenderTexture3D(int width, int height, int depth, RenderTextureFormat format, GraphicsFormat graphicsFormat)
    {
        RenderTexture renderTexture = new(width, height, 0, format)
        {
            graphicsFormat = graphicsFormat,
            dimension = TextureDimension.Tex3D,
            volumeDepth = depth,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
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

    bool NeedDispatch(ComputeBuffer dispatchBuffer)
    {
        int[] dispatchFlag = {0};
        dispatchBuffer.GetData(dispatchFlag);
        return dispatchFlag[0] == 1;
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

    private Vector3 IndexToVector(uint index, Vector3Int volumeDim)
    {
        int x = (int)(index % volumeDim.x);
        int y = (int)((index / volumeDim.x) % volumeDim.y);
        int z = (int)((index / volumeDim.x / volumeDim.y) % volumeDim.z);
        return new Vector3(x, y, z);
    }

    private Vector3 IndexToVector(int index, Vector3Int volumeDim)
    {
        int x = (index % volumeDim.x);
        int y = ((index / volumeDim.x) % volumeDim.y);
        int z = ((index / volumeDim.x / volumeDim.y) % volumeDim.z);
        return new Vector3(x, y, z);
    }

    private int VectorToIndex(Vector3 pos, Vector3Int volumeDim)
    {
        int index = ((int)pos.x + (int)pos.y * volumeDim.x + (int)pos.z * volumeDim.x * volumeDim.y);
        return index;
    }
    

    private RenderTexture CopyData(Texture3D src)
    {
        var dst = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("CopyData");
        computeShader.SetTexture(kernel, Volume, dst);
        computeShader.SetTexture(kernel, Origin, src);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        return dst;
    }

    private RenderTexture InitBias()
    {
        var temp = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("InitBias");
        computeShader.SetTexture(kernel, Bias, temp);
        Debug.Log(numThreads);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        return temp;
    }

    /// <summary>
    /// return the connected part of the target index
    /// </summary>
    /// <param name="targetIndex"></param>
    /// <returns></returns>
    public List<uint> GetCluster(uint targetIndex)
    {
        int bkgThreshold = config.ViewThresh;
        var connection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("InitClusterSeed");
        computeShader.SetTexture(kernel, Connection, connection);
        computeShader.SetInt(SeedIndex, (int)targetIndex);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        int[] dimsArray = { dim.x, dim.y, dim.z };
        uint activeCount;

        HashSet<uint> cluster = new() { targetIndex };
        computeShader.SetInts(Dims, dimsArray);
        computeShader.SetFloat(ViewThreshold, bkgThreshold / 255.0f);
        //Update Step
        var activeSet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        do
        {
            activeSet.SetCounterValue(0);

            kernel = computeShader.FindKernel("UpdateCluster");
            computeShader.SetTexture(kernel, Connection, connection);
            computeShader.SetTexture(kernel, Origin, config.ScaledVolume);
            computeShader.SetBuffer(kernel, ActiveSet, activeSet);
            computeShader.SetFloat(ViewThreshold, bkgThreshold / 255.0f);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

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
        var connection = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UNorm);
        var utilComputeShader = Resources.Load<ComputeShader>("ComputeShaders/Utility");
        int kernel = utilComputeShader.FindKernel("InitForegroundBoundary");
        utilComputeShader.SetTexture(kernel, Connection, connection);
        utilComputeShader.SetTexture(kernel, Origin, config.ScaledVolume);
        utilComputeShader.SetTexture(kernel, Threshold, threshold);
        utilComputeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        int[] dimsArray = { dim.x, dim.y, dim.z };
        uint activeCount;

        HashSet<uint> foreground = new();
        utilComputeShader.SetInts(Dims, dimsArray);
        utilComputeShader.SetTexture(kernel, Threshold, threshold);
        //Update Step
        var activeSet = new ComputeBuffer(16777216, sizeof(uint), ComputeBufferType.Append);
        do
        {
            activeSet.SetCounterValue(0);

            kernel = utilComputeShader.FindKernel("UpdateForeground");
            utilComputeShader.SetTexture(kernel, Connection, connection);
            utilComputeShader.SetTexture(kernel, Origin, config.ScaledVolume);
            utilComputeShader.SetBuffer(kernel, ActiveSet, activeSet);
            utilComputeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
            uint[] activeData = new uint[activeCount];
            activeSet.GetData(activeData);
            foreground.UnionWith(activeData);
            //Debug.Log($"active buffer count: {activeCount}");
        } while (activeCount > 0);

        Debug.Log($"cluster size: {foreground.Count}");

        int extendWidth = 3;
        do
        {
            activeSet.SetCounterValue(0);

            kernel = utilComputeShader.FindKernel("ExtendForeground");
            utilComputeShader.SetTexture(kernel, Connection, connection);
            utilComputeShader.SetTexture(kernel, Origin, config.ScaledVolume);
            utilComputeShader.SetBuffer(kernel, ActiveSet, activeSet);
            utilComputeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

            //Get Active Set Count
            activeCount = GetAppendBufferSize(activeSet);
            uint[] activeData = new uint[activeCount];
            activeSet.GetData(activeData);
            foreground.UnionWith(activeData);
            //Debug.Log($"active buffer count: {activeCount}");
        } while (activeCount > 0 && extendWidth-- > 0);
        activeSet.Release();

        Debug.Log($"cluster size: {foreground.Count}");
        return foreground.ToList();

    }

    internal void AdjustThreshold(Vector3 hitPos, Vector3 direction)
    {
        int defaultThreshold = config.BkgThresh;
        float viewRadius = config.viewRadius;
        var utilComputeShader = Resources.Load<ComputeShader>("ComputeShaders/Utility");

        int kernel = utilComputeShader.FindKernel("ModifyThreshold");
        utilComputeShader.SetTexture(kernel, Threshold, threshold);
        utilComputeShader.SetVector(HitPos, hitPos);
        utilComputeShader.SetVector(Direction, direction);
        utilComputeShader.SetFloat(ViewRadius, viewRadius);
        utilComputeShader.SetInt(DefaultThreshold, defaultThreshold);
        utilComputeShader.SetInt(ThresholdOffset, config.thresholdOffset);
        utilComputeShader.Dispatch(kernel, dim.x / config.thresholdBlockSize / numThreads.x, dim.y / config.thresholdBlockSize / numThreads.y, dim.z / config.thresholdBlockSize / numThreads.z);
    }

    internal RenderTexture InitThreshold()
    {
        int defaultThreshold = config.BkgThresh;
        var utilComputeShader = Resources.Load<ComputeShader>("ComputeShaders/Utility");

        int kernel = utilComputeShader.FindKernel("InitThreshold");
        utilComputeShader.SetTexture(kernel, Threshold, threshold);
        utilComputeShader.SetInt(DefaultThreshold, defaultThreshold);
        utilComputeShader.Dispatch(kernel, dim.x / config.thresholdBlockSize / numThreads.x, dim.y / config.thresholdBlockSize / numThreads.y, dim.z / config.thresholdBlockSize / numThreads.z);

        return threshold;
    }

    public Vector3Int cubesDim = new(11, 11, 5);
    public Color cubeColor = new Color(1, 1, 0, 0.25f);
    public Color wireColor = new Color(1, 1, 0, 0.8f);
    public int distThreshold = 3;
    public List<uint> firstRemedy = new();
    public List<uint> tracingRemedy = new(); 
    public float remedyRate = 0.5f;
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
    private static readonly int DispatchBuffer = Shader.PropertyToID("dispatchBuffer");
    private static readonly int Connection = Shader.PropertyToID("connection");
    private static readonly int Origin = Shader.PropertyToID("origin");
    private static readonly int BkgThreshold = Shader.PropertyToID("bkgThreshold");
    private static readonly int Volume = Shader.PropertyToID("volume");
    private static readonly int Bias = Shader.PropertyToID("bias");
    private static readonly int GwdtBuffer1 = Shader.PropertyToID("gwdtBuffer1");
    private static readonly int GwdtBuffer2 = Shader.PropertyToID("gwdtBuffer2");
    private static readonly int SelectionTargetBuffer = Shader.PropertyToID("selectionTargetBuffer");
    private static readonly int Selection1 = Shader.PropertyToID("selection");
    private static readonly int TargetNum = Shader.PropertyToID("targetNum");
    private static readonly int MaskTargetBuffer = Shader.PropertyToID("maskTargetBuffer");
    private static readonly int Undo = Shader.PropertyToID("undo");
    private static readonly int MaskedVolumeBuffer = Shader.PropertyToID("maskedVolumeBuffer");
    private static readonly int EraseTargetBuffer = Shader.PropertyToID("eraseTargetBuffer");
    private static readonly int TargetBuffer = Shader.PropertyToID("targetBuffer");
    private static readonly int Intensity = Shader.PropertyToID("intensity");
    private static readonly int ViewThreshold = Shader.PropertyToID("viewThreshold");
    private static readonly int HitPos = Shader.PropertyToID("hitPos");
    private static readonly int Direction = Shader.PropertyToID("direction");
    private static readonly int ViewRadius = Shader.PropertyToID("viewRadius");
    private static readonly int DefaultThreshold = Shader.PropertyToID("defaultThreshold");
    private static readonly int ThresholdOffset = Shader.PropertyToID("thresholdOffset");
    private static readonly int PhiBuffer = Shader.PropertyToID("phiBuffer");
    private static readonly int Buff = Shader.PropertyToID("buff");
    private static readonly int PhiMax = Shader.PropertyToID("phiMax");
    private static readonly int Diff = Shader.PropertyToID("diff");
    private static readonly int Before = Shader.PropertyToID("before");
    private static readonly int After = Shader.PropertyToID("after");

    void OnDrawGizmosSelected()
    {
        int[] cubeStatus = new int[cubesDim.x * cubesDim.y * cubesDim.z];
        foreach (var t in tracingRemedy)
        {
            var pos = IndexToVector(t, dim);
            pos = pos.Div(dim).Mul(cubesDim);
            int index = VectorToIndex(pos, cubesDim);
            cubeStatus[index] = 1;
        }
        if(tracingRemedy.Count==0)
        {
            foreach (var t in firstRemedy)
            {
                var pos = IndexToVector(t, dim);
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
            Vector3 coordA = IndexToVector(a, new Vector3Int(cubesDim.x, cubesDim.y, cubesDim.z));
            Vector3 posA = coordA.Div(cubesDim) - 0.5f * Vector3.one;
            posA = config.cube.transform.TransformPoint(posA);
            var cameraPos = Camera.current.transform.position;
            float distToCameraA = Vector3.Distance(posA, cameraPos);

            Vector3 coordB = IndexToVector(b, new Vector3Int(cubesDim.x, cubesDim.y, cubesDim.z));
            Vector3 posB = coordB.Div(cubesDim) - 0.5f * Vector3.one;
            posB = config.cube.transform.TransformPoint(posB);
            float distToCameraB = Vector3.Distance(posB, cameraPos);
            if (distToCameraA < distToCameraB) return 1;
            if (distToCameraA > distToCameraB) return -1;
            return 0;
        });

        for (int i = 0; i < cubeStatus.Length; i++)
        {
            int index = indexes[i];
            Vector3 coord = IndexToVector(index, new Vector3Int(cubesDim.x, cubesDim.y, cubesDim.z));
            Vector3 offset = coord - center;


            float dist = Math.Abs(offset.x) + Math.Abs(offset.y) + Math.Abs(offset.z);
            if (dist < 0)
            {
                Gizmos.color = new Color(1, 0, 0, 0.1F);
                //Vector3 pos = coord.Div(cubesDim) - 0.5f * Vector3.one;
                //pos = config.cube.transform.TransformPoint(pos);
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

            if (cubeStatus[index]> 1733.183471* remedyRate)
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
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetBuffer(kernel, PhiBuffer, phiBuffer);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));

        float[] phiData = new float[phiBuffer.count];
        phiBuffer.GetData(phiData);
        float phiMax = 0;
        foreach (var t in phiData)
        {
            phiMax = Math.Max(phiMax, t);
        }
        Debug.Log($"phiMax: {phiMax}");
        phiBuffer.Release();

        var buff = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UNorm);
        kernel = computeShader.FindKernel("GetBuff");
        computeShader.SetTexture(kernel, Buff, buff);
        computeShader.SetTexture(kernel, Phi, phi);
        computeShader.SetTexture(kernel, Gwdt, gwdt);
        computeShader.SetFloat(PhiMax, phiMax);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));
        return buff;
    }

    RenderTexture GetDiff(RenderTexture before, RenderTexture after)
    {
        var diff = InitRenderTexture3D(dim.x, dim.y, dim.z, RenderTextureFormat.R8, GraphicsFormat.R8_UNorm);
        int kernel = computeShader.FindKernel("GetDiff");
        computeShader.SetTexture(kernel, Diff, diff);
        computeShader.SetTexture(kernel, Before, before);
        computeShader.SetTexture(kernel, After, after);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / (float)numThreads.x), Mathf.CeilToInt(dim.y / (float)numThreads.y), Mathf.CeilToInt(dim.z / (float)numThreads.z));
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
