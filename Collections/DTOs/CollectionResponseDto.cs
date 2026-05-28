namespace DataForge.Collections.DTOs;

public class CollectionResponseDto
{
    public string Collection { get; set; } = string.Empty;
    public CollectionMetaDto Meta { get; set; } = null!;
    public CollectionSchemaDto Schema { get; set; } = null!;
}
