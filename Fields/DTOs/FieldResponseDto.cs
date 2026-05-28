namespace DataForge.Fields.DTOs;

public class FieldResponseDto
{
    public string Collection { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public FieldMetaResponseDto Meta { get; set; } = null!;
    public FieldSchemaResponseDto Schema { get; set; } = null!;
}
