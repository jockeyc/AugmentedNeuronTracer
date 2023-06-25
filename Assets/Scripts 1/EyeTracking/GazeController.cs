using CommandStructure;
using MathNet.Numerics;
using Microsoft.MixedReality.Toolkit.Input;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem.XR.Haptics;
using UnityEngine.Timeline;
using UnityEngine.XR.OpenXR.Input;
using static UnityEngine.Rendering.PostProcessing.PostProcessResources;

public class GazeController : MonoBehaviour
{
    public enum EyeInteractionState { None, Repair, EditThresh }
    FuzzyGazeInteractor interactor;
    GameObject EyePointer = null;
    GameObject CurPointer = null;

    public EyeInteractionState currentState = EyeInteractionState.None;

    public Material mMaterial;

    //private float defaultDistanceInMeters = 2;
    public GameObject cube;
    int maxHitCount = 1;

    List<float> hitPoints = new();
    int mHitCount = 0;

    float preTime;
    float preComputeTime;
    float preHitTime;

    int scanCount = 0;
    Vector3[] scanPoints = new Vector3[5000];
    int[] scanPathLengthCount = new int[100];
    Vector3 scanCenter = Vector3.zero;

    Vector3 preLocalHitPos = Vector3.zero;

    HashSet<int> targetIndexs = new HashSet<int>();

    List<Vector3> test = new List<Vector3>();

    public Config config;

    float traceTime = 0;

    public float editTimeInterval = 2.0f;
    public float dampTime = 0.05f;
    RenderTexture eyeHeatMap;


