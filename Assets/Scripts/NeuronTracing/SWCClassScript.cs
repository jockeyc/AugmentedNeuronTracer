using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
Created by BlackFJ
*/

///<summary>
///
///</summary>
namespace SWC
{
    public class SWCNode
    {
        private Vector3 xyz;
        private int type;
        private float radius;
        private int id, pid;

        SWCNode(int _id, int _type, float _x, float _y, float _z, float _r, int _pid)
        {
            xyz = new Vector3(_x, _y, _z);
            type = _type;
            radius = _r;
            id = _id;
            pid = _pid;
        }

        SWCNode(int _id, int _type, Vector3 _xyz, float _r, int _pid)
        {
            xyz = _xyz;
            type = _type;
            radius = _r;
            id = _id;
            pid = _pid;
        }

        public float X
        {
            get { return xyz.x; }
        }

        public float Y
        {
            get { return xyz.y; }
        }

        public float Z
        {
            get { return xyz.z; }
        }

        public float R
        {
            get { return radius; }
        }

        public int Id
        {
            get { return id; }
        }

        public int PId
        {
            get { return pid; }
        }

        public int Type
        {
            get { return type; }
        }
    }

    public class SWCTree
    {
        private List<SWCNode> swcNodeList;

        public void Write()
        {

        }
    }


}