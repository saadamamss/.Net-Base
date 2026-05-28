using DataForge.Common.DDL;
using DataForge.Data;
using DataForge.Relations.DTOs;
using Microsoft.EntityFrameworkCore;

namespace DataForge.Relations;

public class RelationsService
{
    private readonly AppDbContext _db;
    private readonly DdlService   _ddl;

    public RelationsService(AppDbContext db, DdlService ddl)
    {
        _db  = db;
        _ddl = ddl;
    }

    public async Task<List<RelationResponseDto>> GetAllAsync()
    {
        var relations = await _db.RelationMetas
            .OrderBy(r => r.ManyCollection)
            .ThenBy(r => r.ManyField)
            .ToListAsync();

        return await MapRelationsAsync(relations);
    }

    public async Task<List<RelationResponseDto>> GetByCollectionAsync(int collectionId)
    {
        var collection = await _db.CollectionMetas
            .FirstOrDefaultAsync(c => c.Id == collectionId);

        if (collection is null)
            throw new KeyNotFoundException("Collection not found");

        var relations = await _db.RelationMetas
            .Where(r => r.ManyCollection == collection.TableName
                     || r.OneCollection  == collection.TableName)
            .OrderBy(r => r.ManyCollection)
            .ThenBy(r => r.ManyField)
            .ToListAsync();

        return await MapRelationsAsync(relations);
    }

    // ── Private helpers ───────────────────────────────────────────

    private async Task<List<RelationResponseDto>> MapRelationsAsync(List<RelationMeta> relations)
    {
        var result = new List<RelationResponseDto>();

        // Detect M2M junction pairs: same ManyCollection appears >1 and is not a real collection
        var collectionTableNames = (await _db.CollectionMetas.Select(c => c.TableName).ToListAsync()).ToHashSet();
        var m2mPairs = relations
            .GroupBy(r => r.ManyCollection)
            .Where(g => g.Count() > 1 && !collectionTableNames.Contains(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var r in relations)
        {
            var fkInfo = await _ddl.GetForeignKeyInfoAsync(r.ManyCollection, r.ManyField);

            string? junctionCollection   = null;
            string? junctionField        = null;
            string? relatedJunctionField = null;

            // M2M: derive junction info from the pair of relation_metas rows
            if (m2mPairs.TryGetValue(r.ManyCollection, out var pair))
            {
                var partner = pair.FirstOrDefault(x => x.Id != r.Id);
                if (partner != null)
                {
                    junctionCollection   = r.ManyCollection;
                    junctionField        = r.ManyField;
                    relatedJunctionField = partner.ManyField;
                }
            }

            result.Add(new RelationResponseDto
            {
                Collection        = r.ManyCollection,
                Field             = r.ManyField,
                RelatedCollection = r.OneCollection,

                Schema = new RelationSchemaDto
                {
                    ConstraintName   = fkInfo?.ConstraintName,
                    Table            = fkInfo?.Table,
                    Column           = fkInfo?.Column,
                    ForeignKeySchema = fkInfo?.ForeignKeySchema,
                    ForeignKeyTable  = fkInfo?.ForeignKeyTable,
                    ForeignKeyColumn = fkInfo?.ForeignKeyColumn,
                    OnUpdate         = fkInfo?.OnUpdate,
                    OnDelete         = fkInfo?.OnDelete,

                    JunctionCollection   = junctionCollection,
                    JunctionField        = junctionField,
                    RelatedJunctionField = relatedJunctionField,
                },

                Meta = new RelationMetaResponseDto
                {
                    Id             = r.Id,
                    ManyCollection = r.ManyCollection,
                    ManyField      = r.ManyField,
                    OneCollection  = r.OneCollection,
                    OneField       = r.OneField,
                    OneDeselectAction = r.OnDeselect,
                    JunctionField     = junctionField,
                },
            });
        }

        return result;
    }
}
