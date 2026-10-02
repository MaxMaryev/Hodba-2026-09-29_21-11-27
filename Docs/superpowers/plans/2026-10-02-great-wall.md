# Great Wall implementation plan

Goal: Bring the approved desert-wall concept into the playable Field as real geometry, 500 metres tall (updated by user request).

Architecture: A fixed wall at world X=1,800 m runs north/south. Stream a bounded set of 256 m bays around the walker; each bay shares a low polygon mesh with massive buttresses. A dedicated masonry shader uses the existing desert lighting and height fog. Authoritative movement and offline walking use the same wall obstacle query, independent of Unity physics and floating origin. The proving ground stays available for body tuning.

- [x] Add regression coverage for crossing, sliding, offline movement and origin shifts.
- [x] Add the obstacle query and wall layout, then connect movement and background walking.
- [x] Add pooled bay geometry, masonry material/shader and Bootstrap lifecycle integration.
- [x] Import and compile in Unity, run EditMode tests, inspect the wall from the game camera and save a preview.

The existing concept image and the user's request to add it to the game authorize this design. Preserve all unrelated work already present in the checkout.

Validation: Unity imported and compiled the scripts and shader. All 7 wall tests passed. Full EditMode suite (before the additional cadence test): 83/85 passed; Rhythm_IsContinuousAcrossStepBoundaries and Places_HaveCharacter failed in the independent gait/ground tests. Runtime verified 193 shared-mesh bays, 24 km far clip and a daylight game-camera preview at ArtSource/Field/Previews/GreatWall_Game.png. No Android device performance measurement was performed.


## Lighting refinement, 2026-10-02

The wall now supplies its local bounds and periodic pier layout to the common atmosphere shader. Direct-light rays are tested against the wall on the ground, rocks, footprints, wall faces and dust, including distances beyond URP shadow cascades. A local sky-occlusion approximation reduces ambient irradiance near the structure and cools shaded ground bounce. Four density-weighted samples along each visible fog segment estimate its sunlight exposure; extinction remains unchanged while shaded scattering becomes dimmer and cooler.

Validation: 4/4 GPU lighting probes passed after reproducing 3 failures before implementation. Full EditMode suite: 92/94 passed; the same gait-rhythm and ground-ripple tests remain failing. All affected shaders compiled in the editor. Matched game-render previews were captured with the same sun direction and exposure: ArtSource/Field/Previews/WallLighting_Shadow.png and WallLighting_Sunlit.png. Initial Edit Mode was restored. This uses analytic occlusion and approximated scattering/ambient light, not full volumetric GI; mobile performance has not been measured.

## Continuous fog and solar-disc shadows, 2026-10-03

Replace the four fog samples with a clipped view-ray interval in the main panel's convex shadow volume and an exact integral of the clamped exponential height density. The same integral supplies fog extinction, including view rays crossing the density floor. Airborne pier shadows are deliberately omitted; fog uses the central solar direction.

Direct wall visibility now uses a circular solar disc with a 0.27 degree angular radius. Panel edge coverage uses circle-segment area; finite pier side and top coverage uses a product approximation near silhouette corners. The shared function also softens dust, sand veil and ambient ground bounce. The coping's 4 m overhang remains outside the analytic silhouette.

Keep wall renderers' ShadowCastingMode.On and existing 35 m URP shadow distance, as explicitly requested: standard URP Lit traveller materials still receive wall shadows. Within these near-camera cascades, multiplying the URP shadow and analytic visibility can retain a harder edge; this limitation is accepted rather than disabling player shading.

Validation: five new GPU tests initially failed on the old implementation. The final focused suite covers fog continuity, exact density weighting (including the floor), axis-parallel rays, solar-disc edge coverage, penumbra growth, partial narrow-pier occlusion, ambient continuity, floating-origin stability, wall movement and sunlight occlusion. Controlled URP renders at 14 and 5 degrees were saved to Logs/WallSoft_Face.png and Logs/WallSoft_ShadowEnd.png. Wall, ground, dust and veil shaders compiled without warnings after cleanup. Android performance has not been measured.
