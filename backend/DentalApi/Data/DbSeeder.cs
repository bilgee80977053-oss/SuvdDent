using DentalApi.Models;

namespace DentalApi.Data;

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (db.Clinics.Any()) return;

        var clinic = new Clinic { Name = "Сувд дент", WorkHours = "08:00-18:00" };
        db.Clinics.Add(clinic);
        db.SaveChanges();

        db.Users.Add(new User
        {
            ClinicId = clinic.Id,
            FullName = "Захирал",
            Phone = "99000000",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("ChangeMe123!"),
            Role = Role.Director
        });

        db.Services.AddRange(
            new Service {
                ClinicId = clinic.Id, Name = "Ломбо",
                Description = "Цоорсон шүдийг нөхөж, шүдний хэлбэр, үйл ажиллагааг сэргээх эмчилгээ.",
                MinPrice = 95000, MaxPrice = 210000 },
            new Service {
                ClinicId = clinic.Id, Name = "Шүд авалт",
                Description = "Эмчилгээгээр аврах боломжгүй шүдийг эрүүнээс авах мэс засал.",
                MinPrice = 50000,
                AftercareText = "Марльыг 20 минут тасралтгүй зуу. 2 цагийн дараа хоол идэнэ. Халуун хоол, тамхи, архинаас 1-2 хоног зайлсхий." },
            new Service {
                ClinicId = clinic.Id, Name = "Цайруулалт",
                Description = "Шүдний өнгийг цайруулах үйлчилгээ.",
                MinPrice = 300000, MaxPrice = 300000 });
        db.SaveChanges();
    }
}