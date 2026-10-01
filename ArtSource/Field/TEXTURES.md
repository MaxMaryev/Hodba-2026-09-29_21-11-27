# Procedural field texture sources

Run `blender --background --factory-startup --python-exit-code 1 --python ArtSource/Field/textures.py` from the project root. No external image inputs or add-ons are used; NumPy ships with Blender. Seed: 29092026. Run `ArtSource/Field/test_textures.py` with Blender's bundled Python to validate the actual PNG payloads.

T1 Ash is a warm fine ash field covering 2 × 2 m. T2 Ripples covers 4 × 4 m with 40 dominant asymmetric ridges along V (10 cm nominal pitch across U), bending, attenuation, split branches, and interruptions. T3 Crust covers 4 × 4 m, using periodically warped blue-noise Voronoi plate boundaries, finite T-junction branches, light crack deposits, and fine surface grains. These maps are 2048 × 2048. T5 AshMicro covers 0.5 × 0.5 m at 1024 × 1024 and supplies close-range grains that share T1's average color without sharing its pattern.

The current ground delivery consists of albedo, +Y tangent normal, and unsigned 16-bit height. Older AO, roughness, and metallic/smoothness files remain in the asset directory for compatibility but are not generated or validated by this texture stage.

The albedo maps contain material color variation only, with no directional lighting. AO is a procedural cavity estimate, not a raytraced bake. Relit diagnostic previews are separate images in `Previews/` and must not be used as albedo.

T4 is a nonseamless left boot impression and its correct mirrored right version, 256 × 512, with a 14 × 32 cm canvas, 2 cm maximum depression, approximately 4.5 mm rim, and disturbed ejecta. The albedo alpha is the common decal mask. Normal X is inverted on the right version, in addition to horizontal mirroring. Decal normal and height need the albedo alpha mask when composited over ground; neither is independently transparent.

Use sRGB import for albedo only. All other maps are linear data. Import normal files as normal maps with green-channel flipping disabled. Ground textures use Repeat wrapping; footprints use Clamp. Ground height decoding is `sample × maximum`: T1 0..6 mm, T2 0..12 mm, T3 0..12 mm, and T5 0..3 mm. Their encoded mean is approximately 0.5. T4 retains its signed −20..+6 mm decal range. Exact achieved values and color statistics are in `texture-validation.json`.

The 2048 maps are sampled over 2047 intervals and T5 over 1023 intervals; both duplicate the boundary texel on each axis. Wrapped central derivatives retain matching normal boundaries. This trades one repeated texel row/column for exact encoded edge identity. Validation reads and decompresses the exported PNG bytes, then checks boundary identity, normal lengths and positive Z, 16-bit height precision, sizes, mean color, luminance variation, quadrant and 10 cm block uniformity, height means, ripple frequency, crack dimensions, site spacing, and footprint mirrors.

Visual QA inspected all material swatch/normal sheets, the ripple 2 × 2 repetition, and both footprint diagnostics. Unity runtime appearance still depends on the consuming shaders, normal strength, displacement and decal blending, and lighting; those are outside this generator.
