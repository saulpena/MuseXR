# SplatPrune

A bare scene for checking splat worlds against each other in the Editor: the threshold
conservatory at `worldScale` 1.7 and a `PruneCam` at the intro's eye point (0, 1.6, 0), 90 degree
FOV, far clip 400 m, black background.

Used to compare an original world with its pruned version, SH order 3 against 0, and the splat
layer at different resolutions (with an opaque cube to check occlusion).

**Editor captures misplace the splat layer**: splats land in the bottom-left fraction of the frame
equal to URP renderScale (0.8), while meshes and text fill it. Compare splat against splat inside
that region only, and judge framing from headset captures. See `PERFORMANCE.md`.
