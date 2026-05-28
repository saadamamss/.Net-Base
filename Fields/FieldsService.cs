using System.Text.Json;
using DataForge.Collections;
using DataForge.Common.DDL;
using DataForge.Common.Sanitizer;
using DataForge.Data;
using DataForge.Fields.DTOs;
using DataForge.Relations;
using Microsoft.EntityFrameworkCore;

namespace DataForge.Fields;

public class FieldsService
{
    private readonly AppDbContext _db;
    private readonly IdentifierSanitizerService _sanitizer;
    private readonly DdlService _ddl;

    private readonly List<string> RESERVED_NAMES = new List<string> { "id" };

    public FieldsService(AppDbContext db, IdentifierSanitizerService sanitizer, DdlService ddl)
    {
        _db = db;
        _sanitizer = sanitizer;
        _ddl = ddl;
    }

    public async Task<FieldResponseDto> CreateAsync(int collectionId, CreateFieldDto dto)
    {
        var collection = await ResolveCollection(collectionId);

        var rawName = dto.Field?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(rawName))
            throw new ArgumentException("Field name is required");

        var sanitizedName = _sanitizer.ToSnakeCase(rawName);

        if (sanitizedName.Length < 1)
            throw new ArgumentException("Field name must contain at least one letter or digit");

        if (RESERVED_NAMES.Contains(sanitizedName))
            throw new InvalidOperationException($"\"{rawName}\" is a reserved field name");

        var existing = await _db.FieldMetas.AnyAsync(f => f.CollectionId == collectionId && f.Name == sanitizedName);
        if (existing)
            throw new InvalidOperationException($"Field \"{rawName}\" already exists");

        var rawType = dto.Schema?.DataType ?? dto.Type;

        var isRelationField = dto.Relation != null || dto.Meta?.Interface == "image";
        var isFilesInterface = dto.Meta?.Interface == "files";
        var isAliasType = string.Equals(dto.Type, "ALIAS", StringComparison.OrdinalIgnoreCase);

        if (!isRelationField && !isAliasType && !isFilesInterface)
        {
            await _ddl.AddColumnAsync(
                collection.TableName,
                sanitizedName,
                rawType,
                dto.Meta?.Required ?? false,
                dto.Schema?.DefaultValue,
                dto.Schema?.IsUnique ?? false,
                dto.Schema?.IsIndexed ?? false,
                dto.Schema?.MaxLength);
        }

        var iface = dto.Meta?.Interface;
        var special = (dto.Relation?.Type, iface) switch
        {
            (_, "image") => "file",
            (_, "files") => "files",
            ("m2o", _) => "m2o",
            ("o2m", _) => "o2m",
            ("m2m", _) => "m2m",
            (null, "list-o2m" or "table-o2m") => "o2m",
            _ => null
        };

        string? optionsJson = null;
        if (dto.Meta?.Options != null)
            optionsJson = JsonSerializer.Serialize(dto.Meta.Options);

        var field = new FieldMeta
        {
            CollectionId = collectionId,
            Name = sanitizedName,
            Label = rawName,
            Type = dto.Type,
            Required = dto.Meta?.Required ?? false,
            SortOrder = 0,
            Hidden = dto.Meta?.Hidden ?? false,
            Readonly = dto.Meta?.Readonly ?? false,
            Searchable = dto.Meta?.Searchable ?? false,
            Width = dto.Meta?.Width ?? "full",
            Note = dto.Meta?.Note,
            Interface = dto.Meta?.Interface,
            Special = special,
            Options = optionsJson,
            DefaultValue = dto.Schema?.DefaultValue,
            MaxLength = dto.Schema?.MaxLength,
            IsUnique = dto.Schema?.IsUnique ?? false,
            IsIndexed = dto.Schema?.IsIndexed ?? false,
        };

        _db.FieldMetas.Add(field);
        await _db.SaveChangesAsync();

