using Fusion;
using Microsoft.MixedReality.Toolkit;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SocialPlatforms;
using UnityEngine.Timeline;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit;

public class Primitive : NetworkBehaviour
{
    //public static GameObject CreateCylinder(Marker marker, Transform parentTransform, float Scale = 1 / 512.0f)
    //{
    //	GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
    //	Vector3 a = marker.position * Scale - new Vector3(0.5f, 0.5f, 0.5f); ;
    //	Vector3 b = marker.parent.position * Scale - new Vector3(0.5f, 0.5f, 0.5f);
    //	float length = Vector3.Distance(a, b);
    //	Vector3 ab = (a - b).normalized;
    //	Vector3 y_axis = new Vector3(0, 1, 0);
    //	sphere.transform.parent = parentTransform;
    //	sphere.transform.localScale = new Vector3((float)marker.radius * Scale, length / 2, (float)marker.radius * Scale);
    //	sphere.transform.Rotate(Vector3.Cross(ab, y_axis), -Mathf.Acos(Vector3.Dot(ab, y_axis)) * 180 / Mathf.PI);
    //	sphere.transform.localPosition = (a + b) / 2;
    //	sphere.GetComponent<MeshRenderer>().material.color = Color.red;
    //	return sphere;
    //}

    static Material[] materials = new Material[10];
    //static Color[] colors = { Color.red, Color.red, Color.red , Color.red};

    public static GameObject MyCylinder(float radiusA, float radiusB, float height)
    {
        Mesh mesh = new Mesh();
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Color> colors = new List<Color>();
        const int cnt = 20;
        float deltaRad = Mathf.PI * 2 / cnt;
        for (int i = 0; i < cnt; i++)
        {
            float rad = i * deltaRad;
            float x = radiusA * Mathf.Sin(rad) / 2;
            float z = radiusA * Mathf.Cos(rad) / 2;
            float y = radiusB * Mathf.Sin(rad) / 2;
            float w = radiusB * Mathf.Cos(rad) / 2;

            verts.Add(new Vector3(x, height / 2, z));
            verts.Add(new Vector3(y, -height / 2, w));
        }
        mesh.SetVertices(verts);
        List<int> indexList = new List<int>();
        for (int i = 0; i < cnt; i++)
        {
            if (i == cnt - 1)
            {
                // ���һ����͵�һ������������
                indexList.Add(2 * i);
                indexList.Add(2 * i + 1);
                indexList.Add(0);

                indexList.Add(0);
                indexList.Add(2 * i + 1);
                indexList.Add(1);
            }
            else
            {
                // Ҫע�ⶥ�������˳�����shaderû��Cull Off���������ܲ��ɼ�
                indexList.Add(2 * i);
                indexList.Add(2 * i + 1);
                indexList.Add(2 * (i + 1));

                indexList.Add(2 * (i + 1));
                indexList.Add(2 * i + 1);
                indexList.Add(2 * i + 3);
            }
        }
        mesh.SetIndices(indexList.ToArray(), MeshTopology.Triangles, 0);
        mesh.RecalculateNormals();

        GameObject cylinder = new GameObject("cylinder");
        cylinder.AddComponent<MeshFilter>().mesh = mesh;
        cylinder.AddComponent<MeshRenderer>().material = Resources.Load<Material>("Textures/Default");
        //cylinder.GetComponent<MeshRenderer>().material.enableInstancing = true;

        return cylinder;
    }

