import 'package:flutter/material.dart';

import 'app_theme.dart';

/// Presentation-only widgets matching the Nexora Dental Figma Clinical Premium
/// screens. Data, identity and booking transitions remain with the controllers.
class ClinicalHeader extends StatelessWidget implements PreferredSizeWidget {
  const ClinicalHeader({
    super.key,
    this.onBack,
    this.actions = const [],
  });

  final VoidCallback? onBack;
  final List<Widget> actions;

  @override
  Size get preferredSize => const Size.fromHeight(76);

  @override
  Widget build(BuildContext context) => AppBar(
    toolbarHeight: 76,
    elevation: 0,
    backgroundColor: Colors.white,
    leading: onBack == null
        ? const Icon(Icons.medical_services_outlined, color: AppColors.blue)
        : IconButton(
            key: const ValueKey('back-button'),
            tooltip: 'Буцах',
            onPressed: onBack,
            icon: const Icon(Icons.arrow_back_rounded),
          ),
    titleSpacing: 0,
    title: const Text(
      'NEXORA  /  DENTAL',
      style: TextStyle(
        color: AppColors.navy,
        fontSize: 16,
        fontWeight: FontWeight.w800,
        letterSpacing: 0.6,
      ),
    ),
    actions: [
      ...actions,
      const Padding(
        padding: EdgeInsets.only(left: 4, right: 20),
        child: Icon(Icons.circle, size: 17, color: AppColors.blue),
      ),
    ],
  );
}

class ClinicalPageIntro extends StatelessWidget {
  const ClinicalPageIntro({
    super.key,
    required this.eyebrow,
    required this.title,
    this.description,
    this.progress,
    this.titleKey,
  });

  final String eyebrow;
  final String title;
  final String? description;
  final double? progress;
  final Key? titleKey;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        eyebrow,
        style: const TextStyle(
          color: AppColors.muted,
          fontSize: 12,
          fontWeight: FontWeight.w600,
          letterSpacing: 0.4,
        ),
      ),
      const SizedBox(height: 20),
      Text(
        title,
        key: titleKey,
        style: Theme.of(context).textTheme.headlineMedium,
      ),
      if (description != null) ...[
        const SizedBox(height: 12),
        Text(
          description!,
          style: Theme.of(context).textTheme.bodyMedium?.copyWith(
            color: AppColors.muted,
          ),
        ),
      ],
      if (progress != null) ...[
        const SizedBox(height: 18),
        ClipRRect(
          borderRadius: BorderRadius.circular(4),
          child: LinearProgressIndicator(
            value: progress!.clamp(0, 1),
            minHeight: 4,
            backgroundColor: AppColors.line,
          ),
        ),
      ],
    ],
  );
}

class ClinicalHero extends StatelessWidget {
  const ClinicalHero({
    super.key,
    required this.onBook,
    this.enabled = true,
  });
  final VoidCallback onBook;
  final bool enabled;

  @override
  Widget build(BuildContext context) => Container(
    width: double.infinity,
    padding: const EdgeInsets.all(20),
    decoration: BoxDecoration(
      color: AppColors.navy,
      borderRadius: BorderRadius.circular(22),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Align(
          alignment: Alignment.centerRight,
          child: Icon(Icons.auto_awesome_rounded,
              color: Color(0xFF94DCE9), size: 28),
        ),
        const Text(
          'Шүдний үзлэгт\nцаг захиалах',
          style: TextStyle(
            fontSize: 25,
            height: 1.2,
            fontWeight: FontWeight.w800,
            color: Colors.white,
          ),
        ),
        const SizedBox(height: 10),
        const Text(
          'Эмч болон өөрт тохирох цагийг\nхэдхэн алхмаар сонгоорой.',
          style: TextStyle(color: Color(0xFFD3E6F1), fontSize: 13),
        ),
        const SizedBox(height: 16),
        SizedBox(
          width: double.infinity,
          child: FilledButton.icon(
            onPressed: enabled ? onBook : null,
            style: FilledButton.styleFrom(
              backgroundColor: Colors.white,
              foregroundColor: AppColors.navy,
              minimumSize: const Size(0, 43),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(10),
              ),
            ),
            label: const Text('Цаг сонгох'),
            icon: const Icon(Icons.arrow_forward_rounded, size: 19),
          ),
        ),
      ],
    ),
  );
}

class ClinicalFeatureTile extends StatelessWidget {
  const ClinicalFeatureTile({
    super.key,
    required this.icon,
    required this.label,
    this.onTap,
  });