        if (dto.Meta?.Interface == "image")
        {
            await _ddl.AddForeignKeyColumnAsync(
                collection.TableName,
                sanitizedName,
                "file_metas",
                "id",
                dto.Meta.Required ?? false,
                "uuid");

            _db.RelationMetas.Add(new RelationMeta
            {
                ManyCollection = collection.TableName,
                ManyField = sanitizedName,
                OneCollection = "file_metas",
            });
        }
        else if (dto.Relation?.Type == "m2o")
        {
            var related = await _db.CollectionMetas
                .FirstOrDefaultAsync(c => c.Name == dto.Relation.RelatedCollection);
            if (related == null)
                throw new ArgumentException($"Related collection \"{dto.Relation.RelatedCollection}\" not found");

            var onDelete = dto.Relation.OnDelete ?? "SET NULL";

            await _ddl.AddForeignKeyColumnAsync(
                collection.TableName,
                sanitizedName,
                related.TableName,
                "id",
                dto.Meta?.Required ?? false,
                related.PkType,
                onDelete);

            var relMeta = new RelationMeta
            {
                ManyCollection = collection.TableName,
                ManyField = sanitizedName,
                OneCollection = related.TableName,
                OnDelete   = onDelete,
                OnDeselect = dto.Relation.OnDeselect,
            };

            if (dto.CorrespondingField is { Enabled: true, FieldKey: not null or "" })
            {
                var o2mFieldName = _sanitizer.ToSnakeCase(dto.CorrespondingField.FieldKey);
                _db.FieldMetas.Add(new FieldMeta
                {
                    CollectionId = related.Id,
                    Name = o2mFieldName,
                    Label = dto.CorrespondingField.FieldKey,
                    Type = "ALIAS",
                    Special = "o2m",
                    Interface = "list-o2m",
                    Hidden = false,
                    Required = false,
                    IsSystem = false,
                    SortOrder = 0,
                });
                relMeta.OneField = o2mFieldName;
            }

            _db.RelationMetas.Add(relMeta);
        }
        else if (dto.Relation?.Type == "o2m")
        {
            var related = await _db.CollectionMetas
                .FirstOrDefaultAsync(c => c.Name == dto.Relation.RelatedCollection);
            if (related == null)
                throw new ArgumentException($"Related collection \"{dto.Relation.RelatedCollection}\" not found");

            var fkField = dto.Relation.ForeignKey ?? $"{collection.Name}_id";
            var onDelete = dto.Relation.OnDelete ?? "SET NULL";

            await _ddl.AddForeignKeyColumnAsync(
                related.TableName,
                fkField,
                collection.TableName,
                "id",
                false,
                collection.PkType,
                onDelete);

            var existingChildField = await _db.FieldMetas
                .FirstOrDefaultAsync(f => f.CollectionId == related.Id && f.Name == fkField);
            if (existingChildField == null)
            {
                var fkFieldType = collection.PkType switch
                {
                    "auto-increment" => "INTEGER",
                    "bigint" => "BIGINT",
                    "uuid" => "UUID",
                    _ => "INTEGER",
                };
                _db.FieldMetas.Add(new FieldMeta
                {
                    CollectionId = related.Id,
                    Name = fkField,
                    Label = collection.Name,
                    Type = fkFieldType,
                    Special = "m2o",
                    Hidden = true,
                    Interface = "select-dropdown-m2o",
                    Required = false,
                    IsSystem = false,
                    SortOrder = 0,
                });
            }

            _db.RelationMetas.Add(new RelationMeta
            {
                ManyCollection = related.TableName,
                ManyField = fkField,
                OneCollection = collection.TableName,
                OneField = sanitizedName,
                OnDelete   = onDelete,
                OnDeselect = dto.Relation.OnDeselect,
            });

            {
                var o2mOptions = !string.IsNullOrEmpty(optionsJson)
                    ? JsonSerializer.Deserialize<Dictionary<string, object>>(optionsJson)!
                    : new Dictionary<string, object>();
                o2mOptions.TryAdd("displayTemplate", "{{id}}");
                o2mOptions.TryAdd("enableCreate", true);
                o2mOptions.TryAdd("enableSelect", true);
                field.Options = JsonSerializer.Serialize(o2mOptions);
            }
        }
        else if (dto.Meta?.Interface == "files")
        {
            var junctionTable = $"{collection.TableName}_files";
            var junctionField = $"{collection.Name}_id";
            var relatedJunctionField = "file_metas_id";

            await _ddl.CreateJunctionTableAsync(
                junctionTable,
                junctionField, collection.TableName, collection.PkType,
                relatedJunctionField, "file_metas", "uuid");

            _db.RelationMetas.Add(new RelationMeta
            {
                ManyCollection = junctionTable,
                ManyField = relatedJunctionField,
                OneCollection = "file_metas",
            });

            _db.RelationMetas.Add(new RelationMeta
            {
                ManyCollection = junctionTable,
                ManyField = junctionField,
                OneCollection = collection.TableName,
                OneField = sanitizedName,
            });

        }
        else if (dto.Relation?.Type == "m2m")
        {
            var related = await _db.CollectionMetas
                .FirstOrDefaultAsync(c => c.Name == dto.Relation.RelatedCollection);
            if (related == null)
                throw new ArgumentException($"Related collection \"{dto.Relation.RelatedCollection}\" not found");

            var junctionTable = dto.Relation.JunctionCollection
                ?? $"{collection.TableName}_{related.TableName}";
            var junctionField = dto.Relation.JunctionField
                ?? $"{collection.Name}_id";
            var relatedJunctionField = dto.Relation.RelatedJunctionField
                ?? $"{related.Name}_id";

            await _ddl.CreateJunctionTableAsync(
                junctionTable,
                junctionField, collection.TableName, collection.PkType,
                relatedJunctionField, related.TableName, related.PkType);

            var relMeta1 = new RelationMeta
            {
                ManyCollection = junctionTable,
                ManyField = relatedJunctionField,
                OneCollection = related.TableName,
                OnDelete = dto.Relation.OnDeleteRelated ?? "SET NULL",
            };

            var relMeta2 = new RelationMeta
            {
                ManyCollection = junctionTable,
                ManyField = junctionField,
                OneCollection = collection.TableName,
                OneField = sanitizedName,
                OnDelete = dto.Relation.OnDelete ?? "SET NULL",
            };

            if (dto.CorrespondingField is { Enabled: true, FieldKey: not null or "" })
            {
                var o2mFieldName = _sanitizer.ToSnakeCase(dto.CorrespondingField.FieldKey);
                _db.FieldMetas.Add(new FieldMeta
                {
                    CollectionId = related.Id,
                    Name = o2mFieldName,
                    Label = dto.CorrespondingField.FieldKey,
                    Type = "ALIAS",
                    Special = "m2m",
                    Interface = "list-m2m",
                    Hidden = false,
                    Required = false,
                    IsSystem = false,
                    SortOrder = 0,
                });
                relMeta1.OneField = o2mFieldName;
            }

            _db.RelationMetas.Add(relMeta1);
            _db.RelationMetas.Add(relMeta2);

        }

