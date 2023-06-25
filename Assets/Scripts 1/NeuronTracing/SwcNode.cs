using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SwcNode
{
    public SwcNode left;
    public SwcNode right;
    public Vector3 position;
    public float radius;
    public SwcNode parent;
    public int index;
    public int type;
    public bool isBranch_root = false;
    public GameObject sphere;
    public GameObject cylinder;

    public SwcNode(Vector3 pos, float r,Transform paintingBoard)
    {
        Debug.Log(pos);
        position = paintingBoard.worldToLocalMatrix*new Vector4(pos.x,pos.y,pos.z,1);
        Debug.Log(position);
        radius = r;
    }

    public SwcNode(Marker marker,Vector3 scale)
    {
        position = new Vector3(marker.position.x * scale.x, marker.position.y * scale.y, marker.position.z * scale.z) - new Vector3(0.5f, 0.5f, 0.5f);
        radius = marker.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
    }

    public virtual void AddChild(SwcNode child)
    {
        if (left == null) left = child;
        else
        {
            right = child;
            isBranch_root = true;
        }
        child.parent = this;
    }

    public virtual void RemoveChild(SwcNode child)
    {
        if (left == child) left = null;
        if (right == child) right = null;
    }

    public void RemoveAllChild()
    {
        if (left != null)
        {
            left.parent = null;
            left = null;
        }
        if (right != null)
        {
            right.parent = null;
            right = null;
        }
    }

    public virtual bool HasChild() {
        return left != null || right != null;
    }

    public bool isLeaf()
    {
        return left == null && right == null;
    }

    public bool isBranchRoot()
    {
        return isBranch_root;
    }

    public SwcNode() { }


}

public class SwcSoma : SwcNode
{
    public List<SwcNode> children;

    public SwcSoma(Vector3 pos, float r)
    {
        position = pos;
        radius = r;
        parent = null;
        children = new List<SwcNode>();
    }

    public SwcSoma(Marker marker, Vector3 scale)
    {
        position = new Vector3(marker.position.x * scale.x, marker.position.y * scale.y, marker.position.z * scale.z) - new Vector3(0.5f, 0.5f, 0.5f);
        radius = marker.radius * Mathf.Max(scale.x,Mathf.Max(scale.y,scale.z));
        parent = null;
        children = new List<SwcNode>();
    }

    public override void AddChild(SwcNode child)
    {
        children.Add(child);
        child.parent = this;
    }

    public override void RemoveChild(SwcNode child)
    {
        foreach(var node in children)
        {
            if (node == child) children.Remove(node);
        }
    }

    public override bool HasChild()
    {
        return children.Count > 0;
    }
}

public class SwcBranch
{
    public SwcBranch parent;
    public SwcNode headNode;
    public SwcNode tailNode;

}