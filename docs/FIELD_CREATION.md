# Field Creation — Backend Architecture

## Overview

When the frontend creates a field, it sends a **nested payload** with two distinct concerns:

```
POST /api/v1/collections/{collectionId}/fields
```

```json
{
  "field": "email",
  "type": "character varying",
  "meta": {
    "interface": "input",
    "width": "full",
    "required": true,
    "options": { "placeholder": "Enter email...", "max_length": 100 }
  },
  "schema": {
    "data_type": "character varying",
    "default_value": null,
    "max_length": 255,
    "is_nullable": true,
    "is_unique": false,
    "is_indexed": false
  }
}
```

**Two responsibilities:**

| Part | Stored where | What it describes |
|------|-------------|-------------------|
| `meta` | `field_metas` table row | UI behaviour, display config, validation rules |
| `schema` | Dynamic table column (DDL) | DB column type, constraints, index |

---

## Pipeline

```
Frontend Payload
       ↓
CreateFieldDto  (Fields/DTOs/CreateFieldDto.cs)
       ↓
FieldsService.CreateAsync  (Fields/FieldsService.cs)
       ↓
     ┌─► DdlService.AddColumnAsync  (Common/DDL/DdlService.cs)  → ALTER TABLE + optional INDEX
     │
     └─► new FieldMeta { ... }  → INSERT INTO field_metas
```

---

## How to add a new `meta` property

These are stored in the `field_metas` table. Example: adding a `placeholder` meta field.

### 1. FieldMeta entity — add property

File: `Fields/FieldMeta.cs`

```csharp
public string? Placeholder { get; set; }
```

### 2. FieldMetaDto — add property for deserialization

File: `Fields/DTOs/CreateFieldDto.cs`

```csharp
public class FieldMetaDto
{
    // ... existing fields
    public string? Placeholder { get; set; }
}
```

### 3. FieldsService — map DTO → entity

File: `Fields/FieldsService.cs`, inside `CreateAsync`:

```csharp
var field = new FieldMeta
{
    // ... existing fields
    Placeholder = dto.Meta?.Placeholder,
};
```

### 4. EF Migration

```bash
dotnet ef migrations add AddFieldPlaceholder
dotnet ef database update
```

---

## How to add a new `schema` (DDL) property

These affect the dynamic table column in PostgreSQL. Example: adding a `CHECK` constraint.

### 1. DdlService — add parameter + SQL

File: `Common/DDL/DdlService.cs`

```csharp
public async Task<int> AddColumnAsync(
    // ... existing params
    string? checkExpression = null)    // 👈 new param
{
    // ... existing SQL building

    if (!string.IsNullOrEmpty(checkExpression))
        sql += $" CHECK ({checkExpression})";

    // ...
}
```

### 2. FieldSchemaDto — add property

File: `Fields/DTOs/CreateFieldDto.cs`

```csharp
public class FieldSchemaDto
{
    // ... existing fields
    public string? CheckExpression { get; set; }
}
```

### 3. FieldsService — pass to DDL

File: `Fields/FieldsService.cs`, inside `CreateAsync`:

```csharp
await _ddl.AddColumnAsync(
    // ... existing args
    dto.Schema?.CheckExpression);
```

_No migration needed_ — this only affects the dynamic table, not `field_metas`.

---

## All layers at a glance

| Layer | File | Purpose |
|-------|------|---------|
| **Entity** | `Fields/FieldMeta.cs` | ORM class — columns in `field_metas` table |
| **DTO** | `Fields/DTOs/CreateFieldDto.cs` | Deserialize the nested frontend payload |
| **Service** | `Fields/FieldsService.cs` | Business logic — validates, calls DDL, saves entity |
| **DDL Service** | `Common/DDL/DdlService.cs` | Raw SQL — `ALTER TABLE ... ADD COLUMN` + constraints |
| **Controller** | `Fields/FieldsController.cs` | HTTP endpoint — delegates to service |
| **DB Config** | `Data/AppDbContext.cs` | EF config — max lengths, defaults, relationships |
| **Migration** | `Migrations/*.cs` | Auto-generated from entity changes; run `dotnet ef migrations add` + `database update` |
| **Enum** | `Fields/FieldMeta.cs` (`FieldType`) | Allowed types for the old enum-based path |

---

## FieldType enum values

Used only by the **old enum-based code path** (e.g., `CreateOptionalFieldsAsync` in `CollectionService`).  
The new path accepts raw PostgreSQL type strings directly (`character varying`, `text`, `integer`, etc.).

Current enum: `STRING`, `TEXT`, `NUMBER`, `BOOLEAN`, `DATE`, `UUID`, `BIGINT`, `FLOAT`, `DECIMAL`

Add new entries in `Fields/FieldMeta.cs` — both the enum and the `FieldTypeSchema` dictionary with the `DbType` mapping.

---

## Key files

```
backend/
├── Fields/
│   ├── FieldMeta.cs           ← entity + enum + type schema
│   ├── FieldsService.cs       ← CRUD logic
│   ├── FieldsController.cs    ← HTTP API
│   └── DTOs/
│       ├── CreateFieldDto.cs  ← nested payload DTO (meta + schema)
│       ├── UpdateFieldDto.cs  ← update DTO (flat)
│       ├── ReorderFieldDto.cs ← sort order DTO
│       └── FieldTypeDto.cs    ← enum response DTO
├── Common/DDL/
│   └── DdlService.cs          ← CREATE / ALTER / DROP dynamic tables
└── Data/
    └── AppDbContext.cs         ← EF config
```
