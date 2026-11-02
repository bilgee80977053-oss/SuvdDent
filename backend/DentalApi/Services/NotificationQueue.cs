using System.Globalization;
using DentalApi.Data;
using DentalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DentalApi.Services;

public static class NotificationQueue
{
    public static async Task EnqueueAsync(AppDbContext db, Appointment appointment, NotificationEvent kind,
        DateTimeOffset now)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Notifications require an appointment transaction.");
        var user = await (from p in db.Patients
                          join u in db.Users on new { p.ClinicId, Id = p.UserId } equals new { u.ClinicId, Id = (int?)u.Id }
                          where p.ClinicId == appointment.ClinicId && p.Id == appointment.PatientId && p.IsActive && u.IsActive && u.Role == Role.Patient
                          select u).SingleOrDefaultAsync();
        if (user is null) return;
        var consent = await db.Set<NotificationPreference>().FindAsync(appointment.ClinicId, user.Id);
        if (consent is null) return;
        var local = TimeZoneInfo.ConvertTime(appointment.StartUtc,
            TimeZoneInfo.FindSystemTimeZoneById(appointment.ClinicTimeZoneIdSnapshot));
        var message = kind switch
        {
            NotificationEvent.Confirmed => "Таны захиалга баталгаажлаа.\nДэлгэрэнгүй мэдээллийг харах бол\nапп руу нэвтэрнэ үү.",
            NotificationEvent.Cancelled => "Таны захиалга амжилттай цуцлагдлаа.\nШинээр цаг захиалах бол\nманай аппыг ашиглана уу.",
            _ => "Таны захиалсан цаг 24 цагийн дараа эхэлнэ.\nДэлгэрэнгүй мэдээллийг апп руу нэвтэрч харна уу."
        };
        message += "\nЦаг: " + local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            + " (" + appointment.ClinicTimeZoneIdSnapshot + ")";
        async Task Add(NotificationChannel channel, string recipient, DateTimeOffset due)
        {
            if (await db.Set<NotificationOutbox>().AnyAsync(x => x.ClinicId == appointment.ClinicId &&
                x.AppointmentId == appointment.Id && x.Event == kind && x.Channel == channel)) return;
            db.Add(new NotificationOutbox { Appointment = appointment, ClinicId = appointment.ClinicId,
                AppointmentId = appointment.Id, Event = kind, Channel = channel, Recipient = recipient,
                Body = message, DueUtcTicks = due.UtcTicks, NextAttemptUtcTicks = due.UtcTicks });
        }
        if (kind != NotificationEvent.Reminder && consent.EmailEnabled && user.IsEmailVerified &&
            !string.IsNullOrWhiteSpace(user.Email))
            await Add(NotificationChannel.Email, user.Email, now);
        if (consent.SmsEnabled && user.IsPhoneVerified && !string.IsNullOrWhiteSpace(user.Phone))
        {
            var due = kind == NotificationEvent.Reminder ? appointment.StartUtc.AddHours(-24) : now;
            // Do not send a misleading 24-hour reminder for a late booking.
            if (kind != NotificationEvent.Reminder || due >= now)
                await Add(NotificationChannel.Sms, user.Phone, due);
        }
    }
}
