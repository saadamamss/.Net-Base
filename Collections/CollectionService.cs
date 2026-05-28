using DataForge.Collections.DTOs;
using DataForge.Common.DDL;
using DataForge.Common.Sanitizer;
using DataForge.Data;
using DataForge.Fields;
using Microsoft.EntityFrameworkCore;

namespace DataForge.Collections;

public class CollectionService
{
    private readonly AppDbContext _db;
    private readonly IdentifierSanitizerService _sanitizer;
    private readonly DdlService _ddl;

    public CollectionService(AppDbContext db, IdentifierSanitizerService sanitizer, DdlService ddl)
    {
        _db = db;
        _sanitizer = sanitizer;
        _ddl = ddl;
    }

    public async Task<CollectionResponseDto> CreateAsync(CreateCollectionDto dto)
    {
        var tableName = _sanitizer.BuildTableName(dto.Collection);

        var existing = await _db.CollectionMetas
            .FirstOrDefaultAsync(c => c.TableName == tableName);

        if (existing != null)
            throw new InvalidOperationException($"Collection \"{dto.Collection}\" already exists");

        var pkType = dto.Meta?.PkType ?? "auto-increment";
        var primaryKey = dto.Meta?.PrimaryKey ?? "id";

        await _ddl.CreateTableAsync(tableName, pkType);

        var collection = new CollectionMeta
        {
            Name = dto.Collection,
            TableName = tableName,
            Singleton = false,
            PrimaryKey = primaryKey,
            PkType = pkType,
        };

        _db.CollectionMetas.Add(collection);
        await _db.SaveChangesAsync();

        // Create FieldMeta for the primary key column (id)
        var pkFieldType = pkType switch
        {
            "auto-increment" => "INTEGER",
            "string" => "STRING",
            _ => "UUID"
        };

        _db.FieldMetas.Add(new FieldMeta
        {
            CollectionId = collection.Id,
            Name = _sanitizer.ToSnakeCase(primaryKey),
            Label = primaryKey.Equals("id", StringComparison.OrdinalIgnoreCase) ? "ID" : primaryKey,
            Type = pkFieldType,
            Required = true,
            SortOrder = 0,
            Hidden = true,
            Readonly = true,
            Searchable = false,
            Width = "full",
            IsSystem = true,
        });

        await CreateOptionalFieldsAsync(collection.Id, tableName, dto.Schema);

        // Return response
        return await BuildResponseAsync(collection.Id);
    }

