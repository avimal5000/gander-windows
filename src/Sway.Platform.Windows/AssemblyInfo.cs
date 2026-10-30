using System.Runtime.CompilerServices;

// The spike and diagnostic programs reuse the camera and pose pipeline classes instead of keeping copies.
[assembly: InternalsVisibleTo("SwayTools")]
[assembly: InternalsVisibleTo("Sway.Platform.Windows.Tests")]