    public static GameObject CreateCylinder(Marker marker, Vector3Int dim, Transform parentTransform, float radiusBias = 0)
    {
        if (marker.parent == null) return null;
        var positionA = marker.position.Div(dim) - 0.5f * Vector3.one;
        float radiusA = (marker.radius + radiusBias) * Mathf.Min(1.0f / dim.x, Mathf.Min(1.0f / dim.y, 1.0f / dim.z)) * parentTransform.parent.localScale.x;

        var parent = marker.parent;
        var positionB = marker.parent.position.Div(dim) - 0.5f * Vector3.one;
        float radiusB = (parent.radius + radiusBias) * Mathf.Min(1.0f / dim.x, Mathf.Min(1.0f / dim.y, 1.0f / dim.z)) * parentTransform.parent.localScale.x;
        //radiusA = Mathf.Min(parentTransform.localScale.x);

        positionA = parentTransform.TransformPoint(positionA);
        positionB = parentTransform.TransformPoint(positionB);


        Transform temp = GameObject.Find("Temp").transform;
        float length = Vector3.Distance(positionA, positionB);
        GameObject myCylinder = MyCylinder(radiusA, radiusB, length);
        myCylinder.transform.position = (positionA + positionB) / 2;
        myCylinder.transform.up = (positionA - positionB).normalized;
        myCylinder.transform.SetParent(temp, true);
        //myCylinder.transform.SetParent(parentTransform);

        myCylinder.GetComponent<MeshRenderer>().material = materials[marker.type];
        return myCylinder;
    }
    [Rpc]
    public static void RpcCreateCylinder(NetworkRunner runner, Vector3 positionA, Vector3 positionB, float radiusA, float radiusB, int type, RpcInfo info = default)
    {
        Transform temp = GameObject.Find("Temp").transform;
        float length = Vector3.Distance(positionA, positionB);
        GameObject myCylinder = MyCylinder(radiusA, radiusB, length);
        myCylinder.transform.position = (positionA + positionB) / 2;
        myCylinder.transform.up = (positionA - positionB).normalized;
        myCylinder.transform.SetParent(temp, true);
        //myCylinder.transform.SetParent(parentTransform);

        myCylinder.GetComponent<MeshRenderer>().material = Resources.Load<Material>($"Textures/{type}"); ;
    }
    public static GameObject CreateCylinderTemp(Marker marker, Vector3Int dim, Transform parentTransform, Transform temp, int colortype, float radiusBias = 0)
    {
        if (marker.parent == null) return null;
        var positionA = marker.position.Div(dim) - 0.5f * Vector3.one;
        float radiusA = (marker.radius + radiusBias) * Mathf.Min(1.0f / dim.x, Mathf.Min(1.0f / dim.y, 1.0f / dim.z)) * parentTransform.parent.localScale.x;

        var parent = marker.parent;
        var positionB = marker.parent.position.Div(dim) - 0.5f * Vector3.one;
        float radiusB = (parent.radius + radiusBias) * Mathf.Min(1.0f / dim.x, Mathf.Min(1.0f / dim.y, 1.0f / dim.z)) * parentTransform.parent.localScale.x;
        //radiusA = Mathf.Min(parentTransform.localScale.x);

        positionA = parentTransform.TransformPoint(positionA);
        positionB = parentTransform.TransformPoint(positionB);


        float length = Vector3.Distance(positionA, positionB);
        GameObject myCylinder = MyCylinder(radiusA, radiusB, length);
        myCylinder.transform.SetParent(temp, false);
        myCylinder.transform.position = (positionA + positionB) / 2;
        myCylinder.transform.up = (positionA - positionB).normalized;
        //myCylinder.transform.SetParent(parentTransform);

        myCylinder.GetComponent<MeshRenderer>().material = materials[colortype];
        return myCylinder;
    }

    public static GameObject CreateCylinder(Marker marker, Vector3Int dim, Transform parentTransform, float colorIntensity, float radiusBias = 0)
    {
        if (marker.parent == null) return null;
        var positionA = marker.position.Div(dim) - 0.5f * Vector3.one;
        float radiusA = (marker.radius + radiusBias) * Mathf.Min(1.0f / dim.x, Mathf.Min(1.0f / dim.y, 1.0f / dim.z)) * parentTransform.parent.localScale.x;

        var parent = marker.parent;
        var positionB = marker.parent.position.Div(dim) - 0.5f * Vector3.one;
        float radiusB = (parent.radius + radiusBias) * Mathf.Min(1.0f / dim.x, Mathf.Min(1.0f / dim.y, 1.0f / dim.z)) * parentTransform.parent.localScale.x;
        //radiusA = Mathf.Min(parentTransform.localScale.x);

        positionA = parentTransform.TransformPoint(positionA);
        positionB = parentTransform.TransformPoint(positionB);


        Transform temp = GameObject.Find("Temp").transform;
        float length = Vector3.Distance(positionA, positionB);
        GameObject myCylinder = MyCylinder(radiusA, radiusB, length);
        myCylinder.transform.position = (positionA + positionB) / 2;
        myCylinder.transform.up = (positionA - positionB).normalized;
        myCylinder.transform.SetParent(temp, true);

        //myCylinder.GetComponent<MeshRenderer>().material.color = Color.Lerp(Color.red, Color.green, colorIntensity);
        // myCylinder.GetComponent<MeshRenderer>().material.color = Color.Lerp(new Color(158, 1, 66), new Color(78, 98, 171), colorIntensity);
        myCylinder.GetComponent<MeshRenderer>().material.shader = Shader.Find("Unlit/Color");
        myCylinder.GetComponent<MeshRenderer>().material.shader = Shader.Find("Standard");
        myCylinder.GetComponent<MeshRenderer>().material.color = GetColor(colorIntensity);

        var info = myCylinder.AddComponent<NodeInformation>();
        info.imgIndex = (int)marker.img_index(1024, 1024 * 1024);
        info.type = marker.type;
        info.radius = marker.radius;
        info.position = marker.position;
        info.batch = marker.batch;
        return myCylinder;
    }

