# PICO Environment Starter

Clean Unity 6.5 project copied from the previous game's wilderness scene.

## PICO SDK

The PICO XR SDK is intentionally not included in this repository. Install the
matching `com.bytedance.pico.xr` package from the official PICO distribution
channel into `Packages/com.bytedance.pico.xr` before opening or building the project.
The package is excluded from Git because it contains vendor SDK and license
material.

## Start scene

- `Assets/Scenes/EnvironmentScene.unity`
- Included in Build Settings as the only enabled scene.

## Preserved

- Wilderness terrain and vegetation
- Scene props, lighting, atmosphere, and skybox
- PICO XR player rig and camera
- Environment authoring tools under `Tools > Environment`

## Removed

- Monster and monster asset pack
- Golden staff and its source model
- Rhythm rocks and hit effects
- Gameplay scripts, audio, UI, song data, and editor tooling

The cleaned scene was opened and saved by Unity with zero missing script components.

## Build

Open the project with the Unity version recorded in `ProjectSettings/ProjectVersion.txt`,
select Android, and use the project's existing Android build helper to export an
APK. Build outputs and APKs are intentionally excluded from source control.
