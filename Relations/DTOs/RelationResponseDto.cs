namespace DataForge.Relations.DTOs;

public class RelationResponseDto
{
    public string Collection { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string RelatedCollection { get; set; } = string.Empty;
    public RelationMetaResponseDto Meta { get; set; } = null!;
    public RelationSchemaDto Schema { get; set; } = null!;
}
