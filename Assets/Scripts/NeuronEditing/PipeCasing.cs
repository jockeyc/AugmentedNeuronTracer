using CommandStructure;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Subsystems;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;

public class PipeCasing : MonoBehaviour
{
    public List<Marker> markers;
    public Dictionary<Marker, GameObject> spheres;
    [SerializeField] private int center;
    private Transform cube;
    [SerializeField] int len = 0;
    public List<uint> targets;
    private Config config;
    public float radiusBias = 2;
    public float pipeExtension = 1;
    public Vector3Int dim;
    HandsAggregatorSubsystem aggregator;
    // Start is called before the first frame update
    void Start()
    {
        aggregator = XRSubsystemHelpers.GetFirstRunningSubsystem<HandsAggregatorSubsystem>();
    }

    // Update is called once per frame
    void Update()
    {
    }

    [InspectorButton]
    public void MoveOn()
    {
        center = Math.Min(markers.Count, center + 1);
        Activate();
    }

    [InspectorButton]
    public void MoveBack()
    {
        center = Math.Max(0, center - 1);
        Activate();
    }

    [InspectorButton]
    public void AddLength()
    {
        len = Math.Min(len + 1, markers.Count);
        Activate();
    }

    [InspectorButton]
    public void DecLength()
    {
        len = Math.Max(0, len - 1);
        Activate();
    }

    private void AddRadiusBias()
    {
        radiusBias++;
        Activate();
    }

    private void DecRadiusBias()
    {
        radiusBias--;
        Activate();
    }    
    private void AddPipeExtension()
    {
        pipeExtension++;
        Activate();
    }

    private void DecPipeExtension()
    {
        pipeExtension--;
        Activate();
    }

    [InspectorButton]
    public void Trace()
    {
        config.invoker.Execute(new MaskCommand(config.tracer, targets));
        ClearPipes();
    }

    public void Activate()
    {
        ClearPipes();
        for (int i = 0; i < len && i + center < markers.Count; i++)
        {
            Marker marker = markers[i + center];
            var cylinder = Primitive.CreateCylinder(marker, dim, cube, radiusBias);
            cylinder.GetComponent<MeshRenderer>().material.color = Color.white;
            cylinder.transform.SetParent(GameObject.Find("Pipe").transform, true);
            targets.AddRange(GetTargets(marker, radiusBias , pipeExtension));


            if (i == len - 1 || i + center == markers.Count - 1)
            {
                cylinder.AddComponent<BoxCollider>();
                var si = cylinder.AddComponent<StatefulInteractable>();
                si.IsPokeSelected.OnEntered.AddListener((args) => {
                    HeadInteract();
                });
            }
            else if (i == 0)
            {
                cylinder.AddComponent<BoxCollider>();
                var si = cylinder.AddComponent<StatefulInteractable>();
                si.IsPokeSelected.OnEntered.AddListener((args) => {
                    TailInteract();
                });
            }
        }
    }

    private void HeadInteract()
    {
        bool jointIsValid = aggregator.TryGetJoint(TrackedHandJoint.IndexTip, XRNode.RightHand, out HandJointPose jointPose);
        bool handIsValid = aggregator.TryGetPinchProgress(XRNode.RightHand, out bool isReadyToPinch, out bool isPinching, out float pinchAmount);
        if (jointIsValid && handIsValid && isPinching && pinchAmount >= 0.95)
        {
            MoveOn();
        }
        else
        {
            AddLength();
        }
    }

    private void TailInteract()
    {
        bool jointIsValid = aggregator.TryGetJoint(TrackedHandJoint.IndexTip, XRNode.RightHand, out HandJointPose jointPose);
        bool handIsValid = aggregator.TryGetPinchProgress(XRNode.RightHand, out bool isReadyToPinch, out bool isPinching, out float pinchAmount);
        if (jointIsValid && handIsValid && isPinching && pinchAmount >= 0.95)
        {
            MoveBack();
        }
        else
        {
            DecLength();
        }
    }

    public void ClearPipes()
    {
        for (int i = 0; i < GameObject.Find("Pipe").transform.childCount; i++)
        {
            GameObject.Destroy(GameObject.Find("Pipe").transform.GetChild(i).gameObject);
        }
        targets.Clear();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="marker"></param>
    /// <param name="radiusBias">Modified value of pipe radius</param>
    /// <param name="pipeExtension">Distance extended along the direction</param>
    /// <returns></returns>
    private List<uint> GetTargets(Marker marker, float radiusBias, float pipeExtension)
    {
        var dim = config._scaledDim;
        int[] dimsArray = new int[3] { dim.x, dim.y, dim.z };
        ComputeShader computeShader = Resources.Load("ComputeShaders/Utility") as ComputeShader;
        var sourceSet = new ComputeBuffer(10000, sizeof(uint), ComputeBufferType.Append);
        sourceSet.SetCounterValue(0);
        var direction = (marker.parent.position - marker.position).normalized;

        int kernel = computeShader.FindKernel("GetPipeCasingTargets");
        computeShader.SetInts("dims", dimsArray);
        computeShader.SetFloat("pipeRadius", marker.radius);
        computeShader.SetFloat("radiusBias", radiusBias);
        computeShader.SetVector("start", marker.position - pipeExtension * direction);
        computeShader.SetVector("end", marker.parent.position + pipeExtension* direction );
        computeShader.SetBuffer(kernel, "sourceSet", sourceSet);
        computeShader.Dispatch(kernel, (dim.x / 8), (dim.y / 8), (dim.z / 8));
        uint sourceCount = GetAppendBufferSize(sourceSet);
        uint[] sourceData = new uint[sourceCount];
        sourceSet.GetData(sourceData);
        sourceSet.Release();
        return sourceData.ToList();
    }
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

    internal void Initial(Marker marker, Vector3Int dim, Transform cubeTransform)
    {
        config = GameObject.FindGameObjectWithTag("Config").GetComponent<Config>();
        markers = new();
        targets = new();
        markers.Add(marker);
        center = 0;
        this.dim = dim;
        this.cube = cubeTransform;
        var parent = marker.parent;
        while (parent != null)
        {
            markers.Add(parent);
            parent = parent.parent;
        }
        //Activate();
    }
}
