using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class Test : MonoBehaviour
{
    public Texture3D texture;
    // Start is called before the first frame update
    void Start()
    {
        var res = Preprocess.DivideIntoClusters(texture,10);
#if UNITY_EDITOR
        AssetDatabase.DeleteAsset("Assets/Resources/Textures/divided.Asset");
        AssetDatabase.CreateAsset(res, "Assets/Resources/Textures/divided.Asset");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
#endif
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
