namespace DataForge.Relations.DTOs;

public class RelationMetaResponseDto
{
    public int     Id                { get; set; }
    public string  ManyCollection    { get; set; } = string.Empty;
    public string  ManyField         { get; set; } = string.Empty;
    public string  OneCollection     { get; set; } = string.Empty;
    public string? OneField          { get; set; }
    public string? OneDeselectAction { get; set; }
    public string? JunctionField     { get; set; }
}
