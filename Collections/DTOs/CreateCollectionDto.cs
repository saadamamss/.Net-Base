using System.ComponentModel.DataAnnotations;

namespace DataForge.Collections.DTOs;

public class CreateCollectionDto
{
    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string Collection { get; set; } = string.Empty;

    public CollectionCreateMetaDto? Meta { get; set; }
    public CollectionCreateSchemaDto? Schema { get; set; }
}

public class CollectionCreateMetaDto
{
    public string PrimaryKey { get; set; } = "id";
    public string PkType { get; set; } = "auto-increment";
}

public class CollectionCreateSchemaDto
{
    public bool Status { get; set; } = false;
    public bool Sort { get; set; } = false;
    public bool DateCreated { get; set; } = false;
    public bool UserCreated { get; set; } = false;
    public bool DateUpdated { get; set; } = false;
    public bool UserUpdated { get; set; } = false;
}
