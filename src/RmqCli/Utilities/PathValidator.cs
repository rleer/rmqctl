namespace RmqCli.Utilities;

public static class PathValidator
{
    public static bool IsValidFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Check for invalid characters
        char[] invalidChars = Path.GetInvalidPathChars();
        if (path.Any(c => invalidChars.Contains(c)))
            return false;

        try
        {
            // Normalize the path (absolute or relative)
            var normalized = Path.GetFullPath(path, Environment.CurrentDirectory);

            // Make sure it has a file name part
            var fileName = Path.GetFileName(normalized);
            if (string.IsNullOrEmpty(fileName))
                return false;

            // Disallow reserved names on Windows
            if (OperatingSystem.IsWindows())
            {
                var reservedNames = new[]
                {
                    "CON", "PRN", "AUX", "NUL",
                    "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
                };

                if (reservedNames.Contains(fileName.ToUpperInvariant()))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}