    public static GameObject CreateSphere(Marker marker, Vector3Int dim, Transform parentTransform)
    {
        var position = marker.position.Div(dim) - 0.5f * Vector3.one;
        var radius = marker.radius * Mathf.Min(1.0f / dim.x, Mathf.Min(1.0f / dim.y, 1.0f / dim.z));
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        if (marker.type > 4) marker.type = 0;
        sphere.GetComponent<MeshRenderer>().material = materials[marker.type];

        Transform temp = GameObject.Find("Temp").transform;
        sphere.transform.position = parentTransform.TransformPoint(position);
        sphere.transform.SetParent(temp, true);
        sphere.transform.localScale = new Vector3(1, 1, 1) * radius;

        if (marker.isLeaf)
        {
            //sphere.GetComponent<MeshRenderer>().material = materials[4];
            //sphere.transform.localScale = sphere.transform.localScale * 2;
            var statefulInteractable = sphere.AddComponent<StatefulInteractable>();
            statefulInteractable.ToggleMode = StatefulInteractable.ToggleType.OneWayToggle;
            var pipeCasing = sphere.AddComponent<PipeCasing>();
            pipeCasing.Initial(marker, sphere, dim, parentTransform);
            statefulInteractable.OnClicked.AddListener(() =>
            {
                if (statefulInteractable.IsToggled)
                {
                    pipeCasing.AddLength();
                    pipeCasing.Activate();

                }
                else
                {
                    pipeCasing.ClearPipes();
                }
            });
        }

        var info = sphere.AddComponent<NodeInformation>();
        info.imgIndex = (int)marker.img_index(1024, 1024 * 1024);
        info.type = marker.type;
        info.radius = marker.radius;
        info.position = marker.position;
        info.batch = marker.batch;
        return sphere;
    }

    [Rpc]
    public static void RpcClearTree(NetworkRunner runner)
    {
        GameObject temp = GameObject.Find("Temp");
        for (int i = 0; i < temp.transform.childCount; i++)
        {
            GameObject.Destroy(temp.transform.GetChild(i).gameObject);
        }
    }

    [Rpc]
    public static void RpcCreateSphere(NetworkRunner runner, Vector3 position, float radius, int type, RpcInfo info = default)
    {
        if (info.IsInvokeLocal) return;
        Debug.Log(info);
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.GetComponent<MeshRenderer>().material = Resources.Load<Material>($"Textures/{type}"); ;

        Transform temp = GameObject.Find("Temp").transform;
        sphere.transform.position = position;
        sphere.transform.SetParent(temp, true);
        sphere.transform.localScale = new Vector3(1, 1, 1) * radius;
    }

    public static GameObject CreateSphere(Vector3 position, float radius, int type)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.GetComponent<MeshRenderer>().material = Resources.Load<Material>($"Textures/{type}"); ;

