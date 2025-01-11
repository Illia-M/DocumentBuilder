using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using NJsonSchema;
using Newtonsoft.Json.Linq;

public class YamlValidator
{
    public static async Task<YamlValidationResult> ValidateYamlWithYamlSchemaAsync(string yamlFilePath, string yamlSchemaPath, CancellationToken cancellationToken)
    {
        // Load YAML Schema
        var yamlSchemaContent = await File.ReadAllTextAsync(yamlSchemaPath, cancellationToken);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        var yamlSchemaObject = deserializer.Deserialize(new StringReader(yamlSchemaContent));

        // Convert YAML schema to JSON format
        var serializer = new SerializerBuilder().JsonCompatible().Build();
        var jsonSchemaContent = serializer.Serialize(yamlSchemaObject);

        // Load and parse JSON Schema
        var schema = await JsonSchema.FromJsonAsync(jsonSchemaContent, cancellationToken);

        // Load YAML content to validate
        var yamlContent = await File.ReadAllTextAsync(yamlFilePath, cancellationToken);
        var yamlObject = deserializer.Deserialize(new StringReader(yamlContent));

        // Convert YAML content to JSON for validation
        var jsonContent = serializer.Serialize(yamlObject);
        var jsonObject = JObject.Parse(jsonContent);

        // Validate JSON content against schema
        var errors = schema.Validate(jsonObject);

        if (errors.Count == 0)
        {
            return new YamlValidationResult
            {
                IsFileValid = true,
                Errors = [],
            };
        }
        else
        {
            return new YamlValidationResult
            {
                IsFileValid = false,
                Errors = errors.Select(error => $"{error.Path}: {error.Kind} - {error.Property}").ToArray(),
            };
        }
    }

    public class YamlValidationResult
    {
        public bool IsFileValid { get; set; }

        public IReadOnlyCollection<string> Errors { get; set; }
    }
}