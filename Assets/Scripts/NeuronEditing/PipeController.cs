using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.SpatialManipulation;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class PipeController : MonoBehaviour
{
    public PipeCasing pipeCasing;
    StatefulInteractable si;
    // Start is called before the first frame update
    void Start()
    {
        si = GetComponent<StatefulInteractable>();

        si.IsGrabSelected.OnEntered.AddListener((args) => {
            Debug.Log("si grabselected");
        });
    }

    // Update is called once per frame
    void Update()
    {
        //if(OM.isSelected)
        //{
        //    Debug.Log("what!!!");
        //    pipeCasing.MoveOn();
        //}
        //if (OM.isHovered)
        //{
        //    Debug.Log("AAA!!!");
        //    pipeCasing.MoveOn();
        //}
        //for(int i=0;i<transform.childCount;i++)
        //{
        //    var obj = transform.GetChild(i);
        //    DestroyImmediate(obj);
        //}

    }

    


}
