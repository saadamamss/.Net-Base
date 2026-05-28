using System.Globalization;
using System.Text.Json;
using Dapper;
using DataForge.Common.Models;
using DataForge.Common.QueryBuilder;
using DataForge.Common.Sanitizer;
using DataForge.Collections;
using DataForge.Data;
using DataForge.Fields;
using DataForge.Items.DTOs;
using DataForge.Relations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DataForge.Items;

public class ItemsService
{
  private readonly QueryBuilderService _qb;
  private readonly AppDbContext _db;
  private readonly IdentifierSanitizerService _sanitizer;
  private readonly string _connectionString;

  public ItemsService(QueryBuilderService qb, AppDbContext db, IdentifierSanitizerService sanitizer, IConfiguration config)
  {
    _qb = qb;
    _db = db;
    _sanitizer = sanitizer;
    _connectionString = config.GetConnectionString("DefaultConnection")!;
  }

  public async Task<PaginatedResult<Dictionary<string, object?>>> FindManyAsync(
      string tableName, QueryItemsDto query, FieldMeta[] fieldsMeta)
  {
    var m2mMap = await LoadM2mJunctionMap(tableName, fieldsMeta);
    var result = await _qb.FindManyAsync(tableName, query.Page ?? 1, query.Limit ?? 20);
    var items = result.Items.ToList();
    for (var i = 0; i < items.Count; i++)
      items[i] = await ResolveRelations(tableName, items[i], query.Fields, fieldsMeta, m2mMap);
    result.Items = items;
    return result;
  }

  public async Task<Dictionary<string, object?>> FindOneAsync(
      string tableName, string id, string? fields = null, FieldMeta[]? fieldsMeta = null)
  {
    var row = await _qb.FindOneAsync(tableName, id);
    if (fieldsMeta != null)
    {
      var m2mMap = await LoadM2mJunctionMap(tableName, fieldsMeta);
      row = await ResolveRelations(tableName, row, fields, fieldsMeta, m2mMap);
    }
    return row;
  }

  public async Task<Dictionary<string, object?>> CreateAsync(
     string tableName,
     FieldMeta[] fields,
     Dictionary<string, object?> body
   )
  {
    var m2mMap = await LoadM2mJunctionMap(tableName, fields);
    var o2mMap = await LoadO2mRelationMap(tableName, fields);
    var sanitizedData = ValidateAndSanitizeBody(fields, body);
    var row = await _qb.InsertOneAsync(tableName, sanitizedData);
    var itemId = row.GetValueOrDefault("id")?.ToString();
    if (itemId != null)
    {
      await SaveM2mRelations(tableName, body, itemId, m2mMap);
      await SaveO2mRelations(tableName, body, itemId, o2mMap);
    }
    return row;
  }

  public async Task<Dictionary<string, object?>> UpdateAsync(
      string tableName,
      string id,
      FieldMeta[] fields,
      Dictionary<string, object?> body
    )
  {
    var m2mMap = await LoadM2mJunctionMap(tableName, fields);
    var o2mMap = await LoadO2mRelationMap(tableName, fields);
    var sanitizedData = ValidateAndSanitizeBody(fields, body, false);
    var row = await _qb.UpdateOneAsync(tableName, id, sanitizedData);
    await SaveM2mRelations(tableName, body, id, m2mMap);
    await SaveO2mRelations(tableName, body, id, o2mMap);
    return row;
  }

  public async Task RemoveAsync(string tableName, string id)
  {
    await _qb.DeleteOneAsync(tableName, id);
  }

  public async Task RemoveManyAsync(string tableName, List<string> ids)
  {
    if (ids == null || ids.Count == 0)
        throw new BadHttpRequestException("No IDs provided");

    await _qb.DeleteManyAsync(tableName, ids);
  }

  private record FKResolveConfig(HashSet<string>? Fields, string? SubPath);

