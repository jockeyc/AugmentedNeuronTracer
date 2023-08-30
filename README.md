# AugmentedNeuronTracer
Augmented Neuron Tracer (ANT), a progressive and controllable semi-automatic neuron tracing tool in AR environment using Microsoft Hololens2 platform.
## Getting Started
1. Install Unity 2021.3.26f1c1.
2. Clone this repository to your computer.
3. Open the cloned project "AugmentedNeuronTracer" with 2021.3.26f1c1.
4. Open the scene "ANT" in Secenes.
5. Open the Resources/Prefabs/Config, modify settings in the inspector.
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
│   │   │   ├── AutoMenu.prefab //menu for auto reconstruction and area background threshold adjusting
│   │   │   ├── Config.prefab //configuration of reconstruction
│   │   │   ├── EyeMenu.prefab //menu for tracing branch with eye
│   │   │   ├── IsolateMenu.prefab //menu for isolating branch signals
│   │   │   ├── HandMenuBase.prefab // menu for basic hand menu
│   │   │   ├── PaintingBoard.prefab // parent transform of reconstruction and neuronal image
│   │   ├── Textures //materials in different colors of reconstruction
│   ├── Scenes
│   │   ├── ANT.unity //main scene
│   │   ├── MRTK Scenes //MRTK example scenes
│   ├── Scripts
│   │   ├── Command //for replay of reconstruction
│   │   │   ├── CommandStructure.cs
│   │   ├── Config.cs //configuration of reconstruction
│   │   ├── Menu //scripts of menus
│   │   │   ├── AutoMenu.cs
│   │   │   ├── EyeMenu.cs
│   │   │   ├── MainMenu.cs
│   │   │   ├── IsolateMenu.cs
│   │   │   ├── SubMenu.cs
│   │   ├── Mutiplayer //for mutiplayer
│   │   │   ├── BasicSpawner.cs
│   │   ├── NeuronEditing 
│   │   │   ├── GazeController.cs //for controlling the eye tracking interaction for tracing branch or adjusting threshold
│   │   │   ├── GestureController.cs //for controlling the hand gesture interaction for manual reconstruction
│   │   │   ├── PipeCasing.cs //script of pipecasing for signal isolating
│   │   │   ├── Primitive.cs //for reconstruction neuron from markers
│   │   │   ├── PipeController.cs //for controlling pipecasing with hands
│   │   ├── NeuronTracing
│   │   │   ├── FIM.cs //reconstrucion with fast iterative methods boosted by compute shader
│   │   │   ├── FastMarching.cs //reconstruection with fast marching methods
│   │   │   ├── Heap.cs //assisted fmm
│   │   │   ├── HierarchyPruning.cs // for culling
│   │   │   ├── Marker.cs 
│   │   │   ├── Tracer.cs //for neuron tracing
│   │   │   ├── VirutalFinger.cs //for manual reconstruction
│   │   ├── Utility
│   │   │   ├── ErrorHeatMapCompute.cs 
│   │   │   ├── Importer.cs //import the neuronal image from v3dpbd or TIFF
│   │   │   ├── LoadSwc.cs //load reconstructed neuron file swc
│   │   │   ├── OccupancyMapCompute.cs 
│   │   │   ├── VectorExtension.cs //for extension of the static methods of Vector class
│   │   ├── Visualization
│   │   │   ├── PostProcess.cs //for volume rendering of neuronal image with post process
│   ├── Shaders
│   │   ├── VRAccelerated.shader //volume rendering (VR) accelerated with chebyshev distance
│   │   ├── VRBase.shader // VR without any acceleration
│   │   ├── VRFixedThresh.shader // VR with fixed background threshold
│   │   ├── VRFlexibleThresh.shader // VR with area background threshold
│   │   ├── VRHeatMap.shader // heat map rendering
│   ├── Textures // texture of neuronal images and processed images
├── MRTK3 //packages of MRTK3
└── Packages //packages configuration
```
