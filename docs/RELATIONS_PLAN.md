# Relations Feature — Implementation Plan (M2O / O2M / M2M)

## Overview

Add full relation support to DataForge. Users can create three relation types:
- **M2O** (Many-to-One): `posts.author_id → users.id` — FK lives on this collection
- **O2M** (One-to-Many): `posts.comments` (virtual) ← `comments.post_id` — FK lives on related collection
- **M2M** (Many-to-Many): `posts.creators` ↔ `creators.posts` via junction table

All three types are created through the field creation flow. The backend handles DDL, `relation_metas` insertion, and resolution at read time.

---

## 0. How the `files` Field Type Works — The Reference M2M Pattern

> **Read this before implementing anything.** The built-in `files` field type is a real, already-working M2M in the system. It is the clearest example of how every M2M relation is structured — in the DB, in `relation_metas`, and in the resolver. All generic M2M you implement must follow this exact same pattern.

### What happens when a user creates a `files` field on `posts`

The user creates a field named e.g. `images` with `interface = "files"` on the `posts` collection.

**DDL result:**
```
posts               (unchanged — no new column)
posts_files         (new junction table)
  ├── id                    UUID PK
  ├── posts_id              UUID FK → posts.id
  └── file_metas_id         UUID FK → file_metas.id
file_metas          (system table — already exists, never touched)
```

There is **no column added to `posts`**. The field `images` is purely virtual — it only exists as metadata.

**`relation_metas` result — always 2 rows for any M2M:**

| # | many_collection | many_field | one_collection | one_field |
|---|---|---|---|---|
| Row 1 | `posts_files` | `file_metas_id` | `file_metas` | `NULL` |
| Row 2 | `posts_files` | `posts_id` | `posts` | `images` |

**Row 2 is the key insight:** `one_field = "images"` is how the system knows that `posts.images` is a valid virtual field even though no `images` column exists on the `posts` table. It is pure metadata.

### How the resolver uses these 2 rows

When the API sees `?fields=images.*` on a `posts` query:

1. Looks up `relation_metas` where `one_collection = "posts"` AND `one_field = "images"` → finds Row 2
2. Reads `many_collection = "posts_files"` → that is the junction table
3. Looks up `relation_metas` where `many_collection = "posts_files"` AND `many_field ≠ "posts_id"` → finds Row 1 → `one_collection = "file_metas"` is the related table, `many_field = "file_metas_id"` is the FK to pluck
4. Executes: `SELECT file_metas_id FROM posts_files WHERE posts_id = <thisRow.id>`
5. Executes: `SELECT * FROM file_metas WHERE id = ANY(<ids from step 4>)`
6. For each file row, constructs `url` from `filename_disk` (same logic as current `ItemsService.ResolveRelatedRow` lines 158-161)
7. Returns the array of file objects as `row["images"]`

### Why this matters for your implementation

