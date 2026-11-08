import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/widgets.dart';
import '../../core/models.dart';
import '../auth/session.dart';
import '../booking/clinic_time.dart';

class AppointmentsScreen extends ConsumerStatefulWidget {
  const AppointmentsScreen({super.key});
  @override
  ConsumerState<AppointmentsScreen> createState() => _AppointmentsScreenState();
}

class _AppointmentsScreenState extends ConsumerState<AppointmentsScreen> {
  final rows = <Appointment>[];
  bool busy = true, more = true;
  Object? error;
  @override
  void initState() {
    super.initState();
    load(reset: true);
  }

  Future<void> load({bool reset = false}) async {
    if (!reset && busy) {
      return;
    }
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final batch = await ref
          .read(apiProvider)
          .appointments(skip: reset ? 0 : rows.length);
      if (!mounted) {
        return;
      }
      setState(() {
        if (reset) {
          rows.clear();
        }
        rows.addAll(batch);
        more = batch.length == 50;
      });
    } catch (e) {
      if (mounted) {
        setState(() => error = e);
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) => AppPage(
    title: 'Миний захиалгууд',
    navigationIndex: 1,
    actions: [
      IconButton(
        tooltip: 'Шинэчлэх',
        onPressed: busy ? null : () => load(reset: true),
        icon: const Icon(Icons.refresh),
      ),
    ],
    child: busy && rows.isEmpty
        ? const Center(child: CircularProgressIndicator())
        : error != null && rows.isEmpty
        ? ErrorPanel(error: error, retry: () => load(reset: true))
        : RefreshIndicator(
            onRefresh: () => load(reset: true),
            child: ListView(
              padding: const EdgeInsets.all(16),
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                if (rows.isEmpty) const EmptyPanel('Одоогоор захиалга алга.'),
                ...rows.map(
                  (row) => Card(
                    child: ListTile(
                      title: Text(row.serviceName),
                      subtitle: Text(
                        '${row.doctorName}\n${clinicDateTime(row.startUtc, row.clinicTimeZoneId)}\n${row.reference} · ${row.statusLabel}',
                      ),
                      isThreeLine: true,
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => context.push('/appointments/${row.id}'),
                    ),
                  ),
                ),
                if (error != null) Text(safeMessage(error)),
                if (more || error != null)
                  TextButton(
                    onPressed: busy ? null : () => load(),
                    child: Text(busy ? 'Түр хүлээнэ үү' : 'Илүү харах'),
                  ),
              ],
            ),
          ),
  );
}

class AppointmentDetailsScreen extends ConsumerWidget {
  final int id;
  final bool confirmation;
  const AppointmentDetailsScreen(
    this.id, {
    super.key,
    this.confirmation = false,
  });
  @override
  Widget build(BuildContext context, WidgetRef ref) => AppPage(
    title: confirmation ? 'Захиалга баталгаажлаа' : 'Захиалгын дэлгэрэнгүй',
    child: DataPanel<Appointment>(
      load: () => ref.read(apiProvider).appointment(id),
      builder: (row) => ListView(
        padding: const EdgeInsets.all(24),
        children: [
          Icon(
            confirmation ? Icons.check_circle_outline : Icons.event_note,
            size: 64,
            color: Theme.of(context).colorScheme.primary,
          ),
          const SizedBox(height: 16),
          Text(
            row.reference,
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.headlineMedium,
          ),
          const SizedBox(height: 24),
          _line('Төлөв', row.statusLabel),
          _line('Эмч', row.doctorName),
          _line('Үйлчилгээ', row.serviceName),
          _line(
            'Эхлэх цаг',
            clinicDateTime(row.startUtc, row.clinicTimeZoneId),
          ),
          _line('Дуусах цаг', clinicDateTime(row.endUtc, row.clinicTimeZoneId)),
          _line('Үргэлжлэх хугацаа', '${row.durationMinutes} минут'),
          _line('Үнийн мэдээлэл', money(row.minPrice, row.maxPrice)),
          const InfoBanner(
            'Захиалга хадгалагдлаа. Email болон SMS мэдэгдлийн хүргэлтийн төлөвийг энд харуулахгүй.',
          ),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: () => context.go('/appointments'),
            child: const Text('Миний захиалгууд'),
          ),
          TextButton(
            onPressed: () => context.go('/'),
            child: const Text('Нүүр хуудас руу'),
          ),
        ],
      ),
    ),
  );
  Widget _line(String label, String value) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 10),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
        const SizedBox(height: 4),
        Text(value),
      ],
    ),
  );
}
