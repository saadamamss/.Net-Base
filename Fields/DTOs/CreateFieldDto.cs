namespace DataForge.Fields.DTOs;

public class FieldMetaPayloadDto
{
    public string? Interface { get; set; }
    public string? Width { get; set; }
    public bool? Required { get; set; }
    public bool? Hidden { get; set; }
    public bool? Readonly { get; set; }
    public bool? Searchable { get; set; }
    public string? Note { get; set; }
    public Dictionary<string, object>? Options { get; set; }
}

public class FieldSchemaPayloadDto
{
    public string? DataType { get; set; }
    public string? DefaultValue { get; set; }
    public int? MaxLength { get; set; }
    public bool? IsNullable { get; set; }
    public bool? IsUnique { get; set; }
    public bool? IsIndexed { get; set; }
}

public class RelationDto
{
    public string Type { get; set; } = string.Empty;
    public string RelatedCollection { get; set; } = string.Empty;

    // M2O only
    public string? ForeignKey { get; set; }

    // O2M
    public string? OnDeselect { get; set; }
    public string? OnDelete { get; set; }

    // M2M only
    public string? JunctionCollection { get; set; }
    public string? JunctionField { get; set; }
    public string? RelatedJunctionField { get; set; }
    public string? OnDeleteRelated { get; set; }
}

public class CorrespondingFieldDto
{
    public bool Enabled { get; set; }
    public string FieldKey { get; set; } = string.Empty;
}

public class CreateFieldDto
{
    public string Field { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public FieldMetaPayloadDto? Meta { get; set; }
    public FieldSchemaPayloadDto? Schema { get; set; }
    public RelationDto? Relation { get; set; }
    public CorrespondingFieldDto? CorrespondingField { get; set; }
}
