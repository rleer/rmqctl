namespace rmqctl.Utilities;

public static class TextFormatters
{
    public static string EscapeMarkup(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
    }
}