  final IconData icon;
  final String label;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => Expanded(
    child: Card(
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(15),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 16, horizontal: 6),
          child: Column(
            children: [
              Icon(icon, color: AppColors.blue, size: 26),
              const SizedBox(height: 12),
              Text(
                label,
                maxLines: 2,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  fontSize: 12,
                  color: AppColors.ink,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

class ClinicalChoiceCard extends StatelessWidget {
  const ClinicalChoiceCard({
    super.key,
    required this.label,
    required this.subtitle,
    required this.selected,
    required this.onTap,
    this.leadingText,
    this.icon,
  });

  final String label;
  final String subtitle;
  final bool selected;
  final VoidCallback? onTap;
  final String? leadingText;
  final IconData? icon;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 12),
    child: Semantics(
      selected: selected,
      button: true,
      child: Material(
        color: selected ? AppColors.sky : Colors.white,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
          side: BorderSide(
            color: selected ? AppColors.blue : AppColors.line,
            width: selected ? 1.5 : 1,
          ),
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Row(
              children: [
                Container(
                  width: 50,
                  height: 55,
                  decoration: BoxDecoration(
                    color: selected
                        ? const Color(0xFFCFEAF9)
                        : AppColors.paper,
                    borderRadius: BorderRadius.circular(13),
                  ),
                  child: Center(
                    child: icon == null
                        ? Text(
                            leadingText ?? '',
                            style: const TextStyle(
                              color: AppColors.blue,
                              fontWeight: FontWeight.w800,
                              fontSize: 18,
                            ),
                          )
                        : Icon(icon, color: AppColors.blue),
                  ),
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        label,
                        style: const TextStyle(
                          color: AppColors.navy,
                          fontSize: 16,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 7),
                      Text(
                        subtitle,
                        style: const TextStyle(
                          color: AppColors.muted,
                          fontSize: 12,
                          height: 1.4,
                        ),
                      ),
                    ],
                  ),
                ),
                Icon(
                  selected
                      ? Icons.check_circle_rounded
                      : Icons.arrow_forward_ios_rounded,
                  color: AppColors.blue,
                  size: selected ? 22 : 16,
                ),
              ],
            ),
          ),
        ),
      ),
    ),
  );
}

class ClinicalInfoPanel extends StatelessWidget {
  const ClinicalInfoPanel({
    super.key,
    required this.title,
    required this.children,
  });
  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(title,
              style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 18),
          ...children,
        ],
      ),
    ),
  );
}

class ClinicalDetail extends StatelessWidget {
  const ClinicalDetail(this.label, this.value, {super.key});
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 11),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 111,
          child: Text(label,
              style: const TextStyle(color: AppColors.muted, fontSize: 13)),
        ),
        Expanded(
          child: Text(value,
              style: const TextStyle(
                color: AppColors.navy,
                fontSize: 14,
                fontWeight: FontWeight.w600,
              )),
        ),
      ],
    ),
  );
}

class ClinicalNavigation extends StatelessWidget {
  const ClinicalNavigation({
    super.key,
    required this.activeIndex,
    this.onHome,
    this.onBook,
    this.onAppointments,
    this.onProfile,
  });

  final int activeIndex;
  final VoidCallback? onHome;
  final VoidCallback? onBook;
  final VoidCallback? onAppointments;
  final VoidCallback? onProfile;

  @override
  Widget build(BuildContext context) {
    final items = [
      (Icons.home_outlined, 'Нүүр', onHome),
      (Icons.calendar_month_outlined, 'Захиалга', onBook),
      (Icons.favorite_outline, 'Миний цаг', onAppointments),
      (Icons.person_outline_rounded, 'Профайл', onProfile),
    ];
    return SafeArea(
      top: false,
      child: Container(
        decoration: const BoxDecoration(
          color: Colors.white,
          border: Border(top: BorderSide(color: AppColors.line)),
        ),
        child: Row(
          children: [
            for (var i = 0; i < items.length; i++)
              Expanded(
                child: InkWell(
                  onTap: items[i].$3,
                  child: Padding(
                    padding: const EdgeInsets.symmetric(vertical: 11),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(items[i].$1,
                            color: activeIndex == i
                                ? AppColors.blue
                                : AppColors.muted),
                        const SizedBox(height: 3),
                        Text(
                          items[i].$2,
                          style: TextStyle(
                            fontSize: 11,
                            fontWeight: activeIndex == i
                                ? FontWeight.w700
                                : FontWeight.w400,
                            color: activeIndex == i
                                ? AppColors.blue
                                : AppColors.muted,
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
