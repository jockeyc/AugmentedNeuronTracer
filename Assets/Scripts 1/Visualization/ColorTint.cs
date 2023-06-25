using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Preprocess
{
    public static Texture3D DivideIntoClusters(Texture3D src, int bkgThreshold)
    {
        byte[] srcData = src.GetPixelData<byte>(0).ToArray();
        HashSet<int> searched = new();
        List<List<int>> clusters = new();
        for (int k = 0; k < src.depth; k++)
        {
            for (int j = 0; j < src.height; j++)
            {
                for (int i = 0; i < src.width; i++)
                {
                    int index = k * src.height * src.width + j * src.width + i;
                    if (searched.Contains(index)) continue;
                    else
                    {
                        searched.Add(index);
                        if (srcData[index] >= bkgThreshold)
                        {
                            Queue<int> queue = new();
                            queue.Enqueue(index);
                            List<int> cluster = new();
                            clusters.Add(cluster);
                            while (queue.Count > 0)
                            {
                                int peek = queue.Dequeue();
                                cluster.Add(peek);
                                (int x, int y, int z) = IndexToVec(peek, src.width, src.height, src.depth);
                                for (int d = z - 1; d <= z + 1; d++)
                                {
                                    if (d < 0 || d >= src.depth) continue;
                                    for (int h = y - 1; h <= y + 1; h++)
                                    {
                                        if (h < 0 || h >= src.height) continue;
                                        for (int w = x - 1; w <= x + 1; w++)
                                        {
                                            if (w < 0 || w >= src.width) continue;
                                            int subIndex = d * src.height * src.width + h * src.width + w;
                                            if (searched.Contains(subIndex)) continue;
                                            searched.Add(subIndex);
                                            if (srcData[subIndex] >= bkgThreshold)
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
        byte[] clusterData = new byte[srcData.Length];
        for(int i = 0; i < clusters.Count; i++)
        {
            foreach(int index in clusters[i])
            {
                clusterData[index] = (byte)i;
            }

        }
        Texture3D res = new(src.width, src.height, src.depth, TextureFormat.R8, false);
        res.filterMode = FilterMode.Point;
        res.wrapMode = TextureWrapMode.Clamp;
        res.SetPixelData<byte>(clusterData, 0, 0);
        res.Apply();
        return res;
    }

    static (int, int, int) IndexToVec(int index, int width, int height, int depth)
    {
        int x = index % width;
        int y = index / width % height;
        int z = index / width / height % depth;
        return (x, y, z);
    }
}