  private record FieldConfig(
    int Depth,
    HashSet<string>? JunctionFilter,                                 // null=all junction cols, set=specific (M2M)
    Dictionary<string, FKResolveConfig?>? FKFields,                  // per-FK resolve config (M2M depth≥2)
    HashSet<string>? ResolvedFields                                  // FK sub-fields for M2O/O2M at depth≥1
  );

  private record M2mJunctionInfo(string JunctionCollection, string JunctionField, string RelatedJunctionField);
  private record O2mRelationInfo(string ManyTable, string FkColumn);

  // ── Field depth config parsing ───────────────────────────────────
  //   null/empty → globalDepth = 0 (flat IDs for all alias fields)
  //   "images"                             → depth 0
  //   "images.*"                           → depth 1, junction all cols
  //   "images.file_metas_id"               → depth 1, junction only file_metas_id col
  //   "images.*.*"                         → depth 2, resolve all FKs, all cols
  //   "images.*.filename_disk"             → depth 2, resolve all FKs, only filename_disk
  //   "images.file_metas_id.*"             → depth 2, resolve only file_metas_id FK, all cols
  //   "images.file_metas_id.url"           → depth 2, resolve only file_metas_id FK, only url
  //   "images.*.id,images.*.url"           → depth 2, resolve all FKs, id+url from each
  //   "images.posts_id.title,images.file_metas_id.url" → each FK its own fields
  //   "images.posts_id.images.*"           → recursive: resolve posts_id FK, then images M2M
  //   "*"   → global depth 0
  //   "*.*" → global depth 1 (all alias fields, junction all cols)
  private static (int globalDepth, Dictionary<string, FieldConfig> configs) ParseDepthConfig(string? fields)
  {
    var globalDepth = -1;
    var configs = new Dictionary<string, FieldConfig>();

    if (string.IsNullOrWhiteSpace(fields))
      return (0, configs);

    var parts = fields.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    foreach (var part in parts)
    {
      var segs = part.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
      if (segs.Length == 0) continue;

      var fieldName = segs[0];
      var depth = segs.Length - 1;

      HashSet<string>? junctionFilter = null;
      Dictionary<string, FKResolveConfig?>? fkFields = null;
      HashSet<string>? resolvedFields = null;

      if (segs.Length >= 2)
      {
        junctionFilter = segs[1] == "*" ? null : new HashSet<string> { segs[1] };
        resolvedFields = segs[1] == "*" ? null : new HashSet<string> { segs[1] };
      }
      if (segs.Length >= 3)
      {
        fkFields = new Dictionary<string, FKResolveConfig?>();
        var fkKey = segs[1] == "*" ? "*" : segs[1];
        var last = segs[^1];
        resolvedFields = last == "*" ? null : new HashSet<string> { last };

        if (segs.Length >= 4)
        {
          // Recursive: 4+ segments → store remaining as sub-path
          var subPath = string.Join(".", segs[2..]);
          fkFields[fkKey] = new FKResolveConfig(null, subPath);
        }
        else
        {
          // 3 segments → last could be a field filter or a relation name
          // Set both Fields (for column filtering) and SubPath (for possible relation resolution)
          fkFields[fkKey] = new FKResolveConfig(
            last == "*" ? null : new HashSet<string> { last },
            last);  // SubPath enables recursive resolution of relation fields
        }
      }

      if (fieldName == "*")
      {
        globalDepth = Math.Max(globalDepth, depth);
        continue;
      }

      if (configs.TryGetValue(fieldName, out var existing))
      {
        var maxDepth = Math.Max(existing.Depth, depth);

        // Merge JunctionFilter: null (all) wins
        var mergedFilter = existing.JunctionFilter == null || junctionFilter == null
            ? null : new HashSet<string>(existing.JunctionFilter.Union(junctionFilter));

        // Merge FKFields (FKResolveConfig dict)
        Dictionary<string, FKResolveConfig?>? mergedFk = null;
        if (existing.FKFields != null || fkFields != null)
        {
          mergedFk = new Dictionary<string, FKResolveConfig?>();
          if (existing.FKFields != null)
            foreach (var kv in existing.FKFields)
              mergedFk[kv.Key] = kv.Value;
          if (fkFields != null)
            foreach (var kv in fkFields)
            {
              if (mergedFk.TryGetValue(kv.Key, out var existingCfg) && existingCfg != null && kv.Value != null)
              {
                // Merge Fields (null = all cols wins)
                var mergedFields = existingCfg.Fields == null || kv.Value.Fields == null
                    ? null : new HashSet<string>(existingCfg.Fields.Union(kv.Value.Fields));
                // Merge SubPaths (join with comma)
                var mergedSubPath = existingCfg.SubPath != null && kv.Value.SubPath != null
                    ? existingCfg.SubPath + "," + kv.Value.SubPath
                    : existingCfg.SubPath ?? kv.Value.SubPath;
                mergedFk[kv.Key] = new FKResolveConfig(mergedFields, mergedSubPath);
              }
              else
              {
                mergedFk[kv.Key] = kv.Value;
              }
            }
        }

        // If mergedFk has "*" key, ALL junction filter becomes null
        if (mergedFk != null && mergedFk.ContainsKey("*"))
          mergedFilter = null;

        // Merge ResolvedFields: null (all) wins
        HashSet<string>? mergedResolved = null;
        if (existing.ResolvedFields != null && resolvedFields != null)
        {
          mergedResolved = new HashSet<string>(existing.ResolvedFields);
          mergedResolved.UnionWith(resolvedFields);
        }

        configs[fieldName] = new FieldConfig(maxDepth, mergedFilter, mergedFk, mergedResolved);
      }
      else
      {
        configs[fieldName] = new FieldConfig(depth, junctionFilter, fkFields, resolvedFields);
      }
    }

    return (globalDepth, configs);
  }

