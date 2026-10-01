# Procedural field texture sources

Run `blender --background --factory-startup --python-exit-code 1 --python ArtSource/Field/textures.py` from the project root. No external image inputs or add-ons are used; NumPy ships with Blender. Seed: 29092026. Run `ArtSource/Field/test_textures.py` with Blender's bundled Python to validate the actual PNG payloads.

T1 Ash is a warm fine ash field covering 2 × 2 m. T2 Ripples covers 4 × 4 m with 40 dominant asymmetric ridges along V (10 cm nominal pitch across U), bending, attenuation, split branches, and interruptions. T3 Crust covers 4 × 4 m, using periodically warped blue-noise Voronoi plate boundaries, finite T-junction branches, light crack deposits, and fine surface grains. These maps are 2048 × 2048. T5 AshMicro covers 0.5 × 0.5 m at 1024 × 1024 and supplies close-range grains that share T1's average color without sharing its pattern.

The current ground delivery consists of albedo, +Y tangent normal, and unsigned 16-bit height. Older AO, roughness, and metallic/smoothness files remain in the asset directory for compatibility but are not generated or validated by this texture stage.

The albedo maps contain material color variation only, with no directional lighting. Relit diagnostic previews are separate images in `Previews/` and must not be used as albedo.

T4 is a nonseamless left boot impression and its correct mirrored right version, 256 × 512, with a 14 × 32 cm canvas, 2 cm maximum depression, approximately 4.5 mm rim, and disturbed ejecta. The albedo alpha is the common decal mask. Normal X is inverted on the right version, in addition to horizontal mirroring. Decal normal and height need the albedo alpha mask when composited over ground; neither is independently transparent.

Use sRGB import for albedo only. All other maps are linear data. Import normal files as normal maps with green-channel flipping disabled. Ground textures use Repeat wrapping; footprints use Clamp. Ground height decoding is `sample × maximum`: T1 0..6 mm, T2 0..12 mm, T3 0..12 mm, and T5 0..3 mm. Their encoded mean is approximately 0.5. T4 retains its signed −20..+6 mm decal range. Exact achieved values and color statistics are in `texture-validation.json`.

The 2048 maps are sampled over 2047 intervals and T5 over 1023 intervals; both duplicate the boundary texel on each axis. Wrapped central derivatives retain matching normal boundaries. This trades one repeated texel row/column for exact encoded edge identity. Validation reads and decompresses the exported PNG bytes, then checks boundary identity, normal lengths and positive Z, 16-bit height precision, sizes, mean color, luminance variation, quadrant and 10 cm block uniformity, height means, ripple frequency, crack dimensions, site spacing, and footprint mirrors.

## Close-range acceptance checks

For the 1 px variance test, discard only the duplicated last row and column. On the remaining periodic core, let `B` be the 3 × 3 binomial blur with kernel `[1,2,1]ᵀ·[1,2,1]/16` and wrapped edges. Report `var(h − B(h)) / var(h − mean(h))`. Height uses the decoded **exported 16-bit PNG**, and albedo uses sRGB luma of the exported 8-bit PNG. The same metric was run on commit `657bf8a` before overwriting its files.

| Map | Commit `657bf8a` | Current | Limit |
| --- | ---: | ---: | ---: |
| T1 height | 33.11% | 0.357% | ≤10% |
| T1 albedo | 37.75% | 3.54% | ≤25% |
| T5 height | 33.15% | 0.201% | ≤10% |
| T5 albedo | 38.46% | 1.55% | Informational |

T1 gradient isotropy uses an eight-bin angular histogram of central differences on the exported height core: largest/smallest bin is **1.049** (limit 1.15). T5 is **1.051**. The normal map is recomputed from this height with OpenGL +Y orientation.

T2 ridge tracks are maxima in one-pitch windows of the exported height. The strongest 3–9 cycle Fourier mode of their shared lateral displacement gives a **0.80 m** meander wavelength and about **1.55 cm** amplitude. The exported branch marker diagnostic contains **240** branch events over the 16 m² tile, or **15.0/m²**. Crest relief has about **±29%** 10–90 percentile modulation relative to its mean.

T3's crack diagnostic is scanned along U, V, and shears at ±2.5° and ±5°; a 3 × 3 structure tensor must confirm the local crack tangent. The longest qualifying straight run is **35.2 mm** (limit 50 mm). For the plate-noise diagnostic, the maximum narrow-band/sideband FFT power ratio at the old grid frequencies 8, 11, 12, 280, and 390 cycles/tile is **1.36** (limit 2.0). The 99.9th percentile of the absolute plate tilt is **0.40 mm**; plate tone range is **3.78%**.

`Previews/` contains exact 320 × 320 pixel crops named `<material>_Albedo_Crop320_1to1.png` and `<material>_Normal_Crop320_1to1.png`, plus `<material>_Grazing_3x3.png` for every T1/T2/T3/T5 material. `T2_Ripples_Branch_Normal_Crop320_1to1.png` shows a branch close up. `texture-validation.json` carries full precision measurements and the baseline values.

Visual QA inspected the 1:1 crops, the 3 × 3 grazing sheets, and the footprint diagnostics. Unity runtime appearance still depends on the consuming shaders, normal strength, displacement and decal blending, and lighting; those are outside this generator.
