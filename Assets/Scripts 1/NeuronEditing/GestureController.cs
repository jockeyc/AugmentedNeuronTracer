using Microsoft.MixedReality.Toolkit.Subsystems;
using Microsoft.MixedReality.Toolkit;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using System.Linq;
using CommandStructure;

public class GestureController : MonoBehaviour
{
    public enum OperationType
    {
        None,
        Enhance,
        Weaken,
        Draw,
        Erase
    }

    HandsAggregatorSubsystem aggregator;
    public OperationType operation = OperationType.None;
    public bool canDraw = true;
    private List<Vector3> track = new();
    Config _config;
    List<GameObject> line = new();
    float lineRadius = 10;

    VirutalFinger vf;
    void Start()
    {
        _config = gameObject.GetComponent<Config>();
        aggregator = XRSubsystemHelpers.GetFirstRunningSubsystem<HandsAggregatorSubsystem>();
        vf = new();
    }

    // Update is called once per frame
    void Update()
    {
        bool jointIsValid = aggregator.TryGetJoint(TrackedHandJoint.IndexTip, XRNode.RightHand, out HandJointPose jointPose);
        bool handIsValid = aggregator.TryGetPinchProgress(XRNode.RightHand, out bool isReadyToPinch, out bool isPinching, out float pinchAmount);
        if (jointIsValid && handIsValid && isPinching && pinchAmount>=0.95 && operation!=OperationType.None)
        {
            //draw the finger track
            float radius = 0.005f;
            var curPos = jointPose.Position;
            var lastPos = track.Count>0? track.Last():curPos;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.localScale = new Vector3(radius, radius, radius);
            sphere.transform.position = curPos;
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.transform.localScale = new Vector3(radius, Vector3.Distance(curPos, lastPos)/2, radius);
            cylinder.transform.position = (lastPos + curPos) / 2;
            cylinder.transform.up = lastPos - curPos;
            track.Add(curPos);
            line.Add(sphere);
            line.Add(cylinder);
        }
        else
        {
            if (track.Count > 0)
            {
                switch (operation)
                {
                    case OperationType.Enhance:
                        _config.invoker.Execute(new AdjustCommand(_config.tracer, Track2Indexes(track, 1), 10));
                        break;
                    case OperationType.Weaken:
                        _config.invoker.Execute(new AdjustCommand(_config.tracer, Track2Indexes(track, 1), -10));
                        break;
                    case OperationType.Draw:
                        {
                            List<Marker> markers = new();
                            foreach(var t in track)
                            {
                                var pos = _config.cube.transform.InverseTransformPoint(t) + 0.5f * Vector3.one;

                                markers.Add(new Marker(new Vector3(pos.x*_config._scaledDim.x,pos.y*_config._scaledDim.y,pos.z*_config._scaledDim.z)));
                            }
                            //var curve = vf.RefineSketchCurve(markers, _config.Volume, 10);
                            //_config.invoker.Execute(new AdjustCommand(_config.tracer, Markers2Indexes(curve), 0));
                            break;
                        }
                    case OperationType.Erase:
                        {
                            List<Vector3> positions = new();
                            foreach (var t in track)
                            {
                                var pos = _config.cube.transform.InverseTransformPoint(t) + 0.5f * Vector3.one;
                                pos = pos.Mul(_config._originalDim);
                                positions.Add(pos);
                            }
                            _config.tracer.CloseToTrack(positions);
                        }
                        break;
                    default: 
                        break;
                }
            
                track.Clear();
                foreach (var s in line) Destroy(s);
            }
        }

        if (jointIsValid)
        {
            var pos = _config.cube.transform.InverseTransformPoint(jointPose.Position) + new Vector3(.5f, .5f, .5f);
            //_config._cube.GetComponent<MeshRenderer>().material.SetVector("_rightHandIndexTip", pos);
        }

         
    }

    public void SetcanDraw(bool value) 
    {
        canDraw = value;
    }

    private List<uint> Track2Indexes(List<Vector3> track,int radius)
    {
        var cube = _config.cube;
        Vector3Int dim = _config._scaledDim;
        Debug.Log(track.Count);
        HashSet<uint> targets = new();
        foreach (Vector3 v in track)
        {
            var pos = cube.transform.InverseTransformPoint(v) + new Vector3(.5f, .5f, .5f);
            pos = new Vector3(pos.x * dim.x, pos.y * dim.y, pos.z * dim.z);
            int x = (int)pos.x;
            int y = (int)pos.y;
            int z = (int)pos.z;
            int index = x + y * dim.x + z * dim.x * dim.y;
            targets.Add((uint)index);
            for (int i = x - radius; i <= x + radius; i++)
                for (int j = y - radius; j <= y + radius; j++)
                    for (int k = z - radius; k <= z + radius; k++)
                    {
                        if (i < 0 || i >= dim.x || j < 0 || j >= dim.y || k < 0 || k >= dim.z) continue;
                        if (Vector3Int.Distance(new(x, y, z), new(i, j, k)) <= 2)
                        {
                            int indexNeighbour = i + dim.x * j + k * dim.x * dim.y;
                            targets.Add((uint)indexNeighbour);
                        }
                    }
        }
        return targets.ToList();
    }

    private List<uint> Markers2Indexes(List<Marker> markers)
    {
        List<uint> indexes = new();
        foreach (var marker in markers)
        {
            int x = (int)marker.position.x;
            int y = (int)marker.position.y;
            int z = (int)marker.position.z;
            Vector3Int dim = _config._scaledDim;
            int index = x + y * dim.x + z * dim.x * dim.y;
            indexes.Add((uint)index);
        }
        return indexes;
    }
}
