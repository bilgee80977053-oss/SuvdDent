using DentalApi.Contracts;
using DentalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Controllers;

[ApiController, Authorize(Policy = PlatformJwtService.Policy)]
[Route("api/platform/clinics")]
public sealed class PlatformCatalogController(PlatformCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PlatformClinicDto[]>> Clinics() => Result(await catalog.Clinics());
    [HttpGet("{clinicId:int}/doctors")]
    public async Task<ActionResult<PlatformDoctorDto[]>> Doctors(int clinicId) => Result(await catalog.ClinicDoctors(clinicId));
    [HttpGet("{clinicId:int}/doctors/{doctorProfileId:int}/services")]
    public async Task<ActionResult<PlatformDoctorServiceDto[]>> Services(int clinicId,int doctorProfileId) =>
        Result(await catalog.DoctorServices(clinicId,doctorProfileId));
    private ActionResult<T[]> Result<T>(CatalogResult<T> result) => result.Status == 200 ? Ok(result.Items) :
        StatusCode(result.Status,new PlatformCatalogError(result.Status switch
        { 400 => "INVALID_CATALOG_IDENTIFIER", 404 => "CATALOG_RESOURCE_NOT_FOUND", _ => "CATALOG_BUSY" }));
}
