# Volcanic Ground Textures Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate and validate the T1, T2, T3, and T5 seamless volcanic-ground albedo, 16-bit height, and OpenGL normal maps required by the approved specification.

**Architecture:** Extend the existing deterministic NumPy generator and its independent PNG acceptance test. Material builders return albedo and physical height fields; one export path writes the requested maps, records metrics, and creates diagnostics. Unity import metadata remains asset-local and preserves existing GUIDs.

**Tech Stack:** Blender 4.5 bundled Python, NumPy, custom PNG encoder/decoder, PowerShell build wrapper, Unity texture importer metadata.

---

### Task 1: Acceptance tests for the new contract

**Files:**
- Modify: `ArtSource/Field/test_textures.py`

- [ ] **Step 1: Replace the legacy six-map assumptions with an explicit material contract**

```python
MATERIALS = {
    'T1_Ash': ((2048, 2048), ['Albedo', 'Height', 'Normal']),
    'T2_Ripples': ((2048, 2048), ['Albedo', 'Height', 'Normal']),
    'T3_Crust': ((2048, 2048), ['Albedo', 'Height', 'Normal']),
    'T5_AshMicro': ((1024, 1024), ['Albedo', 'Height', 'Normal']),
}
```

Add assertions for RGB 8-bit albedo/normal, grayscale 16-bit height, exact boundary identity, target mean colors, T1/T5 luminance statistics, quadrant/block uniformity, height mean, normal validity, T2 pitch/orientation, and T3 crack statistics using metrics recorded by the generator.

- [ ] **Step 2: Run the acceptance test and verify that the missing T5 contract fails**

Run:

```powershell
& 'D:\Max\Tools\Blender\blender-4.5.9-windows-x64\4.5\python\bin\python.exe' ArtSource/Field/test_textures.py
```

Expected: FAIL because `T5_AshMicro_Albedo.png` and the new validation fields do not exist.

- [ ] **Step 3: Commit the red test**

```powershell
git add -- ArtSource/Field/test_textures.py
git commit -m "Проверять контракт текстур вулканической земли"
```

### Task 2: Shared deterministic generation and validation helpers

**Files:**
- Modify: `ArtSource/Field/textures.py`

- [ ] **Step 1: Add periodic filtering, sparse-grain, normalization, and metric helpers**

Implement focused helpers with these interfaces:

Use FFT convolution for `periodic_box_mean(field, radius)`, subtract it in `high_pass`, and rescale the zero-mean result in `normalize_mean_std`. `periodic_grains(n, count, radius_px, seed)` places one jittered centre per toroidal grid cell and stamps a smooth circular kernel with wrapped indices. `material_metrics(albedo, height01, normal, tile_m, block_m)` returns JSON-safe scalar measurements. `save_ground(prefix, albedo, height01, tile_m, height_mm, report)` converts height to metres, computes normals, exports the three maps, records metrics, and writes the diagnostic sheets.

`periodic_grains` uses a jittered toroidal grid rather than unconstrained random points, preventing clusters and empty regions. `save_ground` writes only Albedo, Height, and Normal, derives normal from decoded physical height, and records measured means, standard deviations, quadrant deviation, block deviation, height mean, seam error, and normal error.

- [ ] **Step 2: Run a syntax/import check**

Run:

```powershell
& 'D:\Max\Tools\Blender\blender-4.5.9-windows-x64\4.5\python\bin\python.exe' -m py_compile ArtSource/Field/textures.py
```

Expected: PASS with no output.

- [ ] **Step 3: Commit the shared generator infrastructure**

```powershell
git add -- ArtSource/Field/textures.py
git commit -m "Добавить основу генерации бесшовных карт земли"
```

### Task 3: T1 and T5 ash materials

**Files:**
- Modify: `ArtSource/Field/textures.py`

- [ ] **Step 1: Implement ash builders with independent color and height channels**

Add:

Implement `build_ash(n, tile_m, seed, micro=False)` to return `(albedo_rgb, height01, masks)`. It combines periodic fine and medium fields after subtracting a periodic mean at 10% of the tile, stamps independent jittered masks for slag and pumice, and builds height from separately seeded clump, pit, and isotropic ripple fields.

Use target albedo mean `[184, 177, 167] / 255`, T1 pixel luminance standard deviation 6–10% of its mean, dark coverage 0.5–1%, light coverage near 1%, and block/quadrant deviations within the specification. Use physical ranges 6 mm for T1 and 3 mm for T5, both centered near encoded 0.5.

- [ ] **Step 2: Generate T1 and T5 and run their focused checks**

Run:

```powershell
./ArtSource/Field/build.ps1 -Stage Textures
```

Expected at this intermediate stage: T1 and T5 assertions pass; later T2/T3 assertions may still fail.

- [ ] **Step 3: Commit ash generation and outputs**

```powershell
git add -- ArtSource/Field/textures.py ArtSource/Field/texture-validation.json ArtSource/Field/Previews/T1_Ash_* ArtSource/Field/Previews/T5_AshMicro_* Assets/Art/Field/Textures/T1_Ash_Albedo.png Assets/Art/Field/Textures/T1_Ash_Height.png Assets/Art/Field/Textures/T1_Ash_Normal.png Assets/Art/Field/Textures/T5_AshMicro_Albedo.png Assets/Art/Field/Textures/T5_AshMicro_Height.png Assets/Art/Field/Textures/T5_AshMicro_Normal.png
git commit -m "Сгенерировать рыхлый и крупный вулканический пепел"
```

