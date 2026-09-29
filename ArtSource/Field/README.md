# Field asset production

Blender **4.5.9 LTS**, procedural generation, no external images, paid assets or Mixamo dependency.
Official archive: https://download.blender.org/release/Blender4.5/blender-4.5.9-windows-x64.zip

From the repository root:

```powershell
./ArtSource/Field/build.ps1 -Stage All -Blender 'D:\Max\Tools\Blender\blender-4.5.9-windows-x64\blender.exe'
./ArtSource/Field/build.ps1 -Stage Verify
```

Stages: All, Rocks, Textures, Traveler, Audio, Verify. No add-ons or pip packages are needed. NumPy ships with Blender; Blender's audaspace decodes the audio sources.
Generators overwrite only this asset pack's outputs. Review changes before rebuilding an artist-edited output.

## Delivery

- `Assets/Art/Field/Models`: 8 small rocks (224 triangles each), 3 boulders (816 each), traveler (7976).
- `Assets/Art/Field/Textures`: separate PBR maps, 1024 rock/character atlases, 2048 ground sources, 256×512 footprints.
- `Assets/Art/Field/Audio`: 30 mono footsteps, calm and gust wind loops (90 s stereo), ash-hiss loop (40 s mono); 48 kHz 16-bit WAV.
- `Assets/Art/Field/Prefabs`, `Materials`, `Scenes`: built by Unity editor command **Hodba → Field → Build and Validate**.
- `ArtSource/Field`: editable `.blend` authoring files, generators, validation, previews.

FBX: metres, Y up, forward +Z. Blender authoring uses Z up, forward -Y. Rock origins lie at the horizontal bounds centre and lowest plane. Mesh transforms are unit scale on export.
The boulder ash apron faces Unity **+Z**; rotate the entire prefab so this face points into the wind. Small-rock ash is a thin top deposit. All 11 rock bases are closed and planar. No static light/shadow is baked into albedo.

Rocks use separate 1024 atlases for M1 and M2, with 6% inset per cell and extruded UV gutters. Pores use normal maps; AO is separate. Atlas features are not physically identical in size on all rock variants. M3 has a four-band textile/leather atlas and a 21-bone Humanoid skeleton.

Texture height ranges, tiling, and normal conventions: see [TEXTURES.md](TEXTURES.md). Normal maps use +Y; packed metallic/smoothness has R=0, A=1−roughness. Preserve the alpha channel on packed maps. Ground textures use 1024 mobile overrides; original files remain 2048.

## Audio

CC0 Freesound recordings, listed with the reasons for each choice in [Audio/SOURCES.md](Audio/SOURCES.md). The Audio stage fetches any missing HQ preview into `Audio/Source`; an original `<id>.wav` placed there takes precedence. Loops are equal-power crossfaded over 6 s (hiss: 2 s) and limited circularly, so neither the waveform nor the gain jumps at the seam. The calm and gust loops share one gain. Steps are cut automatically: isolated footfalls, crest factor at most 22 dB, the best signal-to-noise first, loudest 100 ms normalised to −18 dBFS. Unity imports steps as mono Decompress On Load and loops as Compressed In Memory, both Vorbis. `audio-validation.json` records every clip; `test_audio.py` checks formats, seams, clipping, edges and the loudness spread.

## Animation and preview

Traveler reference is T-pose. FBX clips are `Walk_Tired` (1.2 seconds) and `Idle_Breathing` (4 seconds), possibly prefixed by the rig name in Unity. In-place animation; no root motion. Preview movement is 1.3 m/s. Clothing is skinned, without cloth simulation. The default traveler prefab casts shadows only; switch renderer Shadow Casting Mode to On to inspect its body.

The demo is an asset review scene, not a game controller or a world generator. Its camera uses a new Forward Renderer appended to the existing URP asset; the original 2D renderer remains the default. Unity build records fresh import, mesh and avatar checks in `unity-validation.json` and images in `Previews/`. Run it after changing any sources.

The validation project used for batch checks is separate from the open user project. Do not confuse editor screenshots with performance verification on Android. Android requires a real device and is reported separately.
