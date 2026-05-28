using DataForge.Config;
using Asp.Versioning;
using DataForge.Common.Filters;
using DataForge.Data;
using DataForge.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using DataForge.Auth;
using DataForge.Mail;
using DataForge.Upload;
using DataForge.Folders;
using Serilog;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using DataForge.Common.Sanitizer;
using DataForge.Common.DDL;
using DataForge.Collections;
using DataForge.Fields;
using DataForge.Common.QueryBuilder;
using DataForge.Items;
using DataForge.Help;
using DataForge.Relations;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/app.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();



var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ── Strongly-Typed Config ──────────────────────────────────────
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("App"));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
builder.Services.Configure<CorsSettings>(builder.Configuration.GetSection("CorsSettings"));
builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("MailSettings"));
builder.Services.Configure<RateLimitingSettings>(builder.Configuration.GetSection("RateLimiting"));

// ── Rate Limiting ──────────────────────────────────────────────
var rateLimitSettings = builder.Configuration.GetSection("RateLimiting").Get<RateLimitingSettings>()!;

builder.Services.AddRateLimiter(options =>
{
    // Global policy — 100 requests/min
    options.AddFixedWindowLimiter("global", o =>
    {
        o.PermitLimit = rateLimitSettings.PermitLimit;
        o.Window = TimeSpan.FromMinutes(rateLimitSettings.WindowMinutes);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });

    // Login policy — 5 requests/min
    options.AddFixedWindowLimiter("login", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });

    // Custom 429 response
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            success = false,
            message = "Too many requests. Please try again later.",
            code = "TOO_MANY_REQUESTS",
            data = (object?)null
        }, token);
    };
});

// ── CORS ───────────────────────────────────────────────────────
var corsSettings = builder.Configuration.GetSection("CorsSettings").Get<CorsSettings>()!;
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(corsSettings.GetOrigins())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ── Database ───────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// ── Identity ───────────────────────────────────────────────────
builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// ── JWT Authentication ─────────────────────────────────────────
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()!;
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret))
    };

    options.Events = new JwtBearerEvents
    {
        OnChallenge = async context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            var response = new
            {
                success = false,
                message = "Unauthorized. Please login first.",
                code = "UNAUTHORIZED",
                data = (object?)null
            };
            await context.Response.WriteAsJsonAsync(response);
        },
        OnForbidden = async context =>
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            var response = new
            {
                success = false,
                message = "You do not have permission to access this resource.",
                code = "FORBIDDEN",
                data = (object?)null
            };
            await context.Response.WriteAsJsonAsync(response);
        }
    };
});

// ── Service ───────────────────────────────────────────────
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UsersService>();
builder.Services.AddScoped<MailService>();
builder.Services.AddScoped<UploadService>();
builder.Services.AddSingleton<IdentifierSanitizerService>();
builder.Services.AddScoped<DdlService>();
builder.Services.AddScoped<CollectionService>();
builder.Services.AddScoped<FieldsService>();
builder.Services.AddScoped<ItemsService>();
builder.Services.AddScoped<QueryBuilderService>();
builder.Services.AddScoped<HelpService>();
builder.Services.AddScoped<FoldersService>();
builder.Services.AddScoped<RelationsService>();

// ── Controllers ────────────────────────────────────────────────
builder.Services.AddControllers(options =>
{
    options.Filters.Add<GlobalExceptionFilter>();
});

// ── API Versioning ─────────────────────────────────────────────
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
});

// ── Swagger (Dev only) ─────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── AutoMapper ─────────────────────────────────────────────────
builder.Services.AddAutoMapper(typeof(Program).Assembly);

// ── HTTP Context ───────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// ── Pipeline ───────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseRateLimiter();
app.UseStaticFiles();
// ── Security Headers ───────────────────────────────────────────
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseLoggingMiddleware();
// app.UseHttpsRedirection();
app.UseCookieToHeaderMiddleware();
// app.UseApiKeyMiddleware();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();


// ── Seed Database ──────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}
app.Run();