  // ── Resolve all relation fields ──────────────────────────────────
  private async Task<Dictionary<string, object?>> ResolveRelations(
      string tableName, Dictionary<string, object?> row, string? fields, FieldMeta[] fieldsMeta,
      Dictionary<string, M2mJunctionInfo>? m2mMap = null)
  {
    var (globalDepth, configs) = ParseDepthConfig(fields);
    var fieldMap = fieldsMeta.ToDictionary(f => f.Name);

    foreach (var kvp in fieldMap)
    {
      var fm = kvp.Value;
      if (fm.Special == null) continue;

      var depth = configs.TryGetValue(fm.Name, out var cfg) ? cfg.Depth : globalDepth;
      if (depth < 0) continue;

      switch (fm.Special)
      {
        case "m2o":
        case "file":
          if (depth >= 1)
            row[fm.Name] = await ResolveM2O(tableName, fm, row, cfg?.ResolvedFields);
          break;

        case "o2m":
          var o2mThisId = ParseRowId(row, "id");
          if (o2mThisId == null) break;
          row[fm.Name] = depth >= 1
              ? await ResolveO2MObjects(tableName, fm, o2mThisId)
              : await ResolveO2MIds(tableName, fm, o2mThisId);
          break;

        case "m2m":
        case "files":
          var m2mThisId = ParseRowId(row, "id");
          if (m2mThisId == null) break;
          if (m2mMap == null || !m2mMap.TryGetValue(fm.Name, out var mj)) break;
          row[fm.Name] = depth >= 1
              ? await ResolveM2MJunction(mj, m2mThisId, depth, cfg)
              : await ResolveM2MIds(mj, m2mThisId);
          break;
      }
    }

    // Filter root row to only explicitly requested non-relation fields
    if (!string.IsNullOrWhiteSpace(fields))
    {
      var topLevelFields = ParseTopLevelFields(fields);
      if (topLevelFields != null && !topLevelFields.Contains("*"))
      {
        var relationNames = fieldsMeta.Where(f => f.Special != null).Select(f => f.Name).ToHashSet();
        var keysToRemove = row.Keys
            .Where(k => !relationNames.Contains(k) && !topLevelFields.Contains(k) && !topLevelFields.Contains(k.ToLowerInvariant()))
            .ToList();
        foreach (var k in keysToRemove)
          row.Remove(k);
      }
    }

    return row;
  }