### Task 4: T2 asymmetric wind ripples

**Files:**
- Modify: `ArtSource/Field/textures.py`

- [ ] **Step 1: Replace cosine ridges with a periodic asymmetric profile**

Add:

```python
def asymmetric_ripple(phase):
    p = phase - np.floor(phase)
    windward = np.clip(p / .70, 0, 1)
    lee = np.clip((1 - p) / .30, 0, 1)
    return np.where(p < .70, windward * windward * (3 - 2 * windward),
                    lee * lee * (3 - 2 * lee))
```

Integrate a periodic phase field with 40 nominal cycles over 4 m, ±15% slow pitch modulation, 1–3 cm meander, local attenuation, terminating segments, and sparse split branches. Encode a 12 mm height range and optional albedo with crests 3–5% darker than troughs.

- [ ] **Step 2: Generate and verify T2**

Run the texture stage and assert dominant U pitch 8–12 cm, substantially lower dominant V power, valid height range, and exact seams.

- [ ] **Step 3: Commit T2 generation and outputs**

```powershell
git add -- ArtSource/Field/textures.py ArtSource/Field/texture-validation.json ArtSource/Field/Previews/T2_Ripples_* Assets/Art/Field/Textures/T2_Ripples_Albedo.png Assets/Art/Field/Textures/T2_Ripples_Height.png Assets/Art/Field/Textures/T2_Ripples_Normal.png
git commit -m "Сгенерировать асимметричную ветровую рябь"
```

### Task 5: T3 irregular cracked crust

**Files:**
- Modify: `ArtSource/Field/textures.py`

- [ ] **Step 1: Implement the crust builder**

Add:

Implement `build_crust(n, seed)` to return `(albedo_rgb, height01, masks)`. It evaluates a periodic warped variable-density Voronoi base, adds seeded finite line segments clipped at primary boundaries for T-junctions, converts distance fields into 1–3 px cracks with 3–8 mm depressions and 1 mm lips, limits per-plate albedo offsets to ±2%, and masks sparse light crack deposits.

The builder records cell-size, crack-width, crack-depth, plate-tone, and junction metrics. Normalize encoded height to 0..12 mm with mean near 0.5 while preserving requested physical relative depth.

- [ ] **Step 2: Generate and verify T3**

Run the texture stage and verify seams, mean color `[140,133,123] ± 4`, neighboring plate tone spread at most 4%, and recorded crack dimensions.

- [ ] **Step 3: Commit T3 generation and outputs**

```powershell
git add -- ArtSource/Field/textures.py ArtSource/Field/texture-validation.json ArtSource/Field/Previews/T3_Crust_* Assets/Art/Field/Textures/T3_Crust_Albedo.png Assets/Art/Field/Textures/T3_Crust_Height.png Assets/Art/Field/Textures/T3_Crust_Normal.png
git commit -m "Сгенерировать корку вулканического пепла"
```

### Task 6: Unity metadata, documentation, and full verification

**Files:**
- Create: `Assets/Art/Field/Textures/T5_AshMicro_Albedo.png.meta`
- Create: `Assets/Art/Field/Textures/T5_AshMicro_Height.png.meta`
- Create: `Assets/Art/Field/Textures/T5_AshMicro_Normal.png.meta`
- Modify: `ArtSource/Field/TEXTURES.md`
- Modify: `ArtSource/Field/README.md`
- Modify: `ArtSource/Field/test_textures.py`

- [ ] **Step 1: Add T5 Unity importer metadata**

Copy the established T1 importer structure with new GUIDs. Set Albedo `sRGBTexture: 1`, Height/Normal `sRGBTexture: 0`, Normal `textureType: 1`, Repeat wrapping, mipmaps enabled, and 1024 mobile limits.

- [ ] **Step 2: Document exact ranges and outputs**

Update `TEXTURES.md` with T1 0..6 mm, T2 0..12 mm, T3 0..12 mm, and T5 0..3 mm decoding. State that AO/roughness/packed maps are legacy files outside this delivery and that albedo contains no lighting.

- [ ] **Step 3: Run the texture build and independent acceptance test**

Run:

```powershell
./ArtSource/Field/build.ps1 -Stage Textures
& 'D:\Max\Tools\Blender\blender-4.5.9-windows-x64\4.5\python\bin\python.exe' ArtSource/Field/test_textures.py
```

Expected: generation completes and the test prints a PASS line covering T1, T2, T3, and T5.

- [ ] **Step 4: Inspect the generated report and repository diff**

Run:

```powershell
Get-Content -Raw ArtSource/Field/texture-validation.json
git diff --check
git status --short
```

Expected: all recorded metrics meet the approved specification; no whitespace errors; unrelated pre-existing user changes remain untouched.

- [ ] **Step 5: Commit the final metadata and documentation**

```powershell
git add -- ArtSource/Field/TEXTURES.md ArtSource/Field/README.md ArtSource/Field/test_textures.py Assets/Art/Field/Textures/T5_AshMicro_*.meta
git commit -m "Завершить поставку текстур вулканической земли"
```
