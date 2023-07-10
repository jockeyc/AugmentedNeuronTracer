
using CommandStructure;
using Fusion;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

public class Config : MonoBehaviour
{
    [SerializeField] public string path;
    [SerializeField] public string savePath;
    [SerializeField] public bool needImport;
    [SerializeField] public bool scale = true;
    [SerializeField] public bool gaussianSmoothing = true;
    [SerializeField] public bool forceRootCenter = false;
    [SerializeField] public string imageName;
    [SerializeField] private Texture3D _volume;
    [SerializeField] private Texture3D _origin;
    [SerializeField] private RenderTexture _filtered;
    [SerializeField] public Vector3Int _scaledDim;
    [SerializeField] public Vector3Int _originalDim;
    [SerializeField] public Texture3D _occupancy;
    [SerializeField] private int _bkgThresh;
    [SerializeField] public int _blockSize;
    [SerializeField] public GameObject seed;
    [SerializeField] public GameObject cube;
    [SerializeField] public GameObject paintingBoard;
    [SerializeField] public Vector3Int _rootPos;
    [SerializeField] public float _somaRadius = -1;
    [SerializeField] private int _viewThresh;
    [SerializeField] public uint _curIndex = 0;
    [SerializeField] public int thresholdBlockSize = 2;
    [SerializeField] private ShaderType _vrShaderType = ShaderType.Base;
    [SerializeField] public PostProcessVolume _postProcessVolume;
    [SerializeField] public bool useBatch = true;
    [SerializeField] public bool useKeyBoard = true;
    [SerializeField] public int customThresh = 30;
    public NetworkRunner runner;

    private byte[] volumeData;

    public List<Blocker> _blockers = new();

    public Tracer tracer;
    public GestureController gestureController;
    public GazeController gazeController;
    public CMDInvoker invoker;

    public Material originMaterial;
    public Material bkgThresholdMaterial;
    public Material fixedBkgThresholdMaterial;

    public int thresholdOffset = 0;
    public float viewRadius = 5.0f;

    public bool volumeRenderingWithChebyshev = false;

    public Texture3D occupancyMap;
    public Texture3D distanceMap;

    public enum ShaderType {
        Base, FlexibleThreshold, FixedThreshold, BaseAccelerated
    }
    private void Awake()
    {
        if (path.Length > 0 && needImport)
        {
            _origin = new Importer().Load(path);
        }
        if (_origin == null) return;
        imageName = _origin.name;

        savePath = CreateSavePath($"C:\\Users\\80121\\Desktop\\MyResult\\{imageName}\\");

        _originalDim = new Vector3Int(_origin.width, _origin.height, _origin.depth);
        if (scale)
        {
            _volume = TextureScaler.Scale(_origin, _scaledDim, gaussianSmoothing);   //Scale volume
            _scaledDim = new Vector3Int(_volume.width, _volume.height, _volume.depth);
        }
        else
        {
            _scaledDim = _originalDim;
        }
        Debug.Log($"{_volume.width},{_volume.height},{_volume.depth}");

        volumeData = _volume.GetPixelData<byte>(0).ToArray();

        tracer = gameObject.AddComponent<Tracer>();
        gestureController = gameObject.GetComponent<GestureController>();
        gazeController = gameObject.GetComponent<GazeController>();

        invoker = gameObject.AddComponent<CMDInvoker>();
        invoker.tracer = tracer;
        invoker.savePath = savePath + "\\commands.json";

        //volume rendering post process
        _postProcessVolume = GameObject.Find("volume").GetComponent<PostProcessVolume>();
        if(_postProcessVolume.profile.GetSetting<BaseVolumeRendering>()==null)_postProcessVolume.profile.AddSettings<BaseVolumeRendering>();
        _postProcessVolume.profile.GetSetting<BaseVolumeRendering>().volume.overrideState = true;
        _postProcessVolume.profile.GetSetting<BaseVolumeRendering>().volume.value = _origin;

        if(volumeRenderingWithChebyshev)
        {
            var computer = gameObject.AddComponent<OccupancyMapCompute>();
            computer.ComputeOccupancyMap();
            computer.ComputeDistanceMap();
            _postProcessVolume.profile.GetSetting<BaseVolumeRendering>().occupancyMap.overrideState = true;
            _postProcessVolume.profile.GetSetting<BaseVolumeRendering>().distanceMap.overrideState = true;
            _postProcessVolume.profile.GetSetting<BaseVolumeRendering>().dimension.overrideState = true;
            _postProcessVolume.profile.GetSetting<BaseVolumeRendering>().blockSize.overrideState = true;
            _postProcessVolume.profile.GetSetting<BaseVolumeRendering>().occupancyMap.value = computer.occupancyMap;
            _postProcessVolume.profile.GetSetting<BaseVolumeRendering>().distanceMap.value = computer.distanceMap;
            _postProcessVolume.profile.GetSetting<BaseVolumeRendering>().dimension.value = computer.dimension;
            _postProcessVolume.profile.GetSetting<BaseVolumeRendering>().blockSize.value = computer.blockSize;
            VRShaderType = ShaderType.BaseAccelerated;
        }
    }

