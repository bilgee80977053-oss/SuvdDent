import 'package:flutter/material.dart';

import '../app/app_theme.dart';
import '../app/clinical_widgets.dart' show clinicalMotion;
import 'dental_knowledge.dart';
import 'models.dart';

String mnt(int value) {
  final negative = value < 0 ? '-' : '';
  final digits = value.abs().toString();
  final groups = <String>[];
  for (var end = digits.length; end > 0; end -= 3) {
    groups.insert(0, digits.substring(end - 3 < 0 ? 0 : end - 3, end));
  }
  return '$negative${groups.join(' ')} ₮';
}

String clinicPriceLabel(BookingService? service) {
  if (service == null) return 'Эмнэлгийн үнийг лавлана уу';
  if (service.minPrice < 0 ||
      (service.maxPrice != null && service.maxPrice! < service.minPrice)) {
    return 'Үнэ баталгаажаагүй';
  }
  if (service.minPrice == 0 && (service.maxPrice ?? 0) == 0) {
    return 'Үнэ эмнэлгээс тодруулна';
  }
  if (service.maxPrice != null && service.maxPrice != service.minPrice) {
    return '${mnt(service.minPrice)} – ${mnt(service.maxPrice!)}';
  }
  return mnt(service.minPrice);
}

AnimationStyle _serviceSheetAnimation(BuildContext context) {
  final duration = clinicalMotion(context, 240);
  return duration == Duration.zero
      ? AnimationStyle.noAnimation
      : AnimationStyle(
          duration: duration,
          reverseDuration: clinicalMotion(context, 180),
        );
}

class ClinicalServiceDetails {
  static Future<void> show(
    BuildContext context, {
    required String category,
    BookingService? service,
    bool isAcademic = false,
    VoidCallback? onBook,
  }) => showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    useSafeArea: true,
    backgroundColor: AppColors.paper,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(28)),
    ),
    // HeroController pairs PageRoutes, not this modal. Preserve the
    // real sheet's backdrop, swipe dismissal, back stack and focus.
    sheetAnimationStyle: _serviceSheetAnimation(context),
    constraints: const BoxConstraints(maxWidth: 680),
    builder: (sheetContext) {
      final content = DentalKnowledge.forService(service?.name ?? category);
      return FractionallySizedBox(
        key: const ValueKey('clinical-service-sheet-content'),
        heightFactor: 0.88,
        child: Column(
          children: [
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(24, 8, 24, 24),
                children: [
                  Row(
                    children: [
                      const Expanded(
                        child: Text(
                          'ҮЙЛЧИЛГЭЭНИЙ ТАНИЛЦУУЛГА',
                          style: TextStyle(
                            color: AppColors.blue,
                            fontSize: 12,
                            fontWeight: FontWeight.w800,
                            letterSpacing: 0.5,
                          ),
                        ),
                      ),
                      if (onBook != null && service != null)
                        IconButton(
                          tooltip: 'Танилцуулгыг хаах',
                          onPressed: () => Navigator.of(sheetContext).pop(),
                          icon: const Icon(Icons.close_rounded),
                        ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  Text(service?.name ?? content.name,
                      style: Theme.of(sheetContext).textTheme.headlineMedium),
                  const SizedBox(height: 12),
                  Text(content.summary,
                      style: const TextStyle(color: AppColors.muted,
                          fontSize: 14, height: 1.55)),
                  if (service != null &&
                      service.description.trim().isNotEmpty) ...[
                    const SizedBox(height: 15),
                    _CompactInfo(
                      icon: Icons.info_outline,
                      heading: 'Эмнэлгийн үйлчилгээний тайлбар',
                      text: service.description,
                    ),
                  ],
                  const SizedBox(height: 17),
                  _PricePanel(service: service, isAcademic: isAcademic),
                  const SizedBox(height: 20),
                  const _SectionHeader('Үзлэг, ажилбарын үед'),
                  const SizedBox(height: 10),
                  for (var i = 0; i < content.steps.length; i++)
                    _StepLine(index: i + 1, text: content.steps[i]),
                  const SizedBox(height: 16),
                  _CompactInfo(icon: Icons.help_outline_rounded,
                      heading: 'Хэзээ хэрэгтэй вэ?', text: content.whenUseful),
                  const SizedBox(height: 11),
                  _CompactInfo(icon: Icons.health_and_safety_outlined,
                      heading: 'Дараах арчилгаа', text: content.aftercare),
                  if (service?.aftercareText != null &&
                      service!.aftercareText!.trim().isNotEmpty) ...[
                    const SizedBox(height: 11),
                    _CompactInfo(
                      icon: Icons.medical_information_outlined,
                      heading: 'Suvd Dent-ийн дараах арчилгааны заавар',
                      text: service.aftercareText!,
                    ),
                  ],
                  const SizedBox(height: 11),
                  _CompactInfo(icon: Icons.payments_outlined,
                      heading: 'Үнэ юунаас хамаарах вэ?', text: content.priceFactors),
                  const SizedBox(height: 17),
                  Text(content.note, style: const TextStyle(
                    color: AppColors.muted, fontSize: 12, height: 1.5)),
                  const SizedBox(height: 10),
                  const Text(
                    'Энэ хэсэг нь ерөнхий эрүүл мэндийн тайлбар. '
                    'Suvd Dent эмнэлэгт яг ямар үйлчилгээ, техник хэрэгсэл '
                    'байгааг эмнэлгийн баталгаажсан каталогоос шалгана.',
                    style: TextStyle(color: AppColors.muted,
                        fontSize: 11, height: 1.5),
                  ),
                ],
              ),
            ),
            Container(
              color: Colors.white,
              padding: const EdgeInsets.fromLTRB(24, 12, 24, 18),
              child: SafeArea(
                top: false,
                child: SizedBox(
                  width: double.infinity,
                  child: onBook != null && service != null
                      ? FilledButton.icon(
                          onPressed: () {
                            Navigator.of(sheetContext).pop();
                            onBook();
                          },
                          icon: const Icon(Icons.calendar_month_outlined),
                          label: const Text('Энэ үйлчилгээгээр цаг захиалах'),
                        )
                      : OutlinedButton(
                          onPressed: () => Navigator.of(sheetContext).pop(),
                          child: const Text('Хаах'),
                        ),
                ),
              ),
            ),
          ],
        ),
      );
    },
  );
}

