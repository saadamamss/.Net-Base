using System.Text.Json.Serialization;
using DataForge.Collections;

namespace DataForge.Fields;

public enum FieldType
{
    STRING,
    TEXT,
    INTEGER,
    BOOLEAN,
    DATE,
    UUID,
    BIGINT,
    FLOAT,
    DECIMAL,
    ALIAS
}

public class FieldTypeDefinition
{
    public string Label { get; set; }
    public string DbType { get; set; }
    public string DefaultDbDefault { get; set; }
    public string FormComponent { get; set; }
    public string InputType { get; set; }
}

public static class FieldTypeSchema
{
    private static readonly Dictionary<FieldType, FieldTypeDefinition> _schema =
        new Dictionary<FieldType, FieldTypeDefinition>
        {
            [FieldType.STRING] = new FieldTypeDefinition
            {
                Label = "String",
                DbType = "VARCHAR(255)",
                FormComponent = "InputText",
                InputType = "text"
            },
            [FieldType.TEXT] = new FieldTypeDefinition
            {
                Label = "Text",
                DbType = "TEXT",
                FormComponent = "Textarea",
                InputType = "textarea"
            },
            [FieldType.INTEGER] = new FieldTypeDefinition
            {
                Label = "Integer",
                DbType = "INTEGER",
                DefaultDbDefault = "0",
                FormComponent = "InputNumber",
                InputType = "number"
            },
            [FieldType.BOOLEAN] = new FieldTypeDefinition
            {
                Label = "Boolean",
                DbType = "BOOLEAN",
                DefaultDbDefault = "false",
                FormComponent = "Toggle",
                InputType = "checkbox"
            },
            [FieldType.DATE] = new FieldTypeDefinition
            {
                Label = "Date",
                DbType = "DATE",
                FormComponent = "DatePicker",
                InputType = "date"
            },
            [FieldType.UUID] = new FieldTypeDefinition
            {
                Label = "UUID",
                DbType = "UUID",
                FormComponent = "InputText",
                InputType = "text"
            },
            [FieldType.BIGINT] = new FieldTypeDefinition
            {
                Label = "Big Integer",
                DbType = "BIGINT",
                DefaultDbDefault = "0",
                FormComponent = "InputNumber",
                InputType = "number"
            },
            [FieldType.FLOAT] = new FieldTypeDefinition
            {
                Label = "Float",
                DbType = "NUMERIC",
                DefaultDbDefault = "0",
                FormComponent = "InputNumber",
                InputType = "number"
            },
            [FieldType.DECIMAL] = new FieldTypeDefinition
            {
                Label = "Decimal",
                DbType = "NUMERIC",
                DefaultDbDefault = "0",
                FormComponent = "InputNumber",
                InputType = "number"
            },

            [FieldType.ALIAS] = new FieldTypeDefinition
            {
                Label = "Alias",
                DbType = "",
                FormComponent = "None",
                InputType = "none"
            }
        };

    public static IReadOnlyDictionary<FieldType, FieldTypeDefinition> Schema => _schema;

    public static FieldTypeDefinition GetDefinition(FieldType fieldType)
    {
        return _schema.TryGetValue(fieldType, out var definition)
            ? definition
            : throw new ArgumentException($"Invalid FieldType: {fieldType}");
    }

    public static IReadOnlyList<FieldType> ValidFieldTypes => _schema.Keys.ToList().AsReadOnly();
}


public class FieldMeta
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Required { get; set; } = false;
    public int SortOrder { get; set; } = 0;

    public bool Hidden { get; set; } = false;
    public bool Readonly { get; set; } = false;
    public bool Searchable { get; set; } = false;
    public string Width { get; set; } = "full";
    public string? Note { get; set; }

    // New properties for field creation form
    public string? Interface { get; set; }

    [JsonIgnore]
    public string? Options { get; set; }

    [JsonPropertyName("options")]
    public object? OptionsData => !string.IsNullOrEmpty(Options)
        ? System.Text.Json.JsonSerializer.Deserialize<object>(Options)
        : null;

    public string? Special { get; set; }
    public string? DefaultValue { get; set; }
    public int? MaxLength { get; set; }
    public bool IsUnique { get; set; } = false;
    public bool IsIndexed { get; set; } = false;
    public bool IsSystem { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public CollectionMeta Collection { get; set; } = null!;
}
