using System.Threading.RateLimiting;
using System.Text;
using DentalApi.Data;
using DentalApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton<JwtService>();
builder.Services.AddScoped<VerificationService>();
builder.Services.AddScoped<EmailService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            NameClaimType = "name",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    DbSeeder.Seed(db);
}


app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

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
            "SuvdDent SMTP тест",
            "Энэ бол SMTP холболтын туршилт.");

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
