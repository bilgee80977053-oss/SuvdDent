
using System.Threading.RateLimiting;
using System.Text;
using DentalApi.Data;
using DentalApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Controllers болон Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Нэвтрэх үед авсан JWT токеноо оруулна уу."
    });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
});

// SQLite өгөгдлийн сан
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("Default")));

// Үйлчилгээний бүртгэл
builder.Services.AddSingleton<JwtService>();
builder.Services.AddScoped<VerificationService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<DoctorProfileService>();
builder.Services.AddScoped<SchedulingService>();
builder.Services.AddSingleton<MongoliaLocations>();
builder.Services.AddScoped<ClinicLocationService>();
builder.Services.AddScoped<ClinicSchedulingPolicy>();

// JWT Authentication
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    builder.Configuration["Jwt:Key"]!)),

            NameClaimType = "name",
            RoleClaimType = "role",

            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // JWT баталгаажсаны дараа хэрэглэгчийн төлөвийг шалгах
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;

                var userIdText =
                    principal?.FindFirst("userId")?.Value;

                var subjectText =
                    principal?.FindFirst("sub")?.Value;

                var clinicIdText =
                    principal?.FindFirst("clinicId")?.Value;

                var tokenVersionText =
                    principal?.FindFirst("tokenVersion")?.Value;

                var roleText =
                    principal?.FindFirst("role")?.Value;

                // Токены шаардлагатай мэдээллийг шалгах
                if (!int.TryParse(userIdText, out var userId) ||
                    !int.TryParse(subjectText, out var subjectId) ||
                    !int.TryParse(clinicIdText, out var clinicId) ||
                    !int.TryParse(
                        tokenVersionText, out var tokenVersion) ||
                    userId <= 0 ||
                    subjectId != userId ||
                    clinicId <= 0 ||
                    tokenVersion < 0 ||
                    string.IsNullOrWhiteSpace(roleText))
                {
                    context.Fail("Invalid token claims.");
                    return;
                }

                // Өгөгдлийн сангаас хэрэглэгчийг шалгах
                var db = context.HttpContext.RequestServices
                    .GetRequiredService<AppDbContext>();

                var user = await db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new
                    {
                        u.ClinicId,
                        u.TokenVersion,
                        u.IsActive,
                        u.Role
                    })
                    .FirstOrDefaultAsync(
                        context.HttpContext.RequestAborted);

                // Токены мэдээллийг өгөгдлийн сантай харьцуулах
                if (user is null ||
                    !user.IsActive ||
                    user.ClinicId != clinicId ||
                    user.TokenVersion != tokenVersion ||
                    user.Role.ToString() != roleText)
                {
                    context.Fail("Token revoked or user inactive.");
                    return;
                }
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddPlatformIdentity(builder.Configuration);

// Хүсэлтийн тооны хязгаарлалт
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

    options.AddPolicy("platform-auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey:
                context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",

            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

// Swagger зөвхөн Development орчинд
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Өгөгдлийн сангийн migration
if (!app.Environment.IsEnvironment("Testing"))
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    db.Database.Migrate();
    var seedPhone = app.Configuration["Seed:AdminPhone"];
    var seedPassword = app.Configuration["Seed:AdminPassword"];
    if (app.Environment.IsDevelopment() ||
        (!string.IsNullOrWhiteSpace(seedPhone) && !string.IsNullOrWhiteSpace(seedPassword)))
    {
        DbSeeder.Seed(db, seedPhone, seedPassword, app.Logger);
    }
    else
    {
        app.Logger.LogWarning("Seed:AdminPhone болон Seed:AdminPassword тохиргоогүй тул seed алгаслаа.");
    }
}

// Middleware
app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

// Gmail SMTP туршилт
if (app.Environment.IsDevelopment() &&
    args.Contains("--test-email"))
{
    using var scope = app.Services.CreateScope();

    var emailService = scope.ServiceProvider
        .GetRequiredService<EmailService>();

    var config = scope.ServiceProvider
        .GetRequiredService<IConfiguration>();

    var testEmail = config["Email:Username"];

    if (string.IsNullOrWhiteSpace(testEmail))
        throw new InvalidOperationException(
            "Туршилтын имэйл тохируулаагүй байна.");

    try
    {
        await emailService.SendAsync(
            testEmail,
            "Nexora Dental SMTP тест",
            "Энэ бол Nexora Dental-ийн SMTP холболтын туршилт.");

        Console.WriteLine("SMTP тест: Амжилттай");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"SMTP төрөл: {ex.GetType().Name}");

        Console.Error.WriteLine(
            $"SMTP тайлбар: {ex.Message}");
    }

    return;
}

app.Run();

// Exposes the entry point to WebApplicationFactory integration tests.
public partial class Program { }
