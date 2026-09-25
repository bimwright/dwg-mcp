using System;
using System.IO;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public static class ToastThumbnailLoader
    {
        public static byte[] TryLoadBytes(string path, long maxBytes)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            try
            {
                if (!File.Exists(path))
                    return null;

                var info = new FileInfo(path);
                if (info.Length > maxBytes)
                    return null;

                return File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }
    }
}
