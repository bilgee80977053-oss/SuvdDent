using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Fixture = DentalApi.IntegrationTests.AppointmentTests.Fixture;

namespace DentalApi.IntegrationTests;

public sealed class PlatformCatalogTests
{
    [Fact]
    public async Task Platform_catalog_returns_real_scoped_data_and_only_public_fields()
    {
        using var f=new Fixture();await f.Seed();using var client=await f.Account();
        var clinics=(await client.GetFromJsonAsync<PlatformClinicDto[]>("/api/platform/clinics"))!;
        Assert.Equal(2,clinics.Length);
        var doctors=(await client.GetFromJsonAsync<PlatformDoctorDto[]>("/api/platform/clinics/101/doctors?clinicId=202"))!;
        Assert.Equal(31,Assert.Single(doctors).Id);
        var services=(await client.GetFromJsonAsync<PlatformDoctorServiceDto[]>("/api/platform/clinics/101/doctors/31/services?clinicId=202"))!;
        Assert.Equal(51,Assert.Single(services).Id);Assert.Equal(30,services[0].DurationMinutes);
        var foreign=(await client.GetFromJsonAsync<PlatformDoctorDto[]>("/api/platform/clinics/202/doctors"))!;
        Assert.Equal(32,Assert.Single(foreign).Id);
        using var cross=await client.GetAsync("/api/platform/clinics/101/doctors/32/services");Assert.Equal(HttpStatusCode.NotFound,cross.StatusCode);
        using var response=await client.GetAsync("/api/platform/clinics/101/doctors");
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[]{"biography","fullName","id","photoPath","practiceStartYear","specialty"},
            json.RootElement[0].EnumerateObject().Select(x=>x.Name).OrderBy(x=>x).ToArray());
        var raw=await response.Content.ReadAsStringAsync();
        foreach(var field in new[]{"password","token","email","phone","patientId","userId","medicalNotes"})
            Assert.DoesNotContain(field,raw,StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData("/api/platform/clinics")]
    [InlineData("/api/platform/clinics/101/doctors")]
    [InlineData("/api/platform/clinics/101/doctors/31/services")]
    public async Task Anonymous_and_legacy_tokens_cannot_access_platform_catalog(string path)
    {
        using var f=new Fixture();await f.Seed();using var anonymous=f.App.Client();using var legacy=await f.App.AsAsync(15);
        using var a=await anonymous.GetAsync(path);using var b=await legacy.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized,a.StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,b.StatusCode);
    }
    [Theory]
    [InlineData("profile")][InlineData("unpublished")][InlineData("user")][InlineData("role")]
    [InlineData("service")][InlineData("mapping")][InlineData("hours")][InlineData("director")][InlineData("timezone")]
    public async Task Ineligible_records_are_hidden_and_other_clinic_is_unchanged(string mode)
    {
        using var f=new Fixture();await f.Seed();using var client=await f.Account();
        await f.Mutate(async db=>
        {
            if(mode=="profile")await db.DoctorProfiles.Where(x=>x.Id==31).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsActive,false));
            if(mode=="unpublished")await db.DoctorProfiles.Where(x=>x.Id==31).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsPublished,false));
            if(mode=="user")await db.Users.Where(x=>x.Id==13).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsActive,false));
            if(mode=="role")await db.Users.Where(x=>x.Id==13).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Role,Role.Nurse));
            if(mode=="service")await db.Services.Where(x=>x.Id==51).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsActive,false));
            if(mode=="mapping")await db.DoctorServices.Where(x=>x.ClinicId==101).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsActive,false));
            if(mode=="hours")await db.DoctorWorkingHours.Where(x=>x.ClinicId==101).ExecuteDeleteAsync();
            if(mode=="director")await db.Users.Where(x=>x.Id==15).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsActive,false));
            if(mode=="timezone")await db.Clinics.Where(x=>x.Id==101).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.TimeZoneStatus,TimeZoneStatus.PendingVerification)
                .SetProperty(x=>x.TimeZoneVerifiedClinicId,(int?)null).SetProperty(x=>x.TimeZoneVerifiedByUserId,(int?)null).SetProperty(x=>x.TimeZoneVerifiedAtUtc,(DateTime?)null));
        });
        Assert.Equal(202,Assert.Single((await client.GetFromJsonAsync<PlatformClinicDto[]>("/api/platform/clinics"))!).Id);
        using var hidden=await client.GetAsync("/api/platform/clinics/101/doctors/31/services");Assert.Equal(HttpStatusCode.NotFound,hidden.StatusCode);
        Assert.Equal(61,Assert.Single((await client.GetFromJsonAsync<PlatformDoctorServiceDto[]>("/api/platform/clinics/202/doctors/32/services"))!).Id);
    }
    [Theory]
    [InlineData("/api/platform/clinics/0/doctors",400)]
    [InlineData("/api/platform/clinics/-1/doctors",400)]
    [InlineData("/api/platform/clinics/999/doctors",404)]
    [InlineData("/api/platform/clinics/101/doctors/0/services",400)]
    [InlineData("/api/platform/clinics/101/doctors/999/services",404)]
    public async Task Invalid_or_missing_identifiers_have_explicit_status(string path,int status)
    {
        using var f=new Fixture();await f.Seed();using var client=await f.Account();using var response=await client.GetAsync(path);
        Assert.Equal(status,(int)response.StatusCode);
    }
}
