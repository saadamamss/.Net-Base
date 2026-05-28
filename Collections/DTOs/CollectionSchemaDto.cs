namespace DataForge.Collections.DTOs;

public class CollectionSchemaDto
{
    /// <summary>اسم الجدول الحقيقي في الـ DB</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>نوع الـ primary key column في الـ DB</summary>
    public string PrimaryKeyType { get; set; } = string.Empty;
}
