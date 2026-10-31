# 01: Pose pipeline (spike S1)

Status: DRAFT. Code is written; the measurements are pending the user's run on the Windows desktop.

## The question

Can head pose from a webcam (Logitech C922) tell the user's two monitors apart, on Windows, with a bundled landmark
model run locally? This is the W1 go/no-go gate (PLAN.md section 6, risk 1). Targets: separates clearly means
held-out nearest-centroid accuracy of 95% or more and d-prime of 3 or more for every pair of monitors; marginal means
85% or more.

## The pipeline under test

1. Camera: Windows.Media.Capture `MediaFrameReader`, about 640x480, frames dropped after use (never saved).
2. First face box: `Windows.Media.FaceAnalysis.FaceDetector` on a Gray8 copy. After that the box follows the previous
   frame's landmarks (bounding box, squared, enlarged 1.5x, as MediaPipe does) and the detector runs again only when
   the face-presence score falls below 0.5 or the landmarks are unusable.
3. Landmarks: the ONNX model below, run with ONNX Runtime 1.20.1 (CPU).
4. Pose: `Gander.Core.PoseGeometry` turns landmarks into yaw, pitch, roll. Head frame from the outer eye corners
   (33, 263) and the nose-bridge-to-chin line (168, 152); the nose tip (1) only rejects a flipped fit. Yaw is positive
   toward the user's right, pitch positive looking up, roll positive right ear toward right shoulder, for an unmirrored
   image. Unit tested with synthetic faces rotated by known angles.
5. Gate: `Gander.Core.SeparabilityAnalyzer` (port of the Mac gate maths): per-monitor mean and standard deviation, pairwise
   d-prime along the line between centers, leave-one-round-out nearest-centroid accuracy, verdict.

Run it with `GanderTools` option 3: 3a live readout, 3b gate run, 3c describe the model (no camera, for diagnosing a failed run), 3d cold-start test (how fast a far-turned face is
found from no face). Output: `gander-pose.txt` (report) and `gander-pose-trace.json` (numbers only) next to the exe.

## The model chosen

| Item | Value |
|---|---|
| What | Google MediaPipe Face Landmarker (Face Mesh V2), converted to ONNX with tf2onnx |
| Repository | https://huggingface.co/fernandotonon/QtMeshEditor-facemesh-onnx |
| Pinned URL | https://huggingface.co/fernandotonon/QtMeshEditor-facemesh-onnx/resolve/32714088ed9830a98df6ee653ad406a3b83ef014/face_landmarks.onnx |
| Commit | 32714088ed9830a98df6ee653ad406a3b83ef014 (repository last modified 2026-07-13) |
| SHA-256 | d16e5a55e6a480284d468ee32469692464049518c311662ab3956681de31e3e9 |
| Size | 4,920,979 bytes (about 4.7 MB) |
| Fetch | `scripts/fetch-pose-model.sh` (verifies the SHA-256; the file is git-ignored and never committed) |

### Input and output shapes (read with onnxruntime, opset 18, producer tf2onnx)

| Tensor | Name | Shape | Meaning |
|---|---|---|---|
| Input | `input_12` | `[N, 256, 256, 3]` float32 | NHWC, RGB, scaled to [0, 1], the cropped face |
| Output 0 | `Identity` | `[N, 1, 1, 1434]` float32 | 478 landmarks x (x, y, z) in 256-pixel crop space |
| Output 1 | `Identity_1` | `[N, 1, 1, 1]` float32 | face-presence logit (apply sigmoid) |
| Output 2 | `Identity_2` | `[N, 1]` float32 | unused auxiliary output |

A random-noise input returned a presence logit of -10.5 (no face), as expected. The code does not hard-code any of
these names or shapes: it reads them from the session at startup, prints them, and picks the landmark output (the one
with at least 468 x 3 values) and the first single-value output after it as the presence score.

The model card and the conversion notes (`docs/MOCAP_SPIKE.md` and `scripts/export-facecap-onnx.py` in
fernandotonon/QtMeshEditor) say the upstream pipeline rotates the crop so the eye line is level (using corners 33 to 263)
before running the model. This spike does not rotate the crop, so large head roll may reduce landmark quality. Roll is
small when reading monitors, but if the results are poor this is the first thing to add.

