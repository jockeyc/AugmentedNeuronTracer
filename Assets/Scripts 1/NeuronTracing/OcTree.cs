using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ARNeuron
{
    enum Orientation
    {
        BackBottomLeft, BackBottomRight, BackTopLeft, BackTopRight, FrontBottomLeft, FrontBottomRight, FrontTopLeft, FrontTopRight
    }
    class OctNode
    {
        static int MinSize = 4;
        Vector3Int pos;
        Vector3Int aabbMin, aabbMax;
        List<OctNode> children;
        public OctNode()
        {
            pos = new Vector3Int(-1, -1, -1);
        }
        public OctNode(int x, int y, int z)
        {
            pos = new Vector3Int(x, y, z);
        }
        public OctNode(Vector3Int a, Vector3Int b)
        {
            if (a.x>b.x||a.y>b.y||a.z>b.z)
            {
                throw new ArgumentException("Octree Node Boundary is invalid");
            }
            //pos = new Vector3Int(-1, -1,-1);
            aabbMin = a; 
            aabbMax = b;

            children = new List<OctNode>();
            for (var i = Orientation.BackBottomLeft; i < Orientation.FrontTopRight; i++)
            {
                children.Add(null);
            }
        }

        void insert(Vector3Int newPos)
        {
            if (isLeaf())
            {
                if (pos == -1 * Vector3Int.one)
                {
                    pos = newPos;
                    return;
                }
                else
                {
                    Vector3Int oldPos = pos;
                    pos = -1 * Vector3Int.one;
                    Vector3Int center = (aabbMin + aabbMax) / 2;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3Int newAABB = new Vector3Int((i & 4) == 1 ? aabbMax.z : aabbMin.z,
                                                            (i & 2) == 1 ? aabbMax.y : aabbMin.y,
                                                            (i & 1) == 1 ? aabbMax.x : aabbMin.x);
                        Vector3Int newAABBMin = Vector3Int.Min(newAABB, center);
                        Vector3Int newAABBMax = Vector3Int.Max(newAABB, center);
                        children[children.Count] = new OctNode(newAABBMin, newAABBMax);
                    }

                    children[getOctantContainingPoint(oldPos)].insert(oldPos);
                    children[getOctantContainingPoint(newPos)].insert(newPos);
                }
            }
            else
            {
                children[getOctantContainingPoint(newPos)].insert(newPos);
            }
        }

        private bool find(int x, int y, int z)
        {
            throw new NotImplementedException();
        }

        private bool isLeaf()
        {
            return children[0] == null;
        }

        private int getOctantContainingPoint(Vector3Int point)
        {
            Vector3Int aabbCenter = (aabbMax + aabbMin) / 2;
            Orientation orientation = (Orientation)((point.y < aabbCenter.y ? 1 : 0) << 2 + (point.x < aabbCenter.x ? 1 : 0) << 1 + (point.z < aabbCenter.z ? 1 : 0));
            return (int)orientation;
        }
    }
    public class OcTree
    {
        OctNode root;

    }
}
