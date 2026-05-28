# Relations UI — Wireframe & Flow

## Overview

When creating a new field, the user can pick a relation type from the interface selector.
The field creation drawer has a **"Relationship"** tab in the left sidebar (same as Directus).
Clicking it shows the relation setup UI based on which relation type was chosen.

---

## Step 1 — Choose Relation Type

Before entering the field name, the user picks the relation type from the interface list.
Add a "Relationships" group in `interfacesByType` for `UUID` and `ALIAS` types,
and a "Files" group for the built-in M2M to `file_metas`:

```
┌─────────────────────────────────────┐
│  Choose an Interface                │
│                                     │
│  ── Relationships ──────────────── │
│                                     │
│  [↗ icon]  Many to One             │
│            Select a related item    │
│                                     │
│  [≡ icon]  One to Many             │
│            List of related items    │
│                                     │
│  [⊞ icon]  Many to Many            │
│            Multiple related items   │
│                                     │
│  ── Files ──────────────────────── │
│                                     │
│  [🗂 icon]  Files                  │
│            Multiple file uploads    │
│                                     │
└─────────────────────────────────────┘
```

M2O maps to type `UUID`, O2M and M2M map to type `ALIAS`, Files maps to type `ALIAS`.

---

## Step 2 — Field Name Input

Same as any normal field — user types the field name.

```
Field Name: [  comments         ]
```

For M2O this is the actual FK column name (e.g. `author`).
For O2M/M2M this is the virtual field name (e.g. `comments`, `creators`).

---

## Step 3 — Relationship Tab UI

After entering field name, user clicks the **"Relationship"** tab in the left sidebar.
The UI shown depends on the chosen relation type.

---

### M2O — Many to One

```
┌──────────────────────────────────────────────────────────────────┐
│                                                                  │
│   This Collection          Related Collection                    │
│   ┌──────────────┐         ┌──────────────────────────────┐     │
│   │   posts      │         │   users               [≡]   │     │
│   └──────────────┘         └──────────────────────────────┘     │
│                                                                  │
│   ┌──────────────┐    →    ┌──────────────────────────────┐     │
│   │   author     │         │   id                  [≡]   │     │
│   └──────────────┘         └──────────────────────────────┘     │
│   (field name,              (always id, readonly)               │
│    readonly)                                                     │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

- "This Collection" = current collection name, readonly
- "Related Collection" = dropdown of all collections, user picks one
- Left FK field = the field name user typed, readonly
- Right FK field = always `id`, readonly
- Arrow direction: left → right (many side points to one side)

---

### O2M — One to Many

```
┌──────────────────────────────────────────────────────────────────┐
│                                                                  │
│   This Collection          Related Collection                    │
│   ┌──────────────┐         ┌──────────────────────────────┐     │
│   │   posts      │         │   comments            [≡]   │     │
│   └──────────────┘         └──────────────────────────────┘     │
│                                                                  │
│   ┌──────────────┐    →    ┌──────────────────────────────┐     │
│   │   id         │         │   post_id             [≡]   │     │
│   └──────────────┘         └──────────────────────────────┘     │
│   (always id,               (FK field on related table,         │
│    readonly)                 user can edit, auto-filled          │
│                              as "{thisCollection}_id")           │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

- "This Collection" = current collection, readonly
- "Related Collection" = dropdown, user picks one
- Left field = always `id`, readonly
- Right field = FK field name on related table, **editable**, auto-filled as `{collectionName}_id`
- Arrow direction: left → right

---

### M2M — Many to Many

```
┌──────────────────────────────────────────────────────────────────┐
│                                                                  │
│  This Collection   Junction Collection    Related Collection     │
│  ┌─────────────┐   ┌──────────────────┐  ┌───────────────────┐  │
│  │   posts     │   │  posts_creators  │  │   creators  [≡]  │  │
│  └─────────────┘   └──────────────────┘  └───────────────────┘  │
│                     (auto-filled, editable)                      │
│                                                                  │
│  ┌─────────────┐ → ┌──────────────────┐                         │
│  │   id        │   │   post_id        │                         │
│  └─────────────┘   └──────────────────┘                         │
│  (readonly)         (editable, auto-filled)                      │
│                                                                  │
│                    ┌──────────────────┐ ← ┌───────────────────┐ │
│                    │   creator_id     │   │   id              │ │
│                    └──────────────────┘   └───────────────────┘ │
│                    (editable, auto-filled)  (readonly)           │
│                                                                  │
│                    ☐  Auto Fill                                  │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

- "This Collection" = current collection, readonly
- "Junction Collection" = auto-filled as `{thisTable}_{relatedTable}`, **editable**
- "Related Collection" = dropdown, user picks one
- Top arrow: `posts.id` → `junction.post_id` (editable, auto-filled as `{thisCollection}_id`)
- Bottom arrow: `junction.creator_id` ← `creators.id` (editable, auto-filled as `{relatedCollection}_id`)
- "Auto Fill" checkbox = when checked, auto-generates all junction field names (same as Directus)

---

### Files (M2M to file_metas)

```
┌──────────────────────────────────────────────────────────────────────┐
│                                                                      │
│  This Collection   Junction Collection    Related Table              │
│  ┌─────────────┐   ┌──────────────────┐  ┌───────────────────────┐  │
│  │   posts     │   │  col_posts_files │  │  file_metas  (system) │  │
│  └─────────────┘   └──────────────────┘  └───────────────────────┘  │
│                     (auto-filled, READONLY)   (always file_metas)   │
│                                                                      │
│  ┌─────────────┐ → ┌──────────────────┐                             │
│  │   id        │   │  post_id         │                             │
│  └─────────────┘   └──────────────────┘                             │
│  (readonly)         (auto-filled, READONLY)                          │
│                                                                      │
│                    ┌──────────────────┐ ← ┌───────────────────────┐ │
│                    │  file_metas_id   │   │  id                   │ │
│                    └──────────────────┘   └───────────────────────┘ │
│                    (auto-filled, READONLY)  (readonly)               │
│                                                                      │
│  ☑ Auto Fill (always on, not toggleable)                            │
│  ─────────────────────────────────────────────────────────────      │
│  This is a preset M2M to the built-in file_metas table.              │
│  All values are auto-configured and non-editable.                    │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

