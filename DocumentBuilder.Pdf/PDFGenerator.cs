using ImageMagick;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SixLabors.ImageSharp;

public static class PDFGenerator
{
    public static async Task CreatePdfFromImages(string imageDirectory, string outputDirectory, PdfMetadata metadata, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(imageDirectory))
        {
            throw new ArgumentException($"'{nameof(imageDirectory)}' cannot be null or empty.", nameof(imageDirectory));
        }

        if (string.IsNullOrEmpty(outputDirectory))
        {
            throw new ArgumentException($"'{nameof(outputDirectory)}' cannot be null or empty.", nameof(outputDirectory));
        }

        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        string pdfPath = Path.Combine(outputDirectory, metadata.OutputFileName);

        if (!Directory.Exists(outputDirectory))
            throw new DirectoryNotFoundException("Output directory does not exist.");

        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".tiff", ".tif" };

        var imageFiles = Directory.GetFiles(imageDirectory)
                                  .Where(file => imageExtensions.Contains(Path.GetExtension(file).ToLower()))
                                  .OrderBy(file => Path.GetFileNameWithoutExtension(file))
                                  .ToList();

        if (!imageFiles.Any())
            throw new FileNotFoundException("No supported image files found in the specified directory.");

        try
        {
            await CreatePdfFile(metadata, pdfPath, imageFiles, cancellationToken);
        }
        catch (Exception ex)
        {
            if (File.Exists(pdfPath))
            {
                Console.WriteLine($"File {pdfPath} created, but unhandled error occurred({ex.Message}), so we delete this file...");

                File.Delete(pdfPath);
                Console.WriteLine($"File {pdfPath} deleted");
            }

            throw;
        }
    }

    private static async Task CreatePdfFile(PdfMetadata metadata, string pdfPath, List<string> imageFiles, CancellationToken cancellationToken)
    {
        using var pdfDocument = new PdfDocument();

        await BuildPdfDocument(pdfDocument, metadata, imageFiles, cancellationToken);

        pdfDocument.Save(pdfPath);
    }

    private static async Task BuildPdfDocument(PdfDocument pdfDocument, PdfMetadata metadata, List<string> imageFiles, CancellationToken cancellationToken)
    {
        pdfDocument.Options.CompressContentStreams = true;
        pdfDocument.Options.EnableCcittCompressionForBilevelImages = false;
        pdfDocument.Options.NoCompression = false;
        pdfDocument.Options.ColorMode = PdfColorMode.Rgb;
        pdfDocument.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Always;

        SetDocumentMetadata(pdfDocument, metadata);

        int imageCount = 0;
        int pagesCreated = 0;
        int pageNumber = 1;
        var tocEntries = new List<(string Title, int PageNumber)>();

        foreach (var imagePath in imageFiles.Select(imagePath => (pageNumber: pageNumber++, imagePath)))
        {
            var image = await GetPageImage(imagePath.imagePath, imagePath.pageNumber, cancellationToken);

            Interlocked.Increment(ref imageCount);

            Console.WriteLine($"[{DateTime.Now.ToShortTimeString()}] Loaded image {imageCount}/{imageFiles.Count}");

            tocEntries.Add((image.imagePath, image.pageNumber));

            bool writeTrace = true;
            var page = pdfDocument.AddPage();

            using (var xImage = XImage.FromStream(() => new MemoryStream(image.imageBytes)))
            {
                double fontSize = 12;
                double lineSpacing = fontSize + 4;
                double textHeight = 0;

                var fileName = Path.GetFileName(imagePath.imagePath);
                var fileSize = new FileInfo(imagePath.imagePath).Length;
                string formattedFileSize = TextFormatHelper.FormatFileSize(fileSize);
                var md5Hash = HashHelper.ComputeHash(image.imageBytes, "MD5");
                var sha1Hash = HashHelper.ComputeHash(image.imageBytes, "SHA1");
                DateTimeOffset? dateTaken = GetImageDateTaken(imagePath.imagePath);

                string detailsText = $"Original file name: {fileName} | Date Taken: {(dateTaken.HasValue ? dateTaken.Value : "Unknown")} | Size: {formattedFileSize} | MD5: {md5Hash} | SHA1: {sha1Hash}";

                using (var gfx = XGraphics.FromPdfPage(page))
                {
                    XFont font = new XFont("Arial", fontSize, XFontStyle.Regular);
                    double maxWidth = xImage.PointWidth - 20;

                    List<string> wrappedText = new();
                    if (writeTrace)
                    {
                        wrappedText = WrapTextToFitPage(detailsText, font, gfx, maxWidth);
                        textHeight = wrappedText.Count * lineSpacing;
                    }

                    page.Width = xImage.PointWidth;
                    page.Height = xImage.PointHeight + (writeTrace ? textHeight + 10 : 0);

                    gfx.DrawImage(xImage, 0, 0, page.Width, xImage.PointHeight);

                    if (writeTrace)
                    {
                        double currentY = xImage.PointHeight + 10;

                        XBrush backgroundBrush = XBrushes.LightGray;
                        XBrush textBrush = XBrushes.Black;

                        gfx.DrawRectangle(backgroundBrush, new XRect(0, xImage.PointHeight, xImage.PointWidth, textHeight + 10));

                        foreach (var line in wrappedText)
                        {
                            gfx.DrawString(line, font, textBrush, new XRect(10, currentY, maxWidth, lineSpacing), XStringFormats.TopLeft);

                            currentY += lineSpacing;
                        }
                    }

                    var pageMetadata = metadata.GetImageMetadata(imagePath.imagePath);

                    if (pageMetadata is not null && !string.IsNullOrEmpty(pageMetadata.HiddenTextOverlay))
                        WriteTextOverlay(gfx, pageMetadata.HiddenTextOverlay);
                }
            }

            SetPageMetadata(page, image.imagePath, image.pageNumber, metadata);

            pagesCreated++;

            Console.WriteLine($"[{DateTime.Now.ToShortTimeString()}] Added image {pagesCreated}/{imageFiles.Count}");
        }

        if (tocEntries.Any())
        {
            AddTableOfContents(pdfDocument, tocEntries, metadata);
        }
    }

    private static List<string> WrapTextToFitPage(string text, XFont font, XGraphics gfx, double maxWidth)
    {
        string delimiter = " | ";
        var lines = new List<string>();
        var words = text.Split(delimiter);

        string currentLine = "";
        foreach (var word in words)
        {
            string testLine = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine}{delimiter}{word}";
            double textWidth = gfx.MeasureString(testLine, font).Width;

            if (textWidth <= maxWidth)
            {
                currentLine = testLine;
            }
            else
            {
                if (!string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                }

                currentLine = word;
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
        {
            lines.Add(currentLine);
        }

        return lines;
    }

    private static async Task<(string imagePath, int pageNumber, byte[] imageBytes)> GetPageImage(string imagePath, int pageNumber, CancellationToken cancellationToken)
    {
        string pageTitle = Path.GetFileNameWithoutExtension(imagePath);
        byte[] imageBytes = null;

        try
        {
            using var magickImage = new MagickImage();
            await magickImage.ReadAsync(imagePath, cancellationToken);

            //CompressionType compressionType = CompressionType.None; uint quality = 75; uint colorCount = 256;
            //CompressionType compressionType = CompressionType.PngWithReducedPalette; uint quality = 75; uint colorCount = 128;
            //CompressionType compressionType = CompressionType.JpegCompression; uint quality = 75; uint colorCount = 256;
            CompressionType compressionType = CompressionType.JpegCompression; uint quality = 100; uint colorCount = 256;
            //CompressionType compressionType = CompressionType.ColorDepthReduction; uint quality = 75; uint colorCount = 128;

            using var memoryStream = new MemoryStream();

            magickImage.SetCompression(CompressionMethod.LosslessJPEG);
            // Apply the selected compression type
            switch (compressionType)
            {
                case CompressionType.JpegCompression:
                    magickImage.SetCompression(CompressionMethod.LosslessJPEG);
                    await CompressWithJpeg(magickImage, memoryStream, quality, cancellationToken);
                    break;

                case CompressionType.ColorDepthReduction:
                    await ReduceColorDepth(magickImage, memoryStream, colorCount, cancellationToken);
                    break;

                case CompressionType.PngWithReducedPalette:
                    await CompressPngWithReducedPalette(magickImage, memoryStream, colorCount, cancellationToken);
                    break;

                case CompressionType.None:
                    magickImage.Format = MagickFormat.Png;
                    magickImage.SetCompression(CompressionMethod.LosslessJPEG);
                    await magickImage.WriteAsync(memoryStream, cancellationToken);
                    break;

                default:
                    throw new ArgumentException("Invalid compression type specified.");
            }

            memoryStream.Seek(0, SeekOrigin.Begin);
            imageBytes = memoryStream.ToArray();
        }
        catch (MagickCoderErrorException ex) when (ex.Message.Contains("JPEG compression support is not configured"))
        {
            Console.WriteLine($"Error loading {imagePath}: {ex.Message}");
            throw;
        }
        catch (MagickCoderErrorException ex)
        {
            Console.WriteLine($"Problematic file: {imagePath} - {ex.Message}");
            throw;
        }

        return (imagePath, pageNumber, imageBytes);
    }

    private static void WriteTextOverlay(XGraphics gfx, string overlayText)
    {
        XFont font = new XFont("Arial", 12, XFontStyle.Regular);
        XBrush invisibleBrush = new XSolidBrush(XColor.FromArgb(0, 0, 0, 0));

        gfx.DrawString(overlayText, font, invisibleBrush, new XPoint(20, 20));
    }

    public static DateTimeOffset? GetImageDateTaken(string imageFilePath)
    {
        try
        {
            using var image = Image.Load(imageFilePath);
            var exifProfile = image.Metadata.ExifProfile;

            if (exifProfile != null)
            {
                var dateTakenValue = exifProfile.GetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.DateTimeOriginal);

                if (dateTakenValue != null && DateTimeOffset.TryParse(dateTakenValue.ToString(), out DateTimeOffset parsedDate))
                {
                    return parsedDate;
                }
            }
        }
        catch
        {
        }

        try
        {
            var fileInfo = new FileInfo(imageFilePath);
            var fallbackDate = fileInfo.CreationTime != DateTime.MinValue
                ? fileInfo.CreationTime
                : fileInfo.LastWriteTime;

            return new DateTimeOffset(fallbackDate, TimeZoneInfo.Local.GetUtcOffset(fallbackDate));
        }
        catch
        {
            return null;
        }
    }

    private static void AddTableOfContents(PdfDocument document, List<(string Title, int PageNumber)> tocEntries, PdfMetadata metadata)
    {
        const int maxEntriesPerPage = 50;
        var fontTitle = new XFont("Arial", 11, XFontStyle.Bold);
        var fontEntry = new XFont("Arial", 8, XFontStyle.Regular);

        int pageIndex = 0;
        int tocPagesCount = 1 + (int)Math.Floor(tocEntries.Count / (decimal)maxEntriesPerPage);

        foreach (var pageEntries in tocEntries.OrderBy(x => x.PageNumber).Chunk(maxEntriesPerPage))
        {
            var tocPage = document.InsertPage(pageIndex++);
            tocPage.Width = XUnit.FromPoint(595);
            tocPage.Height = XUnit.FromPoint(842);

            using var gfx = XGraphics.FromPdfPage(tocPage);

            gfx.DrawString("Table of content", fontTitle, XBrushes.Black, new XRect(0, 40, tocPage.Width, 20), XStringFormats.TopCenter);

            double yPosition = 60;

            foreach (var (filePath, pageNumber) in pageEntries)
            {
                var title = GetPageTitle(metadata, filePath, pageNumber) ?? Path.GetFileNameWithoutExtension(filePath);

                if (title.Length > 100)
                    title = title.Substring(0, 100);

                double titleWidth = gfx.MeasureString(title, fontEntry).Width;
                double pageNumberWidth = gfx.MeasureString(pageNumber.ToString(), fontEntry).Width;

                double dotsWidth = tocPage.Width - 80 - titleWidth - pageNumberWidth;
                string dots = new string('.', (int)(dotsWidth / gfx.MeasureString(".", fontEntry).Width));

                string tocLine = $"{title} {dots} {pageNumber + pageIndex}";
                gfx.DrawString(tocLine, fontEntry, XBrushes.Black, new XRect(40, yPosition, tocPage.Width - 80, 20), XStringFormats.TopLeft);

                CreateManualLinkAnnotation(tocPage, new XRect(40, 842 - yPosition - 20, tocPage.Width - 80, 20), pageNumber + pageIndex - 1);

                yPosition += 15;
            }
        }
    }

    private static void CreateManualLinkAnnotation(PdfPage tocPage, XRect bounds, int targetPageIndex)
    {
        var annotationDict = new PdfDictionary();
        annotationDict.Elements.SetName("/Type", "/Annot");
        annotationDict.Elements.SetName("/Subtype", "/Link");

        annotationDict.Elements.SetRectangle("/Rect", new PdfRectangle(bounds));

        var destArray = new PdfArray();
        destArray.Elements.Add(tocPage.Owner.Pages[targetPageIndex]);
        destArray.Elements.Add(new PdfName("/Fit"));

        annotationDict.Elements["/Dest"] = destArray;

        PdfArray annotsArray;
        if (tocPage.Elements.ContainsKey("/Annots"))
        {
            annotsArray = (PdfArray)tocPage.Elements["/Annots"];
        }
        else
        {
            annotsArray = new PdfArray(tocPage.Owner);
            tocPage.Elements["/Annots"] = annotsArray;
        }

        annotsArray.Elements.Add(annotationDict);
    }

    private static async Task CompressWithJpeg(MagickImage image, MemoryStream outputStream, uint quality, CancellationToken cancellationToken)
    {
        image.Format = MagickFormat.Jpeg;
        image.Quality = quality;

        await image.WriteAsync(outputStream, cancellationToken);
    }

    private static async Task ReduceColorDepth(MagickImage image, MemoryStream outputStream, uint colorCount, CancellationToken cancellationToken)
    {
        image.Format = MagickFormat.Png;

        image.Quantize(new QuantizeSettings { Colors = colorCount });

        await image.WriteAsync(outputStream, cancellationToken);
    }

    private static async Task CompressPngWithReducedPalette(MagickImage image, MemoryStream outputStream, uint colorCount, CancellationToken cancellationToken)
    {
        image.Format = MagickFormat.Png;

        image.ColorType = ColorType.Palette;
        image.Quantize(new QuantizeSettings { Colors = colorCount });

        await image.WriteAsync(outputStream, cancellationToken);
    }

    private static void SetDocumentMetadata(PdfDocument document, PdfMetadata metadata)
    {
        document.Info.Title = metadata.Title;
        document.Info.Author = metadata.Author;
        document.Info.Keywords = metadata.Keywords;
        document.Info.Subject = metadata.Subject;
        document.Info.Elements.SetString("/LicenseType", metadata.LicenseType);
    }

    private static void SetPageMetadata(PdfPage page, string imagePath, int pageNumber, PdfMetadata metadata)
    {
        /*
          1.	Title (/Title): The title or description of the page.
          2.	Author (/Author): The creator or author of the page content.
          3.	Subject (/Subject): A brief description of the page’s content or purpose.
          4.	Keywords (/Keywords): Keywords or tags relevant to the page’s content.
          5.	Creation Date (/CreationDate): Date when the page was created.
          6.	ModDate (/ModDate): Date when the page was last modified.
          7.	Custom Attributes: You can define additional custom fields if needed.
      */

        var pageMetadata = metadata.GetImageMetadata(imagePath);

        if (pageMetadata is not null)
        {
            page.Elements.SetString("/Title", pageMetadata.Title ?? $"Page {pageNumber}");
            page.Elements.SetString("/Description", pageMetadata.Description ?? "");
        }
        else
        {
            page.Elements.SetString("/Title", $"Page {pageNumber}");
        }
    }

    private static string? GetPageTitle(PdfMetadata metadata, string imagePath, int pageNumber)
    {
        var pageMetadata = metadata.GetImageMetadata(imagePath);

        if (pageMetadata is not null)
        {
            return pageMetadata.Title;
        }

        return null;
    }
}
