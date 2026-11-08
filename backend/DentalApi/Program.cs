
using System.Threading.RateLimiting;
using System.Text;
using DentalApi.Data;
using DentalApi.Demo;
using DentalApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

// This command uses its own DI container and never builds/starts the API host.
if (args.Contains("--prepare-demo"))
{
    Environment.ExitCode = await DemoPreparation.RunCommandAsync(args);
    return;
}

var validateDemoOnly = args.Contains("--validate-demo");
var builder = WebApplication.CreateBuilder(args.Where(x => x != "--validate-demo").ToArray());
if (validateDemoOnly && !builder.Environment.IsEnvironment("Demo"))
    throw new InvalidOperationException("DEMO validation requires the Demo environment.");

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
builder.Services.AddDbContext<AppDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var environment = services.GetRequiredService<IHostEnvironment>();
    options.UseSqlite(environment.IsEnvironment("Demo")
        ? DemoDatabaseSafety.StartupConnectionString(configuration)
        : configuration.GetConnectionString("Default"));
});

// Үйлчилгээний бүртгэл
builder.Services.AddSingleton<JwtService>();
builder.Services.AddScoped<VerificationService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<DoctorProfileService>();
builder.Services.AddScoped<SchedulingService>();
builder.Services.AddSingleton<MongoliaLocations>();
builder.Services.AddScoped<ClinicLocationService>();
builder.Services.AddScoped<ClinicSchedulingPolicy>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AppointmentService>();
builder.Services.AddScoped<PlatformCatalogService>();
builder.Services.AddScoped<IAppointmentConfirmationNotifications, AppointmentConfirmationNotifications>();
builder.Services.AddSingleton<IDemoAppointmentNotificationProvider, DemoAppointmentNotificationProvider>();
builder.Services.AddScoped<AppointmentNotificationProcessor>();
// Explicit opt-in: tests invoke the processor deterministically, never a racing worker.
if (builder.Configuration.GetValue<bool>("DemoNotifications:Enabled") &&
    !builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<DemoNotificationWorker>();

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
if (app.Environment.IsEnvironment("Demo"))
{
    DemoDatabaseSafety.ValidateRuntimeConfiguration(app.Configuration);
    using var scope = app.Services.CreateScope();
    await DemoDatabaseSafety.ValidateReadyAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
        app.Configuration["Demo:Root"]!, app.Configuration["Demo:DatabasePath"]!);
    if (validateDemoOnly)
    {
        Console.WriteLine("DEMO startup validation succeeded. No server started.");
        return;
    }
}
else if (!app.Environment.IsEnvironment("Testing"))
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    db.Database.Migrate();
    DbSeeder.Seed(db);
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
