namespace DotnetStarterKit.Common.Models;

public class PaginatedResult<T>
{
    public IEnumerable<T> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int Limit { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)Total / Limit);
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;
}