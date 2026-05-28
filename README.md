# DataForge API

A **headless CMS** built with **.NET 8** — dynamically manages collections, fields, relations, and items at runtime. Inspired by Directus.

---

## Tech Stack

| Layer    | Technology |
|----------|-----------|
| **Runtime** | .NET 8 (ASP.NET Core) |
| **ORM** | Entity Framework Core 8 + Dapper 2.1 |
| **Database** | PostgreSQL (Npgsql) |
| **Auth** | ASP.NET Identity + JWT (HttpOnly Cookies) |
| **Mapping** | AutoMapper 14 |
| **Mail** | MailKit 4 |
| **Logging** | Serilog (Console + File) |
| **Docs** | Swagger (dev only) |
| **Rate Limiting** | `System.Threading.RateLimiting` |

---

## Features

### Dynamic Content Engine
- **Collections** — create tables at runtime with custom schemas
- **Fields** — String, Text, Integer, Boolean, Date, UUID, BigInt, Float, Decimal, Alias
- **Relations** — M2O, O2M, M2M with automatic junction tables
- **File/Image fields** — auto-generated URL enrichment for uploaded assets
- **Depth-controlled resolution** — nested relation resolving via field query syntax (e.g. `author.name`, `categories.categories_id.*`)

### Items API
- Full CRUD on any dynamic collection
- **Field selection** — `?fields=title,author.name`
- **Depth syntax** — `?fields=*,categories.*.*`
- **Pagination** — `?page=1&limit=20`
- **Bulk delete** — `DELETE /items/{collection}` with ID array
- **Dynamic type coercion** — auto-casts values by field type
- **M2M/O2M save** — manages junction tables and FK updates

### Auth & Security
- JWT access token in HttpOnly cookie (15 min)
- Refresh token in HttpOnly cookie (7 days)
- Email verification flow
- Forgot/reset password
- Account locking after 5 failed attempts
- Token revocation on logout/password change
- RBAC (Admin, User, Moderator)
- Rate limiting — 100 req/min global, 5 req/min login
- Security headers (X-Frame-Options, X-Content-Type-Options, X-XSS-Protection)

### Infrastructure
- Unified `ApiResponse<T>` envelope on every endpoint
- Global exception filter
- Request logging middleware (ID + timing)
- Cookie-to-header middleware
- CORS configured from `appsettings.json`
- AutoMapper profiles
- Swagger in development
- DB migration on startup + seed data
- File upload (images 5MB / files 10MB)

---

## Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL server running locally

### 1. Clone

```bash
git clone https://github.com/your-username/dataforge.git
cd dataforge/backend
```

### 2. Configure `appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=DataForgeDB;Username=postgres;Password=your-password;"
  },
  "JwtSettings": {
    "Secret": "your-super-secret-key-at-least-32-characters!!",
    "Issuer": "DataForge",
    "Audience": "DataForge",
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 7
  },
  "CorsSettings": {
    "AllowedOrigins": "http://localhost:3000"
  }
}
```

### 3. Run

```bash
dotnet run
```

Migrations run automatically on startup. The seeder creates an admin user:

| Field    | Value             |
|----------|-------------------|
| Email    | admin@starter.com |
| Password | Admin@123456      |

Swagger: `http://localhost:5000/swagger`

---

## API Reference

### Auth — `/api/v1/auth`

| Method   | Endpoint        | Auth     | Description                |
|----------|-----------------|----------|----------------------------|
| `POST`   | `/register`     | Public   | Register new user          |
| `POST`   | `/verify-email` | Public   | Verify email with token    |
| `POST`   | `/login`        | Public   | Login (sets HttpOnly cookies) |
| `POST`   | `/refresh`      | Cookie   | Refresh access token       |
| `POST`   | `/logout`       | Required | Logout + clear cookies     |
| `POST`   | `/forgot-password` | Public | Send reset email           |
| `POST`   | `/reset-password` | Public | Reset password with token  |
| `GET`    | `/me`           | Required | Get current user           |

### Items — `/api/v1/items/{collection}`

| Method   | Endpoint                      | Description                |
|----------|-------------------------------|----------------------------|
| `GET`    | `/{collection}`               | List items (paginated)     |
| `GET`    | `/{collection}/{id}`          | Get single item            |
| `POST`   | `/{collection}`               | Create item                |
| `PATCH`  | `/{collection}/{id}`          | Update item                |
| `DELETE` | `/{collection}`               | Bulk delete items          |
| `DELETE` | `/{collection}/{id}`          | Delete single item         |

**Query Parameters:**
| Param    | Type   | Default | Description |
|----------|--------|---------|-------------|
| `page`   | int    | 1       | Page number |
| `limit`  | int    | 20      | Items per page (max 100) |
| `fields` | string | —       | Field selection + relation depth |

