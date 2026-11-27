import 'package:flutter/material.dart';

import 'live_flow_controller.dart';
import 'live_presentation.dart';

class RescheduleAppointmentScreen extends StatelessWidget {
  final LiveFlowController flow;
  final RescheduleDraft draft;

  const RescheduleAppointmentScreen({
    super.key,
    required this.flow,
    required this.draft,
  });

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: flow,
    builder: (context, _) {
      final active = flow.session != null && identical(flow.reschedule, draft);
      final canChoose = !flow.busy && !draft.uncertain;
      final availability = draft.availability;
      final selection = draft.selection;
      final replacement = draft.replacement;
      return PopScope(
        canPop: !active || !flow.busy,
        child: Scaffold(
          appBar: AppBar(title: const Text('Захиалгын цаг шилжүүлэх')),
          body: !active
              ? const Center(child: Text('Дахин нэвтэрнэ үү.'))
              : ListView(
                  padding: const EdgeInsets.all(20),
                  children: [
                    if (flow.busy)
                      const LinearProgressIndicator(
                        semanticsLabel: 'Ачаалж байна',
                      ),
                    if (flow.error != null)
                      Semantics(
                        liveRegion: true,
                        child: Text(
                          flow.error!,
                          style: TextStyle(
                            color: Theme.of(context).colorScheme.error,
                          ),
                        ),
                      ),
                    if (replacement != null) ...[
                      Semantics(
                        liveRegion: true,
                        child: Text(
                          replacement.status == 'Confirmed'
                              ? 'Шинэ цаг баталгаажлаа.'
                              : 'Цаг шилжсэн боловч тухайн шинэ захиалга одоо идэвхгүй байна.',
                        ),
                      ),
                      AppointmentDetailsCard(appointment: replacement),
                    ] else ...[
                      const Text('Анхны захиалгын мэдээлэл'),
                      AppointmentDetailsCard(appointment: draft.source),
                      const SizedBox(height: 12),
                      if (draft.uncertain)
                        const Text(
                          'Өмнөх хүсэлтийн үр дүн тодорхойгүй. Ижил хүсэлтээр шалгахаас өмнө шинэ цаг бүү сонгоорой.',
                        )
                      else ...[
                        const Text(
                          'Шинэ цаг батлагдсаны дараа л хуучин цаг чөлөөлөгдөнө. Эмч болон үйлчилгээ өөрчлөгдөхгүй.',
                        ),
                        const SizedBox(height: 8),
                        Text(LiveLabels.date(draft.date)),
                        OutlinedButton(
                          onPressed: canChoose
                              ? () async {
                                  final date = await showMongolianDatePicker(
                                    context,
                                    draft.date,
                                    timeZoneId: draft.source.clinicTimeZoneId,
                                  );
                                  if (context.mounted &&
                                      date != null &&
                                      identical(flow.reschedule, draft)) {
                                    await flow.loadRescheduleSlots(
                                      onDate: calendarDate(date),
                                    );
                                  }
                                }
                              : null,
                          child: const Text('Өөр огноо сонгох'),
                        ),
                        OutlinedButton(
                          onPressed: canChoose
                              ? () => flow.loadRescheduleSlots()
                              : null,
                          child: const Text('Боломжит цагийг шинэчлэх'),
                        ),
                      ],
                      if (availability != null) ...[
                        Text(LiveLabels.zone(availability.timeZoneId)),
                        const SizedBox(height: 8),
                        if (availability.slots.isEmpty)
                          const Text('Энэ өдөр боломжит цаг алга.'),
                        if (!draft.uncertain)
                          LiveSlotChoices(
                            page: availability,
                            selected: selection,
                            enabled: canChoose,
                            onSelected: flow.selectRescheduleSlot,
                          ),
                        if (selection != null)
                          BookingTimeSummary(
                            slot: selection,
                            date: availability.date,
                            timeZoneId: availability.timeZoneId,
                          ),
                      ],
                      FilledButton(
                        onPressed: flow.busy || selection == null
                            ? null
                            : flow.submitReschedule,
                        child: Text(
                          draft.uncertain
                              ? 'Ижил хүсэлтээр дахин шалгах'
                              : 'Шинэ цаг баталгаажуулах',
                        ),
                      ),
                    ],
                  ],
                ),
        ),
      );
    },
  );
}
