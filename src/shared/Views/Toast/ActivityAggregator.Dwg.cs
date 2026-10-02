using System;
namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public sealed partial class ActivityAggregator
    {
        // Preserve the AutoCAD completion adapter's explicit capture count, even without a thumbnail.
        public bool RecordResult(string title, string body, bool success, bool hasImage,
            bool frameUsable, string imagePath = null, long? durationMs = null)
        {
            lock (_gate)
            {
                var render = RecordResult(title, body, success, imagePath, frameUsable, durationMs);
                if (success && hasImage && string.IsNullOrEmpty(imagePath)) _images++;
                if (success && !hasImage && !string.IsNullOrEmpty(imagePath)) _images--;
                return render;
            }
        }
    }
}
