namespace DataForge.Fields.DTOs;

public class UpdateFieldDto
{
    public string? Field { get; set; }
    public string? Type { get; set; }
    public string? Label { get; set; }
    public FieldMetaPayloadDto? Meta { get; set; }
    public FieldSchemaPayloadDto? Schema { get; set; }
}
