# DotnetStarterKit

A production-ready **.NET 8** REST API starter kit with authentication, authorization, file upload, mail service, and more. Built to be cloned and used as a base for real projects.

---

## Tech Stack

- **Framework** — ASP.NET Core 8
- **Database** — MySQL via Entity Framework Core (Pomelo)
- **Auth** — ASP.NET Identity + JWT (HttpOnly Cookies)
- **Mail** — MailKit
- **Logging** — Serilog (Console + File)
- **Docs** — Swagger (Dev only)

---

## Features

### Auth & Security
- JWT Access Token stored in HttpOnly Cookie (15 min)
- Refresh Token stored in HttpOnly Cookie (7 days)
- Email Verification — token valid for 24 hours
- Forgot / Reset Password — token valid for 1 hour
- Account Locking — after 5 failed attempts, locked for 30 minutes
- Token Revocation via `tokenVersion` on logout / password reset
- RBAC — Admin, User, Moderator roles

### Infrastructure
- Unified `ApiResponse<T>` on all endpoints
- Global Exception Filter
- Logging Middleware — Request ID + timing on every request
- Cookie-to-Header Middleware — auto-injects access token into Authorization header
- Rate Limiting — 100 req/min global, 5 req/min on login
- CORS configured from `appsettings.json`
- Security Headers (X-Frame-Options, X-Content-Type-Options, etc.)
- API Versioning — `/api/v1`
- Swagger — development only
- Health Check endpoint

### Modules
- Users CRUD with pagination (Admin only for list/delete)
- File Upload — images (5MB) + files (10MB)
- Mail Service — Welcome, Verification, Password Reset emails
- DB Seeder — roles + admin user on first run

---

## Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- MySQL server running locally

### 1. Clone the repo

```bash
git clone https://github.com/your-username/DotnetStarterKit.git
cd DotnetStarterKit
```

### 2. Configure `appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=StarterKitDB;User=root;Password=;"
  },
  "JwtSettings": {
    "Secret": "your-super-secret-key-at-least-32-characters!!",
    "Issuer": "DotnetStarterKit",
    "Audience": "DotnetStarterKit",
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 7
  },
  "CorsSettings": {
    "AllowedOrigins": "http://localhost:3000,http://localhost:5173"
  },
  "MailSettings": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "User": "your-email@gmail.com",
    "Password": "your-app-password",
    "From": "No Reply <noreply@app.com>"
  },
  "RateLimiting": {
    "PermitLimit": 100,
    "WindowMinutes": 1
  },
  "App": {
    "FrontendUrl": "http://localhost:3000"
  }
}
```

### 3. Run migrations & seed

Migrations run automatically on startup. The seeder creates roles and an admin user:

| Field    | Value               |
|----------|---------------------|
| Email    | admin@starter.com   |
| Password | Admin@123456        |
| Role     | Admin               |

### 4. Run the project

```bash
dotnet run
```

Swagger available at: `http://localhost:5000/swagger`

---

## API Endpoints

### Auth — `/api/v1/auth`

| Method | Endpoint          | Auth     | Description                    |
|--------|-------------------|----------|--------------------------------|
| POST   | /register         | Public   | Register + send verification email |
| POST   | /verify-email     | Public   | Verify email with token        |
| POST   | /login            | Public   | Login + set cookies            |
| POST   | /refresh          | Cookie   | Refresh access token           |
| POST   | /logout           | Required | Logout + clear cookies         |
| POST   | /forgot-password  | Public   | Send password reset email      |
| POST   | /reset-password   | Public   | Reset password with token      |
| GET    | /me               | Required | Get current user               |

### Users — `/api/v1/users`

| Method | Endpoint | Auth       | Description              |
|--------|----------|------------|--------------------------|
| GET    | /        | Admin only | Get all users (paginated)|
| GET    | /:id     | Required   | Get user by ID           |
| PATCH  | /:id     | Required   | Update user              |
| DELETE | /:id     | Admin only | Delete user              |

### Upload — `/api/v1/upload`

| Method | Endpoint    | Auth       | Description        |
|--------|-------------|------------|--------------------|
| POST   | /image      | Required   | Upload image (5MB) |
| POST   | /file       | Required   | Upload file (10MB) |
| DELETE | /:filename  | Admin only | Delete file        |

### Health — `/api/v1/health`

| Method | Endpoint | Auth   | Description              |
|--------|----------|--------|--------------------------|
| GET    | /        | Public | Health check + DB status |

---

## Project Structure

```
DotnetStarterKit/
├── Auth/               → AuthController, AuthService, DTOs
├── Common/
│   ├── Filters/        → GlobalExceptionFilter
│   ├── Middleware/     → LoggingMiddleware, CookieToHeaderMiddleware
│   └── Models/         → ApiResponse<T>, PaginatedResult<T>, AppCodes
├── Config/             → Strongly-typed settings
├── Data/               → AppDbContext, DbSeeder
├── Health/             → HealthController
├── Mail/               → MailService
├── Upload/             → UploadController, UploadService
├── Users/              → UsersController, UsersService, DTOs, User entity
├── wwwroot/uploads/    → Uploaded files served statically
├── logs/               → Serilog daily log files
├── appsettings.json
└── Program.cs
```

---

## Local Mail Testing

Use [Mailpit](https://github.com/axllent/mailpit) to test emails locally without a real SMTP server:

```bash
# Download and run mailpit
./mailpit
```

Update `appsettings.Development.json`:
```json
{
  "MailSettings": {
    "Host": "localhost",
    "Port": 1025,
    "User": "",
    "Password": "",
    "From": "No Reply <noreply@app.com>"
  }
}
```

Mail UI available at: `http://localhost:8025`

---

## License

MIT