# About
FastPoints is a point cloud rendering plugin for Unity designed for both non-technical users and programmers.

The renderer supports PLY, LAS, and LAZ files with a drag-and-drop interface and is specifically optimized for large point clouds.

Visualization of attributes other than color is not currently supported.

Contact: [Elias Neuman-Donihue](contact@eliasnd.com)

# Publication

[FastPoints: A State-of-the-Art Point Cloud Renderer for Unity](https://arxiv.org/abs/2302.05002)

# Installation
## MacOS


1. Install the [Unity Hub](https://unity.com)

2. Install the latest version of the Unity Editor and create a new Unity project

3. Clone this repository into the Assets folder of your project

4. Create a new folder called Plug-Ins also under the Assets folder

5. Install the [native plugin](https://github.com/eliasnd/fastpoints-native) and move the generated `fastpoints-native.bundle` file to the Plug-Ins folder

## Windows and Linux

1. Install the [Unity Hub](https://unity.com)

2. Install the latest version of the Unity Editor and create a new Unity project

3. Clone this repository into the Assets folder of your project

4. Create a new folder called Plug-Ins also under the Assets folder

5. Install the [native plugin](https://github.com/eliasnd/fastpoints-native/tree/windows) and move the generated `fastpoints-native.dll` and `laszip.dll` files to the Plug-Ins folder

# Usage

To load a point cloud, drag and drop any .ply, .las, or .laz file into the editor's Assets panel.

To add a loaded point cloud to your scene, right click in the Hierarchy panel and select "Create Empty"

In the inspector panel on the right side of the screen, select "Add Component," search for "Point Cloud Renderer," and select the result

Drag your loaded cloud from the Assets panel to the "Handle" field in the component that appears

In a moment, a low-detail version of your point cloud will appear in the Scene view. Once this happens, preprocessing operations will start and will need to be restarted if the Unity editor is quit before they finish. You can safely quit the program without losing preprocessing progress once the "Converter Status" field on the Point Cloud Renderer component reads "DONE"

After preprocessing finishes, the full point cloud will render in the Scene view instead of the low-detail version.

The Point Cloud Renderer component exposes a number of settings to customize 

## Basic Settings

- **Point size**: visual size of each point

- **Point budget**: how many points can be rendered at once. Higher budgets yield better visual results but worse performance 

## Advanced Settings

### Conversion

- **Decimated cloud size**: How many points should be subsampled for the inital decimated cloud. Higher point counts will yield better results, but will take longer to display initially
- **Source**: Path to point cloud on disk. Leave empty to use path specified by handle object
- **Target**: Path to write converted cloud to. Leave empty to use default location (/your/project/path/ConvertedClouds)
- **Method**: Subsampling method for the Potree Converter. Valid options are poisson (default), poisson_average, and random.
- **Encoding**: Encoding to use for the Potree Converter. Valid options are UNCOMPRESSED (default) and BROTLI
- **Chunk method**: Chunking strategy for the Potree Converter. Valid options are LASZIP (default), LAS_CUSTOM, and SKIP

For more information on settings controlling the Potree Converter, please refer to their [repo](https://github.com/potree/PotreeConverter)

### Rendering

- **Camera to render to**: What camera to use for rendering. Defaults to currently enabled camera, either scene view or game view
- **Max nodes to create / frame**: Maximum number of nodes to create a frame. Higher values lead to faster loading but a greater load on the CPU
- **Max nodes to load / frame**: Maximum number of nodes to load per frame. Higher values lead to faster loading but a greater load on the GPU
- **Smallest node size to render**: Minimum node size that will be displayed. The ideal value for this may change depending on your resolution, so lower this if you are seeing less detail than expected.
- **Cache point budget**: Maximum number of points stored in cache. Higher values will make loading faster but use more RAM.
- **Draw gizmos**: Whether bounding boxes for nodes and frustums for cameras should be drawn in scene view
- **Use decimated cloud**: Whether the smaller decimated cloud should be rendered instead of the full point cloud. Only possible if decimation was done during the current editor session

# Implementation

This plugin uses an adapted version of the Potree converter to convert and seamlessly render clouds within the Unity environment. For more details please refer to my paper linked above.

# Known Issues

The loading bar is currently not very descriptive - it will stay at 0% during point cloud conversion, which is the largest chunk of the preprocessing pipeline, then jump to 100%. Hoping to implement a more accurate version in the near future.
