using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

[Collection("Toast position settings")]
public class UiLocalizationTests
{
    [Theory]
    [InlineData(null, "en")]
    [InlineData("auto", "en")]
    [InlineData("ja", "en")]
    [InlineData("zh", "en")]
    [InlineData("zh-TW", "en")]
    [InlineData(" vi ", "vi")]
    [InlineData("ZH-cn", "zh-CN")]
    public void ResolverKeepsTheShippedLocaleSet(string raw, string expected)
        => Assert.Equal(expected, LocaleResolver.Resolve(raw));

    [Fact]
    public void ExistingEnglishFacadeWorksBeforePluginStartup()
        => Assert.Equal("BIMwright Settings", L.T("settings.title"));

    [Theory]
    [InlineData("en", "BIMwright Settings", "1,234")]
    [InlineData("vi", "Cài đặt BIMwright", "1.234")]
    [InlineData("zh-CN", "BIMwright 设置", "1,234")]
    public void EmbeddedCatalogsHaveIdenticalKeysAndPlaceholderTokens(string locale, string title, string count)
    {
        var english = EmbeddedCatalog.Load("en");
        var translated = EmbeddedCatalog.Load(locale);
        Assert.NotNull(translated);
        Assert.Equal(english.Keys.OrderBy(k => k, StringComparer.Ordinal), translated.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var key in english.Keys)
            Assert.Equal(Tokens(english[key]), Tokens(translated[key]));

        var table = StringTable.Load(locale, EmbeddedCatalog.Load, null);
        Assert.Equal(title, table.Text("settings.title"));
        Assert.Contains(count, table.Text("toast.generic.results", ("count", 1234)));
    }

    [Fact]
    public void CatalogFallbackAndFormattingDoNotUseProcessCultureOrReplaceInsertedText()
    {
        var english = new Dictionary<string, string> { ["test"] = "{name}|{count:n}|{count}|{missing}|{name}|{nil}" };
        var notes = new List<string>();
        var table = StringTable.Load("vi", code => code == "en" ? english : new Dictionary<string, string>(), notes.Add);
        var current = CultureInfo.CurrentCulture;
        Assert.Equal("{count:n}|1.234|1234|{missing}|{count:n}|", table.Text("test", ("name", "{count:n}"), ("count", 1234), ("nil", null)));
        Assert.Same(current, CultureInfo.CurrentCulture);
        Assert.Equal("unknown.key", table.Text("unknown.key"));
        Assert.Equal(string.Empty, table.Text(null));
        Assert.Empty(notes);
        Assert.Equal("en", StringTable.Load("vi", code => code == "en" ? english : null, notes.Add).Locale);
        Assert.Single(notes);
        Assert.Equal("unknown.key", StringTable.Load("en", _ => null, notes.Add).Text("unknown.key"));
        Assert.Equal(2, notes.Count);
    }

    [Theory]
    [InlineData("{\"x\":\"a\",\"x\":\"b\"}")]
    [InlineData("{\"x\":1}")]
    [InlineData("{\"x\":\" \"}")]
    [InlineData("{\"_meta\":{\"locale\":\"en\"}}")]
    [InlineData("{\"x\":\"a\"}{}")]
    [InlineData("{")]
    public void LoaderRejectsInvalidCatalogs(string json)
        => Assert.ThrowsAny<JsonException>(() => EmbeddedCatalog.Parse(json, "vi"));

    [Fact]
    public void LanguageSettingsPreserveOtherKeysAndRejectMalformedFiles()
    {
        var path = Path.GetTempFileName();
        var original = PluginSettings.FilePathOverride;
        try
        {
            PluginSettings.FilePathOverride = path;
            File.WriteAllText(path, "{\"toastIdleSeconds\":30,\"uiLanguage\":3}");
            Assert.Equal("en", PluginSettings.LoadUiLanguage());
            Assert.True(PluginSettings.SaveUiLanguage(" ZH-cn "));
            Assert.Equal("zh-CN", PluginSettings.LoadUiLanguage());
            Assert.Equal(30, JObject.Parse(File.ReadAllText(path)).Value<int>("toastIdleSeconds"));
            Assert.Equal("en", L.Locale);
            File.WriteAllText(path, "{broken");
            Assert.Equal("en", PluginSettings.LoadUiLanguage());
            Assert.False(PluginSettings.SaveUiLanguage("vi"));
            Assert.Equal("{broken", File.ReadAllText(path));
        }
        finally { PluginSettings.FilePathOverride = original; File.Delete(path); }
    }

    private static string[] Tokens(string text) => StringTable.Placeholder.Matches(text).Cast<System.Text.RegularExpressions.Match>()
        .Select(m => m.Value).Distinct().OrderBy(token => token, StringComparer.Ordinal).ToArray();
}
