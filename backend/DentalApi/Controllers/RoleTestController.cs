
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RoleTestController : ControllerBase
{
    [HttpGet("patient")]
    [Authorize(Roles = "Patient")]
    public IActionResult PatientAccess()
    {
        return Ok(new
        {
            message = "Өвчтөний эрх амжилттай шалгагдлаа"
        });
    }

    [HttpGet("doctor")]
    [Authorize(Roles = "Doctor")]
    public IActionResult DoctorAccess()
    {
        return Ok(new
        {
            message = "Эмчийн эрх амжилттай шалгагдлаа"
        });
    }

    [HttpGet("director")]
    [Authorize(Roles = "Director")]
    public IActionResult DirectorAccess()
    {
        return Ok(new
        {
            message = "Захирлын эрх амжилттай шалгагдлаа"
        });
    }
}
