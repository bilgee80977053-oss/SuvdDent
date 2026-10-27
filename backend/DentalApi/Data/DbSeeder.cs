using DentalApi.Models;

namespace DentalApi.Data;

public static class DbSeeder
{
    public static bool Seed(AppDbContext db, string? adminPhone, string? adminPassword, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(adminPhone) || string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning("Эхний Director хэрэглэгч үүсгэх тохиргоо дутуу тул seed хийгдээгүй.");
            return false;
        }

        if (db.Clinics.Any()) return false;

        using var transaction = db.Database.BeginTransaction();

        var clinic = new Clinic { Name = "Сувд дент", WorkHours = "08:00-18:00" };
        db.Clinics.Add(clinic);
        db.SaveChanges();

        db.Users.Add(new User
        {
            ClinicId = clinic.Id,
            FullName = "Захирал",
            Phone = adminPhone.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
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
        transaction.Commit();
        return true;
    }
}