## License evidence, and what is still to verify

- The Hugging Face model card (front matter `license: apache-2.0`, and the text "Apache-2.0 ... Google MediaPipe model,
  Apache-2.0 code AND weights") states Apache-2.0.
- The conversion script in the same author's GitHub repository states the same ("All five models are Google MediaPipe
  models, Apache-2.0 (code AND weights)").
- The upstream MediaPipe project is Apache-2.0.

**Resolved 2026-10-31.** Google's own model card for MediaPipe Face Mesh V2 (PDF linked from the Face Landmarker guide at
developers.google.com/edge/mediapipe/solutions/vision/face_landmarker; authors Geng Yan and Ivan Grishchenko, model date
September 15, 2022) says on its first page: "LICENSED UNDER Apache License, Version 2.0". So the primary source for the
weights is Apache-2.0, which allows redistribution with the license text and attribution (both are in
THIRD-PARTY-NOTICES.md and ship in the zip). Remaining caveats: the ONNX file is a third party's conversion (its weights
were not byte-compared with Google's file), no NOTICE file was found, and the model card lists out-of-scope uses (not for
life-critical decisions; no facial recognition) and says the model is meant for tracking mode with face location, scale and
rotation roughly right, which matches the face-loss behavior seen at large head turns.

## Results: first run on the user's desktop (2026-10-30)

C922, monitor 1 left (1920x1080), monitor 2 right (2560x1440), 3b gate run, numbers read from `gander-pose-trace.json`
(about 16 frames per second, roughly 62 ms between frames).

| Monitor | Round | Yaw (degrees) | Pitch (degrees) | Spread (std) |
|---|---|---|---|---|
| 1 (left) | 1 | no face found in any frame | | |
| 1 (left) | 2 | about -52.3 | about +8.0 | about 1.1 yaw, 0.9 pitch |
| 2 (right) | 1 | about -10.1 (after settling) | about -11.5 | about 0.4 yaw, 1.0 pitch |
| 2 (right) | 2 | about -18.0 | about -14.6 | about 0.3 yaw, 0.5 pitch |

- **The signal is strong.** Looking at the left monitor sits about 34 to 42 degrees of yaw away from the right
  monitor, with each look holding within about 1 degree. The sign matches CONTRACT: turning toward the user's left is
  negative yaw. This is comparable to the Mac gate (displays 20 to 60 degrees apart).
- **The right monitor drifted between rounds:** yaw -10 then -18 (8 degrees), pitch -11.5 then -14.6 (3 degrees), the
  same kind of posture drift the Mac gate showed. With the monitors this far apart it does not threaten
  separation, but it is why calibration needs a margin and why recalibration must be easy.
- **Cold start failed at the left monitor.** In round 1 the very first look was at the left monitor, about 50 degrees
  off the camera, and the Windows face detector found no face in any frame (every sample `face: false`, which also
  left the program without round 1 data for monitor 1). In round 2 the face was already being tracked from the previous
  look, so the same pose worked. The landmark model handles the pose; the first detection does not. Tracking through a
  large turn works. Re-acquiring a face already at 50 degrees does not.
- **Pose zero is not at the camera.** Both monitors read negative yaw and negative pitch, so the camera is not centered
  in front of the user. Expected for a monitor-mounted webcam; calibration absorbs it.

**Verdict.** The two displays separate clearly by a wide margin once a face is found, so head pose passes the gate on
this setup. One defect remains to fix before it is a daily driver: re-acquiring the face when it has been lost while
the head is far turned.

Not yet measured: the tool's own per-run report (d-prime, held-out accuracy, CPU, latency) was not pasted, live readout
(3a) behavior, and CPU use.

## What it changes in SPEC.md

Nothing yet. If the gate passes, a decision records the model, the pose method, and the measured separation. If it does
not, options are: rotate the crop to level the eyes, add the iris landmarks (468 to 477) for gaze, try a different model
conversion, or move the camera (PLAN.md risk 4).
