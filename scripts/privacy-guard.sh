#!/usr/bin/env bash
# Privacy and behavior guard (PLAN.md section 5): fails if networking, image or video persistence, cursor movement,
# input-event synthesis or capture, or other forbidden calls appear in shipped code. Scans src/ (tests excluded),
# which includes the spike tools.
# Escape hatch for a reviewed false positive: append the comment `privacy-guard:allow` to that line.
set -euo pipefail
cd "$(dirname "$0")/.."

PATTERNS=(
  # Networking
  'System\.Net\.Http' 'HttpClient' 'System\.Net\.Sockets' 'TcpClient' 'UdpClient' 'TcpListener' 'new Socket\('
  'WebClient' 'WebRequest' 'HttpWebRequest' 'ClientWebSocket' 'WebSocket' 'System\.Net\.NetworkInformation'
  'Dns\.GetHost' 'WinHttp' 'WinINet' 'URLDownloadToFile' 'Windows\.Networking' 'Windows\.Web\.Http'
  'WebView2' 'System\.Windows\.Controls\.WebBrowser' 'Process\.Start\("http'
  # Persisting camera imagery
  'BitmapEncoder' 'PngBitmapEncoder' 'JpegBitmapEncoder' 'BmpBitmapEncoder' 'TiffBitmapEncoder' 'GifBitmapEncoder'
  'Bitmap\.Save\(' 'Image\.Save\(' 'SaveAsync\(' 'SoftwareBitmap.*Encoder' 'MediaEncodingProfile'
  'LowLagMediaRecording' 'LowLagPhotoCapture' 'StartRecordToStorageFile' 'CapturePhotoToStorageFile'
  'IMFSinkWriter' 'MFCreateSinkWriter'
  # The cursor is never moved, and Sway neither synthesizes nor captures input events
  'SetCursorPos' 'mouse_event' 'keybd_event' 'SendInput' 'SetWindowsHookEx' 'WH_KEYBOARD' 'WH_MOUSE'
  'RegisterRawInputDevices' 'GetRawInputData' 'GetAsyncKeyState' 'GetKeyboardState' 'GetKeyState' 'GetCursorPos'
  'ClipCursor' 'BlockInput'
  # Remote code and elevation
  'Assembly\.LoadFrom' 'Assembly\.Load\(' 'runas'
)

regex=$(IFS='|'; echo "${PATTERNS[*]}")
hits=$(grep -rnE --exclude-dir=obj --exclude-dir=bin --include='*.cs' --include='*.xaml' --include='*.csproj' "$regex" src 2>/dev/null | grep -v 'privacy-guard:allow' || true)

if [[ -n "$hits" ]]; then
  echo "Privacy guard FAILED. Forbidden APIs found (networking, image persistence, cursor/input events):" >&2
  echo "$hits" >&2
  exit 1
fi
echo "Privacy guard passed: no networking, image-persistence, cursor or input-event APIs in src/."