  private static HashSet<string>? ParseTopLevelFields(string? fields)
  {
    if (string.IsNullOrWhiteSpace(fields)) return null;
    var names = new HashSet<string>();
    foreach (var part in fields.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
    {
      var dot = part.IndexOf('.');
      names.Add(dot >= 0 ? part[..dot] : part);
    }
    return names;
  }

  // ── M2O / file: resolve FK → related object (depth ≥ 1) ─────────
  private async Task<object?> ResolveM2O(string tableName, FieldMeta fm, Dictionary<string, object?> row, HashSet<string>? resolvedFields = null)
  {
    if (!row.TryGetValue(fm.Name, out var fkValue) || fkValue == null)
      return null;

        var relation = await _db.RelationMetas
            .FirstOrDefaultAsync(r => r.ManyCollection == tableName && r.ManyField == fm.Name);

    if (relation == null) return fkValue;

    return await ResolveRelatedRow(relation.OneCollection, fkValue, resolvedFields);
  }

  // ── O2M depth 0: flat child PKs ────────────────
  private async Task<List<object>> ResolveO2MIds(string tableName, FieldMeta fm, object thisId)
  {
    var relation = await _db.RelationMetas
        .FirstOrDefaultAsync(r => r.OneCollection == tableName && r.OneField == fm.Name);
    if (relation == null) return [];

    using var connection = new NpgsqlConnection(_connectionString);
    var safeTable = relation.ManyCollection.Replace("\"", "\"\"");
    var safeField = relation.ManyField.Replace("\"", "\"\"");
    var where = SqlWhere(safeField, thisId);
    var rows = await connection.QueryAsync($"SELECT id FROM \"{safeTable}\" WHERE {where}");
    return rows.Select(r => ((IDictionary<string, object?>)r)["id"]!).ToList();
  }

  // ── O2M depth ≥ 1: child objects ────────────────
  private async Task<List<object?>> ResolveO2MObjects(string tableName, FieldMeta fm, object thisId)
  {
    var relation = await _db.RelationMetas
        .FirstOrDefaultAsync(r => r.OneCollection == tableName && r.OneField == fm.Name);
    if (relation == null) return [];

    using var connection = new NpgsqlConnection(_connectionString);
    var safeTable = relation.ManyCollection.Replace("\"", "\"\"");
    var safeField = relation.ManyField.Replace("\"", "\"\"");
    var where = SqlWhere(safeField, thisId);
    var rows = await connection.QueryAsync($"SELECT * FROM \"{safeTable}\" WHERE {where}");
    return rows.Select<dynamic, object?>(r =>
    {
      var dict = ((IDictionary<string, object?>)r).ToDictionary(k => k.Key, k => k.Value);
      EnrichFileMetaWithUrl(relation.ManyCollection, dict);
      return dict;
    }).ToList();
  }

  // ── M2M / files depth 0: flat related PKs ───────
  private async Task<List<object?>> ResolveM2MIds(M2mJunctionInfo mj, object thisId)
  {
    using var connection = new NpgsqlConnection(_connectionString);
    var safeJunction = mj.JunctionCollection.Replace("\"", "\"\"");
    var safeRelField = mj.RelatedJunctionField.Replace("\"", "\"\"");
    var where = SqlWhere(mj.JunctionField, thisId);
    var rows = await connection.QueryAsync($"SELECT \"{safeRelField}\" FROM \"{safeJunction}\" WHERE {where}");
    return rows.Select(r => ((IDictionary<string, object?>)r).First().Value).ToList();
  }

  // ── M2M / files depth ≥ 1: junction rows ───────
  private async Task<List<object?>> ResolveM2MJunction(M2mJunctionInfo mj, object thisId, int depth, FieldConfig? cfg)
  {
    using var connection = new NpgsqlConnection(_connectionString);
    var safeJunction = mj.JunctionCollection.Replace("\"", "\"\"");
    var where = SqlWhere(mj.JunctionField, thisId);
    var rows = await connection.QueryAsync($"SELECT * FROM \"{safeJunction}\" WHERE {where}");

    // Determine which FK columns to target
    var hasAll = cfg?.FKFields != null && cfg.FKFields.ContainsKey("*");
    var junctionFilter = hasAll ? null : cfg?.JunctionFilter;

    if (depth < 2)
    {
      return rows.Select<dynamic, object?>(r =>
      {
        var dict = ((IDictionary<string, object?>)r).ToDictionary(k => k.Key, k => k.Value);
        if (junctionFilter != null)
          return junctionFilter.ToDictionary(k => k, k => dict.GetValueOrDefault(k));
        return dict;
      }).ToList();
    }

    var junctionRelations = await _db.RelationMetas
        .Where(r => r.ManyCollection == mj.JunctionCollection)
        .ToListAsync();

    // Select target FKs: either all relations, or only the filtered ones
    var targetRelations = junctionFilter == null
        ? junctionRelations
        : junctionRelations.Where(r => r.ManyField != null && junctionFilter.Contains(r.ManyField)).ToList();

    // Directus-style validation: when using * at junction level with specific
    // sub-fields, verify the field exists on ALL FK target collections
    if (hasAll && targetRelations.Count > 0)
    {
      var allFields = cfg?.FKFields?.GetValueOrDefault("*");
      if (allFields?.Fields != null && allFields.Fields.Count > 0)
      {
        var targetTableNames = targetRelations.Select(r => r.OneCollection).Distinct().ToList();
        var collections = await _db.CollectionMetas
            .Where(c => targetTableNames.Contains(c.TableName))
            .Include(c => c.Fields)
            .ToListAsync();
        var fieldsByTable = collections.ToDictionary(c => c.TableName, c => new HashSet<string>(c.Fields.Select(f => f.Name)));
        var nameByTable = collections.ToDictionary(c => c.TableName, c => c.Name);

        foreach (var rel in targetRelations)
        {
          if (!fieldsByTable.TryGetValue(rel.OneCollection, out var existingFields)) continue;
          foreach (var requestedField in allFields.Fields)
          {
            if (!existingFields.Contains(requestedField))
            {
              var collName = nameByTable.GetValueOrDefault(rel.OneCollection, rel.OneCollection);
              throw new BadHttpRequestException(
                $"You don't have permission to access field \"{requestedField}\" in collection \"{collName}\" or it does not exist. Queried in \"{mj.JunctionCollection}.{rel.ManyField}\".");
            }
          }
        }
      }
    }

    var result = new List<object?>();
    foreach (var raw in rows)
    {
      var dict = ((IDictionary<string, object?>)raw).ToDictionary(k => k.Key, k => k.Value);

      foreach (var rel in targetRelations)
      {
        if (rel.ManyField == null || !dict.TryGetValue(rel.ManyField, out var fkVal) || fkVal == null) continue;

        // Fetch FK row
        var row = await ResolveRelatedRow(rel.OneCollection, fkVal, null);
        if (row is not Dictionary<string, object?> rowDict) continue;

        // Get FKResolveConfig for this FK column
        var fkCfg = cfg?.FKFields?.GetValueOrDefault(rel.ManyField)
                  ?? (hasAll ? cfg?.FKFields?.GetValueOrDefault("*") : null);

        // Recursive resolution: if SubPath is set, continue resolving relations
        if (fkCfg?.SubPath != null)
        {
          var childFields = await _db.CollectionMetas
              .Where(c => c.TableName == rel.OneCollection)
              .Include(c => c.Fields)
              .SelectMany(c => c.Fields)
              .ToArrayAsync();
          rowDict = await ResolveRelations(rel.OneCollection, rowDict, fkCfg.SubPath, childFields);
        }

        // Apply terminal field filter
        if (fkCfg?.Fields != null)
          rowDict = rowDict.Where(kv => fkCfg.Fields.Contains(kv.Key)).ToDictionary(k => k.Key, k => k.Value);

        dict[rel.ManyField] = rowDict;
      }

      // Strip other junction columns when targeting specific FK(s)
      if (junctionFilter != null)
      {
        var filtered = new Dictionary<string, object?>();
        foreach (var rel in targetRelations)
          if (rel.ManyField != null && dict.TryGetValue(rel.ManyField, out var val))
            filtered[rel.ManyField] = val;
        // Also include direct junction columns in filter that aren't FK relations
        var fkCols = targetRelations.Select(r => r.ManyField).ToHashSet();
        foreach (var col in junctionFilter)
          if (!fkCols.Contains(col) && dict.TryGetValue(col, out var val))
            filtered[col] = val;
        result.Add(filtered);
      }
      else
      {
        result.Add(dict);
      }
    }

    return result;
  }

  // ── M2O depth ≥ 1: resolve a single FK row ──────
  private async Task<object?> ResolveRelatedRow(string tableName, object fk, HashSet<string>? fields = null)
  {
    using var connection = new NpgsqlConnection(_connectionString);
    var safeTable = tableName.Replace("\"", "\"\"");
    var row = await connection.QueryFirstOrDefaultAsync(
        $"SELECT * FROM \"{safeTable}\" WHERE {SqlWhere("id", fk)}");
    if (row == null) return null;
    var dict = ((IDictionary<string, object?>)row).ToDictionary(k => k.Key, k => k.Value);
    EnrichFileMetaWithUrl(tableName, dict);

    if (fields != null)
    {
      var filtered = dict.Where(kv => fields.Contains(kv.Key)).ToDictionary(k => k.Key, k => k.Value);
      return filtered;
    }

    return dict;
  }

  // ── Helpers ─────────────────────────────────────
  private async Task<Dictionary<string, M2mJunctionInfo>> LoadM2mJunctionMap(
      string tableName, FieldMeta[] fieldsMeta)
  {
    var m2mFields = fieldsMeta.Where(f => f.Special == "m2m" || f.Special == "files").ToArray();
    if (m2mFields.Length == 0) return new();

    var fieldNames = m2mFields.Select(f => f.Name).ToHashSet();
    var sourceRels = await _db.RelationMetas
        .Where(r => r.OneCollection == tableName && fieldNames.Contains(r.OneField ?? ""))
        .ToListAsync();

    if (sourceRels.Count == 0) return new();

    var junctionTables = sourceRels.Select(r => r.ManyCollection).Distinct().ToList();
    var allPartnerRels = await _db.RelationMetas
        .Where(r => junctionTables.Contains(r.ManyCollection))
        .ToListAsync();

    var result = new Dictionary<string, M2mJunctionInfo>();
    foreach (var rel in sourceRels)
    {
      if (rel.OneField == null) continue;
      var partner = allPartnerRels.FirstOrDefault(r =>
          r.ManyCollection == rel.ManyCollection && r.Id != rel.Id);
      result[rel.OneField] = new M2mJunctionInfo(
          rel.ManyCollection,
          rel.ManyField ?? "",
          partner?.ManyField ?? ""
      );
    }
    return result;
  }

  private async Task<Dictionary<string, O2mRelationInfo>> LoadO2mRelationMap(
      string tableName, FieldMeta[] fieldsMeta)
  {
    var o2mFields = fieldsMeta.Where(f => f.Special == "o2m").ToArray();
    if (o2mFields.Length == 0) return new();

    var fieldNames = o2mFields.Select(f => f.Name).ToHashSet();
    var rels = await _db.RelationMetas
        .Where(r => r.OneCollection == tableName && fieldNames.Contains(r.OneField ?? ""))
        .ToListAsync();

    var result = new Dictionary<string, O2mRelationInfo>();
    foreach (var rel in rels)
    {
      if (rel.OneField == null) continue;
      result[rel.OneField] = new O2mRelationInfo(rel.ManyCollection, rel.ManyField ?? "");
    }
    return result;
  }

  private static string SqlWhere(string field, object id)
  {
    var safe = field.Replace("\"", "\"\"");
    return id switch
    {
      int i => $"\"{safe}\" = {i}",
      long l => $"\"{safe}\" = {l}",
      Guid g => $"\"{safe}\" = '{g}'::uuid",
      _ => $"\"{safe}\" = '{id.ToString()!.Replace("'", "''")}'"
    };
  }

  private static void EnrichFileMetaWithUrl(string tableName, Dictionary<string, object?> dict)
  {
    if (tableName == "file_metas" && dict.TryGetValue("filename_disk", out var fd) && fd is string filenameDisk)
      dict["url"] = $"/uploads/images/{filenameDisk}";
  }

  private static object? ParseRowId(Dictionary<string, object?> row, string key)
  {
    if (!row.TryGetValue(key, out var val) || val == null)
      return null;
    return val switch
    {
      int i => i,
      long l => l,
      Guid g => g,
      string s when int.TryParse(s, out var i) => i,
      string s when Guid.TryParse(s, out var g) => g,
      string s => s,
      _ => val
    };
  }

  private static object? CoerceValue(string fieldType, object? raw)
  {
    if (raw is JsonElement je)
      raw = je.ValueKind switch
      {
        JsonValueKind.String => je.GetString(),
        JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => je.GetString()
      };

    if (raw == null || raw is DBNull)
      return null;

    if (!Enum.TryParse<FieldType>(fieldType, true, out var type))
      return raw;

    return type switch
    {
      FieldType.DATE => DateTime.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
        ? DateOnly.FromDateTime(dt)
        : raw,

      FieldType.INTEGER => Convert.ToInt32(raw),
      FieldType.BIGINT => Convert.ToInt64(raw),
      FieldType.FLOAT or FieldType.DECIMAL => Convert.ToDecimal(raw),
      FieldType.BOOLEAN => raw is bool b ? b : Convert.ToBoolean(raw),
      FieldType.UUID => raw is string s && Guid.TryParse(s, out var guid) ? guid : raw,

      _ => raw
    };
  }

  private async Task SaveM2mRelations(
      string tableName, Dictionary<string, object?> body, string itemId,
      Dictionary<string, M2mJunctionInfo> m2mMap)
  {
    using var connection = new NpgsqlConnection(_connectionString);

    foreach (var kvp in body)
    {
      if (!m2mMap.TryGetValue(kvp.Key, out var mj)) continue;

      var relatedIds = kvp.Value switch
      {
        string s => (JsonSerializer.Deserialize<JsonElement[]>(s) ?? [])
            .Select(e => e.ValueKind == JsonValueKind.Number ? e.GetRawText() : e.GetString() ?? "")
            .ToList(),
        JsonElement je when je.ValueKind == JsonValueKind.Array =>
            je.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Number ? e.GetRawText() : e.GetString() ?? "")
                .ToList(),
        _ => []
      };

      var safeJunction = _sanitizer.Sanitize(mj.JunctionCollection, "table name");
      var safeField = _sanitizer.Sanitize(mj.JunctionField, "column name");
      var safeRelField = _sanitizer.Sanitize(mj.RelatedJunctionField, "column name");

      await connection.ExecuteAsync(
        $"DELETE FROM \"{safeJunction}\" WHERE \"{safeField}\" = @ItemId",
        new { ItemId = CoerceId(itemId) });

      foreach (var relatedId in relatedIds)
      {
        await connection.ExecuteAsync(
          $"INSERT INTO \"{safeJunction}\" (\"{safeField}\", \"{safeRelField}\") VALUES (@ItemId, @RelId)",
          new { ItemId = CoerceId(itemId), RelId = CoerceId(relatedId) });
      }
    }
  }

