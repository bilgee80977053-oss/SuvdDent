import 'package:flutter/material.dart';

import 'app_theme.dart';

Duration clinicalMotion(BuildContext context, [int milliseconds = 180]) =>
    MediaQuery.disableAnimationsOf(context) ||
        MediaQuery.accessibleNavigationOf(context)
    ? Duration.zero
    : Duration(milliseconds: milliseconds);

class _ClinicMark extends StatelessWidget {
  const _ClinicMark();

  @override
  Widget build(BuildContext context) => ExcludeSemantics(
    child: Container(
      width: 36,
      height: 36,
      decoration: BoxDecoration(
        gradient: const LinearGradient(
          colors: [AppColors.navy, AppColors.blue],
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
        ),
        borderRadius: BorderRadius.circular(12),
      ),
      child: const Icon(
        Icons.medical_services_outlined,
        color: Colors.white,
        size: 21,
      ),
    ),
  );
}

/// Presentation-only widgets matching the Nexora Dental Figma Clinical Premium
/// screens. Data, identity and booking transitions remain with the controllers.
class ClinicalHeader extends StatelessWidget implements PreferredSizeWidget {
  const ClinicalHeader({super.key, this.onBack, this.actions = const []});

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
        ? const Center(child: _ClinicMark())
        : IconButton(
            key: const ValueKey('back-button'),
            tooltip: 'Буцах',
            onPressed: onBack,
            icon: const Icon(Icons.arrow_back_rounded),
          ),
    titleSpacing: 0,
    title: const Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'Suvd Dent',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(
            color: AppColors.navy,
            fontSize: 19,
            fontWeight: FontWeight.w800,
          ),
        ),
        Text(
          'Дархан-Уул · Төв салбар',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(
            color: AppColors.muted,
            fontSize: 10,
            fontWeight: FontWeight.w500,
          ),
        ),
      ],
    ),
    actions: actions,
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
      Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 5),
        decoration: BoxDecoration(
          color: AppColors.sky,
          borderRadius: BorderRadius.circular(50),
        ),
        child: Text(
          eyebrow,
          style: const TextStyle(
            color: AppColors.blue,
            fontSize: 12,
            fontWeight: FontWeight.w700,
          ),
        ),
      ),
      const SizedBox(height: 12),
      Semantics(
        header: true,
        child: Text(
          title,
          key: titleKey,
          style: Theme.of(context).textTheme.headlineMedium,
        ),
      ),
      if (description != null) ...[
        const SizedBox(height: 12),
        Text(
          description!,
          style: Theme.of(context).textTheme.bodyMedium
              ?.copyWith(color: AppColors.muted),
        ),
      ],
      if (progress != null) ...[
        const SizedBox(height: 18),
        TweenAnimationBuilder<double>(
          tween: Tween(end: progress!.clamp(0.0, 1.0).toDouble()),
          duration: clinicalMotion(context, 220),
          builder: (context, value, child) => ClipRRect(
            borderRadius: BorderRadius.circular(4),
            child: LinearProgressIndicator(
              value: value,
              semanticsLabel: 'Захиалгын явц',
              minHeight: 5,
              backgroundColor: AppColors.line,
            ),
          ),
        ),
      ],
    ],
  );
}

class ClinicalHero extends StatelessWidget {
  const ClinicalHero({super.key, required this.onBook, this.enabled = true});
  final VoidCallback onBook;
  final bool enabled;

