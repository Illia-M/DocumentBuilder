public class PdfMetadata
{
    public string OutputFileName { get; set; } = $"output_{DateTimeOffset.Now.ToUnixTimeSeconds()}.pdf";
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Keywords { get; set; }
    public string? ArchiveCode { get; set; }
    public string? Subject { get; set; }
    public string? LicenseType { get; set; }

    public Dictionary<string, PdfPageMetadata> Pages { get; set; } = new();

    public PdfPageMetadata? GetImageMetadata(string fileNameOrPath)
    {
        var fileKey = fileNameOrPath.Any(c => c == Path.PathSeparator) ? Path.GetFileName(fileNameOrPath) : fileNameOrPath;

        if (Pages is not null && Pages.TryGetValue(fileKey, out var pageMetadata))
        {
            return pageMetadata;
        }

        return null;
    }
}
