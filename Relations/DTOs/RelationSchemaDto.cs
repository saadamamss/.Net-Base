namespace DataForge.Relations.DTOs;

public class RelationSchemaDto
{
    // FK constraint info — جايين من information_schema (null لو مش FK)
    public string? ConstraintName    { get; set; }
    public string? Table             { get; set; }
    public string? Column            { get; set; }
    public string? ForeignKeySchema  { get; set; }
    public string? ForeignKeyTable   { get; set; }
    public string? ForeignKeyColumn  { get; set; }
    public string? OnUpdate          { get; set; }
    public string? OnDelete          { get; set; }

    // M2M only
    public string? JunctionCollection   { get; set; }
    public string? JunctionField        { get; set; }
    public string? RelatedJunctionField { get; set; }
}
