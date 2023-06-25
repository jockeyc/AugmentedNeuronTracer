using Microsoft.MixedReality.Toolkit.SpatialManipulation;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class PipeController : MonoBehaviour
{
    public PipeCasing pipeCasing;
    ObjectManipulator OM;
    // Start is called before the first frame update
    void Start()
    {
        OM = GetComponent<ObjectManipulator>();
        OM.selectEntered.AddListener((SelectEnterEventArgs args) => { pipeCasing.MoveOn(); });
        //OM.selectEntered.AddListener
    }

    // Update is called once per frame
    void Update()
    {
        if(OM.isSelected)
        {
            Debug.Log("what!!!");
            pipeCasing.MoveOn();
        }
        if (OM.isHovered)
        {
            Debug.Log("FUck!!!");
            pipeCasing.MoveOn();
        }
        //for(int i=0;i<transform.childCount;i++)
        //{
        //    var obj = transform.GetChild(i);
        //    DestroyImmediate(obj);
        //}

    }

    


}
