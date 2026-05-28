namespace DataForge.Fields.DTOs;

public class FieldSchemaResponseDto
{
    public string DbType { get; set; } = string.Empty;
    public string? DefaultValue { get; set; }
    public int? MaxLength { get; set; }
    public bool IsNullable { get; set; }
    public bool IsUnique { get; set; }
    public bool IsIndexed { get; set; }
}