class _PricePanel extends StatelessWidget {
  const _PricePanel({required this.service, required this.isAcademic});
  final BookingService? service;
  final bool isAcademic;
  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.all(17),
    decoration: BoxDecoration(
      color: AppColors.sky,
      borderRadius: BorderRadius.circular(17),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text('ҮНИЙН МЭДЭЭЛЭЛ',
            style: TextStyle(color: AppColors.blue,
                fontWeight: FontWeight.w700, fontSize: 11)),
        const SizedBox(height: 9),
        Text(clinicPriceLabel(service),
            style: const TextStyle(
                color: AppColors.navy,
                fontWeight: FontWeight.w800, fontSize: 22)),
        const SizedBox(height: 5),
        Text(
          isAcademic
              ? service == null
                    ? 'Сургалтын энэ тайлбарт жишиг үнэ оруулаагүй.'
                    : 'Сургалтын жишиг үнэ. Бодит төлбөрийн дүн биш.'
              : service == null
              ? 'Одоогоор энэ төрлийн үйлчилгээний үнэ эмнэлгээс ирээгүй.'
              : 'Эмнэлгийн LIVE үйлчилгээний бүртгэлээс ирсэн үнэ. '
                'Оношилгоо, нэмэлт эмчилгээний дараа төлөх дүн өөрчлөгдөж болно.',
          style: const TextStyle(color: AppColors.muted, fontSize: 12),
        ),
      ],
    ),
  );
}

class _SectionHeader extends StatelessWidget {
  const _SectionHeader(this.text);
  final String text;
  @override
  Widget build(BuildContext context) => Text(text,
    style: Theme.of(context).textTheme.titleLarge);
}

class _StepLine extends StatelessWidget {
  const _StepLine({required this.index, required this.text});
  final int index;
  final String text;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 11),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Container(
          height: 28, width: 28,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: AppColors.sky, borderRadius: BorderRadius.circular(9)),
          child: Text('$index', style: const TextStyle(
            fontWeight: FontWeight.w800, color: AppColors.blue)),
        ),
        const SizedBox(width: 12),
        Expanded(child: Padding(
          padding: const EdgeInsets.only(top: 4),
          child: Text(text, style: const TextStyle(
            color: AppColors.ink, fontSize: 14, height: 1.45)),
        )),
      ],
    ),
  );
}

class _CompactInfo extends StatelessWidget {
  const _CompactInfo({
    required this.icon,
    required this.heading,
    required this.text,
  });
  final IconData icon;
  final String heading, text;
  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(15),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, color: AppColors.blue, size: 20),
          const SizedBox(width: 12),
          Expanded(child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(heading, style: const TextStyle(
                color: AppColors.navy, fontWeight: FontWeight.w700)),
              const SizedBox(height: 4),
              Text(text, style: const TextStyle(
                fontSize: 13, color: AppColors.muted, height: 1.45)),
            ],
          )),
        ],
      ),
    ),
  );
}
