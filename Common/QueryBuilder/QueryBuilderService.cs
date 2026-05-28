using System.Text.Json;
using Dapper;
using DataForge.Common.Models;
using DataForge.Common.Sanitizer;
using Npgsql;

namespace DataForge.Common.QueryBuilder;

public class QueryBuilderService
{
    private readonly string _connectionString;
    private readonly IdentifierSanitizerService _sanitizer;

    public QueryBuilderService(IConfiguration configuration, IdentifierSanitizerService sanitizer)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        _sanitizer = sanitizer;
    }

    // ── Find Many ──────────────────────────────────────────────
    public async Task<PaginatedResult<Dictionary<string, object?>>> FindManyAsync(
        string tableName, int page, int limit)
    {
        var safe = _sanitizer.Sanitize(tableName, "table name");
        limit = Math.Min(limit, 100);
        page = Math.Max(page, 1);
        var offset = (page - 1) * limit;

        using var connection = new NpgsqlConnection(_connectionString);

        var rows = await connection.QueryAsync(
            $"SELECT * FROM \"{safe}\" ORDER BY id DESC LIMIT @Limit OFFSET @Offset",
            new { Limit = limit, Offset = offset });

        var count = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM \"{safe}\"");

        var items = rows
            .Select(row => (IDictionary<string, object?>)row)
            .Select(row => row.ToDictionary(k => k.Key, k => k.Value))
            .ToList();

        return new PaginatedResult<Dictionary<string, object?>>
        {
            Items = items,
            Total = count,
            Page = page,
            Limit = limit
        };
    }

    // ── Find One ───────────────────────────────────────────────
    private static object CoerceId(string id) =>
        int.TryParse(id, out var intId) ? intId :
        Guid.TryParse(id, out var guid) ? guid :
        id;

    public async Task<Dictionary<string, object?>> FindOneAsync(string tableName, string id)
    {
        var safe = _sanitizer.Sanitize(tableName, "table name");

        using var connection = new NpgsqlConnection(_connectionString);

        var row = await connection.QueryFirstOrDefaultAsync(
            $"SELECT * FROM \"{safe}\" WHERE id = @Id",
            new { Id = CoerceId(id) });

        if (row is null)
            throw new KeyNotFoundException($"Item with id \"{id}\" not found.");

        return ((IDictionary<string, object?>)row).ToDictionary(k => k.Key, k => k.Value);
    }

    // ── Insert One ─────────────────────────────────────────────
    public async Task<Dictionary<string, object?>> InsertOneAsync(
        string tableName, Dictionary<string, object?> data)
    {
        var safe = _sanitizer.Sanitize(tableName, "table name");

        using var connection = new NpgsqlConnection(_connectionString);

        if (data.Count == 0)
        {
            var defaultRow = await connection.QueryFirstAsync(
                $"INSERT INTO \"{safe}\" DEFAULT VALUES RETURNING *");
            return ((IDictionary<string, object?>)defaultRow).ToDictionary(k => k.Key, k => k.Value);
        }

        var columns = data.Keys
            .Select(k => $"\"{_sanitizer.Sanitize(k, "field name")}\"")
            .ToList();

        var paramNames = data.Keys
            .Select(k => $"@{k}")
            .ToList();

        var sql = $"INSERT INTO \"{safe}\" ({string.Join(", ", columns)}) VALUES ({string.Join(", ", paramNames)}) RETURNING *";

        var parameters = new DynamicParameters();
        foreach (var (key, value) in data)
            parameters.Add(key, ConvertJsonElement(value));

        var row = await connection.QueryFirstAsync(sql, parameters);
        return ((IDictionary<string, object?>)row).ToDictionary(k => k.Key, k => k.Value);
    }

    // ── Update One ─────────────────────────────────────────────
    public async Task<Dictionary<string, object?>> UpdateOneAsync(
        string tableName, string id, Dictionary<string, object?> data)
    {
        var safe = _sanitizer.Sanitize(tableName, "table name");

        if (data.Count == 0)
            return await FindOneAsync(tableName, id);

        using var connection = new NpgsqlConnection(_connectionString);

        var setClauses = data.Keys
            .Select(k => $"\"{_sanitizer.Sanitize(k, "field name")}\" = @{k}")
            .ToList();

        var sql = $"UPDATE \"{safe}\" SET {string.Join(", ", setClauses)} WHERE id = @Id RETURNING *";

        var parameters = new DynamicParameters();
        foreach (var (key, value) in data)
            parameters.Add(key, ConvertJsonElement(value));
        parameters.Add("Id", CoerceId(id));

        var row = await connection.QueryFirstOrDefaultAsync(sql, parameters);

        if (row is null)
            throw new KeyNotFoundException($"Item with id \"{id}\" not found.");

        return ((IDictionary<string, object?>)row).ToDictionary(k => k.Key, k => k.Value);
    }

    // ── Delete Many ────────────────────────────────────────────
    public async Task DeleteManyAsync(string tableName, List<string> ids)
    {
        var safe = _sanitizer.Sanitize(tableName, "table name");

        using var connection = new NpgsqlConnection(_connectionString);

        if (ids.All(i => int.TryParse(i, out _)))
        {
            var intIds = ids.Select(int.Parse).ToArray();
            await connection.ExecuteAsync(
                $"DELETE FROM \"{safe}\" WHERE id = ANY(@Ids)",
                new { Ids = intIds });
        }
        else if (ids.All(i => Guid.TryParse(i, out _)))
        {
            var guidIds = ids.Select(Guid.Parse).ToArray();
            await connection.ExecuteAsync(
                $"DELETE FROM \"{safe}\" WHERE id = ANY(@Ids)",
                new { Ids = guidIds });
        }
        else
        {
            var strIds = ids.ToArray();
            await connection.ExecuteAsync(
                $"DELETE FROM \"{safe}\" WHERE id = ANY(@Ids)",
                new { Ids = strIds });
        }
    }

    // ── Delete One ─────────────────────────────────────────────
    public async Task DeleteOneAsync(string tableName, string id)
    {
        var safe = _sanitizer.Sanitize(tableName, "table name");

        using var connection = new NpgsqlConnection(_connectionString);

        await connection.ExecuteAsync(
            $"DELETE FROM \"{safe}\" WHERE id = @Id",
            new { Id = CoerceId(id) });
    }

    private static object? ConvertJsonElement(object? value)
    {
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => je.GetString()
            };
        }
        return value;
    }
}