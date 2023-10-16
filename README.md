# INTraMR: Intuitive Neuron Tracing in Mixed Reality
INTraMR is a progressive and controllable semi-automatic neuron tracing tool, presenting a powerful solution tailored specifically for mixed reality environments.
## Getting Started
1. Install Unity 2021.3.26f1c1.
2. Clone this repository to your computer.
3. Open the cloned project "AugmentedNeuronTracer" with Unity.
4. Open the scene "INTraMR" in Secenes.
5. Open the Scripts/configuration, modify settings in the inspector.
## Main File Directory
```
AugmentedNeuronTracer
├── Assets
│   ├── Materials
│   ├── Resources
│   │   ├── ComputeShaders
│   │   │   ├── ErrorHeatMap.compute //for compute the error heat map of the reconstruction results
│   │   │   ├── FIM.compute //kernels of fim reconstruction
│   │   │   ├── OccupancyMap.compute //compute occupancy for chebyshev volume rendering acceleration
│   │   │   ├── ParallelDistanceMap.compute //compute distancemap for chebyshev volume rendering acceleration
│   │   │   ├── Utility.compute
│   │   ├── Prefabs
│   │   │   ├── AutoMenu.prefab //menu for auto reconstruction and guiding regional tracing
│   │   │   ├── EyeMenu.prefab //menu for tracing individual branches and removing noises
│   │   │   ├── IsolateMenu.prefab //menu for isolating branches
│   │   │   ├── HandMenuBase.prefab // menu for main menu
│   │   │   ├── PaintingBoard.prefab // parent transform of reconstruction and neuronal image
│   │   ├── Textures //materials in different colors of reconstruction
│   ├── Scenes
│   │   ├── INTraMR.unity //main scene
│   ├── Scripts
│   │   ├── configuration //configuration of tool
│   │   ├── Command //for replay 
│   │   │   ├── CommandStructure.cs
│   │   ├── Config.cs //configuration of reconstruction
│   │   ├── Menu //scripts of menus
│   │   │   ├── AutoMenu.cs
│   │   │   ├── GazeMenu.cs
│   │   │   ├── MainMenu.cs
│   │   │   ├── IsolateMenu.cs
│   │   ├── Mutiplayer //for mutiplayer
│   │   │   ├── BasicSpawner.cs
│   │   ├── NeuronEditing 
│   │   │   ├── GazeController.cs //for controlling the eye tracking interaction
│   │   │   ├── GestureController.cs //for controlling the hand gesture interaction
│   │   │   ├── PipeCasing.cs //pipecasing for signal isolating
│   │   │   ├── Primitive.cs //for reconstruction neuron from markers
│   │   ├── NeuronTracing
│   │   │   ├── FIM.cs //reconstrucion with fast iterative methods boosted by compute shader
│   │   │   ├── HierarchyPruning.cs // for refining
│   │   │   ├── Tracer.cs //for neuron tracing
│   │   │   ├── VirutalFinger.cs //for manual reconstruction
│   │   ├── Utility
│   │   │   ├── ErrorHeatMapCompute.cs 
│   │   │   ├── Importer.cs //import the neuronal image from v3dpbd or TIFF
│   │   │   ├── LoadSwc.cs //load reconstructed neuron file swc
│   │   │   ├── OccupancyMapCompute.cs 
│   │   ├── Visualization
│   │   │   ├── PostProcess.cs //for volume rendering of neuronal image with post process
│   ├── Shaders
│   │   ├── VRAccelerated.shader //volume rendering (VR) accelerated with chebyshev distance
│   │   ├── VRBase.shader // VR without any acceleration
│   │   ├── VRFixedThresh.shader // VR with fixed background threshold
│   │   ├── VRFlexibleThresh.shader // VR with area background threshold
│   ├── Textures // texture of neuronal images and processed images
├── MRTK3 //packages of MRTK3
└── Packages //packages configuration
```