    private async Task CreateOptionalFieldsAsync(int collectionId, string tableName, CollectionCreateSchemaDto? schema)
    {
        if (schema == null) return;

        if (schema.Status)
        {
            await _ddl.AddColumnAsync(tableName, "status", "BOOLEAN", false);
            _db.FieldMetas.Add(new FieldMeta
            {
                CollectionId = collectionId, Name = "status", Label = "Status",
                Type = "BOOLEAN", Required = false, SortOrder = 0,
                Hidden = false, Readonly = false, Searchable = false, Width = "full",
            });
        }

        if (schema.Sort)
        {
            await _ddl.AddColumnAsync(tableName, "sort", "INTEGER", false);
            _db.FieldMetas.Add(new FieldMeta
            {
                CollectionId = collectionId, Name = "sort", Label = "Sort",
                Type = "INTEGER", Required = false, SortOrder = 0,
                Hidden = true, Readonly = true, Searchable = false, Width = "full",
                IsSystem = true,
            });
        }

        if (schema.DateCreated)
        {
            await _ddl.AddColumnAsync(tableName, "created_at", "DATE", false);
            _db.FieldMetas.Add(new FieldMeta
            {
                CollectionId = collectionId, Name = "created_at", Label = "Created At",
                Type = "DATE", Required = false, SortOrder = 0,
                Hidden = true, Readonly = true, Searchable = false, Width = "full",
                IsSystem = true,
            });
        }

        if (schema.UserCreated)
        {
            await _ddl.AddColumnAsync(tableName, "created_by", "STRING", false);
            _db.FieldMetas.Add(new FieldMeta
            {
                CollectionId = collectionId, Name = "created_by", Label = "Created By",
                Type = "STRING", Required = false, SortOrder = 0,
                Hidden = true, Readonly = true, Searchable = false, Width = "full",
                IsSystem = true,
            });
        }

        if (schema.DateUpdated)
        {
            await _ddl.AddColumnAsync(tableName, "updated_at", "DATE", false);
            _db.FieldMetas.Add(new FieldMeta
            {
                CollectionId = collectionId, Name = "updated_at", Label = "Updated At",
                Type = "DATE", Required = false, SortOrder = 0,
                Hidden = true, Readonly = true, Searchable = false, Width = "full",
                IsSystem = true,
            });
        }

        if (schema.UserUpdated)
        {
            await _ddl.AddColumnAsync(tableName, "updated_by", "STRING", false);
            _db.FieldMetas.Add(new FieldMeta
            {
                CollectionId = collectionId, Name = "updated_by", Label = "Updated By",
                Type = "STRING", Required = false, SortOrder = 0,
                Hidden = true, Readonly = true, Searchable = false, Width = "full",
                IsSystem = true,
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<CollectionResponseDto>> GetAllListAsync()
    {
        var collections = await _db.CollectionMetas
            .Include(c => c.Fields.OrderBy(f => f.SortOrder))
            .ToListAsync();

        var result = new List<CollectionResponseDto>();

        foreach (var c in collections)
        {
            result.Add(await BuildResponseFromEntityAsync(c));
        }

        return result;
    }

    public async Task<CollectionResponseDto> GetByIdAsync(int collectionId)
    {
        var collection = await _db.CollectionMetas
            .Include(c => c.Fields.OrderBy(f => f.SortOrder))
            .FirstOrDefaultAsync(c => c.Id == collectionId);

        if (collection == null) throw new KeyNotFoundException("Collection not found.");

        return await BuildResponseFromEntityAsync(collection);
    }

    public async Task DeleteAsync(int collectionId)
    {
        var collection = await _db.CollectionMetas
            .FirstOrDefaultAsync(c => c.Id == collectionId);

        if (collection == null) throw new KeyNotFoundException("Collection not found.");

        var m2mFields = await _db.FieldMetas
            .Where(f => f.CollectionId == collectionId && (f.Special == "m2m" || f.Special == "files"))
            .ToListAsync();

        var fieldNames = m2mFields.Select(f => f.Name).ToHashSet();
        var m2mRelations = await _db.RelationMetas
            .Where(r => r.OneCollection == collection.TableName && fieldNames.Contains(r.OneField ?? ""))
            .ToListAsync();
        var junctionTables = m2mRelations
            .Select(r => r.ManyCollection)
            .Distinct()
            .ToList();

        foreach (var jt in junctionTables.Distinct())
        {
            var rels = await _db.RelationMetas
                .Where(r => r.ManyCollection == jt)
                .ToListAsync();
            _db.RelationMetas.RemoveRange(rels);

            await _ddl.DropTableAsync(jt);
        }

        await _ddl.DropTableAsync(collection.TableName);
        _db.CollectionMetas.Remove(collection);

        await _db.SaveChangesAsync();
    }

    private async Task<CollectionResponseDto> BuildResponseAsync(int collectionId)
    {
        var collection = await _db.CollectionMetas
            .Include(c => c.Fields.OrderBy(f => f.SortOrder))
            .FirstAsync(c => c.Id == collectionId);

        return await BuildResponseFromEntityAsync(collection);
    }

    private async Task<CollectionResponseDto> BuildResponseFromEntityAsync(CollectionMeta collection)
    {
        // اقرأ actual PK type من الـ PkType المخزّن
        string pkDbType = collection.PkType switch
        {
            "auto-increment" => "integer",
            "string"         => "character varying",
            _                => "uuid"
        };

        // تحقق من information_schema إن الجدول فعلاً موجود
        var tableExists = await _ddl.TableExistsAsync(collection.TableName);

        return new CollectionResponseDto
        {
            Collection = collection.Name,
            Meta = new CollectionMetaDto
            {
                Id         = collection.Id,
                Name       = collection.Name,
                TableName  = collection.TableName,
                Singleton  = collection.Singleton,
                PrimaryKey = collection.PrimaryKey,
                PkType     = collection.PkType,
                CreatedAt  = collection.CreatedAt,
                UpdatedAt  = collection.UpdatedAt,
            },
            Schema = new CollectionSchemaDto
            {
                Name           = tableExists ? collection.TableName : "",
                PrimaryKeyType = pkDbType,
            },
        };
    }
}