- **Generic M2M** (`relation.type = "m2m"`) must produce the exact same 2-row pattern in `relation_metas`, just pointing to user collections instead of `file_metas`.
- **The `files` interface** (`interface = "files"` on a field) is just a shorthand for M2M where `relatedCollection` is hardcoded to `file_metas` and the junction table is named `{thisTable}_files`. Treat it as a special case of M2M in `FieldsService.CreateAsync` — detect `interface == "files"`, fill in the M2M defaults, then run the same M2M code path.
- **`one_field` on Row 2 must equal the virtual field name** the user chose (e.g. `"images"`). This is the only way `ResolveRelations` can find the right metadata when resolving that field name.
- **`one_field` on Row 1 is always `NULL`** — the related collection side (`file_metas` / user's related collection) doesn't get a virtual back-reference unless you implement bidirectional M2M later.

### `files` interface as a first-class M2M in `FieldsService.CreateAsync`

```
Input (files interface):
  field: "images"
  type: "ALIAS"
  interface: "files"
  relation: null   ← not sent from frontend, derived here

Derived as M2M:
  relatedTable         = "file_metas"         ← hardcoded for files
  junctionTable        = "{thisTable}_files"  ← e.g. "col_posts_files"
  junctionField        = "{thisCollectionName}_id"  e.g. "post_id"
  relatedJunctionField = "file_metas_id"      ← hardcoded for files

Then runs the exact same M2M code path as generic M2M:
  → CreateJunctionTableAsync(...)
  → INSERT relation_metas row 1 (file_metas side, one_field = NULL)
  → INSERT relation_metas row 2 (posts side, one_field = "images")
  → Special = "m2m" on field_meta
  → Options = { junctionCollection, junctionField, relatedJunctionField }
```

> ⚠️ The `files` interface creates a junction table named `{collectionTable}_files` (e.g. `col_posts_files`). This matches Directus convention and avoids collision with the system `file_metas` table.

> ⚠️ The existing `image` interface (single file M2O) is **not** the same as `files` (M2M). `image` adds a real UUID FK column to the collection table. `files` creates a junction table. Do not confuse them.

---

## What Already Exists (don't touch)

- `relation_metas` table + `RelationMeta.cs` — already in DB ✅
- `DdlService.AddForeignKeyColumnAsync` — used by image field ✅
- `DdlService.CreateTableAsync` — already exists ✅
- `DdlService.DropTableAsync` — already exists ✅
- `DdlService.DropColumnAsync` — already exists ✅
- `ItemsService.ResolveRelations` — currently handles M2O only, needs extending ✅
- `FieldsService.CreateAsync` — handles `interface == "image"` M2O, needs generalizing

---

## 1. DTO Changes

### `CreateFieldDto.cs` — add `RelationDto`

```csharp
public class RelationDto
{
    public string Type { get; set; } = string.Empty;          // "m2o" | "o2m" | "m2m"
    public string RelatedCollection { get; set; } = string.Empty; // collection NAME (not table name)

    // O2M only
    public string? ForeignKey { get; set; }        // field name on related table e.g. "post_id"

    // M2M only
    public string? JunctionCollection { get; set; }       // auto-generated if empty
    public string? JunctionField { get; set; }            // FK on junction pointing to THIS collection e.g. "post_id"
    public string? RelatedJunctionField { get; set; }     // FK on junction pointing to RELATED collection e.g. "creator_id"
}
```

Add to `CreateFieldDto`:
```csharp
public RelationDto? Relation { get; set; }
```

---

## 2. Add `ALIAS` to `FieldType` enum

O2M and M2M fields are **virtual** — they don't add a real column to the collection table. Add `ALIAS` to the `FieldType` enum and `FieldTypeSchema`:

```csharp
// In FieldType enum
ALIAS,

// In FieldTypeSchema._schema
[FieldType.ALIAS] = new FieldTypeDefinition
{
    Label = "Alias",
    DbType = "",              // no DB type — never used for DDL
    FormComponent = "None",
    InputType = "none"
}
```

---

## 3. `FieldsService.CreateAsync` — handle all 3 relation types

After the existing field creation logic, add relation handling. The key rule:

> **Skip `AddColumnAsync`** when `dto.Relation != null && dto.Relation.Type != "m2o"` — ALIAS fields have no real column.

Also skip `AddColumnAsync` when `dto.Type == "ALIAS"`.

### M2O (generalized from image)

```
Input:
  field: "author"
  type: "UUID"
  relation.type: "m2o"
  relation.relatedCollection: "users"

Actions:
  1. AddColumnAsync — already done by normal flow (UUID type)
  2. AddForeignKeyColumnAsync(thisTable, "author", relatedTable, "id")
     → BUT: since AddColumnAsync already added the column, we need a version
       that only adds the FK constraint, not the column again.
       Use: ALTER TABLE ADD CONSTRAINT instead, or check column existence.
       Simplest fix: skip AddColumnAsync for M2O relation fields, let
       AddForeignKeyColumnAsync handle both column + FK (same as image flow).
  3. INSERT relation_metas:
       many_collection = col_posts
       many_field      = author
       one_collection  = col_users
       one_field       = NULL
  4. Special = "m2o" on field_meta
```

### O2M

```
Input:
  field: "comments"       ← virtual field name on posts
  type: "ALIAS"
  relation.type: "o2m"
  relation.relatedCollection: "comments"
  relation.foreignKey: "post_id"   ← FK to add on comments table

Actions:
  1. Skip AddColumnAsync (ALIAS type)
  2. AddForeignKeyColumnAsync("col_comments", "post_id", "col_posts", "id")
     → adds post_id UUID REFERENCES col_posts(id) ON DELETE SET NULL on comments table
  3. INSERT relation_metas:
       many_collection = col_comments
       many_field      = post_id
       one_collection  = col_posts
       one_field       = comments   ← virtual field name on posts
  4. Special = "o2m" on field_meta
```

### M2M

```
Input:
  field: "creators"        ← virtual field name on posts
  type: "ALIAS"
  relation.type: "m2m"
  relation.relatedCollection: "creators"
  relation.junctionCollection: ""  (auto-generate if empty)
  relation.junctionField: ""       (auto-generate if empty)
  relation.relatedJunctionField: "" (auto-generate if empty)

Resolve names:
  junctionTable       = relation.junctionCollection ?? "{thisTable}_{relatedTable}"
                        e.g. "col_posts_col_creators"
  junctionField       = relation.junctionField ?? "{thisCollectionName}_id"
                        e.g. "post_id"
  relatedJunctionField = relation.relatedJunctionField ?? "{relatedCollectionName}_id"
                        e.g. "creator_id"

Actions:
  1. Skip AddColumnAsync (ALIAS type)
  2. CreateTableAsync(junctionTable)
     → CREATE TABLE "col_posts_col_creators" (id UUID PRIMARY KEY DEFAULT gen_random_uuid())
  3. AddForeignKeyColumnAsync(junctionTable, junctionField, thisTable, "id")
     → adds post_id UUID REFERENCES col_posts(id)
  4. AddForeignKeyColumnAsync(junctionTable, relatedJunctionField, relatedTable, "id")
     → adds creator_id UUID REFERENCES col_creators(id)
  5. INSERT relation_metas row 1:
       many_collection = col_posts_col_creators
       many_field      = post_id
       one_collection  = col_posts
       one_field       = creators   ← virtual field name on posts
  6. INSERT relation_metas row 2:
       many_collection = col_posts_col_creators
       many_field      = creator_id
       one_collection  = col_creators
       one_field       = NULL
  7. Special = "m2m" on field_meta
     Options = { "junctionCollection": "col_posts_col_creators", "junctionField": "post_id", "relatedJunctionField": "creator_id" }
     → store junction info in field Options so resolver can use it without querying relation_metas twice
```

### Files (preset M2M to file_metas)

```
Input:
  field: "images"
  type: "ALIAS"
  interface: "files"
  relation: null  ← not sent, derived from interface

Detection in CreateAsync:
  if (dto.Meta?.Interface == "files"):
      → treat as M2M with hardcoded params:
          relatedCollection  = "file_metas"        (resolved to table name)
          if no junction name provided:
              junctionTable       = "{thisTable}_files"        e.g. "col_posts_files"
              junctionField       = "{thisCollectionName}_id"  e.g. "post_id"
              relatedJunctionField = "file_metas_id"           (hardcoded)
      → run same M2M code path

Actions:
  Same as generic M2M, with hardcoded params:
  1. Skip AddColumnAsync (ALIAS type)
  2. CreateJunctionTableAsync(junctionTable, junctionField, thisTable, relatedJunctionField, "file_metas")
  3. INSERT relation_metas row 1:
       many_collection = junctionTable
       many_field      = file_metas_id
       one_collection  = file_metas
       one_field       = NULL
  4. INSERT relation_metas row 2:
       many_collection = junctionTable
       many_field      = junctionField  (e.g. post_id)
       one_collection  = thisTable
       one_field       = field.Name     (e.g. "images")
  5. Special = "m2m" on field_meta
  6. Options = { junctionCollection: junctionTable, junctionField, relatedJunctionField: "file_metas_id" }
```

---

## 4. `FieldsService.DeleteAsync` — clean up all relation types

Extend existing delete logic:

```
Read field.Special:

"m2o":
  1. DROP COLUMN field.Name FROM thisTable
  2. DELETE relation_metas WHERE many_collection=thisTable AND many_field=field.Name

"o2m":
  1. Read relation_metas WHERE one_collection=thisTable AND one_field=field.Name
     → get many_collection (related table) and many_field (FK on related)
  2. DROP COLUMN many_field FROM many_collection
  3. DELETE relation_metas row

"m2m":
  1. Read field.Options → get junctionCollection
  2. DROP TABLE junctionCollection
  3. DELETE relation_metas WHERE many_collection=junctionCollection (both rows)

No Special (normal field):
  existing logic — DROP COLUMN + DELETE field_meta
```

---

## 5. `ItemsService.ResolveRelations` — extend for O2M and M2M

Current logic only handles M2O (single UUID → single object lookup).

### Detect relation type

After finding `relation` from `relation_metas`, check `relation.OneField`:

```
relation_metas lookup strategy:

M2O: many_collection = thisTable, many_field = fieldName
     → one_field IS NULL
     → fkValue is on the current row
     → fetch one row from one_collection WHERE id = fkValue

O2M: look up WHERE one_collection = thisTable AND one_field = fieldName
     → get many_collection and many_field
     → fetch array: SELECT * FROM many_collection WHERE many_field = thisRow.id

M2M: read field.Options.junctionCollection
     → SELECT relatedJunctionField FROM junctionCollection WHERE junctionField = thisRow.id
     → collect related IDs
     → SELECT * FROM relatedTable WHERE id = ANY(relatedIds)
     → return array
```

### Updated `ResolveRelations` pseudocode

```csharp
foreach (relationName, subFields) in relationFields:

  // Try M2O first (FK is on current row)
  var m2oRelation = await _db.RelationMetas
      .FirstOrDefaultAsync(r => r.ManyCollection == tableName && r.ManyField == relationName);

  if (m2oRelation != null && m2oRelation.OneField == null):
    // M2O — existing logic
    var fk = row[relationName] as Guid?
    if fk != null:
      row[relationName] = await ResolveRelatedRow(m2oRelation.OneCollection, fk, subFields)

  else:
    // Try O2M / M2M via one_field
    var virtualRelation = await _db.RelationMetas
        .FirstOrDefaultAsync(r => r.OneCollection == tableName && r.OneField == relationName);

    if (virtualRelation == null) continue;

    var field = fieldsMeta.FirstOrDefault(f => f.Name == relationName);
    
    if (field?.Special == "o2m"):
      // O2M — fetch array
      var thisId = row["id"] as Guid?
      if thisId != null:
        row[relationName] = await ResolveO2M(
            virtualRelation.ManyCollection,
            virtualRelation.ManyField,
            thisId.Value,
            subFields)

    elif (field?.Special == "m2m"):
      // M2M — go through junction
      var options = JsonSerializer.Deserialize<M2MOptions>(field.Options)
      var thisId = row["id"] as Guid?
      if thisId != null:
        row[relationName] = await ResolveM2M(
            options.JunctionCollection,
            options.JunctionField,
            options.RelatedJunctionField,
            virtualRelation.OneCollection,  // related table
            thisId.Value,
            subFields)
```

### New helper methods on `ItemsService`

```csharp
// O2M: SELECT * FROM manyTable WHERE manyField = thisId
private async Task<List<object?>> ResolveO2M(
    string manyTable, string manyField, Guid thisId, HashSet<string> subFields)

// M2M: SELECT relatedId FROM junction WHERE junctionField = thisId
//      then SELECT * FROM relatedTable WHERE id = ANY(relatedIds)
private async Task<List<object?>> ResolveM2M(
    string junctionTable, string junctionField, string relatedJunctionField,
    string relatedTable, Guid thisId, HashSet<string> subFields)
```

> ⚠️ **URL construction for file_metas**: Both `ResolveO2M` and `ResolveM2M` must construct `url` from `filename_disk` when the related table is `file_metas`. Same existing logic in `ItemsService.ResolveRelatedRow` (lines 158-161):
> ```csharp
> if (tableName == "file_metas" && dict.TryGetValue("filename_disk", out var fd) && fd is string filenameDisk)
>     dict["url"] = $"/uploads/images/{filenameDisk}";
> ```
> Extract this into a shared helper so O2M, M2M, and M2O resolution all use the same URL construction.

---

## 6. `QueryItemsDto` — already has `Fields` property ✅

No changes needed.

---

## 7. Frontend — Relations UI

### New component: `RelationSetupDrawer.tsx`

Shown when user picks relation interface in field creation. Has 3 tabs/modes: M2O, O2M, M2M.

**M2O UI:**
```
This Collection: [posts] (readonly)
Related Collection: [dropdown of all collections]
FK field name: auto-filled as "{fieldName}" (readonly, shown for info)
```

**O2M UI:**
```
This Collection: [posts] (readonly)  →  Related Collection: [dropdown]
FK field on related: [input, e.g. "post_id"]
```

**M2M UI:**
```
This Collection: [posts]  →  Junction: [auto-name, editable]  ←  Related Collection: [dropdown]
Junction FK 1: [auto "post_id", editable]
Junction FK 2: [auto "creator_id", editable]
```

### Update `interfacesByType` in `FieldTypeDrawer.tsx`

Add relation interfaces to `UUID` and `ALIAS` types:

```typescript
// UUID type
{ value: "m2o", title: "Many to One", subtitle: "Belongs to one", icon: ArrowRight }

// ALIAS type (new group)
{ value: "o2m", title: "One to Many", subtitle: "Has many", icon: List }
{ value: "m2m", title: "Many to Many", subtitle: "Belongs to many", icon: Grid }
```

### Update `CreateFieldDto` sent from frontend

When saving a relation field, send:
```typescript
{
  field: "comments",
  type: "ALIAS",         // or "UUID" for M2O
  meta: { interface: "o2m" },
  relation: {
    type: "o2m",
    relatedCollection: "comments",
    foreignKey: "post_id"
  }
}
```

### Item form — render relation fields

Add cases in `renderField` for relation types:

- **M2O**: dropdown/search to pick a single related item (same as image but generic)
- **O2M**: read-only list of related items (editing O2M items happens on their own form)
- **M2M**: multi-select or tag-style picker

For MVP: O2M and M2M can be **read-only displays** in the item form — just show the related items. Full editing UI can come later.

---

## 8. `FieldsService.CreateAsync` — fix double DDL for M2O

Currently for `interface == "image"`, `AddColumnAsync` runs first then `AddForeignKeyColumnAsync` runs again. The same issue will affect generic M2O.

**Fix:** Skip `AddColumnAsync` entirely when `dto.Relation?.Type == "m2o"` (or `interface == "image"`). Let `AddForeignKeyColumnAsync` handle the full column creation with FK constraint.

```csharp
var isRelationField = dto.Relation != null;
var isAliasType = dto.Type?.ToUpper() == "ALIAS";

if (!isRelationField && !isAliasType)
{
    await _ddl.AddColumnAsync(...); // normal fields only
}
```

---

## 9. `DdlService` — add `CreateJunctionTableAsync`

A dedicated method for junction tables to make the intent clear:

```csharp
public async Task CreateJunctionTableAsync(
    string junctionTable,
    string field1, string ref1Table,
    string field2, string ref2Table)
{
    var safe = _sanitizer.Sanitize(junctionTable, "table name");
    // CREATE TABLE with id + two FK columns in one shot
    var sql = $@"
        CREATE TABLE IF NOT EXISTS ""{safe}"" (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            ""{field1}"" UUID REFERENCES ""{ref1Table}"" (""id"") ON DELETE CASCADE,
            ""{field2}"" UUID REFERENCES ""{ref2Table}"" (""id"") ON DELETE CASCADE
        )";
    await _db.Database.ExecuteSqlRawAsync(sql);
}
```

> ⚠️ Use `CREATE TABLE IF NOT EXISTS` not `DROP TABLE IF EXISTS` + CREATE — the existing `CreateTableAsync` uses DROP first which is destructive.

---

## Order of Implementation

```
1. Add ALIAS to FieldType enum + FieldTypeSchema
2. Add RelationDto to CreateFieldDto
3. DdlService: add CreateJunctionTableAsync
4. FieldsService.CreateAsync:
     a. Fix double DDL issue (skip AddColumnAsync for relation fields)
     b. Handle M2O (generalize from image — same logic, just use relatedCollection)
     c. Handle O2M
      d. Handle M2M
      e. Handle Files (detect interface == "files", derive M2M to file_metas)
5. FieldsService.DeleteAsync: extend for o2m, m2m, and files cleanup
6. ItemsService: add ResolveO2M + ResolveM2M helpers
7. ItemsService.ResolveRelations: extend to detect and route o2m/m2m
8. Frontend: RelationSetupDrawer component
9. Frontend: update interfacesByType for relation interfaces
10. Frontend: update field creation flow to send RelationDto
11. Frontend: item form — render o2m/m2m as read-only lists (MVP)
```

---

## Key Notes for Implementation

> ⚠️ **Collection name vs table name**: `RelationDto.RelatedCollection` comes from the frontend as the collection **name** (e.g. "comments"), not the table name (e.g. "col_comments"). `FieldsService` must resolve it via `_db.CollectionMetas.FirstOrDefaultAsync(c => c.Name == dto.Relation.RelatedCollection)` to get the actual `TableName`.

> ⚠️ **Junction table naming**: auto-generated junction name uses table names not collection names: `{thisTable}_{relatedTable}` e.g. `col_posts_col_creators`. Keep it deterministic.

> ⚠️ **M2M Options storage**: store `{ junctionCollection, junctionField, relatedJunctionField }` in `field_meta.Options` (already a JSON text column) so the resolver doesn't need to do extra queries to find the junction table.

> ⚠️ **ResolveRelations needs field metadata**: currently `ResolveRelations` only has `tableName` and `row`. To detect O2M vs M2M via `field.Special`, it needs to also receive `FieldMeta[]`. Pass the collection's fields into the method — `ItemsService` already loads them for validation in `CreateAsync`/`UpdateAsync`, same pattern.

> ⚠️ **ALIAS fields must be excluded from INSERT/UPDATE**: `ValidateAndSanitizeBody` in `ItemsService` maps body keys against field names. ALIAS fields have no real column — make sure they're excluded from the sanitized data sent to `QueryBuilderService`. Check `field.Special == "o2m" || field.Special == "m2m"` and skip.

> ⚠️ **`files` interface detection in FieldsService.CreateAsync**: Detect via `dto.Meta?.Interface == "files"` (not via `dto.Relation`). The frontend sends `interface: "files"` without a `relation` object. Your code must resolve this to an M2M with `relatedCollection = "file_metas"` before the generic M2M code path runs.

> ⚠️ **Junction table for `files` uses `_files` suffix**: Name it `{thisTable}_files` (e.g. `col_posts_files`), not `{thisTable}_{relatedTable}`. This matches Directus convention and is shorter/cleaner. The `CreateJunctionTableAsync` call params must be adapted accordingly.

> ⚠️ **`file_metas` is a system table, not a user collection**: When resolving `files` fields, `ResolveM2M` queries `file_metas` directly (not via `CollectionMeta` lookup). The URL construction logic (`filename_disk → url`) must apply here just like the existing M2O image resolution.