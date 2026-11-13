using System;
using System.Collections.Generic;
using System.Globalization;

namespace Bimwright.Dwg.Plugin.Localization
{
    internal static class LocaleResolver
    {
        internal const string Default = "en";
        internal static IReadOnlyList<string> SupportedLocales { get; } = Array.AsReadOnly(new[] { "en", "vi", "zh-CN" });

        internal static string Resolve(string requested)
        {
            var code = requested?.Trim();
            foreach (var locale in SupportedLocales)
                if (string.Equals(code, locale, StringComparison.OrdinalIgnoreCase)) return locale;
            return Default;
        }

        internal static string NativeName(string locale) => locale switch
        {
            "vi" => "Tiếng Việt",
            "zh-CN" => "简体中文",
            _ => "English"
        };

        internal static CultureInfo Culture(string locale) => CultureInfo.GetCultureInfo(locale switch
        {
            "vi" => "vi-VN",
            "zh-CN" => "zh-CN",
            _ => "en-US"
        });
    }
}
