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
  'SocketsHttpHandler' 'System\.Net\.Mail' 'System\.Net\.Security' 'System\.Net\.Quic' 'new Ping\(' 'SmtpClient' 'FtpWebRequest'
  'Dns\.' 'IPAddress\.' 'WSAStartup' 'InternetOpen' 'HttpListener' 'NamedPipeClientStream'
  # Any process launch needs review (the one allowed call opens the camera privacy page and carries the allow comment)
  'Process\.Start\(' 'ShellExecute' 'CreateProcess' 'WinExec'
  # Persisting camera imagery
  'BitmapEncoder' 'PngBitmapEncoder' 'JpegBitmapEncoder' 'BmpBitmapEncoder' 'TiffBitmapEncoder' 'GifBitmapEncoder'
  'Bitmap\.Save\(' 'Image\.Save\(' 'SaveAsync\(' 'SoftwareBitmap.*Encoder' 'MediaEncodingProfile'
  'LowLagMediaRecording' 'LowLagPhotoCapture' 'StartRecordToStorageFile' 'CapturePhotoToStorageFile'
  'IMFSinkWriter' 'MFCreateSinkWriter' 'EncodeToStream' 'ImageFormat\.' 'CopyToStreamAsync' 'FileSavePicker'
  # The cursor is never moved, and Gander neither synthesizes nor captures input events
  'SetCursorPos' 'mouse_event' 'keybd_event' 'SendInput' 'SetWindowsHookEx' 'WH_KEYBOARD' 'WH_MOUSE'
  'RegisterRawInputDevices' 'GetRawInputData' 'GetAsyncKeyState' 'GetKeyboardState' 'GetKeyState' 'GetCursorPos'
  'ClipCursor' 'BlockInput' 'GetLastInputInfo' 'PostMessage.*WM_(KEY|MOUSE|CHAR)' 'SendMessage.*WM_(KEY|MOUSE|CHAR)'
  # Window text is never read (SPEC: no window titles)
  'GetWindowText' 'InternalGetWindowText' 'WM_GETTEXT'
  # Remote code and elevation
  'Assembly\.LoadFrom' 'Assembly\.Load\(' 'Assembly\.LoadFile' 'LoadFromAssemblyPath' 'NativeLibrary\.Load' 'LoadLibrary' 'runas'
)

regex=$(IFS='|'; echo "${PATTERNS[*]}")
hits=$(grep -rnE --exclude-dir=obj --exclude-dir=bin --include='*.cs' --include='*.xaml' --include='*.csproj' --include='*.props' --include='*.targets' --include='*.manifest' "$regex" src 2>/dev/null | grep -v 'privacy-guard:allow' || true)

if [[ -n "$hits" ]]; then
  echo "Privacy guard FAILED. Forbidden APIs found (networking, image persistence, cursor/input events):" >&2
  echo "$hits" >&2
  exit 1
fi

# The model is pinned twice (the fetch script and the runtime check in Sway.Core); the two must agree.
script_sha=$(sed -n 's/^SHA256="\([0-9a-f]\{64\}\)"$/\1/p' scripts/fetch-pose-model.sh)
code_sha=$(sed -n 's/.*PinnedSha256 = "\([0-9a-f]\{64\}\)";.*/\1/p' src/Sway.Core/ModelIntegrity.cs)
if [[ -z "$script_sha" || "$script_sha" != "$code_sha" ]]; then
  echo "Privacy guard FAILED. The model SHA-256 in scripts/fetch-pose-model.sh and ModelIntegrity.PinnedSha256 differ." >&2
  exit 1
fi

# ONNX Runtime telemetry must stay switched off.
if ! grep -rqF 'DisableTelemetryEvents' src --include='*.cs'; then
  echo "Privacy guard FAILED. OrtEnv.DisableTelemetryEvents() is no longer called." >&2
  exit 1
fi

echo "Privacy guard passed: no networking, image-persistence, cursor or input-event APIs in src/."
