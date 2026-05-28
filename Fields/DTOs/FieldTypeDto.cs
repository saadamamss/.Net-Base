namespace DataForge.Fields.DTOs;

public class FieldTypeDto
{
    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DbType { get; set; } = string.Empty;
    public string FormComponent { get; set; } = string.Empty;
    public string InputType { get; set; } = string.Empty;
}
