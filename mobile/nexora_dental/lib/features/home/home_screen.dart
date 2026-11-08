import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/widgets.dart';
import '../auth/session.dart';
import '../booking/booking_controller.dart';

class HomeScreen extends ConsumerWidget {
  const HomeScreen({super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final account = ref.watch(sessionProvider).asData?.value;
    if (account == null) {
      return const SizedBox.shrink();
    }
    return AppPage(
      title: 'Nexora Dental',
      navigationIndex: 0,
      actions: [
        IconButton(
          tooltip: 'Гарах',
          icon: const Icon(Icons.logout),
          onPressed: () async {
            try {
              await ref.read(sessionProvider.notifier).logout();
            } catch (e) {
              if (context.mounted) {
                showSafeError(context, e);
              }
            }
          },
        ),
      ],
      child: ListView(
        padding: const EdgeInsets.all(24),
        children: [
          Text(
            'Сайн байна уу, ${account.fullName}',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 12),
          const Text(
            'Эмнэлэг, эмч, үйлчилгээгээ сонгоод цаг захиалаарай.',
          ),
          const SizedBox(height: 24),
          FilledButton.icon(
            onPressed: () => context.push('/clinics'),
            icon: const Icon(Icons.add_circle_outline),
            label: const Padding(
              padding: EdgeInsets.all(12),
              child: Text('Цаг захиалах'),
            ),
          ),
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: () => context.go('/appointments'),
            icon: const Icon(Icons.event_note),
            label: const Text('Миний захиалгууд'),
          ),
          const SizedBox(height: 24),
          FutureBuilder(
            future: ref.read(storeProvider).pending(account.id),
            builder: (context, snapshot) {
              if (snapshot.hasError) {
                return const InfoBanner(
                  'Хадгалагдсан хүсэлтийг шалгах боломжгүй байна. Дахин оролдох эсвэл шинэ захиалга хийхээс өмнө холболтоо шалгана уу.',
                );
              }
              if (snapshot.data == null) {
                return const SizedBox.shrink();
              }
              try {
                final pending = BookingIntent.fromJson(snapshot.requireData!);
                return Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        const Text(
                          'Өмнөх захиалгын хүсэлт шийдэгдээгүй байна.',
                        ),
                        const SizedBox(height: 8),
                        FilledButton.tonal(
                          onPressed: () => context.push(pending.route),
                          child: const Text('Өмнөх хүсэлт рүү буцах'),
                        ),
                      ],
                    ),
                  ),
                );
              } catch (_) {
                return const InfoBanner(
                  'Хадгалсан хүсэлтийн мэдээлэл гэмтсэн байна. Шинэ хүсэлт үүсгэхээс өмнө шалгана уу.',
                );
              }
            },
          ),
          const InfoBanner(
            'Захиалгын мэдээллийг серверээс дахин шалгаж баталгаажуулна.',
          ),
        ],
      ),
    );
  }
}