  private async Task SaveO2mRelations(
      string tableName, Dictionary<string, object?> body, string itemId,
      Dictionary<string, O2mRelationInfo> o2mMap)
  {
    using var connection = new NpgsqlConnection(_connectionString);

    foreach (var kvp in body)
    {
      if (!o2mMap.TryGetValue(kvp.Key, out var oi)) continue;

      var relatedIds = kvp.Value switch
      {
        string s => (JsonSerializer.Deserialize<JsonElement[]>(s) ?? [])
            .Select(e => e.ValueKind == JsonValueKind.Number ? e.GetRawText() : e.GetString() ?? "")
            .ToList(),
        JsonElement je when je.ValueKind == JsonValueKind.Array =>
            je.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Number ? e.GetRawText() : e.GetString() ?? "")
                .ToList(),
        _ => []
      };

      var safeTable = _sanitizer.Sanitize(oi.ManyTable, "table name");
      var safeFk = _sanitizer.Sanitize(oi.FkColumn, "column name");
      var itemIdObj = CoerceId(itemId);

      // Clear previously linked items that are no longer in the list
      if (relatedIds.Count > 0)
      {
        var placeholders = string.Join(", ", relatedIds.Select((_, i) => $"@p{i}"));
        var clearParams = new Dictionary<string, object> { ["@ItemId"] = itemIdObj };
        for (var i = 0; i < relatedIds.Count; i++)
          clearParams[$"@p{i}"] = CoerceId(relatedIds[i]);

        await connection.ExecuteAsync(
          $"UPDATE \"{safeTable}\" SET \"{safeFk}\" = NULL WHERE \"{safeFk}\" = @ItemId AND \"id\" NOT IN ({placeholders})",
          clearParams);
      }
      else
      {
        await connection.ExecuteAsync(
          $"UPDATE \"{safeTable}\" SET \"{safeFk}\" = NULL WHERE \"{safeFk}\" = @ItemId",
          new { ItemId = itemIdObj });
      }

      // Set FK on each related item
      foreach (var relatedId in relatedIds)
      {
        await connection.ExecuteAsync(
          $"UPDATE \"{safeTable}\" SET \"{safeFk}\" = @ItemId WHERE \"id\" = @RelId",
          new { ItemId = itemIdObj, RelId = CoerceId(relatedId) });
      }
    }
  }

  private Dictionary<string, object?> ValidateAndSanitizeBody(
      FieldMeta[] fields,
      Dictionary<string, object?> body,
      bool checkRequired = true
    )
  {
    var fieldMap = fields.ToDictionary(f => f.Name);
    var result = new Dictionary<string, object?>();

    if (checkRequired)
    {
      foreach (var field in fields)
      {
        if (field.Required && !field.IsSystem && field.Special != "o2m" && field.Special != "m2m" && field.Special != "files"
            && (!body.TryGetValue(field.Name, out var value) || value == null))
        {
          throw new BadHttpRequestException($"Field \"{field.Label}\" is required");
        }
      }
    }

    foreach (var kvp in body)
    {
      if (fieldMap.TryGetValue(kvp.Key, out var field))
      {
        if (field.Special == "o2m" || field.Special == "m2m" || field.Special == "files")
          continue;

        result[kvp.Key] = CoerceValue(field.Type, kvp.Value);
      }
    }

    return result;
  }

  private static object CoerceId(string id) =>
      int.TryParse(id, out var intId) ? intId :
      Guid.TryParse(id, out var guid) ? guid :
      id;

}
