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
4. Pose: `Sway.Core.PoseGeometry` turns landmarks into yaw, pitch, roll. Head frame from the outer eye corners
   (33, 263) and the nose-bridge-to-chin line (168, 152); the nose tip (1) only rejects a flipped fit. Yaw is positive
   toward the user's right, pitch positive looking up, roll positive right ear toward right shoulder, for an unmirrored
   image. Unit tested with synthetic faces rotated by known angles.
5. Gate: `Sway.Core.SeparabilityAnalyzer` (port of the Mac gate maths): per-monitor mean and standard deviation, pairwise
   d-prime along the line between centers, leave-one-round-out nearest-centroid accuracy, verdict.

Run it with `SwayTools` option 3: 3a live readout, 3b gate run, 3c describe the model (no camera, for diagnosing a
failed run). Output: `sway-pose.txt` (report) and `sway-pose-trace.json` (numbers only) next to the exe.

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

Doubts, honestly: the Apache-2.0 statement on the converted model is the converter's claim, repeated from the upstream
terms. I did not find a primary Google statement for this specific model's weights (the Face Landmarker "model card" is
the place to look), and a third party's license field is not proof. Other candidates were seen: `senty-au/face_landmarks_detector-ONNX`
(its API metadata says apache-2.0, but its card's license section was reported empty), and PINTO0309's model zoo
(not examined). **Before any release that ships this model, someone must read Google's Face Landmarker model card and
the MediaPipe license for these weights and confirm redistribution is allowed, and the notice text must be added to
the installer.** THIRD-PARTY-NOTICES.md lists the model with this caveat.

## Results: pending the user's run

To fill in after the user runs option 3b on the Windows desktop (C922, monitor 1 left 1920x1080, monitor 2 right
2560x1440):

- Verdict (separates clearly, marginal, does not separate):
- Held-out accuracy:
- Per monitor: yaw and pitch mean and standard deviation:
- Degrees between the monitor centers, and d-prime:
- Face found in % of frames:
- Latency per frame (ms), frames per second, process CPU (% of the PC and of one core):
- FaceDetector available (yes/no); camera format chosen:
- Anything odd in the live readout (sign of yaw when looking right, jitter, roll):

## What it changes in SPEC.md

Nothing yet. If the gate passes, a decision records the model, the pose method, and the measured separation. If it does
not, options are: rotate the crop to level the eyes, add the iris landmarks (468 to 477) for gaze, try a different model
conversion, or move the camera (PLAN.md risk 4).
