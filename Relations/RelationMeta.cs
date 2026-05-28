namespace DataForge.Relations;

public class RelationMeta
{
    public int Id { get; set; }
    public string ManyCollection { get; set; } = string.Empty;
    public string ManyField { get; set; } = string.Empty;
    public string OneCollection { get; set; } = string.Empty;
    public string? OneField { get; set; }
    public string OnDelete { get; set; } = "SET NULL";
    public string? OnDeselect { get; set; }
}
