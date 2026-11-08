import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/widgets.dart';
import '../../core/models.dart';
import '../auth/session.dart';
import '../booking/clinic_time.dart';

class ClinicsScreen extends ConsumerWidget {
  const ClinicsScreen({super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) => AppPage(
    title: 'Эмнэлэг сонгох',
    child: DataPanel<List<Clinic>>(
      load: ref.read(apiProvider).clinics,
      builder: (rows) => rows.isEmpty
          ? const EmptyPanel('Захиалга авах боломжтой эмнэлэг олдсонгүй.')
          : ListView.separated(
              padding: const EdgeInsets.all(16),
              itemCount: rows.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, i) {
                final row = rows[i];
                return Card(
                  child: ListTile(
                    leading: const Icon(Icons.local_hospital_outlined),
                    title: Text(row.name),
                    subtitle: Text(row.timeZoneId),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => context.push('/clinics/${row.id}/doctors'),
                  ),
                );
              },
            ),
    ),
  );
}

class DoctorsScreen extends ConsumerWidget {
  final int clinicId;
  const DoctorsScreen(this.clinicId, {super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) => AppPage(
    title: 'Эмч сонгох',
    child: DataPanel<List<Doctor>>(
      load: () => ref.read(apiProvider).doctors(clinicId),
      builder: (rows) => rows.isEmpty
          ? const EmptyPanel('Захиалга авах эмч олдсонгүй.')
          : ListView.separated(
              padding: const EdgeInsets.all(16),
              itemCount: rows.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, i) {
                final row = rows[i];
                return Card(
                  child: ListTile(
                    leading: const CircleAvatar(
                      child: Icon(Icons.person_outline),
                    ),
                    title: Text(row.fullName),
                    subtitle: Text(row.specialty),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => context.push(
                      '/clinics/$clinicId/doctors/${row.id}/services',
                    ),
                  ),
                );
              },
            ),
    ),
  );
}

class ServicesScreen extends ConsumerWidget {
  final int clinicId, doctorId;
  const ServicesScreen(this.clinicId, this.doctorId, {super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) => AppPage(
    title: 'Үйлчилгээ сонгох',
    child: DataPanel<List<DentalService>>(
      load: () => ref.read(apiProvider).services(clinicId, doctorId),
      builder: (rows) => rows.isEmpty
          ? const EmptyPanel('Энэ эмчийн үйлчилгээ олдсонгүй.')
          : ListView.separated(
              padding: const EdgeInsets.all(16),
              itemCount: rows.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, i) {
                final row = rows[i];
                return Card(
                  child: ListTile(
                    isThreeLine: true,
                    title: Text(row.name),
                    subtitle: Text(
                      '${row.durationMinutes} минут · ${money(row.minPrice, row.maxPrice)}\n${row.description}',
                    ),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => context.push(
                      '/clinics/$clinicId/doctors/$doctorId/services/${row.id}/book',
                    ),
                  ),
                );
              },
            ),
    ),
  );
}