  @override
  Widget build(BuildContext context) => ClipRRect(
    borderRadius: BorderRadius.circular(24),
    child: DecoratedBox(
      decoration: const BoxDecoration(
        gradient: LinearGradient(
          colors: [AppColors.heroDark, AppColors.heroBlue],
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
        ),
      ),
      child: Stack(
        children: [
          const Positioned(
            top: -60,
            right: -46,
            child: IgnorePointer(
              child: ExcludeSemantics(
                child: Icon(
                  Icons.blur_circular_rounded,
                  size: 245,
                  color: Color(0x245FE0F8),
                ),
              ),
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(22),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Icon(
                  Icons.calendar_month_outlined,
                  color: AppColors.heroLight,
                  size: 25,
                ),
                const SizedBox(height: 12),
                const Text(
                  'Шүдний үзлэгт\nцаг захиалах',
                  style: TextStyle(
                    fontSize: 27,
                    height: 1.13,
                    fontWeight: FontWeight.w800,
                    color: Colors.white,
                  ),
                ),
                const SizedBox(height: 10),
                const Text(
                  'Үйлчилгээ, эмч, өөрт тохирох цагаа сонгоорой.',
                  style: TextStyle(
                    color: AppColors.heroLight,
                    fontSize: 14,
                    height: 1.45,
                  ),
                ),
                const SizedBox(height: 18),
                SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(
                    onPressed: enabled ? onBook : null,
                    style: FilledButton.styleFrom(
                      backgroundColor: Colors.white,
                      foregroundColor: AppColors.navy,
                      minimumSize: const Size(0, 48),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(13),
                      ),
                    ),
                    label: const Text('Цаг сонгох'),
                    icon: const Icon(Icons.arrow_forward_rounded, size: 19),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
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
                    color: selected ? const Color(0xFFCFEAF9) : AppColors.paper,
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
                SizedBox(
                  width: 22,
                  child: AnimatedSwitcher(
                    duration: clinicalMotion(context, 140),
                    child: Icon(
                      selected
                          ? Icons.check_circle_rounded
                          : Icons.chevron_right,
                      key: ValueKey(selected),
                      color: AppColors.blue,
                      size: 22,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    ),
  );
}

/// A two-week date grid based on today's device calendar. Server-side
/// availability still validates the clinic's timezone and allowable horizon.
class ClinicalCalendarPicker extends StatelessWidget {
  const ClinicalCalendarPicker({
    super.key,
    required this.selectedDate,
    required this.onSelected,
    this.enabled = true,
  });

  final DateTime selectedDate;
  final ValueChanged<DateTime> onSelected;
  final bool enabled;

  static const _weekdays = ['Да', 'Мя', 'Лх', 'Пү', 'Ба', 'Бя', 'Ня'];

  @override
  Widget build(BuildContext context) {
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);
    final monday = today.subtract(Duration(days: today.weekday - 1));
    final last = today.add(const Duration(days: 30));
    final selected = DateTime(
      selectedDate.year,
      selectedDate.month,
      selectedDate.day,
    );
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${selected.year} оны ${selected.month}-р сар',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 18),
            Row(
              children: [
                for (final label in _weekdays)
                  Expanded(
                    child: Center(
                      child: Text(
                        label,
                        style: const TextStyle(
                          color: AppColors.muted,
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 8),
            GridView.builder(
              shrinkWrap: true,
              physics: const NeverScrollableScrollPhysics(),
              itemCount: 14,
              gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                crossAxisCount: 7,
                mainAxisSpacing: 8,
                crossAxisSpacing: 5,
                childAspectRatio: 1,
              ),
              itemBuilder: (context, index) {
                final date = monday.add(Duration(days: index));
                final selectable =
                    enabled && !date.isBefore(today) && !date.isAfter(last);
                final isSelected = date == selected;
                return Semantics(
                  selected: isSelected,
                  button: true,
                  label: '${date.year}-${date.month}-${date.day}',
                  child: Material(
                    color: isSelected ? AppColors.navy : AppColors.paper,
                    borderRadius: BorderRadius.circular(11),
                    child: InkWell(
                      onTap: selectable ? () => onSelected(date) : null,
                      borderRadius: BorderRadius.circular(11),
                      child: Center(
                        child: Text(
                          '${date.day}',
                          style: TextStyle(
                            fontWeight: isSelected
                                ? FontWeight.w800
                                : FontWeight.w500,
                            color: isSelected
                                ? Colors.white
                                : selectable
                                ? AppColors.ink
                                : AppColors.muted,
                          ),
                        ),
                      ),
                    ),
                  ),
                );
              },
            ),
          ],
        ),
      ),
    );
  }
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
          Text(title, style: Theme.of(context).textTheme.titleLarge),
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
  Widget build(BuildContext context) {
    final keyText = Text(
      label,
      style: const TextStyle(color: AppColors.muted, fontSize: 13),
    );
    final valueText = Text(
      value,
      style: const TextStyle(
        color: AppColors.navy,
        fontSize: 14,
        fontWeight: FontWeight.w700,
      ),
    );
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: LayoutBuilder(
        builder: (context, constraints) {
          final enlarged = MediaQuery.textScalerOf(context).scale(14) >= 23;
          if (constraints.maxWidth < 270 || enlarged) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [keyText, const SizedBox(height: 4), valueText],
            );
          }
          return Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SizedBox(width: 112, child: keyText),
              Expanded(child: valueText),
            ],
          );
        },
      ),
    );
  }
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
                child: Semantics(
                  selected: activeIndex == i,
                  button: true,
                  enabled: items[i].$3 != null,
                  child: Material(
                    color: Colors.white,
                    child: InkWell(
                      onTap: items[i].$3,
                      child: Padding(
                        padding: const EdgeInsets.symmetric(
                          vertical: 8,
                          horizontal: 2,
                        ),
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            AnimatedContainer(
                              duration: clinicalMotion(context),
                              curve: Curves.easeOut,
                              padding: const EdgeInsets.symmetric(
                                vertical: 4,
                                horizontal: 12,
                              ),
                              decoration: BoxDecoration(
                                color: activeIndex == i
                                    ? AppColors.sky
                                    : Colors.white,
                                borderRadius: BorderRadius.circular(24),
                              ),
                              child: Icon(
                                items[i].$1,
                                size: 23,
                                color: activeIndex == i
                                    ? AppColors.blue
                                    : AppColors.muted,
                              ),
                            ),
                            const SizedBox(height: 1),
                            Text(
                              items[i].$2,
                              textAlign: TextAlign.center,
                              style: TextStyle(
                                fontSize: 11,
                                fontWeight: activeIndex == i
                                    ? FontWeight.w800
                                    : FontWeight.w500,
                                color: activeIndex == i
                                    ? AppColors.navy
                                    : AppColors.muted,
                              ),
                            ),
                          ],
                        ),
                      ),
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
