# About
FastPoints is a point cloud rendering plugin for Unity designed for both non-technical users and programmers.

Contact: [Elias Neuman-Donihue](contact@eliasnd.com)

# Getting Started
### Installation


Install the [Unity Hub](https://unity.com)

Install the latest version of the Unity Editor and create a new Unity project

Clone this repository into the Assets folder of your project

Create a new folder called Plug-Ins also under the Assets folder

Download the binaries for the fastpoints-native plugin for your system [here](https://github.com/eliasnd/fastpoints-native/releases) and place them in the new Plug-Ins folder

### Usage

To load a point cloud, drag and drop any .ply, .las, or .laz file into the editor's Assets panel. 

<!-- <img src="doc/assets_panel.png" alt="drawing" width="700"/> -->

To add a loaded point cloud to your scene, right click in the Hierarchy panel and select "Create Empty"

In the inspector panel on the right side of the screen, select "Add Component," search for "Point Cloud Renderer," and select the result

Drag your loaded cloud from the Assets panel to the "Handle" field in the component that appears

<!-- <img src="doc/add_component.png" alt="add component" height="400" /> -->
<!-- <img src="doc/empty_component.png" alt="empty component" height="400" /> -->

In a moment, a low-detail version of your point cloud will appear in the Scene view. Once this happens, preprocessing operations will start and will need to be restarted if the Unity editor is quit before they finish. You can safely quit the program without losing preprocessing progress once the "Converter Status" field on the Point Cloud Renderer component reads "DONE"

After preprocessing finishes, the full point cloud will render in then Scene view instead of the low-detail version.

<!-- <img src="doc/decimated_cloud.png" alt="rendered cloud" width="700" /> -->

The Point Cloud Renderer component exposes a number of options for customizing the conversion process
- Decimated cloud size: How many points should be subsampled for the inital decimated cloud. Higher point counts will yield better results, but will take longer to display initially
- Cloud path: Where converted clouds should be stored. If not specified, defaults to /ConvertedClouds
- Tree depth: How many layers the final octree should have. Deeper trees take longer to generate, but render more perfomantly
- Max chunk size: The maximum number of points that should be stored in one chunk during the chunking stage of the generation
- Max node size: The maximum number of points that should be stored in any leaf node of the octree. Lower counts will take longer to generate, but yield better performance
