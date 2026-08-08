using Windows.System.UserProfile;

namespace DLNACast.Core.Localization;

public static class SystemLanguage
{
    private static readonly string LanguageTag = ResolveLanguageTag();

    public static bool IsChinese => IsChineseLanguage(LanguageTag);

    public static string Select(string chinese, string english) =>
        IsChinese ? chinese : english;

    public static string SelectForLanguage(string? languageTag, string chinese, string english) =>
        IsChineseLanguage(languageTag) ? chinese : english;

    public static bool IsChineseLanguage(string? languageTag) =>
        !string.IsNullOrWhiteSpace(languageTag) &&
        languageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static string ResolveLanguageTag()
    {
        try
        {
            return GlobalizationPreferences.Languages.FirstOrDefault() ?? "en-US";
        }
        catch
        {
            return "en-US";
        }
    }
}
