# Procedural field texture sources

Run `blender --background --factory-startup --python-exit-code 1 --python ArtSource/Field/textures.py` from the project root. No external image inputs or add-ons are used; NumPy ships with Blender. Seed: 29092026. Run `ArtSource/Field/test_textures.py` with Blender's bundled Python to validate the actual PNG payloads.

T1 Ash is a warm fine ash field covering 2 × 2 m. T2 Ripples covers 4 × 4 m with 40 dominant ridges along V (10 cm pitch across U), bending, attenuation, and phase variation. T3 Crust covers 4 × 4 m, using periodically warped Voronoi plate boundaries, shallow deposits, fine grains, and darker compacted ash. All three are 2048 × 2048 and include albedo, +Y tangent normal, unsigned 16-bit height, AO, roughness, and Unity metallic/smoothness maps. Metallic is zero; alpha is 1 − roughness.

The albedo maps contain material color variation only, with no directional lighting. AO is a procedural cavity estimate, not a raytraced bake. Relit diagnostic previews are separate images in `Previews/` and must not be used as albedo.

T4 is a nonseamless left boot impression and its correct mirrored right version, 256 × 512, with a 14 × 32 cm canvas, 2 cm maximum depression, approximately 4.5 mm rim, and disturbed ejecta. The albedo alpha is the common decal mask. Normal X is inverted on the right version, in addition to horizontal mirroring. Decal normal and height need the albedo alpha mask when composited over ground; neither is independently transparent.

Use sRGB import for albedo only. All other maps are linear data. Import normal files as normal maps with green-channel flipping disabled. Ground textures use Repeat wrapping; footprints use Clamp. Height decoding is `minimum + sample * (maximum − minimum)` in metres. Ranges are T1 −0.002 to +0.002 m, T2 −0.003 to +0.024 m, T3 −0.013 to +0.006 m, and T4 −0.020 to +0.006 m. Ground zero is not necessarily sample 0.5. Exact ranges and achieved values are also in `texture-validation.json`.

The periodic fields are sampled over 2047 intervals and duplicate the boundary texel on both axes. Wrapped central derivatives retain matching normal boundaries. This trades one repeated texel row/column for exact encoded edge identity. The validation reads/decompresses the exported PNG bytes, checks boundary identity on every ground map, normal lengths and positive Z, 16-bit height precision, map sizes, packed channels, ripple frequency, and footprint mirrors.

Visual QA inspected all material swatch/normal sheets, the ripple 2 × 2 repetition, and both footprint diagnostics. Unity runtime appearance still depends on the consuming shaders, normal strength, displacement and decal blending, and lighting; those are outside this generator.
