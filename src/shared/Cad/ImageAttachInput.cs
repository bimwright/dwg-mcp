using System;
using System.IO;

namespace Bimwright.Dwg.Plugin.Cad
{
    /// <summary>Host-free validation and placement math for attach_image.</summary>
    internal static class ImageAttachInput
    {
        internal static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp" };

        // Only a fully qualified local drive path (C:\...) is accepted. UNC, device,
        // drive-relative, relative and URL forms are refused (net48 has no IsPathFullyQualified).
        internal static bool TryValidatePath(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
                return Fail("path is required", out error);
            if (path.IndexOf("://", StringComparison.Ordinal) >= 0)
                return Fail("path must be a local file path, not a URL", out error);
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
                return Fail("UNC and device paths are not allowed; copy the image to a local drive first", out error);
            if (path.Length < 3 || !IsAsciiLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
                return Fail("path must be a fully qualified local path such as C:\\images\\plan.png", out error);
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.IndexOfAny(new[] { '*', '?' }) >= 0 || path.IndexOf(':', 2) >= 0)
                return Fail("path contains characters that are not allowed in a file path", out error);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (Array.IndexOf(Extensions, ext) < 0)
                return Fail("unsupported image type '" + ext + "'; use " + string.Join(", ", Extensions), out error);
            return true;
        }

        internal static bool TryValidateDrive(DriveType driveType, out string error)
        {
            error = null;
            return driveType != DriveType.Network
                || Fail("mapped network drives are not allowed; copy the image to a local drive first", out error);
        }

        internal static bool TryValidateSize(double width, double? height, double rotationDegrees, out string error)
        {
            error = null;
            if (!IsPositiveFinite(width))
                return Fail("width must be a finite positive number", out error);
            if (height.HasValue && !IsPositiveFinite(height.Value))
                return Fail("height must be a finite positive number", out error);
            if (double.IsNaN(rotationDegrees) || double.IsInfinity(rotationDegrees))
                return Fail("rotation_degrees must be a finite number", out error);
            return true;
        }

        /// <summary>Explicit height wins; otherwise keep the pixel aspect ratio.</summary>
        internal static double ResolveHeight(double width, double? height, double pixelWidth, double pixelHeight)
        {
            if (height.HasValue) return height.Value;
            if (!IsPositiveFinite(pixelWidth) || !IsPositiveFinite(pixelHeight))
                throw new ArgumentException("pixel size must be positive");
            return width * pixelHeight / pixelWidth;
        }

        /// <summary>U runs along the image width, V along its height, both rotated counter-clockwise about the insertion point.</summary>
        internal static void Orientation(double width, double height, double rotationDegrees,
            out double ux, out double uy, out double vx, out double vy)
        {
            var radians = rotationDegrees * Math.PI / 180d;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            ux = width * cos; uy = width * sin;
            vx = -height * sin; vy = height * cos;
        }

        private static bool IsPositiveFinite(double value) => value > 0d && !double.IsInfinity(value) && !double.IsNaN(value);
        private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        private static bool Fail(string message, out string error) { error = message; return false; }
    }
}