        await _db.SaveChangesAsync();

        return await MapToResponseAsync(field, collection.Name, collection.TableName);
    }

    public async Task<FieldResponseDto> GetByIdAsync(int collectionId, int fieldId)
    {
        var collection = await ResolveCollection(collectionId);

        var field = await _db.FieldMetas
            .FirstOrDefaultAsync(f => f.Id == fieldId && f.CollectionId == collectionId);

        if (field == null)
            throw new KeyNotFoundException("Field not found");

        return await MapToResponseAsync(field, collection.Name, collection.TableName);
    }

    public async Task<List<FieldResponseDto>> GetAllFieldsAsync()
    {
        var fields = await _db.FieldMetas
            .OrderBy(f => f.CollectionId)
            .ThenBy(f => f.SortOrder)
            .ToListAsync();

        var collections = await _db.CollectionMetas.ToListAsync();
        var collectionMap = collections.ToDictionary(c => c.Id, c => c.Name);
        var tableNameMap  = collections.ToDictionary(c => c.Id, c => c.TableName);

        var result = new List<FieldResponseDto>();
        foreach (var f in fields)
        {
            var collName  = collectionMap.GetValueOrDefault(f.CollectionId, "");
            var tableName = tableNameMap.GetValueOrDefault(f.CollectionId, "");
            result.Add(await MapToResponseAsync(f, collName, tableName));
        }
        return result;
    }

    public async Task<List<FieldResponseDto>> GetAllAsync(int collectionId)
    {
        var collection = await ResolveCollection(collectionId);
        var fields = await _db.FieldMetas
            .Where(f => f.CollectionId == collectionId)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.CreatedAt)
            .ToListAsync();

        var result = new List<FieldResponseDto>();
        foreach (var f in fields)
            result.Add(await MapToResponseAsync(f, collection.Name, collection.TableName));
        return result;
    }

    public async Task<FieldResponseDto> UpdateAsync(int collectionId, int fieldId, UpdateFieldDto dto)
    {
        var collection = await ResolveCollection(collectionId);

        var field = await _db.FieldMetas
            .FirstOrDefaultAsync(f => f.Id == fieldId && f.CollectionId == collectionId);

        if (field == null)
            throw new KeyNotFoundException("Field not found");

        if (dto.Label is not null) field.Label = dto.Label;
        if (dto.Type is not null) field.Type = dto.Type;
        var isVirtual = field.Special == "m2m" || field.Special == "o2m" || field.Special == "files"
            || string.Equals(field.Type, "ALIAS", StringComparison.OrdinalIgnoreCase);

        if (!isVirtual && dto.Field is not null)
        {
            var newName = _sanitizer.ToSnakeCase(dto.Field);
            if (newName != field.Name)
            {
                await _ddl.RenameColumnAsync(collection.TableName, field.Name, newName);
                field.Name = newName;
            }
        }

        if (dto.Meta is not null)
        {
            if (dto.Meta.Interface is not null) field.Interface = dto.Meta.Interface;
            if (dto.Meta.Width is not null) field.Width = dto.Meta.Width;
            if (dto.Meta.Required is not null) field.Required = dto.Meta.Required.Value;
            if (dto.Meta.Hidden is not null) field.Hidden = dto.Meta.Hidden.Value;
            if (dto.Meta.Readonly is not null) field.Readonly = dto.Meta.Readonly.Value;
            if (dto.Meta.Searchable is not null) field.Searchable = dto.Meta.Searchable.Value;
            if (dto.Meta.Note is not null) field.Note = dto.Meta.Note;
            if (dto.Meta.Options is not null)
                field.Options = JsonSerializer.Serialize(dto.Meta.Options);
        }

        if (dto.Schema is not null)
        {
            // ← احفظ القيمة القديمة هنا
            var oldDefault = field.DefaultValue;

            if (dto.Schema.MaxLength is not null)
                field.MaxLength = dto.Schema.MaxLength.Value;

            var newDefault = dto.Schema.DefaultValue is not null
                ? (string.IsNullOrEmpty(dto.Schema.DefaultValue) ? null : dto.Schema.DefaultValue)
                : null;

            if (dto.Schema.DefaultValue is not null)
                field.DefaultValue = newDefault;

            if (dto.Schema.IsUnique is not null)
                field.IsUnique = dto.Schema.IsUnique.Value;

            if (dto.Schema.IsIndexed is not null)
                field.IsIndexed = dto.Schema.IsIndexed.Value;

            // ← المقارنة الصحيحة مع القيمة القديمة
            var defaultChanged = dto.Schema.DefaultValue is not null && newDefault != oldDefault;
            var needsDdl = !isVirtual && (dto.Schema.DataType is not null
                || dto.Schema.MaxLength is not null
                || defaultChanged
                || dto.Schema.IsUnique is not null
                || dto.Schema.IsIndexed is not null);

            if (needsDdl)
            {
                var dbType = dto.Schema.DataType;
                if (dbType is null && dto.Schema.MaxLength is not null
                    && Enum.TryParse<FieldType>(field.Type, true, out var ft))
                {
                    var td = FieldTypeSchema.GetDefinition(ft);
                    dbType = td.DbType;
                }

                await _ddl.AlterColumnAsync(
                    collection.TableName,
                    field.Name,
                    dbType,
                    defaultChanged ? newDefault : null,
                    dto.Schema.IsUnique,
                    dto.Schema.IsIndexed,
                    dto.Schema.IsIndexed ?? false);
            }
        }

        field.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return await MapToResponseAsync(field, collection.Name, collection.TableName);
    }

    public async Task ReorderAsync(int collectionId, List<ReorderFieldDto> items)
    {
        await ResolveCollection(collectionId);

        var ids = items.Select(i => i.Id).ToList();

        var fields = await _db.FieldMetas
            .Where(f => f.CollectionId == collectionId && ids.Contains(f.Id))
            .ToListAsync();

        foreach (var item in items)
        {
            var field = fields.FirstOrDefault(f => f.Id == item.Id);
            if (field != null)
            {
                field.SortOrder = item.SortOrder;
                field.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int collectionId, int fieldId)
    {
        var collection = await ResolveCollection(collectionId);

        var field = await _db.FieldMetas.FirstOrDefaultAsync(f => f.Id == fieldId);
        if (field == null) throw new KeyNotFoundException("Field Not Found");

        if (field.Special == "m2o" || field.Special == "file")
        {
            var relation = await _db.RelationMetas
                .FirstOrDefaultAsync(r => r.ManyCollection == collection.TableName && r.ManyField == field.Name);
            if (relation != null)
            {
                _db.RelationMetas.Remove(relation);
                await _ddl.DropColumnAsync(collection.TableName, field.Name);
            }
        }
        else if (field.Special == "o2m")
        {
            var relation = await _db.RelationMetas
                .FirstOrDefaultAsync(r => r.OneCollection == collection.TableName && r.OneField == field.Name);
            if (relation != null)
            {
                await _ddl.DropColumnAsync(relation.ManyCollection, relation.ManyField);
                _db.RelationMetas.Remove(relation);

                var childCollection = await _db.CollectionMetas
                    .FirstOrDefaultAsync(c => c.TableName == relation.ManyCollection);
                if (childCollection != null)
                {
                    var childField = await _db.FieldMetas
                        .FirstOrDefaultAsync(f => f.CollectionId == childCollection.Id
                            && f.Name == relation.ManyField
                            && f.Special == "m2o"
                            && f.Hidden == true);
                    if (childField != null)
                        _db.FieldMetas.Remove(childField);
                }
            }
        }
        else if (field.Special == "m2m" || field.Special == "files")
        {
            var rel = await _db.RelationMetas
                .FirstOrDefaultAsync(r => r.OneCollection == collection.TableName && r.OneField == field.Name);
            var junctionTable = rel?.ManyCollection;

            if (!string.IsNullOrEmpty(junctionTable))
            {
                var relations = await _db.RelationMetas
                    .Where(r => r.ManyCollection == junctionTable)
                    .ToListAsync();
                _db.RelationMetas.RemoveRange(relations);

                await _ddl.DropTableAsync(junctionTable);
            }
        }
        else
        {
            // حذف أي relation موجودة (defensive)
            var relation = await _db.RelationMetas
                .FirstOrDefaultAsync(r => r.ManyCollection == collection.TableName
                                       && r.ManyField == field.Name);
            if (relation != null)
                _db.RelationMetas.Remove(relation);

            // حذف الكولم الحقيقي دايماً — سواء في relation أو لأ
            // ALIAS و o2m و m2m و files مفيهومش كولم حقيقي
            var isVirtual = field.Special == "o2m"
                || field.Special == "m2m"
                || field.Special == "files"
                || string.Equals(field.Type, "ALIAS", StringComparison.OrdinalIgnoreCase);

            if (!isVirtual)
                await _ddl.DropColumnAsync(collection.TableName, field.Name);
        }

        _db.FieldMetas.Remove(field);
        await _db.SaveChangesAsync();
    }

    private async Task<CollectionMeta> ResolveCollection(int collectionId)
    {
        var collection = await _db.CollectionMetas
            .FirstOrDefaultAsync(c => c.Id == collectionId);

        if (collection == null)
            throw new KeyNotFoundException("Collection not found");

        return collection;
    }

    private FieldResponseDto MapToResponse(FieldMeta field, string collectionName)
    {
        // Fallback DbType من الـ enum — بيتستخدم بس لو column مش موجود في DB
        // (مثلاً ALIAS fields أو o2m alias)
        string fallbackDbType = "";
        try
        {
            if (Enum.TryParse<FieldType>(field.Type, true, out var ft))
                fallbackDbType = FieldTypeSchema.GetDefinition(ft).DbType;
        }
        catch { }

        return new FieldResponseDto
        {
            Collection = collectionName,
            Field = field.Name,
            Type = field.Type,
            Meta = new FieldMetaResponseDto
            {
                Id = field.Id,
                CollectionId = field.CollectionId,
                Name = field.Name,
                Label = field.Label,
                Required = field.Required,
                SortOrder = field.SortOrder,
                Hidden = field.Hidden,
                Readonly = field.Readonly,
                Searchable = field.Searchable,
                Width = field.Width,
                Note = field.Note,
                Interface = field.Interface,
                Special = field.Special,
                Options = field.OptionsData,
                IsSystem = field.IsSystem,
                CreatedAt = field.CreatedAt,
                UpdatedAt = field.UpdatedAt,
            },
            Schema = new FieldSchemaResponseDto
            {
                DbType = fallbackDbType,       // هيتعمله override في MapToResponseAsync
                DefaultValue = field.DefaultValue,
                MaxLength = field.MaxLength,
                IsNullable = !field.Required,  // حقل جديد
                IsUnique = field.IsUnique,
                IsIndexed = field.IsIndexed,
            },
        };
    }

    // النسخة الـ async اللي بتقرأ من DB
    private async Task<FieldResponseDto> MapToResponseAsync(
        FieldMeta field, string collectionName, string tableName)
    {
        var dto = MapToResponse(field, collectionName);

        // ALIAS و o2m و m2m و files مفيهومش كولم حقيقي في الـ DB
        var isVirtual = field.Special == "o2m"
            || field.Special == "m2m"
            || field.Special == "files"
            || string.Equals(field.Type, "ALIAS", StringComparison.OrdinalIgnoreCase);

        if (!isVirtual)
        {
            var colInfo = await _ddl.GetColumnInfoAsync(tableName, field.Name);
            if (colInfo is not null)
            {
                dto.Schema.DbType       = colInfo.DbType;
                dto.Schema.DefaultValue = field.DefaultValue;
                dto.Schema.MaxLength    = colInfo.MaxLength;
                dto.Schema.IsNullable   = colInfo.IsNullable;
                dto.Schema.IsUnique     = colInfo.IsUnique;
                dto.Schema.IsIndexed    = colInfo.IsIndexed;

                // Sync back إلى field_metas لو في اختلاف
                var dirty = false;
                if (field.MaxLength   != colInfo.MaxLength)   { field.MaxLength   = colInfo.MaxLength;   dirty = true; }
                if (field.IsUnique    != colInfo.IsUnique)    { field.IsUnique    = colInfo.IsUnique;     dirty = true; }
                if (field.IsIndexed   != colInfo.IsIndexed)   { field.IsIndexed   = colInfo.IsIndexed;   dirty = true; }
                if (dirty)
                {
                    field.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }
        }

        return dto;
    }
}
