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

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length > maxBytes || stream.Length > int.MaxValue) return null;
                    var bytes = new byte[(int)stream.Length];
                    var count = 0;
                    while (count < bytes.Length)
                    {
                        var read = stream.Read(bytes, count, bytes.Length - count);
                        if (read == 0) return null;
                        count += read;
                    }
                    return stream.ReadByte() == -1 ? bytes : null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