    Vector3 velocity;
    // Start is called before the first frame update
    void Start()
    {
        interactor = GameObject.Find("GazeInteractor").GetComponent<FuzzyGazeInteractor>();
        config = GetComponent<Config>();
        cube = config._cube;

        Vector3Int dim = config._scaledDim;
        eyeHeatMap = new(dim.x, dim.y, 0, RenderTextureFormat.R8)
        {
            //graphicsFormat = GraphicsFormat.R8_UInt,
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            volumeDepth = dim.z,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        EyePointer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        EyePointer.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        EyePointer.SetActive(false);
        CurPointer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        CurPointer.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        CurPointer.SetActive(false);
    }

    int times = 0;
    // Update is called once per frame
    void Update()
    {
        var result = interactor.PreciseHitResult;
        //if (result.IsRaycast) Debug.Log("Is Casting");
        traceTime += Time.deltaTime;
        if (result.IsRaycast)
        {
            var localGazeOrigin = cube.transform.InverseTransformPoint(Camera.main.transform.position);
            var localHitPos = cube.transform.InverseTransformPoint(result.raycastHit.point);
            if (preLocalHitPos == Vector3.zero) preLocalHitPos = localHitPos;
 
            switch (currentState)
            {
                case EyeInteractionState.None:
                    {
                        EyePointer.SetActive(false);
                        break;
                    }
                case EyeInteractionState.Repair:
                    {
                        var timestamp = Time.realtimeSinceStartup;
                        //EyePointer.transform.position = result.raycastHit.point;
                        EyePointer.SetActive(true);
                        EyePointer.GetComponent<MeshRenderer>().material.color = Color.white;
                        EyePointer.transform.position = Vector3.SmoothDamp(EyePointer.transform.position, result.raycastHit.point, ref velocity, dampTime);
                        int curIndex = getIntersection(localHitPos, localGazeOrigin);
                        if (curIndex != -1)
                        {
                            SetPointer(curIndex, true);
                        }

                        if ((timestamp - preTime) > 1.0f / 60)
                        {
                            //for eye heat map
                            //if (curIndex != -1)
                            //{
                            //    int i = curIndex % config._scaledDim.x;
                            //    int j = (curIndex / config._scaledDim.x) % config._scaledDim.y;
                            //    int k = (curIndex / (config._scaledDim.x * config._scaledDim.y) % config._scaledDim.z);
                            //    addHitPoint(new Vector3(i, j, k));
                            //}
                            addScanPoint(localHitPos);
                            preTime = timestamp;
                            preLocalHitPos = localHitPos;

                            if ((timestamp - preComputeTime) >= 0.667f)
                            {
                                double gamma = fittingDistribution();
                                if (gamma > 0.85)
                                {
                                    scanCenter.x /= scanCount;
                                    scanCenter.y /= scanCount;
                                    scanCenter.z /= scanCount;
                                    getSeed(scanCenter, localGazeOrigin);
                                }
                                //recordEyeData(gamma);
                                scanCenter = Vector3.zero;
                                Array.Clear(scanPoints, 0, scanPoints.Length);
                                Array.Clear(scanPathLengthCount, 0, scanPathLengthCount.Length);
                                scanCount = 0;
                                preComputeTime = timestamp;
                            }
                        }
                        break;
                    }
                case EyeInteractionState.EditThresh:
                    {
                        EyePointer.SetActive(true);
                        EyePointer.GetComponent<MeshRenderer>().material.color = Color.white;
                        EyePointer.transform.position = Vector3.SmoothDamp(EyePointer.transform.position, result.raycastHit.point, ref velocity, dampTime);
                        int curIndex = getIntersection(localHitPos, localGazeOrigin);
                        //if (curIndex != -1)
                        //{
                        //    int i = curIndex % config._scaledDim.x;
                        //    int j = (curIndex / config._scaledDim.x) % config._scaledDim.y;
                        //    int k = (curIndex / (config._scaledDim.x * config._scaledDim.y) % config._scaledDim.z);
                        //    Vector3 pos = new Vector3();
                        //    pos.x = i / (float)config._scaledDim.x;
                        //    pos.y = j / (float)config._scaledDim.y;
                        //    pos.z = k / (float)config._scaledDim.z;

                        //    pos = pos - new Vector3(.5f, .5f, .5f);
                        //    pos = cube.transform.TransformPoint(pos);
                        //    EyePointer.transform.position = pos;
                        //    if (config.tracer.Contained((uint)curIndex))
                        //    {
                        //        EyePointer.GetComponent<MeshRenderer>().material.color = Color.blue;
                        //    }
                        //    else
                        //    {
                        //        EyePointer.GetComponent<MeshRenderer>().material.color = Color.red;
                        //    }
                        //}
                        var direction = (localHitPos - localGazeOrigin).normalized;
                        var hitPos = (localHitPos + 0.5f * Vector3.one).Mul(config._scaledDim/config.thresholdBlockSize);
                        //config.tracer.AdjustThreshold(hitPos,direction);
                        if(traceTime > editTimeInterval)
                        {
                            //config.tracer.TraceTrunk(1);
                            config.tracer.dontCoroutine(1);
                            traceTime = 0;
                        }
                        if(times++>=2)
                        {
                            times = 0;
                            //config.invoker.Execute(new ThreshCommand(config.tracer,hitPos,direction));
                            config.tracer.AdjustThreshold(hitPos, direction);
                        }
                        break;
                    }
                default:
                    {
                        break;
                    }
            }
        }
        else
        {
            EyePointer.SetActive(false);
        }
    }

    private void recordEyeData(double gamma)
    {
        string path = Application.dataPath + "/eyeData.txt";
        FileStream fs = new FileStream(path, FileMode.Open);
        fs.Position = fs.Length;
        string s = "";
        for (int i = 0; i < scanPathLengthCount.Length; i++)
        {
            double frequcy = (double)scanPathLengthCount[i] / scanCount;
            s += i.ToString() + " " + frequcy.ToString("f6") + "\n";
        }
        s += gamma + "\n";
        byte[] bytes = new UTF8Encoding().GetBytes(s.ToString());
        fs.Write(bytes, 0, bytes.Length);
        fs.Close();
    }

    private void getSeed(Vector3 scanCenter, Vector3 gazeOrigin)
    {
        var volume = config.Volume;
        int bkgThresh = config.ViewThresh;
        int sz0 = volume.width, sz1 = volume.height, sz2 = volume.depth;
        Vector3 direction = (scanCenter - gazeOrigin).normalized;
        byte[] volumeData = config.VolumeData;
        float min_x = -0.5f, min_y = -0.5f, min_z = -0.5f;
        float max_x = 0.5f, max_y = 0.5f, max_z = 0.5f;
        float tmin_x = (min_x - scanCenter.x) / direction.x;
        float tmin_y = (min_y - scanCenter.y) / direction.y;
        float tmin_z = (min_z - scanCenter.z) / direction.z;
        float tmax_x = (max_x - scanCenter.x) / direction.x;
        float tmax_y = (max_y - scanCenter.y) / direction.y;
        float tmax_z = (max_z - scanCenter.z) / direction.z;
        if (tmax_x < tmin_x) (tmax_x, tmin_x) = (tmin_x, tmax_x);
        if (tmax_y < tmin_y) (tmax_y, tmin_y) = (tmin_y, tmax_y);
        if (tmax_z < tmin_z) (tmax_z, tmin_z) = (tmin_z, tmax_z);
        float tmin = Mathf.Max(tmin_x, Mathf.Max(tmin_y, tmin_z));
        float tmax = Mathf.Min(tmax_x, Mathf.Min(tmax_y, tmax_z));
        float max_length = tmax - tmin;
        float dt = 0.001f;
        Vector3 p = scanCenter + new Vector3(.5f, .5f, .5f);
        float distance = 0;
        uint max_index = 0;
        byte max_intensity = 0;
        for (int t = 0; t < 100000; t++)
        {
            int x = (int)(p.x * sz0);
            int y = (int)(p.y * sz1);
            int z = (int)(p.z * sz2);
            int offset = 1;
            for (int offsetX = -offset; offsetX <= offset; offsetX++)
            {
                for (int offsetY = -offset; offsetY <= offset; offsetY++)
                {
                    for (int offsetZ = -offset; offsetZ <= offset; offsetZ++)
                    {
                        int w = x + offsetX;
                        int h = y + offsetY;
                        int d = z + offsetZ;
                        if (w >= 0 && w < sz0 && h >= 0 && h < sz1 && d >= 0 && d < sz2)
                        {
                            uint index = (uint)(w + (h * sz0) + (d * sz0 * sz1));
                            byte intesity = volumeData[index];
                            if (intesity > max_intensity)
                            {
                                max_intensity = intesity;
                                max_index = index;
                            }
                        }
                    }
                }
            }

            p += direction * dt;
            distance += dt;
            if (distance > max_length) break;
        }
        //Debug.Log("max_intesity" + max_intensity);


        int i = (int)(max_index % sz0);
        int j = (int)((max_index / sz0) % sz1);
        int k = (int)((max_index / (sz0 * sz1) % sz2));
        Vector3 pos = new Vector3();
        pos.x = i / (float)sz0;
        pos.y = j / (float)sz1;
        pos.z = k / (float)sz2;



        if (max_intensity >= bkgThresh)
        {

            if (!config.tracer.Contained(max_index))
            {
                //config.invoker.Execute(new AdjustCommand(config.tracer, max_index));
            }
            //SetPointer((int)max_index, false);
            config._curIndex = max_index;
            pos = pos - new Vector3(.5f, .5f, .5f);
            pos = cube.transform.TransformPoint(pos);
            EyePointer.transform.position = pos;
            EyePointer.GetComponent<MeshRenderer>().material.color = Color.red;
        }
    }

    private float Radius(uint index, byte[] volumeData)
    {
        int sz0 = config._scaledDim.x;
        int sz1 = config._scaledDim.y;
        int sz2 = config._scaledDim.z;

        int x = (int)(index % sz0);
        int y = (int)((index / sz0) % sz1);
        int z = (int)((index / (sz0 * sz1) % sz2));

        double max_r = sz0 / 2;
        max_r = Math.Max(max_r, sz1 / 2);
        max_r = Math.Max(max_r, sz2 / 2);

        double tol_num = 0, bkg_num = 0;
        float ir;
        for (ir = 1; ir < max_r; ir++)
        {
            tol_num = 0;
            bkg_num = 0;
            double dz, dy, dx;
            for (dz = -ir; dz <= ir; dz++)
            {
                for (dy = -ir; dy <= ir; dy++)
                {
                    for (dx = -ir; dx <= ir; dx++)
                    {
                        double r = Math.Sqrt(dz * dz + dy * dy + dx * dx);
                        if (r > ir - 1 && r <= ir)
                        {
                            tol_num++;
                            long i = (long)(x + dx);
                            if (i < 0 || i >= sz0) return ir;
                            long j = (long)(y + dy);
                            if (j < 0 || j >= sz1) return ir;
                            long k = (long)(z + dz);
                            if (k < 0 || k >= sz2) return ir;
                            if (volumeData[k * sz0 * sz1 + j * sz0 + i] <= config.ViewThresh)
                            {
                                bkg_num++;
                                if ((bkg_num / tol_num > 0.05)) return ir;
                            }
                        }
                    }
                }
            }
        }
        return ir;
    }

    private int getIntersection(Vector3 targetPos, Vector3 gazeOrigin)
    {
        var volume = config.Volume;
        int bkgThresh = config.ViewThresh;
        int sz0 = volume.width, sz1 = volume.height, sz2 = volume.depth;
        Vector3 direction = (targetPos - gazeOrigin).normalized;
        byte[] volumeData = config.VolumeData;
        float min_x = -0.5f, min_y = -0.5f, min_z = -0.5f;
        float max_x = 0.5f, max_y = 0.5f, max_z = 0.5f;
        float tmin_x = (min_x - targetPos.x) / direction.x;
        float tmin_y = (min_y - targetPos.y) / direction.y;
        float tmin_z = (min_z - targetPos.z) / direction.z;
        float tmax_x = (max_x - targetPos.x) / direction.x;
        float tmax_y = (max_y - targetPos.y) / direction.y;
        float tmax_z = (max_z - targetPos.z) / direction.z;
        if (tmax_x < tmin_x) (tmax_x, tmin_x) = (tmin_x, tmax_x);
        if (tmax_y < tmin_y) (tmax_y, tmin_y) = (tmin_y, tmax_y);
        if (tmax_z < tmin_z) (tmax_z, tmin_z) = (tmin_z, tmax_z);
        float tmin = Mathf.Max(tmin_x, Mathf.Max(tmin_y, tmin_z));
        float tmax = Mathf.Min(tmax_x, Mathf.Min(tmax_y, tmax_z));
        float max_length = tmax - tmin;
        float dt = 0.001f;
        Vector3 p = targetPos + new Vector3(.5f, .5f, .5f);
        float distance = 0;
        uint max_index = 0;
        byte max_intensity = 0;
        for (int t = 0; t < 100000; t++)
        {
            int x = (int)(p.x * sz0);
            int y = (int)(p.y * sz1);
            int z = (int)(p.z * sz2);
            int offset = 1;
            for (int offsetX = -offset; offsetX <= offset; offsetX++)
            {
                for (int offsetY = -offset; offsetY <= offset; offsetY++)
                {
                    for (int offsetZ = -offset; offsetZ <= offset; offsetZ++)
                    {
                        int w = x + offsetX;
                        int h = y + offsetY;
                        int d = z + offsetZ;
                        if (w >= 0 && w < sz0 && h >= 0 && h < sz1 && d >= 0 && d < sz2)
                        {
                            uint index = (uint)(w + (h * sz0) + (d * sz0 * sz1));
                            byte intesity = volumeData[index];
                            if (intesity > max_intensity)
                            {
                                max_intensity = intesity;
                                max_index = index;
                            }
                        }
                    }
                }
            }

            p += direction * dt;
            distance += dt;
            if (distance > max_length) break;
        }
        //Debug.Log("max_intesity" + max_intensity);


        int i = (int)(max_index % sz0);
        int j = (int)((max_index / sz0) % sz1);
        int k = (int)((max_index / (sz0 * sz1) % sz2));
        Vector3 pos = new Vector3();
        pos.x = i / (float)sz0;
        pos.y = j / (float)sz1;
        pos.z = k / (float)sz2;
        pos = pos - new Vector3(.5f, .5f, .5f);
        pos = cube.transform.TransformPoint(pos);

        if (max_intensity >= bkgThresh)
        {
            return (int)max_index;
        }
        else return -1;
    }
    private double fittingDistribution()
    {
        double[] pathLength = new double[100];
        double[] frequency = new double[100];
        for (int i = 0; i < 100; i++)
        {
            frequency[i] = (double)scanPathLengthCount[i] / scanCount;
            pathLength[i] = 1 / ((double)(i + 1.0d) * (double)(i + 1.0d));
            //Debug.Log("x:" + pathLength[i] + " y:" + frequency[i]);
        }

        var s = Fit.LineThroughOrigin(pathLength, frequency);
        //Debug.Log("fitting A:" + s);
        return s;
    }

    private void addScanPoint(Vector3 localHitPosition)
    {
        //scanPoints[scanCount++] = localHitPosition;
        scanCount++;
        scanCenter += localHitPosition;
        int length = (int)(Vector3.Distance(localHitPosition, preLocalHitPos) * 100);
        length = Math.Min(99, length);
        scanPathLengthCount[length]++;
    }

    public void addHitPoint(Vector3 pos)
    {
        int index = maxHitCount / 1024;
        hitPoints.Add(pos.x);
        hitPoints.Add(pos.y);
        hitPoints.Add(pos.z);
        //Debug.Log("add hit:" + pos.ToString("f4"));
        mHitCount++;

        Vector3Int dim = config._scaledDim;
        ComputeShader computeShader = Resources.Load("ComputeShaders/ErrorHeatMap") as ComputeShader;
        int kernel = computeShader.FindKernel("CSMain");


        string assetName = config.imageName + "_eyeData";
        ComputeBuffer hitsBuffer = new(hitPoints.Count, sizeof(float), ComputeBufferType.Default);
        hitsBuffer.SetData(hitPoints.ToArray());

        computeShader.SetTexture(kernel, "Result", eyeHeatMap);
        computeShader.SetBuffer(kernel, "_Hits", hitsBuffer);
        computeShader.SetInts("dim", new int[] { dim.x, dim.y, dim.z });
        computeShader.SetInt("_HitCount", hitPoints.Count / 3); 

        computeShader.Dispatch(kernel, Mathf.CeilToInt(dim.x / 8), Mathf.CeilToInt(dim.y / 8), Mathf.CeilToInt(dim.z / 8));
        MeshRenderer meshRenderer = GameObject.Find("EyeHeatMap").GetComponent<MeshRenderer>();
        Material material = meshRenderer.material;
        material.SetTexture("_ErrorWeights", eyeHeatMap);

        //AssetDatabase.DeleteAsset($"Assets/Textures/HeatMap/{assetName}.Asset");
        //AssetDatabase.CreateAsset(result, $"Assets/Textures/HeatMap/{assetName}.Asset");
        //AssetDatabase.SaveAssets();
        //AssetDatabase.Refresh
        hitsBuffer.Release();
        //result.Release();
    }

    private void SetPointer(int index, bool isEye)
    {
        int i = index % config._scaledDim.x;
        int j = (index / config._scaledDim.x) % config._scaledDim.y;
        int k = (index / (config._scaledDim.x * config._scaledDim.y) % config._scaledDim.z);
        Vector3 pos = new()
        {
            x = i / (float)config._scaledDim.x,
            y = j / (float)config._scaledDim.y,
            z = k / (float)config._scaledDim.z
        };
        pos -= new Vector3(.5f, .5f, .5f);
        pos = cube.transform.TransformPoint(pos);
        if(!isEye)
        {
            CurPointer.SetActive(true);
        }
        var pointer = isEye ? EyePointer : CurPointer;

        //pointer.transform.position = pos;
        pointer.transform.position = pos;
        if (config.tracer.Contained((uint)index))
        {
            pointer.GetComponent<MeshRenderer>().material.color = Color.blue;
        }
        else
        {
            pointer.GetComponent<MeshRenderer>().material.color = Color.red;
        }
    }
}
