
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
    });

builder.Services.AddAuthorization();

// Хүсэлтийн тооны хязгаарлалт
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

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
