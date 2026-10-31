using System.Runtime.CompilerServices;

// The spike and diagnostic programs reuse the camera and pose pipeline classes instead of keeping copies.
[assembly: InternalsVisibleTo("SwayTools")]
[assembly: InternalsVisibleTo("Sway.Platform.Windows.Tests")]

// Every P/Invoke in this assembly loads a system DLL. Never search the application folder, where a planted DLL could shadow one.
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
