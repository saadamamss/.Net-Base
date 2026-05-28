namespace DataForge.Common.Sanitizer;

using System.Text.RegularExpressions;

public class IdentifierSanitizerService
{
    private static readonly Regex IdentifierRegex =
        new(@"^[a-z][a-z0-9_]{0,62}$", RegexOptions.Compiled);
    private static readonly Regex UuidRegex =
        new(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
    private static readonly Regex InvalidCharsRegex = new Regex(@"[^a-z0-9_]", RegexOptions.Compiled);
    private static readonly Regex LeadingNonLetterRegex = new Regex(@"^[^a-z]+", RegexOptions.Compiled);

    public string Sanitize(string value, string fieldName)
    {
        var normalized = value.ToLower().Trim();
        if (!IdentifierRegex.IsMatch(normalized))
        {
            throw new ArgumentException($"Invalid {fieldName}: '{value}'. Must start with a letter, contain only lowercase letters, digits, and underscores, max 63 characters.");
        }
        return normalized;
    }

    public string ToSnakeCase(string value)
    {
        var result = value
          .ToLower()
          .Trim();
        result = WhitespaceRegex.Replace(result, "_");
        result = InvalidCharsRegex.Replace(result, "");
        result = LeadingNonLetterRegex.Replace(result, "");
        return result.Substring(0, Math.Min(50, result.Length));
    }

    /// <summary>
    /// Builds a table name from the collection name.
    /// </summary>
    public string BuildTableName(string collectionName)
    {
        var safeCollection = Sanitize(ToSnakeCase(collectionName), "collection name");

        var tableName = safeCollection;

        if (tableName.Length > 63)
        {
            throw new ArgumentException(
              $"Table name '{tableName}' exceeds PostgreSQL's 63-character limit. Use a shorter collection name."
            );
        }

        return tableName;
    }

    public string ValidateUuid(string value, string fieldName)
    {
        if (!UuidRegex.IsMatch(value))
        {
            throw new ArgumentException(
              $"Invalid {fieldName}: must be a valid UUID"
            );
        }
        return value;
    }
}
