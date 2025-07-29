namespace RmqCli.Utilities;

public static class OutputUtilities
{
    public static string ToSizeString(double l)
    {
        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;
        double size = l;
        switch (l)
        {
            case >= gb:
                size = Math.Round(l / gb, 2);
                return $"{size} GB";
            case >= mb:
                size = Math.Round(l / mb, 2);
                return $"{size} MB";
            case >= kb:
                size = Math.Round(l / kb, 2);
                return $"{size} KB";
            default:
                return $"{size} bytes";
        }
    }

    public static int GetDigitCount(int number)
    {
        number = Math.Abs(number);
        if (number < 10) return 1;
        if (number < 100) return 2;
        if (number < 1_000) return 3;
        if (number < 10_000) return 4;
        if (number < 100_000) return 5;
        if (number < 1_000_000) return 6;
        if (number < 10_000_000) return 7;
        if (number < 100_000_000) return 8;
        if (number < 1_000_000_000) return 9;

        return 10;
    } 
}