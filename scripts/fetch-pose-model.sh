#!/usr/bin/env bash
# Downloads the face-landmark model used by the pose spike (docs/decisions/01-pose-pipeline.md) to
# src/Sway.Platform.Windows/models/face_landmark.onnx and checks its SHA-256. Development and CI time only: the shipped program
# contains no network code and the model is bundled, never fetched by Gander itself.
# Skips the download when the file is already present and correct; fails loudly on any mismatch.
set -euo pipefail
cd "$(dirname "$0")/.."

# MediaPipe Face Mesh V2 landmarks (478 points), converted to ONNX. Apache-2.0 per the repository's model card.
# The URL is pinned to a commit, so the content cannot change underneath us.
COMMIT="32714088ed9830a98df6ee653ad406a3b83ef014"
URL="https://huggingface.co/fernandotonon/QtMeshEditor-facemesh-onnx/resolve/${COMMIT}/face_landmarks.onnx"
# Keep in step with ModelIntegrity.PinnedSha256 in src/Sway.Core/ModelIntegrity.cs (scripts/privacy-guard.sh checks it).
SHA256="d16e5a55e6a480284d468ee32469692464049518c311662ab3956681de31e3e9"
DEST="src/Sway.Platform.Windows/models/face_landmark.onnx"

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

if [[ -f "$DEST" && "$(sha256_of "$DEST")" == "$SHA256" ]]; then
  echo "Face model already present and verified: $DEST"
  exit 0
fi

mkdir -p "$(dirname "$DEST")"
tmp="$(mktemp "${DEST}.XXXXXX")"
trap 'rm -f "$tmp"' EXIT

echo "Downloading the face model from $URL"
curl --proto '=https' --proto-redir '=https' --tlsv1.2 --fail --location --silent --show-error --retry 3 --output "$tmp" "$URL"

actual="$(sha256_of "$tmp")"
if [[ "$actual" != "$SHA256" ]]; then
  echo "ERROR: SHA-256 mismatch for the downloaded face model." >&2
  echo "  expected: $SHA256" >&2
  echo "  actual:   $actual" >&2
  exit 1
fi

mv "$tmp" "$DEST"
trap - EXIT
echo "Face model saved and verified: $DEST"
