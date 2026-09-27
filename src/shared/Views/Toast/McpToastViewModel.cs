namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public class McpToastViewModel
    {
        public string Title { get; set; }
        /// <summary>Primary outcome line.</summary>
        public string Summary { get; set; }
        /// <summary>Secondary context (filters, path, drawing name, etc.).</summary>
        public string Detail { get; set; }
        /// <summary>Single visible body, retaining the outcome and non-redundant context.</summary>
        public string Body
        {
            get
            {
                var summary = (Summary ?? string.Empty).Trim();
                var detail = (Detail ?? string.Empty).Trim();
                var title = (Title ?? string.Empty).Trim();
                if (string.Equals(detail, title, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(detail, summary, System.StringComparison.OrdinalIgnoreCase))
                    detail = string.Empty;

                if (summary.Length == 0) return detail;
                return detail.Length == 0 ? summary : summary + " · " + detail;
            }
        }

        /// <summary>Optional local image path for thumbnail preview (capture/export).</summary>
        public string ThumbnailPath { get; set; }
        public bool Success { get; set; }
    }
}
