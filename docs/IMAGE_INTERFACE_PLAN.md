# Image Interface — Implementation Plan

## Overview

Add an "image" interface to DataForge, following the Directus pattern: a file field that creates a real FK relation between the user's collection table and a built-in `file_metas` table, tracked through a `relation_metas` metadata table.

---

## 1. New Tables (EF Core Migrations)

### `file_metas`

```sql
CREATE TABLE file_metas (
  id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  filename_disk     VARCHAR NOT NULL,   -- actual stored filename (uuid.ext)
  filename_download VARCHAR NOT NULL,   -- original upload name (photo.jpg)
  title             VARCHAR,            -- optional display name
  type              VARCHAR NOT NULL,   -- MIME type (image/jpeg, etc)
  filesize          BIGINT NOT NULL,
  width             INTEGER,            -- null for non-images
  height            INTEGER,            -- null for non-images
  uploaded_by       UUID REFERENCES users(id),
  uploaded_on       TIMESTAMP DEFAULT NOW()
);
```

### `relation_metas`

```sql
CREATE TABLE relation_metas (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  many_collection VARCHAR NOT NULL,  -- "posts"
  many_field      VARCHAR NOT NULL,  -- "image"
  one_collection  VARCHAR NOT NULL,  -- "file_metas"
  one_field       VARCHAR            -- null for M2O
);
```

Relation type is **inferred**, not stored:
- `one_field IS NULL` → M2O (collection.field → one_collection)
- `one_field NOT NULL` → O2M (one_collection has a field pointing back)

**Unique index** on (many_collection, many_field).

---

## 2. Model Files

- `backend/Upload/FileMeta.cs` — entity class matching `file_metas` schema
- `backend/Relations/RelationMeta.cs` — entity class matching `relation_metas` schema
- Register both in `AppDbContext` (DbSet + OnModelCreating config)

---

## 3. Modify Upload Endpoint

**`UploadService.cs`**
- Inject `AppDbContext`
- On image upload: save file to disk as before, then create a `FileMeta` row
- Return `{ id, url }` instead of just `{ url }`

**`UploadController.cs`**
- Update response shape to match new return type

---

## 4. DDL Support for FK Columns

**`DdlService.cs`** — add method:
```
AddForeignKeyColumnAsync(tableName, columnName, targetTable, targetColumn, required)
```
Generates:
```sql
ALTER TABLE "safe_table" ADD COLUMN IF NOT EXISTS "safe_col" UUID
  REFERENCES "file_metas" ("id") ON DELETE SET NULL
```

---

## 5. Field Creation — "image" Interface Handling

**`FieldsService.CreateAsync`** — after creating the field normally:
- If `dto.Meta.Interface == "image"`:
  1. Call `DdlService.AddForeignKeyColumnAsync` to add UUID col + FK
  2. Insert a `RelationMeta` row:
     - many_collection = collection.TableName (e.g. "col_posts")
     - many_field = sanitizedName
     - one_collection = "file_metas"
     - one_field = null (→ M2O)

**`FieldsService.DeleteAsync`** — clean up all 3 layers:
1. `DROP COLUMN` from the collection table via `DdlService`
2. Delete associated `RelationMeta` row
3. Delete the field from `field_metas`

> ⚠️ Don't only delete the `RelationMeta` row — the actual column must also be dropped, otherwise it stays in the DB as an orphaned FK column.

---

## 6. Frontend — Add "image" to Interfaces

**`interfacesByType` in `FieldTypeDrawer.tsx`**:
- Add `image` to `UUID` type interfaces list:
  ```
  { value: "image", title: "Image", subtitle: "Single file upload", icon: Image }
  ```

No special interface config form needed for MVP.

---

## 7. ItemsService — Resolve Relations on Read

**Goal**: Support `?fields=title,image.*` query param that returns file_meta data nested inside the `image` field.

### `QueryItemsDto` — add `fields` property
```csharp
public string? Fields { get; set; }  // comma-separated, supports dot notation
```

### `ItemsController` — pass `fields` to service
- For both `FindMany` and `FindOne`, pass `query.Fields` to ItemsService

### `QueryBuilderService` / `ItemsService` — resolve logic
After fetching rows:
1. Parse `fields` param — group by prefix:
   - `title` → direct field
   - `image.*` or `image.filename_disk,image.type` → relation prefix
2. For each prefix, check `relation_metas` for (many_collection, many_field)
3. If relation found → query the target table (`file_metas`) for the FK value
4. Merge: replace UUID value with resolved object or sub-fields

Example flow:
```
Request: GET /items/posts/1?fields=title,image.filename_disk,image.type

1. SELECT title, image FROM col_posts WHERE id = 1
   → { title: "Hello", image: "some-uuid" }

2. Check relation_metas WHERE many_collection='col_posts' AND many_field='image'
   → one_collection = "file_metas"

3. SELECT filename_disk, type FROM file_metas WHERE id = 'some-uuid'
   → { filename_disk: "abc.jpg", type: "image/jpeg" }

4. Return: { title: "Hello", image: { filename_disk: "abc.jpg", type: "image/jpeg" } }
```

> ⚠️ `file_metas` has no `url` column. When resolving, construct it in the service layer:
> ```csharp
> url = $"/uploads/{fileMeta.FilenameDisk}"
> ```
> Add `url` as a computed/mapped property on `FileMeta` or build it during resolution. Never return it as `null`.

> ⚠️ When `image.*` is requested, avoid blindly passing `SELECT *` through to the response. At minimum be aware that `uploaded_by` and other internal fields are included — this matters when you add permissions later.

**For MVP**: only support 1 level deep (e.g. `image.*` or `image.field`), not nested relations.

---

## 8. Frontend — Upload UI in Item Form

### New API module: `frontend/src/lib/api/upload.ts`
```typescript
export async function uploadImage(file: File): Promise<{ id: string; url: string }>
```

### Item form — add `case "image"` in `renderField`
- Show current image preview (if value exists)
- Upload button → file picker → POST to `/upload/image` → store returned `id` in the field
- Show file name / url
- Remove/replace button

### Item form — send field value
- When saving, the field value is the UUID string (file_metas.id), same as any other field
- On load, show the preview through relation resolution (`?fields=image.url,image.*`)

---

## Order of Implementation

```
1. file_metas table (model + migration)
2. relation_metas table (model + migration)
3. Modify upload endpoint — creates file_metas row, returns { id, url }
4. AddForeignKeyColumnAsync to DdlService
5. FieldsService: handle "image" interface during create/delete
6. Frontend: add "image" to interfacesByType
7. ItemsService: resolve relations via relation_metas + fields query param
8. Frontend: upload API module + image UI in item form
```