**Field depth syntax:**
- `title,content` — flat fields only
- `image` — returns raw FK (depth 0)
- `image.*` — resolves all junction/FK columns (depth 1)
- `image.filename_disk` — resolves only that column
- `image.*.*` — resolves all FK relations recursively (depth 2)
- `author.name` — resolves M2O relation with specific field
- `categories.categories_id.name` — resolves nested M2M relation

### Collections — `/api/v1/collections`

| Method   | Endpoint         | Auth     | Description           |
|----------|------------------|----------|-----------------------|
| `GET`    | `/`              | Required | List all collections  |
| `GET`    | `/{id}`          | Required | Get single collection |
| `POST`   | `/`              | Required | Create collection     |
| `PATCH`  | `/{id}`          | Required | Update collection     |
| `DELETE` | `/{id}`          | Required | Delete collection     |

### Fields — `/api/v1/collections/{id}/fields`

| Method   | Endpoint        | Auth     | Description       |
|----------|-----------------|----------|-------------------|
| `GET`    | `/`             | Required | List fields       |
| `POST`   | `/`             | Required | Create field      |
| `PATCH`  | `/{id}`         | Required | Update field      |
| `DELETE` | `/{id}`         | Required | Delete field      |
| `PATCH`  | `/sort/reorder` | Required | Reorder fields    |

### Relations — `/api/v1/relations`

| Method   | Endpoint         | Auth     | Description           |
|----------|------------------|----------|-----------------------|
| `GET`    | `/`              | Required | List all relations    |
| `GET`    | `/{collectionId}` | Required | Get relations for collection |

### Upload — `/api/v1/upload`

| Method   | Endpoint    | Auth     | Description        |
|----------|-------------|----------|--------------------|
| `POST`   | `/image`    | Required | Upload image (5MB) |
| `POST`   | `/file`     | Required | Upload file (10MB) |

### Other

| Method   | Endpoint          | Auth     | Description              |
|----------|-------------------|----------|--------------------------|
| `GET`    | `/api/v1/health`  | Public   | Health check + DB status |
| `GET`    | `/api/v1/help`    | Public   | API help/documentation   |
| `GET`    | `/api/v1/folders` | Required | Get folder tree          |

---

## Project Structure

```
backend/
├── Auth/              → AuthController, AuthService, DTOs
├── Collections/       → Dynamic collection management
├── Common/
│   ├── DDL/           → Dynamic SQL schema operations
│   ├── Extensions/    → Middleware registration extensions
│   ├── Filters/       → GlobalExceptionFilter
│   ├── Middleware/     → Logging, CookieToHeader, ApiKey
│   ├── Models/        → ApiResponse<T>, PaginatedResult<T>, AppCodes
│   ├── QueryBuilder/  → Dynamic SQL query builder (Dapper)
│   └── Sanitizer/     → Identifier sanitization
├── Config/            → Strongly-typed settings
├── Data/              → AppDbContext, DbSeeder
├── Fields/            → Dynamic field management
├── Folders/           → File folder hierarchy
├── Health/            → Health check endpoint
├── Help/              → API documentation endpoint
├── Items/             → Dynamic items CRUD engine
├── Mail/              → MailKit email service
├── Relations/         → M2O/O2M/M2M relation management
├── Upload/            → File upload service
├── Users/             → User management
├── Migrations/        → EF Core migrations
├── postman/           → Postman collections
├── wwwroot/uploads/   → Uploaded files
├── logs/              → Serilog daily logs
├── Program.cs         → Application entry point
└── appsettings.json   → Configuration
```

---

## Architecture

DataForge is a **dynamic schema CMS** — collections, fields, and relations are defined at runtime through the API, not through code migrations. The system stores metadata in relational tables (`CollectionMetas`, `FieldMetas`, `RelationMetas`) and creates actual PostgreSQL tables/columns via dynamic DDL.

### Relation Resolution Flow

```
Item Request (GET /items/posts?fields=*,author.name)
    │
    ├── ResolveCollection() → loads CollectionMeta + FieldMeta[]
    │
    ├── ParseDepthConfig() → parses "*,author.name" to depth config
    │   │  *        → globalDepth = 0 (all alias fields at flat level)
    │   │  author   → depth 1, field "name" from related table
    │
    ├── QueryBuilderService → executes raw SQL with Dapper
    │
    └── ResolveRelations()
        ├── M2O/file → resolve FK via RelationMetas
        ├── O2M      → fetch child rows
        └── M2M/files → fetch junction rows, resolve FK columns
```

---

## License

MIT
