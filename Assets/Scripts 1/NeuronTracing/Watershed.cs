using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Resources;
using UnityEditor;
using UnityEditor.MemoryProfiler;
using UnityEngine;

public class Watershed
{
    public ComputeShader computeShader = Resources.Load<ComputeShader>("ComputeShaders/Watershed"); 

    public List<Vector3Int> Segement(byte[] cutData,int bkgThreshold, Vector3Int cutDim)
    {
        int[] region = DivideIntoRegion(cutData, bkgThreshold, cutDim);
        int[] state = new int[region.Length];
        for(int i=0; i<state.Length; i++)
        {
            if (region[i] > 0)
            {
                state[i] = 1;
            }
            else state[i] = 0;
        }
        Texture3D cut = new(cutDim.x, cutDim.y, cutDim.z, TextureFormat.R8, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        cut.SetPixelData<byte>(cutData, 0, 0);
        cut.Apply();

        AssetDatabase.DeleteAsset("Assets/Textures/" + "cut" + ".Asset");
        AssetDatabase.CreateAsset(cut, "Assets/Textures/" + "cut" + ".Asset");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ComputeBuffer regionBuffer = new(region.Length,sizeof(int));
        ComputeBuffer stateBuffer = new(region.Length,sizeof(int));
        regionBuffer.SetData(region);
        stateBuffer.SetData(state);
        uint sourceCount;
        var sourceSet = new ComputeBuffer(2097152, sizeof(uint), ComputeBufferType.Append);
        for(int i = bkgThreshold; i >= 5; i--)
        {
            HashSet<int> bounds = new();
            do
            {
                bounds.Clear();
                for(int index = 0; index < state.Length; index++)
                {
                    if (state[index] == 0)
                    {
                        int regionID = 0;
                        (int x, int y, int z) = IndexToVec(index, cutDim);
                        for(int d = -1; d <= 1; d++)
                            for(int h =-1; h <= 1; h++)
                                for(int  w =-1; w <= 1; w++)
                                {
                                    Vector3Int pos = new(Mathf.Clamp(w+x, 0, cutDim.x-1), Mathf.Clamp(h+y, 0, cutDim.y-1), Mathf.Clamp(d+z, 0, cutDim.z - 1));
                                    int neighbour = VectorToIndex(pos, cutDim);
                                    if (state[neighbour] > 0 && regionID != -1 )
                                    {
                                        if (region[neighbour] > 0 && regionID == 0)
                                        {
                                            regionID = region[neighbour];
                                        }
                                        else if (region[neighbour] > 0 && regionID != region[neighbour])
                                        {
                                            regionID = -1;
                                        }
                                    }
                                }
                        if(regionID != 0)
                        {
                            region[index] = regionID;
                            bounds.Add(index);
                        }
                    }
                }
                foreach(int index in bounds)
                {
                    state[index] = 1 ;
                }

                //sourceSet.SetCounterValue(0);

                //int kernel = computeShader.FindKernel("RegionGrow");
                //computeShader.SetBuffer(kernel, "region", regionBuffer);
                //computeShader.SetBuffer(kernel, "state", stateBuffer);
                //computeShader.SetTexture(kernel, "cut", cut);
                //computeShader.SetBuffer(kernel, "sourceSet", sourceSet);
                //computeShader.SetInt("threshold", i);
                //computeShader.Dispatch(kernel, Mathf.CeilToInt(cutDim.x / 4), Mathf.CeilToInt(cutDim.x / 4), Mathf.CeilToInt(cutDim.x / 4));

                ////kernel = computeShader.FindKernel("UpdateValue");
                ////computeShader.SetBuffer(kernel, "region", regionBuffer);
                ////computeShader.SetBuffer(kernel, "state", stateBuffer);
                ////computeShader.Dispatch(kernel, Mathf.CeilToInt(cutDim.x / 4), Mathf.CeilToInt(cutDim.x / 4), Mathf.CeilToInt(cutDim.x / 4));

                ////Get Source Set Count
                //sourceCount = GetAppendBufferSize(sourceSet);
                //Debug.Log($"current threshold: {i} count: {sourceCount}");

            } while (bounds.Count> 0);
        }
        //regionBuffer.GetData(region);
        List<Vector3Int> shed = new();
        Dictionary<int,int> counts = new Dictionary<int,int>();
        counts[-1] = 0;
        counts[0] = 0;
        counts[1] = 0;
        counts[2] = 0;

        for(int i=0; i<region.Length; i++)
        {
            counts[region[i]]++;
            if (region[i] == -1)
            {
                (int x, int y,int z) = IndexToVec(i, cutDim);
                Vector3Int pos = new(x, y, z);
                shed.Add(pos);
            }
        }

        Debug.Log($"counts -1: { counts[-1]}");
        Debug.Log($"counts 0: { counts[0]}");
        Debug.Log($"counts 1: { counts[1]}");
        Debug.Log($"counts 2: { counts[2]}");
        regionBuffer.Release();
        stateBuffer.Release();
        sourceSet.Release();
        return shed;
    }

    public int[] DivideIntoRegion(byte[] cutData, int bkgThreshold, Vector3Int cutDim)
    {
        HashSet<int> searched = new();
        List<List<int>> clusters = new();
        for (int k = 0; k < cutDim.z; k++)
        {
            for (int j = 0; j < cutDim.y; j++)
            {
                for (int i = 0; i < cutDim.x; i++)
                {
                    int index = k * cutDim.x * cutDim.y + j * cutDim.x + i;
                    if (searched.Contains(index)) continue;
                    else
                    {
                        searched.Add(index);
                        if (cutData[index] >= bkgThreshold)
                        {
                            Queue<int> queue = new();
                            queue.Enqueue(index);
                            List<int> cluster = new();
                            clusters.Add(cluster);
                            while (queue.Count > 0)
                            {
                                int peek = queue.Dequeue();
                                cluster.Add(peek);
                                (int x, int y, int z) = IndexToVec(peek, cutDim);
                                for (int d = z - 1; d <= z + 1; d++)
                                {
                                    if (d < 0 || d >= cutDim.z) continue;
                                    for (int h = y - 1; h <= y + 1; h++)
                                    {
                                        if (h < 0 || h >= cutDim.y) continue;
                                        for (int w = x - 1; w <= x + 1; w++)
                                        {
                                            if (w < 0 || w >= cutDim.x) continue;
                                            int subIndex = d * cutDim.x * cutDim.y + h * cutDim.x + w;
                                            if (searched.Contains(subIndex)) continue;
                                            searched.Add(subIndex);
                                            if (cutData[subIndex] >= bkgThreshold)
                                            {
                                                queue.Enqueue(subIndex);
                                            }
                                        }
                                    }
                                }
                            }

                        }
                    }
                }
            }
        }
        int[] clusterData = new int[cutData.Length];
        clusters.Sort((clusterA, clusterB) => { return clusterA.Count < clusterB.Count?1:-1; });
        
        for (int i = 0; i < 2; i++)
        {
            Debug.Log(clusters[i].Count);
            if (i >= clusters.Count) break;
            foreach (int index in clusters[i])
            {
                clusterData[index] = i+1;
            }

        }
        return clusterData;
    }

    (int, int, int) IndexToVec(int index, Vector3Int dim)
    {
        int x = (int)(index % dim.x);
        int y = (int)((index / dim.x) % dim.y);
        int z = (int)((index / dim.x / dim.y) % dim.z);
        return (x, y, z);
    }

    private int VectorToIndex(Vector3 pos, Vector3Int dim)
    {
        int index = ((int)pos.x + (int)pos.y * dim.x + (int)pos.z * dim.x * dim.y);
        return index;
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
}