- "Related Table" is always `file_metas` — hardcoded, no dropdown
- Junction collection auto-named as `{collectionTable}_files` (e.g. `col_posts_files`) — **readonly**
- `junctionField` = `{collectionName}_id` — **readonly**
- `relatedJunctionField` = `file_metas_id` — **readonly**
- No user configuration needed — it's a preset

---

## Auto-fill Logic (frontend)

When user picks a Related Collection, auto-fill these fields:

### M2O:
```
No auto-fill needed — FK field = field name the user typed
```

### O2M:
```
foreignKey = "{thisCollectionName}_id"
e.g. collection = "posts" → post_id
```

### M2M:
```
junctionCollection      = "{thisTableName}_{relatedTableName}"
                          e.g. "col_posts_col_creators"
junctionField           = "{thisCollectionName}_id"     → "post_id"
relatedJunctionField    = "{relatedCollectionName}_id"  → "creator_id"
```

### Files (preset):
```
junctionCollection      = "{collectionTable}_files"    → "col_posts_files"
junctionField           = "{collectionName}_id"        → "post_id"
relatedJunctionField    = "file_metas_id"              (hardcoded)
```

All auto-filled values are **editable** by the user before saving, except for the **Files** preset where everything is readonly.

---

## Validation (frontend, before submit)

### M2O:
- Related collection must be selected
- Field name must not be empty

### O2M:
- Related collection must be selected
- FK field name must not be empty
- FK field name must be a valid identifier (no spaces, no special chars)

### M2M:
- Related collection must be selected
- Junction collection name must not be empty
- Both junction field names must not be empty
- Junction collection name must not already exist (show warning if it does)

### Files (preset):
- No validation needed — all values are auto-filled and readonly

---

## DTO sent to backend on save

## DTO sent to backend on save

### Files (preset M2M to file_metas):
```json
{
  "field": "images",
  "type": "ALIAS",
  "meta": { "interface": "files", "label": "Images" }
}
```
> No `relation` object needed — the backend detects `interface == "files"` and derives the M2M params automatically (relatedCollection = "file_metas", junction naming auto-configured).

---

### M2O:
```json
{
  "field": "author",
  "type": "UUID",
  "meta": { "interface": "m2o", "label": "Author" },
  "relation": {
    "type": "m2o",
    "relatedCollection": "users"
  }
}
```

### O2M:
```json
{
  "field": "comments",
  "type": "ALIAS",
  "meta": { "interface": "o2m", "label": "Comments" },
  "relation": {
    "type": "o2m",
    "relatedCollection": "comments",
    "foreignKey": "post_id"
  }
}
```

### M2M:
```json
{
  "field": "creators",
  "type": "ALIAS",
  "meta": { "interface": "m2m", "label": "Creators" },
  "relation": {
    "type": "m2m",
    "relatedCollection": "creators",
    "junctionCollection": "col_posts_col_creators",
    "junctionField": "post_id",
    "relatedJunctionField": "creator_id"
  }
}
```

---

## Where to implement in frontend

| File | What to add |
|---|---|
| `FieldTypeDrawer.tsx` | Add M2O to UUID interfaces, add O2M + M2M + Files to ALIAS interfaces |
| `RelationSetupTab.tsx` | New component — renders the correct UI based on `interface` value (m2o, o2m, m2m, files) |
| `FieldCreationDrawer.tsx` | Add "Relationship" tab to sidebar, render `RelationSetupTab` when active |
| `api/fields.ts` | Update `CreateFieldDto` type to include optional `relation` object |

For **Files** interface: the "Relationship" tab shows a read-only summary (no editable fields) since everything is preset.

---

## Notes

- The [≡] icon next to collection dropdowns opens a collection picker (same as existing collection selector in the app).
- All collection dropdowns show only **user-created collections**, not system tables like `file_metas`.
- The "Relationship" tab should only appear in the sidebar when the chosen interface is `m2o`, `o2m`, `m2m`, or `files`.
- For M2O, the "Field" tab (where user sets label, required, etc.) still works normally since a real column is created.
- For O2M and M2M (ALIAS), the "Field" tab should hide options that don't apply: Required, Default Value, Max Length.