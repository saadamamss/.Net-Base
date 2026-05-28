using System.ComponentModel.DataAnnotations;

namespace DataForge.Items.DTOs;

public class QueryItemsDto
{
    [Range(1, int.MaxValue)]
    public int? Page {get; set;} = 1;
    
    [Range(1, 100)]
    public int? Limit {get; set;} = 20;

    public string? Fields { get; set; }
}