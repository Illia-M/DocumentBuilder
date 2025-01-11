using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var inputDirectory = args.ElementAtOrDefault(0);
var outputDirectory = args.ElementAtOrDefault(1);

#if !DEBUG
if (args.Length == 0)
{
    PrintHelp();
    return;
}


if (string.IsNullOrWhiteSpace(inputDirectory))
{
    Console.WriteLine("No input directory");
    PrintHelp();
    return;
}
#endif
#if DEBUG
inputDirectory = "/Users/im/Projects/DocumentBuilder/tests/example_data/SmallCase";
#endif

inputDirectory = Path.GetFullPath(inputDirectory, Directory.GetCurrentDirectory());

if (!Directory.Exists(inputDirectory))
{
    Console.WriteLine("Input directory '{0}' not found.", inputDirectory);
    return;
}

Console.WriteLine("Input directory: {0}", inputDirectory);

if (string.IsNullOrWhiteSpace(outputDirectory))
{
    outputDirectory = Directory.GetCurrentDirectory();

    Console.WriteLine($"No result directory, use current for results: {outputDirectory}");
}

#if DEBUG
outputDirectory = "/Users/im/Projects/DocumentBuilder/tests/";
#endif

outputDirectory = Path.GetFullPath(outputDirectory, Directory.GetCurrentDirectory());

if (!Directory.Exists(outputDirectory))
{
    Console.WriteLine("Output directory not found.");
    PrintHelp();
    return;
}

Console.WriteLine("Output directory: {0}", outputDirectory);

try
{
    var metadata = await LoadMetadata(inputDirectory, CancellationToken.None);

    await PDFGenerator.CreatePdfFromImages(inputDirectory, outputDirectory, metadata, CancellationToken.None);

    Console.WriteLine($"PDF created successfully at: {outputDirectory}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

void PrintHelp()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("    <program> <imageDirectory> <resultDirectory>");
    Console.WriteLine();
    Console.WriteLine("Arguments:");
    Console.WriteLine("    imageDirectory    The directory containing image files to include in the PDF.");
    Console.WriteLine("    resultDirectory        The directory where the resulting PDF will be saved.");
    Console.WriteLine();
    Console.WriteLine("Notes:");
    Console.WriteLine("    - If resultDirectory is not provided, the current directory will be used.");
    Console.WriteLine("    - imageDirectory must exist and contain image files.");
    Console.WriteLine("    - resultDirectory must exist or be created beforehand.");
    Console.WriteLine();

    string osExample;

    if (OperatingSystem.IsWindows())
    {
        osExample = "\n    document-builder-1.0.0.exe \"C:\\Users\\YourUser\\Pictures\" \"C:\\Users\\YourUser\\Documents\"";
    }
    else if (OperatingSystem.IsMacOS())
    {
        osExample = "\n    ./document-builder-1.0.0 \"/Users/youruser/Pictures\" \"/Users/youruser/Documents\"";
    }
    else if (OperatingSystem.IsLinux())
    {
        osExample = "\n    ./document-builder-1.0.0 \"/home/youruser/Pictures\" \"/home/youruser/Documents\"";
    }
    else
    {
        osExample = "\n    ./document-builder-1.0.0 \"~/Pictures\" \"~/Documents\"";
    }

    Console.WriteLine("Example:");
    Console.WriteLine(osExample);
    Console.WriteLine();
}

static async Task<PdfMetadata> LoadMetadata(string directoryPath, CancellationToken cancellationToken)
{
    var yamlFilePath = Path.Combine(directoryPath, "metadata.yaml");

    if (!File.Exists(yamlFilePath))
    {
        Console.WriteLine("Warning: Metadata file not found, used default");

        return new PdfMetadata();
    }
    string? schemaFilePath = null;
    var schemaFileName = "metadata-schema.yaml";

    if (File.Exists(schemaFileName))
    {
        schemaFilePath = schemaFileName;
    }
    else if (File.Exists(Path.Join(Directory.GetCurrentDirectory(), schemaFileName)))
    {
        schemaFilePath = Path.Join(Directory.GetCurrentDirectory(), schemaFileName);
    }

    var assembly = Assembly.GetExecutingAssembly();
    string resourceName = "Settings.metadata-schema.yaml";

    using (Stream stream = assembly.GetManifestResourceStream(resourceName))
    {
        if (stream != null)
        {
            using StreamReader reader = new StreamReader(stream);
            string content = reader.ReadToEnd();
            schemaFilePath = Path.GetTempFileName();

            await File.WriteAllTextAsync(schemaFilePath, content);
        }
    }

    if (schemaFilePath is not null)
    {
        var validationResult = await YamlValidator.ValidateYamlWithYamlSchemaAsync(yamlFilePath, schemaFilePath, cancellationToken);

        if (!validationResult.IsFileValid)
        {
            Console.WriteLine("Error: Metadata file({0}) is invalid:\n{1}", yamlFilePath, string.Join("\n", validationResult.Errors));
            throw new InvalidOperationException("Invalid metadata file schema");
        }
    }
    else
        Console.WriteLine("Warning: Schema file not found, validation skipped");

    var deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();
    var yamlContent = await File.ReadAllTextAsync(yamlFilePath, cancellationToken);

    try
    {
        var metadata = deserializer.Deserialize<PdfMetadata>(yamlContent);

        if (metadata.OutputFileName.Intersect(Path.GetInvalidFileNameChars()).Any())
        {
            Console.WriteLine("Error: OutputFileName contains invalid chars for this system");

            throw new InvalidOperationException("OutputFileName contains invalid chars for this system");
        }

        return metadata;
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error: Metadata file content deserialization failed with - {0}", ex);
        throw;
    }
}