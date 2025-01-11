public static class TextFormatHelper
{
    public static string FormatFileSize(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        if (bytes >= GB)
            return $"{bytes / (double)GB:0.##} GB ({bytes} bytes)";
        if (bytes >= MB)
            return $"{bytes / (double)MB:0.##} MB ({bytes} bytes)";
        if (bytes >= KB)
            return $"{bytes / (double)KB:0.##} KB ({bytes} bytes)";
        return $"{bytes} Bytes";
    }
}