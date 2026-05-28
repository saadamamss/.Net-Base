using DataForge.Common.Sanitizer;
using DataForge.Data;
using Microsoft.EntityFrameworkCore;

namespace DataForge.Common.DDL;

public class DdlService
{
    private readonly AppDbContext _db;
    private readonly IdentifierSanitizerService _sanitizer;

    public DdlService(AppDbContext db, IdentifierSanitizerService sanitizer)
    {
        _db = db;
        _sanitizer = sanitizer;
    }

    public async Task<int> CreateTableAsync(string tableName, string pkType = "auto-increment")
    {
        var safe = _sanitizer.Sanitize(tableName, "table name");

        await _db.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS \"{safe}\"");

        var pkColumn = pkType switch
        {
            "auto-increment" => "id SERIAL PRIMARY KEY",
            "string" => "id VARCHAR(255) PRIMARY KEY",
            _ => "id UUID PRIMARY KEY DEFAULT gen_random_uuid()"
        };

        var sql = $"CREATE TABLE \"{safe}\" ({pkColumn})";
        var result = await _db.Database.ExecuteSqlRawAsync(sql);

        return result;
    }

    public async Task<int> AddColumnAsync(
        string tableName,
        string columnName,
        string fieldTypeStr,
        bool required,
        string? defaultValue = null,
        bool isUnique = false,
        bool isIndexed = false,
        int? maxLength = null)
    {
        var safeTable = _sanitizer.Sanitize(tableName, "table name");
        var safeColumn = _sanitizer.Sanitize(columnName, "column name");

        string dbType;

        if (Enum.TryParse<Fields.FieldType>(fieldTypeStr, true, out var fieldType))
        {
            var typeDef = Fields.FieldTypeSchema.GetDefinition(fieldType);
            dbType = typeDef.DbType;
        }
        else
        {
            var lower = fieldTypeStr.ToLowerInvariant();

            // Whitelist of allowed raw PostgreSQL types
            var allowedRawTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "text", "integer", "bigint", "smallint", "boolean", "uuid",
                "date", "timestamp", "timestamptz", "numeric", "real",
                "double precision", "json", "jsonb",
                "character varying", "varchar"
            };

            if (lower.StartsWith("varchar(") || lower.StartsWith("character varying(")
                || lower.StartsWith("numeric("))
            {
                // Allow parameterized types like VARCHAR(100), NUMERIC(10,2)
                dbType = fieldTypeStr.ToUpperInvariant();
            }
            else if (allowedRawTypes.Contains(lower))
            {
                dbType = lower == "character varying"
                    ? (maxLength.HasValue ? $"VARCHAR({maxLength.Value})" : "VARCHAR(255)")
                    : fieldTypeStr.ToUpperInvariant();
            }
            else
            {
                throw new ArgumentException($"Unsupported field type: \"{fieldTypeStr}\"");
            }
        }

        var sql = $"ALTER TABLE \"{safeTable}\" ADD COLUMN IF NOT EXISTS \"{safeColumn}\" {dbType}";

        string? resolvedDefault = defaultValue;

        if (string.IsNullOrEmpty(resolvedDefault))
        {
            if (Enum.TryParse<Fields.FieldType>(fieldTypeStr, true, out var parsed))
            {
                var def = Fields.FieldTypeSchema.GetDefinition(parsed);
                resolvedDefault = def.DefaultDbDefault;
                if (required && string.IsNullOrEmpty(resolvedDefault))
                {
                    resolvedDefault = parsed switch
                    {
                        Fields.FieldType.STRING or Fields.FieldType.TEXT => "''",
                        Fields.FieldType.DATE => "CURRENT_DATE",
                        Fields.FieldType.UUID => "gen_random_uuid()",
                        _ => null
                    };
                }
            }
            else if (required)
            {
                var lower = fieldTypeStr.ToLowerInvariant();
                resolvedDefault = lower switch
                {
                    string t when t.Contains("varchar") || t == "text" => "''",
                    string t when t == "integer" || t == "bigint" || t.Contains("numeric") || t == "smallint" => "0",
                    string t when t == "boolean" => "false",
                    string t when t == "uuid" => "gen_random_uuid()",
                    string t when t == "date" || t.Contains("timestamp") => "CURRENT_DATE",
                    _ => null
                };
            }
        }

        if (!string.IsNullOrEmpty(resolvedDefault))
        {
            sql += $" DEFAULT {resolvedDefault}";
        }

        if (required)
            sql += " NOT NULL";

        if (isUnique)
            sql += " UNIQUE";

        var result = await _db.Database.ExecuteSqlRawAsync(sql);

        if (isIndexed)
        {
            var indexSql = $"CREATE INDEX IF NOT EXISTS \"idx_{safeTable}_{safeColumn}\" ON \"{safeTable}\" (\"{safeColumn}\")";
            await _db.Database.ExecuteSqlRawAsync(indexSql);
        }

        return result;
    }

    public async Task<int> DropTableAsync(string tableName)
    {
        var safe = _sanitizer.Sanitize(tableName, "table name");
        var sql = $"DROP TABLE IF EXISTS \"{safe}\"";
        var result = await _db.Database.ExecuteSqlRawAsync(sql);
        return result;
    }


    public async Task<int> AlterColumnAsync(
        string tableName,
        string columnName,
        string? newType = null,
        string? newDefault = null,
        bool? isUnique = null,
        bool? isIndexed = null,
        bool newIndex = false)
    {
        var safeTable = _sanitizer.Sanitize(tableName, "table name");
        var safeColumn = _sanitizer.Sanitize(columnName, "column name");
        var result = 0;

        if (!string.IsNullOrEmpty(newType))
        {
            var sql = $"ALTER TABLE \"{safeTable}\" ALTER COLUMN \"{safeColumn}\" TYPE {newType}";
            result += await _db.Database.ExecuteSqlRawAsync(sql);
        }

        if (!string.IsNullOrEmpty(newDefault))
        {
            var sql = $"ALTER TABLE \"{safeTable}\" ALTER COLUMN \"{safeColumn}\" SET DEFAULT '{newDefault}'";
            result += await _db.Database.ExecuteSqlRawAsync(sql);
        }

        if (isUnique == true)
        {
            var constraintName = $"uq_{safeTable}_{safeColumn}";
            var sql = $"ALTER TABLE \"{safeTable}\" ADD CONSTRAINT \"{constraintName}\" UNIQUE (\"{safeColumn}\")";
            result += await _db.Database.ExecuteSqlRawAsync(sql);

            if (isIndexed == true)
            {
                var indexSql = $"CREATE INDEX IF NOT EXISTS \"idx_{safeTable}_{safeColumn}\" ON \"{safeTable}\" (\"{safeColumn}\")";
                await _db.Database.ExecuteSqlRawAsync(indexSql);
            }
        }
        else if (isUnique == false)
        {
            var constraintName = $"uq_{safeTable}_{safeColumn}";
            var sql = $"ALTER TABLE \"{safeTable}\" DROP CONSTRAINT IF EXISTS \"{constraintName}\"";
            result += await _db.Database.ExecuteSqlRawAsync(sql);
        }

        if (isIndexed == true && isUnique != true)
        {
            var indexSql = $"CREATE INDEX IF NOT EXISTS \"idx_{safeTable}_{safeColumn}\" ON \"{safeTable}\" (\"{safeColumn}\")";
            await _db.Database.ExecuteSqlRawAsync(indexSql);
        }
        else if (isIndexed == false)
        {
            var indexSql = $"DROP INDEX IF EXISTS \"idx_{safeTable}_{safeColumn}\"";
            await _db.Database.ExecuteSqlRawAsync(indexSql);
        }

        return result;
    }

    public async Task<int> RenameColumnAsync(string tableName, string oldName, string newName)
    {
        var safeTable = _sanitizer.Sanitize(tableName, "table name");
        var safeOld = _sanitizer.Sanitize(oldName, "column name");
        var safeNew = _sanitizer.Sanitize(newName, "column name");
        var sql = $"ALTER TABLE \"{safeTable}\" RENAME COLUMN \"{safeOld}\" TO \"{safeNew}\"";
        return await _db.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task<bool> TableExistsAsync(string tableName)
    {
        var safe = _sanitizer.Sanitize(tableName, "table name");

        var result = await _db.Database
            .SqlQueryRaw<bool>("SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_schema = 'public' AND table_name = {0}) AS \"Value\"", safe)
            .FirstAsync();
        return result;
    }

    private static string PkTypeToDbType(string pkType) => pkType switch
    {
        "auto-increment" => "INTEGER",
        "string" => "VARCHAR(255)",
        _ => "UUID"
    };

    public async Task AddForeignKeyColumnAsync(
        string tableName,
        string columnName,
        string targetTable,
        string targetColumn  = "id",
        bool   required      = false,
        string targetPkType  = "auto-increment",
        string onDelete      = "SET NULL")
    {
        var safeTable     = _sanitizer.Sanitize(tableName,    "table name");
        var safeColumn    = _sanitizer.Sanitize(columnName,   "column name");
        var safeTarget    = _sanitizer.Sanitize(targetTable,  "table name");
        var safeTargetCol = _sanitizer.Sanitize(targetColumn, "column name");

        var colType = PkTypeToDbType(targetPkType);
        var sql = $"ALTER TABLE \"{safeTable}\" ADD COLUMN IF NOT EXISTS \"{safeColumn}\" {colType}";

        if (required)
            sql += " NOT NULL";

        var onDeleteClause = onDelete.ToUpperInvariant() switch
        {
            "CASCADE"     => "ON DELETE CASCADE",
            "RESTRICT"    => "ON DELETE RESTRICT",
            "NO ACTION"   => "ON DELETE NO ACTION",
            "SET NULL"    => "ON DELETE SET NULL",
            "SET DEFAULT" => "ON DELETE SET DEFAULT",
            _             => "ON DELETE SET NULL"
        };

        sql += $" REFERENCES \"{safeTarget}\" (\"{safeTargetCol}\") {onDeleteClause}";

        await _db.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task CreateJunctionTableAsync(
        string junctionTable,
        string field1, string ref1Table, string ref1PkType,
        string field2, string ref2Table, string ref2PkType,
        string junctionPkType = "auto-increment")
    {
        var safe = _sanitizer.Sanitize(junctionTable, "table name");
        var safeF1 = _sanitizer.Sanitize(field1, "column name");
        var safeR1 = _sanitizer.Sanitize(ref1Table, "table name");
        var safeF2 = _sanitizer.Sanitize(field2, "column name");
        var safeR2 = _sanitizer.Sanitize(ref2Table, "table name");

        var col1Type = PkTypeToDbType(ref1PkType);
        var col2Type = PkTypeToDbType(ref2PkType);

        var pkColumn = junctionPkType switch
        {
            "auto-increment" => "id SERIAL PRIMARY KEY",
            "string" => "id VARCHAR(255) PRIMARY KEY",
            _ => "id UUID PRIMARY KEY DEFAULT gen_random_uuid()"
        };

        // Drop first in case a previous failed attempt left a stale table
        await _db.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS \"{safe}\"");

        var sql = $@"
            CREATE TABLE ""{safe}"" (
                {pkColumn},
                ""{safeF1}"" {col1Type} REFERENCES ""{safeR1}"" (""id"") ON DELETE CASCADE,
                ""{safeF2}"" {col2Type} REFERENCES ""{safeR2}"" (""id"") ON DELETE CASCADE
            )";
        await _db.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task DropColumnAsync(string tableName, string columnName)
    {
        var safeTable = _sanitizer.Sanitize(tableName, "table name");
        var safeColumn = _sanitizer.Sanitize(columnName, "column name");

        await _db.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{safeTable}\" DROP COLUMN IF EXISTS \"{safeColumn}\"");
    }

    // ── Column Info ───────────────────────────────────────────────
    public record ColumnInfo(
        string DbType,
        string? DefaultValue,
        int? MaxLength,
        bool IsNullable,
        bool IsUnique,
        bool IsIndexed
    );

    public async Task<ColumnInfo?> GetColumnInfoAsync(string tableName, string columnName)
    {
        var safe = _sanitizer.Sanitize(tableName, "table name");
        var safeCol = _sanitizer.Sanitize(columnName, "column name");

        // Column basic info from information_schema
        var sql = @"
            SELECT
                c.udt_name                          AS udt_name,
                c.data_type                         AS data_type,
                c.character_maximum_length          AS max_length,
                c.is_nullable                       AS is_nullable,
                c.column_default                    AS column_default
            FROM information_schema.columns c
            WHERE c.table_schema = 'public'
              AND c.table_name   = {0}
              AND c.column_name  = {1}";

        var row = await _db.Database
            .SqlQueryRaw<ColumnInfoRaw>(sql, safe, safeCol)
            .FirstOrDefaultAsync();

        if (row is null) return null;

        // Unique constraint check
        var uniqueSql = @"
            SELECT COUNT(*) AS ""Value""
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
               AND tc.table_schema    = kcu.table_schema
            WHERE tc.constraint_type = 'UNIQUE'
              AND tc.table_schema    = 'public'
              AND tc.table_name      = {0}
              AND kcu.column_name    = {1}";

        var uniqueCount = await _db.Database
            .SqlQueryRaw<int>(uniqueSql, safe, safeCol)
            .FirstAsync();

        // Index check (excluding unique indexes counted above)
        var indexName = $"idx_{safe}_{safeCol}";
        var indexSql = @"
            SELECT COUNT(*) AS ""Value""
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename  = {0}
              AND indexname  = {1}";

        var indexCount = await _db.Database
            .SqlQueryRaw<int>(indexSql, safe, indexName)
            .FirstAsync();

        // Build readable DbType
        var dbType = row.data_type.ToUpperInvariant() switch
        {
            "CHARACTER VARYING" => row.max_length.HasValue
                ? $"VARCHAR({row.max_length})"
                : "VARCHAR",
            "USER-DEFINED" => row.udt_name.ToUpperInvariant(),
            _ => row.data_type.ToUpperInvariant()
        };

        return new ColumnInfo(
            DbType:       dbType,
            DefaultValue: row.column_default,
            MaxLength:    row.max_length,
            IsNullable:   row.is_nullable == "YES",
            IsUnique:     uniqueCount > 0,
            IsIndexed:    indexCount > 0
        );
    }

    // Internal projection class — لازم يكون public عشان EF يقدر يعمله map
    public class ColumnInfoRaw
    {
        public string udt_name      { get; set; } = "";
        public string data_type     { get; set; } = "";
        public int?   max_length    { get; set; }
        public string is_nullable   { get; set; } = "YES";
        public string? column_default { get; set; }
    }

    // ── Foreign Key Info ────────────────────────────────────────────
    public record ForeignKeyInfo(
        string  ConstraintName,
        string  Table,
        string  Column,
        string  ForeignKeySchema,
        string  ForeignKeyTable,
        string  ForeignKeyColumn,
        string  OnUpdate,
        string  OnDelete
    );

    public async Task<ForeignKeyInfo?> GetForeignKeyInfoAsync(string tableName, string columnName)
    {
        var safe    = _sanitizer.Sanitize(tableName,  "table name");
        var safeCol = _sanitizer.Sanitize(columnName, "column name");

        var sql = @"
            SELECT
                tc.constraint_name                AS constraint_name,
                kcu.table_name                    AS table_name,
                kcu.column_name                   AS column_name,
                ccu.table_schema                  AS fk_schema,
                ccu.table_name                    AS fk_table,
                ccu.column_name                   AS fk_column,
                rc.update_rule                    AS on_update,
                rc.delete_rule                    AS on_delete
            FROM information_schema.table_constraints       tc
            JOIN information_schema.key_column_usage        kcu
                ON  tc.constraint_name = kcu.constraint_name
                AND tc.table_schema    = kcu.table_schema
            JOIN information_schema.referential_constraints rc
                ON  tc.constraint_name = rc.constraint_name
                AND tc.table_schema    = rc.constraint_schema
            JOIN information_schema.key_column_usage        ccu
                ON  rc.unique_constraint_name   = ccu.constraint_name
                AND rc.unique_constraint_schema = ccu.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema    = 'public'
              AND kcu.table_name     = {0}
              AND kcu.column_name    = {1}";

        var row = await _db.Database
            .SqlQueryRaw<ForeignKeyInfoRaw>(sql, safe, safeCol)
            .FirstOrDefaultAsync();

        if (row is null) return null;

        return new ForeignKeyInfo(
            ConstraintName:   row.constraint_name,
            Table:            row.table_name,
            Column:           row.column_name,
            ForeignKeySchema: row.fk_schema,
            ForeignKeyTable:  row.fk_table,
            ForeignKeyColumn: row.fk_column,
            OnUpdate:         row.on_update,
            OnDelete:         row.on_delete
        );
    }

    public class ForeignKeyInfoRaw
    {
        public string constraint_name { get; set; } = "";
        public string table_name      { get; set; } = "";
        public string column_name     { get; set; } = "";
        public string fk_schema       { get; set; } = "";
        public string fk_table        { get; set; } = "";
        public string fk_column       { get; set; } = "";
        public string on_update       { get; set; } = "NO ACTION";
        public string on_delete       { get; set; } = "NO ACTION";
    }
}
