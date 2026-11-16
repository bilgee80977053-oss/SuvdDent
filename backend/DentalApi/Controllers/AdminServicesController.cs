using System.ComponentModel.DataAnnotations;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Controllers;

/// <summary>
/// Authorized clinic director manages its own procedure catalog and tariffs.
/// No patient or other clinic may edit these records. Soft deletion preserves
/// existing appointments and payment estimate history.
/// </summary>
[ApiController]
[Authorize(Roles = "Director")]
[Route("api/admin/services")]
public sealed class AdminServicesController(AppDbContext db) : ControllerBase
{
    public sealed record ServiceInput(
        [Required, MinLength(2), MaxLength(160)] string Name,
        [Required, MaxLength(1500)] string Description,
        [Range(0, 1000000000)] long MinPrice,
        [Range(0, 1000000000)] long? MaxPrice,
        [MaxLength(2500)] string? AftercareText);

    private int ClinicId => int.Parse(User.FindFirst("clinicId")!.Value);

    private static string Clean(string value) => value.Trim();

    private static object Response(Service s) => new
    {
        s.Id, s.ClinicId, s.Name, s.Description, s.MinPrice,
        s.MaxPrice, s.AftercareText, s.IsActive
    };

    private static string? Validation(ServiceInput input)
    {
        if (input.Name is null || Clean(input.Name).Length is < 2 or > 160 ||
            input.Description is null || Clean(input.Description).Length == 0)
            return "Үйлчилгээний нэр, тайлбар заавал шаардлагатай.";
        if (input.MaxPrice.HasValue && input.MaxPrice < input.MinPrice)
            return "Үнийн дээд хэмжээ доод хэмжээнээс бага байж болохгүй.";
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var rows = await db.Services.AsNoTracking()
            .Where(s => s.ClinicId == ClinicId)
            .OrderBy(s => s.Name).ToListAsync(cancellationToken);
        return Ok(rows.Select(Response));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        ServiceInput input, CancellationToken cancellationToken)
    {
        var problem = Validation(input);
        if (problem != null) return BadRequest(new { message = problem });
        var name = Clean(input.Name);
        if (await db.Services.AnyAsync(
            s => s.ClinicId == ClinicId && s.Name.ToLower() == name.ToLower(),
            cancellationToken))
            return Conflict(new { message = "Энэ нэртэй үйлчилгээ бүртгэлтэй байна." });

        var row = new Service
        {
            ClinicId = ClinicId,
            Name = name,
            Description = Clean(input.Description),
            MinPrice = input.MinPrice,
            MaxPrice = input.MaxPrice,
            AftercareText = input.AftercareText?.Trim(),
            IsActive = true
        };
        db.Services.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(List), new { }, Response(row));
    }

    [HttpPut("{serviceId:int}")]
    public async Task<IActionResult> Update(
        int serviceId, ServiceInput input, CancellationToken cancellationToken)
    {
        if (serviceId <= 0) return BadRequest();
        var problem = Validation(input);
        if (problem != null) return BadRequest(new { message = problem });
        var row = await db.Services.SingleOrDefaultAsync(
            s => s.Id == serviceId && s.ClinicId == ClinicId, cancellationToken);
        if (row is null) return NotFound();

        var name = Clean(input.Name);
        if (await db.Services.AnyAsync(s => s.ClinicId == ClinicId &&
            s.Id != serviceId && s.Name.ToLower() == name.ToLower(),
            cancellationToken))
            return Conflict(new { message = "Энэ нэртэй үйлчилгээ бүртгэлтэй байна." });

        row.Name = name;
        row.Description = Clean(input.Description);
        row.MinPrice = input.MinPrice;
        row.MaxPrice = input.MaxPrice;
        row.AftercareText = input.AftercareText?.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return Ok(Response(row));
    }

    [HttpPost("{serviceId:int}/deactivate")]
    public async Task<IActionResult> Deactivate(
        int serviceId, CancellationToken cancellationToken)
    {
        var row = await db.Services.SingleOrDefaultAsync(
            s => s.Id == serviceId && s.ClinicId == ClinicId, cancellationToken);
        if (row is null) return NotFound();
        row.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(Response(row));
    }

    [HttpPost("{serviceId:int}/activate")]
    public async Task<IActionResult> Activate(
        int serviceId, CancellationToken cancellationToken)
    {
        var row = await db.Services.SingleOrDefaultAsync(
            s => s.Id == serviceId && s.ClinicId == ClinicId, cancellationToken);
        if (row is null) return NotFound();
        row.IsActive = true;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(Response(row));
    }
}
