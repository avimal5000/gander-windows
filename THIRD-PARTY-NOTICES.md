# Third-party notices

Sway for Windows uses the following third-party components. This file is a working list; the final notice text that
ships with the app must include each license in full.

## ONNX Runtime

- Package: Microsoft.ML.OnnxRuntime 1.20.1
- Use: runs the face-landmark model on the user's PC. No network code is used (see PLAN.md section 5).
- License: MIT, copyright Microsoft Corporation. https://github.com/microsoft/onnxruntime/blob/main/LICENSE

## Face-landmark model (MediaPipe Face Landmarker, ONNX conversion)

- Source: https://huggingface.co/fernandotonon/QtMeshEditor-facemesh-onnx, commit
  32714088ed9830a98df6ee653ad406a3b83ef014, file `face_landmarks.onnx`
  (SHA-256 d16e5a55e6a480284d468ee32469692464049518c311662ab3956681de31e3e9).
- Origin: Google MediaPipe Face Landmarker (Face Mesh V2), converted to ONNX by the repository's author.
- License as stated by the conversion's model card: Apache-2.0. https://www.apache.org/licenses/LICENSE-2.0
- **Not yet verified against Google's own terms for these weights.** See docs/decisions/01-pose-pipeline.md. Confirm
  before the model is shipped in a release, and add the full Apache-2.0 text and any NOTICE content then.
- The model file is fetched at development and CI time by `scripts/fetch-pose-model.sh`; it is not committed to git.
