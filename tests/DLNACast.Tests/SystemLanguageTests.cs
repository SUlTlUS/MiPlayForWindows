using DLNACast.Core.Localization;

namespace DLNACast.Tests;

public sealed class SystemLanguageTests
{
    [Theory]
    [InlineData("zh-CN", true)]
    [InlineData("zh-Hant", true)]
    [InlineData("ZH-sg", true)]
    [InlineData("en-US", false)]
    [InlineData("ja-JP", false)]
    [InlineData(null, false)]
    public void UsesChineseOnlyForZhLanguageTags(string? languageTag, bool expected)
    {
        Assert.Equal(expected, SystemLanguage.IsChineseLanguage(languageTag));
    }

    [Fact]
    public void UsesEnglishForEveryNonChineseLanguage()
    {
        Assert.Equal("English", SystemLanguage.SelectForLanguage("de-DE", "中文", "English"));
    }
}