    public int BkgThresh
    {
        get { return _bkgThresh; }
        set
        {
            if (value < 0 || value > 255)
            {
                throw new ArgumentException("The background threshold must be set correctly at 0 to 255");
            }
            _bkgThresh = value;
            _viewThresh = value;
        }
    }

    public int ViewThresh
    {
        get { return _viewThresh; }
        set
        {
            if (value < 0 || value > 255)
            {
                throw new ArgumentException("The backgroundview threshold must be set correctly at 0 to 255");
            }
            _viewThresh = value;

        }
    }

    public Texture3D Volume
    {
        get => _volume;
        set => _volume = value;
    }

    public Texture3D Origin
    {
        get => _origin;
        set => _origin = value;
    }

    public byte[] VolumeData
    {
        get => volumeData;
    }

    public ShaderType VRShaderType
    {
        get => _vrShaderType;
        set {
            _vrShaderType = value;
        }
    }



    bool save = false;
    private void Update()
    {
        if (Input.GetKey(KeyCode.Z))
        {
            invoker.Undo();
        }
        if (Input.GetKey(KeyCode.X))
        {
            invoker.Redo();
        }
        if(Input.GetKeyDown(KeyCode.C) && useKeyBoard)
        {
            invoker.Execute(new AdjustCommand(tracer,_curIndex));
        }
        if (Input.GetKey(KeyCode.G))
        {
            cube.GetComponent<BoxCollider>().enabled = !cube.GetComponent<BoxCollider>().enabled;
        }
    }

    public void ApplyMask(RenderTexture mask, byte[] maskedVolumeData)
    {
        fixedBkgThresholdMaterial.SetTexture("_Mask", mask);
        volumeData = maskedVolumeData;
    }

    private string CreateSavePath(string path)
    {
        if(!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        int existingNumber = -1;
        foreach (string directory in Directory.GetDirectories(path))
        {
            string directoryName = new DirectoryInfo(directory).Name;
            if (int.TryParse(directoryName, out int number))
            {
                existingNumber = Math.Max(existingNumber, number);
            }
        }

        string newDirectoryPath = Path.Combine(path, $"{existingNumber:D3}");
        Debug.Log(newDirectoryPath);
        if (Directory.Exists(newDirectoryPath) && Directory.GetFiles(newDirectoryPath).Length > 0 || existingNumber == -1)
        {
            string newDirectoryName = $"{existingNumber + 1:D3}"; 
            newDirectoryPath = Path.Combine(path, newDirectoryName);  

            Directory.CreateDirectory(newDirectoryPath);
        }

        return newDirectoryPath;

    }
}