        Transform temp = GameObject.Find("Temp").transform;
        sphere.transform.position = position;
        sphere.transform.SetParent(temp, true);
        sphere.transform.localScale = new Vector3(1, 1, 1) * radius;
        return sphere;
    }
    public static GameObject CreateSphereTemp(Marker marker, Vector3Int dim, Transform parentTransform, Transform temp, int colorType)
    {
        var position = marker.position.Div(dim) - 0.5f * Vector3.one;
        var radius = marker.radius * Mathf.Min(1.0f / dim.x, Mathf.Min(1.0f / dim.y, 1.0f / dim.z));
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        if (marker.type > 4) marker.type = 0;
        sphere.GetComponent<MeshRenderer>().material = materials[colorType];

        //Transform temp = GameObject.Find("Temp").transform;
        sphere.transform.position = parentTransform.TransformPoint(position);
        sphere.transform.SetParent(temp, true);
        sphere.transform.localScale = new Vector3(1, 1, 1) * radius;

        if (marker.isLeaf)
        {
            //sphere.GetComponent<MeshRenderer>().material = materials[4];
            //sphere.transform.localScale = sphere.transform.localScale * 2;
            var statefulInteractable = sphere.AddComponent<StatefulInteractable>();
            statefulInteractable.ToggleMode = StatefulInteractable.ToggleType.OneWayToggle;
            var pipeCasing = sphere.AddComponent<PipeCasing>();
            pipeCasing.Initial(marker, sphere, dim, parentTransform);
            statefulInteractable.OnClicked.AddListener(() =>
            {
                if (statefulInteractable.IsToggled)
                {
                    pipeCasing.AddLength();
                    pipeCasing.Activate();

                }
                else
                {
                    pipeCasing.ClearPipes();
                }
            });
        }

        var info = sphere.AddComponent<NodeInformation>();
        info.imgIndex = (int)marker.img_index(1024, 1024 * 1024);
        info.type = marker.type;
        info.radius = marker.radius;
        info.position = marker.position;
        info.batch = marker.batch;
        return sphere;
    }

    public static GameObject CreateSphere(Marker marker, Vector3Int dim, Transform parentTransform, float colorIntensity)
    {
        var position = marker.position.Div(dim) - 0.5f * Vector3.one;
        var radius = marker.radius * Mathf.Min(1.0f / dim.x, Mathf.Min(1.0f / dim.y, 1.0f / dim.z));
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

        sphere.GetComponent<MeshRenderer>().material.shader = Shader.Find("Unlit/Color");
        sphere.GetComponent<MeshRenderer>().material.shader = Shader.Find("Standard");
        sphere.GetComponent<MeshRenderer>().material.color = GetColor(colorIntensity);
        //sphere.GetComponent<MeshRenderer>().material = materials[marker.type];

        Transform temp = GameObject.Find("Temp").transform;
        sphere.transform.position = parentTransform.TransformPoint(position);
        sphere.transform.SetParent(temp, true);
        sphere.transform.localScale = new Vector3(1, 1, 1) * radius;

        var info = sphere.AddComponent<NodeInformation>();
        info.imgIndex = (int)marker.img_index(1024, 1024 * 1024);
        info.type = marker.type;
        info.radius = marker.radius;
        info.position = marker.position;
        info.batch = marker.batch;
        return sphere;
    }

    private static Color GetColor(float colorIntensity)
    {
        Color[] colors = new Color[11] {new Color(108/255.0f,001/255.0f,33/255.0f),new Color(158/255.0f,001/255.0f,66/255.0f),new Color(214 / 255.0f, 64 / 255.0f, 78 / 255.0f),
                                        new Color(245/255.0f,117/255.0f,71/255.0f), new Color(253 / 255.0f, 185 / 255.0f, 106 / 255.0f),new Color(254 / 255.0f, 232 / 255.0f, 154 / 255.0f),
                                        new Color(245/255.0f,251/255.0f,177/255.0f), new Color(203 / 255.0f, 233 / 255.0f, 157 / 255.0f),new Color(135/255.0f,207/255.0f,164/255.0f),
                                        new Color(100/255.0f,175/255.0f,170/255.0f), new Color(70 / 255.0f, 158 / 255.0f, 180 / 255.0f)};
        float[] ranges = new float[11] { 0.0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f };
        for (int i = 0; i < ranges.Length - 1; i++)
        {
            if (colorIntensity >= ranges[i] && colorIntensity < ranges[i + 1]) return Color.Lerp(colors[i], colors[i + 1], (colorIntensity - ranges[i]) / (ranges[i + 1] - ranges[i]));
        }
        return colors[^1];
    }

    public static void RpcCreateTree(NetworkRunner runner, List<Marker> tree, Transform parentTransform, Vector3Int dim)
    {
        RpcClearTree(runner);
        foreach (var marker in tree)
        {
            Vector3 positionA = marker.position.Div(dim) - 0.5f * Vector3.one;
            positionA = parentTransform.TransformPoint(positionA);
            float radiusA = marker.radius * Mathf.Min(1.0f / dim.x, Mathf.Min(1.0f / dim.y, 1.0f / dim.z)) * parentTransform.parent.localScale.x;

            RpcCreateSphere(runner, positionA, radiusA, marker.type);
            var sphere = CreateSphere(positionA, radiusA, marker.type);
            if (marker.isLeaf)
            {
                var statefulInteractable = sphere.AddComponent<StatefulInteractable>();
                statefulInteractable.ToggleMode = StatefulInteractable.ToggleType.OneWayToggle;
                var pipeCasing = sphere.AddComponent<PipeCasing>();
                pipeCasing.Initial(marker, sphere, dim, parentTransform);
                statefulInteractable.selectEntered.AddListener((SelectEnterEventArgs args) =>
                {
                    Debug.Log("select leaf");
                    if (statefulInteractable.IsToggled)
                    {
                        pipeCasing.AddLength();
                        //pipeCasing.Activate();

                    }
                    else
                    {
                        pipeCasing.ClearPipes();
                    }
                });
            }

            if (marker.parent != null)
            {
                var positionB = marker.parent.position.Div(dim) - 0.5f * Vector3.one;
                float radiusB = marker.parent.radius * Mathf.Min(1.0f / dim.x, Mathf.Min(1.0f / dim.y, 1.0f / dim.z)) * parentTransform.parent.localScale.x;
                positionB = parentTransform.TransformPoint(positionB);

                RpcCreateCylinder(runner, positionA, positionB, radiusA, radiusB, marker.type);
            }
            else
            {
                //sphere.name = "Soma";
            }
        }

    }
    public static void CreateTree(List<Marker> tree, Transform parentTransform, Vector3Int dim)
    {
        materials[0] = Resources.Load<Material>("Textures/Red");
        materials[1] = Resources.Load<Material>("Textures/Yellow");
        materials[2] = Resources.Load<Material>("Textures/Green");
        materials[3] = Resources.Load<Material>("Textures/Cyan");
        materials[4] = Resources.Load<Material>("Textures/Purple");

        foreach (var marker in tree)
        {
            GameObject sphere = CreateSphere(marker, dim, parentTransform);

            if (marker.parent != null)
            {
                CreateCylinder(marker, dim, parentTransform);
            }
            else
            {
                sphere.name = "Soma";
            }
        }
    }
    public static void CreateTreeTemp(List<Marker> tree, Transform parentTransform, Transform temp, int colorType, Vector3Int dim)
    {
        materials[0] = Resources.Load<Material>("Textures/0");
        materials[1] = Resources.Load<Material>("Textures/1");
        materials[2] = Resources.Load<Material>("Textures/2");
        materials[3] = Resources.Load<Material>("Textures/3");
        materials[4] = Resources.Load<Material>("Textures/4");
        materials[5] = Resources.Load<Material>("Textures/5");
        materials[6] = Resources.Load<Material>("Textures/6");
        materials[7] = Resources.Load<Material>("Textures/7");
        materials[8] = Resources.Load<Material>("Textures/8");
        materials[9] = Resources.Load<Material>("Textures/9");

        //float Scale = 1 / 512.0f;
        Dictionary<Marker, SwcNode> map = new Dictionary<Marker, SwcNode>();
        foreach (var marker in tree)
        {
            GameObject sphere = Primitive.CreateSphereTemp(marker, dim, parentTransform, temp, colorType);

            if (marker.parent != null)
            {
                var parent = marker.parent;
                GameObject cylinder = Primitive.CreateCylinderTemp(marker, dim, parentTransform, temp, colorType);
                //Chosen c = cylinder.AddComponent<Chosen>();
                //c.nodeA = parent;
                //c.nodeB = node;
                //node.cylinder = cylinder;
            }
            else
            {
                sphere.name = "Soma";
                //Chosen soma = sphere.AddComponent<Chosen>();
                //soma.nodeA = node;
                //soma.nodeB = node;
            }
        }
    }

    public static void CreateTree(List<Marker> tree, Transform parentTransform, Vector3Int dim, List<float> growth)
    {
        materials[0] = Resources.Load<Material>("Textures/Red");
        materials[1] = Resources.Load<Material>("Textures/Yellow");
        materials[2] = Resources.Load<Material>("Textures/Green");
        materials[3] = Resources.Load<Material>("Textures/Cyan");
        materials[4] = Resources.Load<Material>("Textures/Purple");

        //float Scale = 1 / 512.0f;
        Dictionary<Marker, SwcNode> map = new Dictionary<Marker, SwcNode>();
        foreach (var marker in tree)
        {
            marker.radius = 1.8f * marker.radius;
        }
        for (int i = 0; i < tree.Count; i++)
        {
            Marker marker = tree[i];
            GameObject sphere = Primitive.CreateSphere(marker, dim, parentTransform, growth[i]);

            if (marker.parent != null)
            {
                var parent = marker.parent;
                GameObject cylinder = Primitive.CreateCylinder(marker, dim, parentTransform, growth[i], 0);
            }
            else
            {
                sphere.name = "Soma";
            }
        }
    }

    public static void CreateBranch(List<Marker> branch, Transform parentTransform, Vector3Int dim)
    {
        foreach (var marker in branch)
        {
            GameObject sphere = Primitive.CreateSphere(marker, dim, parentTransform);

            //Marker parent;

            GameObject cylinder = Primitive.CreateCylinder(marker, dim, parentTransform);
            //Chosen c = cylinder.AddComponent<Chosen>();
            //App2.MarkerMap[marker] = c;
            //c.nodeA = parent;
            //c.nodeB = node;
            //node.cylinder = cylinder;
        }
    }
}