# VRChat SDK Video Player Shim

## Demonstration video  
![AVPro Playmode Walkthrough](/uploads/4e2117eec7b640309407462ed1832960/AVPro_Playmode_Walkthrough.mp4)

This package contains a set of scripts which enable support for both UnityVideo and AVPro _in play mode_, including YTDL integration.

Instructions:
- If you don't care for AVPro support, you can simply import the VideoPlayerShim unitypackage as is.
- If you _DO_ want AVPro support, you will need to download the same AVPro package that VRChat is currently using.  
- Last checked the version was 2.5.6, but it may be another version in the future.  
- To check the version of AVPro that VRChat is using, you will need to go into a world _**with an enabled AVPro video player**_ so the log containing the version will be written.  
- Open the debug log ([relevant VRChat docs](https://docs.vrchat.com/docs/debugging-udon-projects#steam-launch-options)) 
- Look for the line that starts with `[AVProVideo] Initializing AVPro Video vX.X.X` where the X.X.X is the version that VRChat is using.
- Download the trail unitypackage file for that version from the [RenderHeads Github](https://github.com/RenderHeads/UnityPlugin-AVProVideo/releases) ([2.5.6 for example](https://github.com/RenderHeads/UnityPlugin-AVProVideo/releases/tag/2.5.6))
- Import that unitypackage into your project, then import the VideoPlayerShim unitypackage after it.
- Setup your VRCAVProVideoPlayers/Speakers/Screens as desired (importing a community video player prefab will also work)
- Press play in unity and try playing a youtube (UnityVideo and AVPro) or twitch link (AVPro only)

That should just work. Please open an issue if you find something isn't matching up like you expect.

Copyright notice:  
A portion of the code in this package is modified logic from the AVPro Trial package in order to make it work with the VRCSDK/ClientSim.  
All rights of the original trial version code are reserved by RenderHeads and is noted as such in the respective files.
