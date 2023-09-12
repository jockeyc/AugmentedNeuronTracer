using MixedReality.Toolkit.SpatialManipulation;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class Blocker : MonoBehaviour
{
    static List<Blocker> blockerList = new();
    Config config;
    Transform cubeTransform;
    Vector3Int dim;
    ObjectManipulator manipulator;
    Tracer tracer;
    public BlockerMenu BMController;
    float deltaTime;
    public int blockerThreshold = 50;

    // Start is called before the first frame update
    void Start()
    {
        blockerList.Add(this);
        config = GameObject.Find("Config").GetComponent<Config>();
        dim = config.scaledDim;
        cubeTransform = config.cube.transform;
        tracer = config.tracer;

        manipulator = gameObject.GetComponent<ObjectManipulator>();
        if (manipulator == null ) manipulator = gameObject.AddComponent<ObjectManipulator>();
        manipulator.selectEntered.AddListener((eventArgs) => ModifyMask(true,-1));
        manipulator.selectExited.AddListener((eventArgs) => ModifyMask(false,3));
        
        //manipulator.selectExited.AddListener((eventArgs) => ActivateBlocker());
    }

    // Update is called once per frame
    void Update()
    {
        //if (manipulator.isSelected)
        //{
        //    deltaTime += Time.deltaTime;
        //    if (deltaTime > 0.5)
        //    {
        //        ModifyMask(false, true);
        //        ModifyMask(true, false);
        //        deltaTime = 0;
        //    }
        //}
    }

    public void ModifyMask(bool undo,int type)
    {
        float time = Time.realtimeSinceStartup;
        var target = getMaskTarget();
        tracer.ModifyMask(target, undo, type);
        Debug.Log($"ModifyMask cost: {Time.realtimeSinceStartup - time}s");
    }

    public bool isInside(Vector3 worldPos)
    {
        var localPos = transform.InverseTransformPoint(worldPos);
        return (localPos.x >= -0.5 && localPos.x < 0.5 && localPos.y >= -0.5 && localPos.y < 0.5 && localPos.z >= -0.5 && localPos.z < 0.5);
    }

    private List<uint> getMaskTarget()
    {
        Vector3 center = cubeTransform.InverseTransformPoint(transform.position);
        Vector3 posMin = -0.5f * Vector3.one;
        Vector3 posMax = 0.5f * Vector3.one;
        posMin = cubeTransform.InverseTransformPoint(transform.TransformPoint(posMin));
        posMax = cubeTransform.InverseTransformPoint(transform.TransformPoint(posMax));
        posMin += 0.5f * Vector3.one;
        posMax += 0.5f * Vector3.one;
        posMin = Vector3.Min(posMin, Vector3.one);
        posMin = Vector3.Max(posMin, Vector3.zero);
        posMax = Vector3.Min(posMax, Vector3.one);
        posMax = Vector3.Max(posMax, Vector3.zero);
        Debug.Log(posMax);
        Debug.Log(posMin);
        posMax = new Vector3(posMax.x * dim.x, posMax.y * dim.y, posMax.z * dim.z);
        posMin = new Vector3(posMin.x * dim.x, posMin.y * dim.y, posMin.z * dim.z);
        Vector3Int dimCut = new((int)(posMax.x - posMin.x), (int)(posMax.y - posMin.y), (int)(posMax.z - posMin.z));
        Vector3Int origin = new((int)posMin.x, (int)posMin.y, (int)posMin.z);
        Debug.Log(dimCut);
        byte[] cutData = new byte[dimCut.x * dimCut.y * dimCut.z];
        byte[] volumeData = config.VolumeData;
        for (int k = 0; k < dimCut.z; k++)
        {
            for (int j = 0; j < dimCut.y; j++)
            {
                for (int i = 0; i < dimCut.x; i++)
                {
                    int index_cut = VectorToIndex(new Vector3Int(i, j, k), dimCut);
                    int index_volume = VectorToIndex(new Vector3Int(i, j, k) + origin, dim);
                    cutData[index_cut] = volumeData[index_volume];
                }
            }
        }
        List<uint> targets = new();
        if (cutData.Length > 0)
        {
            //var shed = new Watershed().Segement(cutData, blockerThreshold, dimCut);
            var shed = new Watershed().Segement(cutData, blockerThreshold, dimCut);

            var points = GameObject.Find("Points");
            points.transform.position = Vector3.zero;
            for (int i = 0; i < points.transform.childCount; i++)
            {
                GameObject.Destroy(points.transform.GetChild(i).gameObject);
            }

            foreach (var point in shed)
            {
                targets.Add((uint)VectorToIndex(point + origin, dim));
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                Vector3 pos = point + origin;
                pos = pos.Div(dim);
                pos -= new Vector3(.5f, .5f, .5f);
                pos = config.cube.transform.TransformPoint(pos);
                sphere.transform.position = pos;
                sphere.transform.SetParent(points.transform, false);
            }
        }
        return targets;
    }


    private int VectorToIndex(Vector3Int pos, Vector3Int dim)
    {
        int index = ((int)pos.x + (int)pos.y * dim.x + (int)pos.z * dim.x * dim.y);
        return index;
    }
    //private List<uint> getMaskTarget()
    //{
    //    Vector3 center = cubeTransform.InverseTransformPoint(transform.position);
    //    Vector3 posMin = center;
    //    Vector3 posMax = center;
    //    for (float x = -0.5f; x <= 0.5f; x++)
    //    {
    //        for (float y = -0.5f; y <= 0.5; y++)
    //        {
    //            for (float z = -0.5f; z <= 0.5; z++)
    //            {
    //                Vector3 cur = new(x, y, z);
    //                cur = transform.TransformPoint(cur);
    //                cur = cubeTransform.InverseTransformPoint(cur);
    //                posMin = Vector3.Min(cur, posMin);
    //                posMax = Vector3.Max(cur, posMax);
    //            }
    //        }
    //    }

    //    posMin += new Vector3(0.5f, 0.5f, 0.5f);
    //    posMax += new Vector3(0.5f, 0.5f, 0.5f);

    //    posMin = Vector3.Min(posMin, Vector3.one);
    //    posMin = Vector3.Max(posMin, Vector3.zero);
    //    posMax = Vector3.Min(posMax, Vector3.one);
    //    posMax = Vector3.Max(posMax, Vector3.zero);

    //    Debug.Log(posMin + " " + posMax);

    //    posMax = new Vector3(posMax.x * dim.x, posMax.y * dim.y, posMax.z * dim.z);
    //    posMin = new Vector3(posMin.x * dim.x, posMin.y * dim.y, posMin.z * dim.z);
    //    Debug.Log(posMin + " " + posMax);
    //    List<uint> maskTarget = new();

    //    for (int k = (int)posMin.z; k < (int)posMax.z; k++)
    //    {
    //        if (k < 0 || k >= posMax.z) continue;
    //        for (int j = (int)posMin.y; j < (int)posMax.y; j++)
    //        {
    //            if(j<0|| j >= posMax.y) continue;
    //            for (int i = (int)posMin.x; i < (int)posMax.x; i++)
    //            {
    //                if(i<0|| i >= posMax.x) continue;
    //                Vector3 localPos = new(i / dim.x, j / dim.y, k / dim.z);
    //                Vector3 worldPos = cubeTransform.TransformPoint(localPos-0.5f*Vector3.one);
    //                if (isInside(worldPos))
    //                {
    //                    uint index = (uint)(i + j * dim.x + k * dim.y * dim.x);
    //                    maskTarget.Add(index);
    //                }
    //            }
    //        }
    //    }
    //    return maskTarget;
    //}

    private void OnDestroy()
    {
        ModifyMask(true,3);
    }

    private void ActivateBlocker()
    {
        BMController?.ActivateBlocker(this);
    }
}
