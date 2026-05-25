namespace Bimwright.Dwg.Plugin
{
    public static class McpResponsePrivacy
    {
        public static object FilterResult(string commandName, object result)
            => ResponseSizeGuard.ApplyResult(result);

        public static string SanitizeError(string error)
            => ErrorSanitizer.Sanitize(error);
    }
}
