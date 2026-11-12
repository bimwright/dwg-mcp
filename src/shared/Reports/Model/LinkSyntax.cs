using System;
using System.Collections.Generic;

namespace Bimwright.Dwg.Plugin.Reports.Model
{
    public enum ReportParseMode
    {
        Raw,
        Stored
    }

    public sealed class LinkTarget
    {
        public bool IsValid => Reason == null;
        public string Reason { get; }
        /// <summary>AutoCAD handles: uppercase hexadecimal without leading zeros, de-duplicated in order.</summary>
        public IReadOnlyList<string> Ids { get; }
        public string DocumentToken { get; }
        public int? StoredIndex { get; }

        internal LinkTarget(string reason, IReadOnlyList<string> ids = null, string documentToken = null, int? storedIndex = null)
        {
            Reason = reason;
            Ids = ids ?? new string[0];
            DocumentToken = documentToken;
            StoredIndex = storedIndex;
        }
    }

    /// <summary>
    /// Raw: dwg:&lt;handle&gt;[,&lt;handle&gt;...][?doc=&lt;document_id&gt;]. Stored: dwg:#&lt;index&gt; into AgentReport.Links.
    /// </summary>
    public static class LinkSyntax
    {
        public const string Scheme = "dwg:";
        /// <summary>Prefix written over rejected targets so they never parse as links again.</summary>
        public const string RejectedScheme = "dwg-";
        public const int MaxHandleDigits = 16;

        public static LinkTarget Parse(string target, ReportParseMode mode = ReportParseMode.Raw, int storedLinkCount = 0)
        {
            if (target == null || !target.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
                return Invalid();
            var start = Scheme.Length;

            if (mode == ReportParseMode.Stored)
            {
                if (target.Length < start + 2 || target[start] != '#') return Invalid();
                var index = 0;
                for (var i = start + 1; i < target.Length; i++)
                {
                    var digit = target[i] - '0';
                    if (digit < 0 || digit > 9 || index > (int.MaxValue - digit) / 10) return Invalid();
                    index = index * 10 + digit;
                }
                return index < storedLinkCount ? new LinkTarget(null, storedIndex: index) : Invalid();
            }
            if (mode != ReportParseMode.Raw) return Invalid();

            var offset = start;
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                if (!ReadHandle(target, ref offset, out var handle)) return Invalid();
                if (seen.Add(handle)) ids.Add(handle);
                if (offset == target.Length || target[offset] != ',') break;
                offset++;
            } while (true);

            string documentToken = null;
            if (offset < target.Length)
            {
                if (target[offset++] != '?' || !Matches(target, offset, "doc=")) return Invalid();
                offset += 4;
                if (target.Length - offset != 32) return Invalid();
                for (var i = 0; i < 32; i++)
                    if (!IsHex(target[offset + i])) return Invalid();
                documentToken = target.Substring(offset, 32).ToLowerInvariant();
            }

            return new LinkTarget(null, ids.AsReadOnly(), documentToken);
        }

        /// <summary>One hexadecimal handle; zero (the null handle) is invalid.</summary>
        private static bool ReadHandle(string text, ref int offset, out string handle)
        {
            handle = null;
            var begin = offset;
            while (offset < text.Length && IsHex(text[offset])) offset++;
            var length = offset - begin;
            if (length == 0) return false;
            var digits = text.Substring(begin, length).TrimStart('0').ToUpperInvariant();
            if (digits.Length == 0 || digits.Length > MaxHandleDigits) return false;
            handle = digits;
            return true;
        }

        private static bool Matches(string text, int offset, string value) =>
            text.Length - offset >= value.Length && string.CompareOrdinal(text, offset, value, 0, value.Length) == 0;

        private static bool IsHex(char value) =>
            value >= '0' && value <= '9' || value >= 'a' && value <= 'f' || value >= 'A' && value <= 'F';

        private static LinkTarget Invalid() => new LinkTarget("bad_syntax");
    }
}
