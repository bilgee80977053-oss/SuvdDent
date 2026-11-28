import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../app/app_theme.dart';
import '../app/clinical_widgets.dart';
import 'clinical_service_details.dart';
import 'dental_api.dart';
import 'models.dart';

/// The screen never charges a card, fabricates a QR or issues a paid receipt.
/// All monetary service prices come from the authenticated server estimate.
class PaymentEstimateScreen extends StatefulWidget {
  const PaymentEstimateScreen({
    super.key,
    required this.api,
    required this.appointment,
    this.allowEstimateEmail = true,
  });

  final DentalApi api;
  final Appointment appointment;
  final bool allowEstimateEmail;

  @override
  State<PaymentEstimateScreen> createState() => _PaymentEstimateScreenState();
}

class _PaymentEstimateScreenState extends State<PaymentEstimateScreen> {
  late Future<PaymentEstimate> _estimate;
  String _method = 'qpay';
  String _delivery = 'email';
  bool _sendingEmail = false;
  String? _emailResult;
  int _selectionVersion = 0;

  static const _methods = <(String, String, IconData, String)>[
    (
      'qpay',
      'QPay / QR',
      Icons.qr_code_2_rounded,
      'Баталгаатай нэхэмжлэх, QR болон төлбөр шалгах серверийн холболт шаардлагатай.',
    ),
    (
      'card',
      'Банкны карт / POS',
      Icons.credit_card_rounded,
      'Эмнэлгийн төлбөрийн терминал эсвэл баталгаажсан картын gateway шаардлагатай.',
    ),
    (
      'transfer',
      'Дансны шилжүүлэг',
      Icons.account_balance_outlined,
      'Эмнэлгийн албан ёсны данс болон гүйлгээ тулгалтын баталгаажуулалт шаардлагатай.',
    ),
    (
      'bankapp',
      'Банкны апп',
      Icons.phone_android_rounded,
      'Мерчантын deeplink/QR нэхэмжлэхийг баталгаатай төлбөрийн үйлчилгээ үүсгэнэ.',
    ),
    (
      'cash',
      'Бэлэн мөнгө',
      Icons.payments_outlined,
      'Зөвхөн эмнэлгийн касс төлбөр хүлээн авч баталгаажуулсны дараа баримт гарна.',
    ),
    (
      'corporate',
      'Байгууллагын нэхэмжлэх',
      Icons.business_outlined,
      'Эмнэлгийн гэрээ, нэхэмжлэл ба баталгаажуулалтын тусдаа үйл явц шаардлагатай.',
    ),
  ];

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void didUpdateWidget(covariant PaymentEstimateScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.api == widget.api &&
        oldWidget.appointment.id == widget.appointment.id &&
        oldWidget.appointment.serviceId == widget.appointment.serviceId) {
      return;
    }
    _selectionVersion++;
    _method = 'qpay';
    _delivery = 'email';
    _sendingEmail = false;
    _emailResult = null;
    _load();
  }

  void _load() {
    _estimate = widget.api.paymentEstimate(
      widget.appointment.id,
      expectedServiceId: widget.appointment.serviceId,
    );
  }

  Future<void> _sendEstimateEmail() async {
    if (_sendingEmail ||
        !widget.allowEstimateEmail ||
        widget.api.settings.isAcademic) {
      return;
    }
    final selection = _selectionVersion;
    setState(() {
      _sendingEmail = true;
      _emailResult = null;
    });
    try {
      final result = await widget.api.emailPaymentEstimate(
        widget.appointment.id,
      );
      if (!mounted || selection != _selectionVersion) return;
      setState(() => _emailResult = result);
    } on ApiFailure catch (e) {
      if (!mounted || selection != _selectionVersion) return;
      setState(() => _emailResult = e.message);
    } catch (_) {
      if (!mounted || selection != _selectionVersion) return;
      setState(
        () => _emailResult = 'Имэйл илгээлтийг баталгаажуулж чадсангүй.',
      );
    } finally {
      if (mounted && selection == _selectionVersion) {
        setState(() => _sendingEmail = false);
      }
    }
  }

  String _estimateText(PaymentEstimate quote) {
    final low = quote.minPriceMnt;
    final high = quote.maxPriceMnt;
    if (low == null) return 'Тариф баталгаажаагүй';
    if (high == null || low == high) return mnt(low);
    return '${mnt(low)} – ${mnt(high)}';
  }

  String _preview(PaymentEstimate quote) => [
    'Suvd Dent',
    'Дархан-Уул • Suvd Dent',
    if (!widget.api.settings.isAcademic)
      'Сонгосон төлбөрийн арга: ${_methods.firstWhere((m) => m.$1 == _method).$2}',
    'ҮНИЙН МЭДЭЭЛЛИЙН ТӨСӨЛ (ТӨЛБӨРИЙН БАРИМТ БИШ)',
    'Захиалгын дугаар: ${quote.appointmentId}',
    'Үйлчилгээ: ${quote.serviceName}',
    'Эмнэлгийн бүртгэл дэх үнэ: ${_estimateText(quote)}',
    'Нэмэлт ажилбар: үзлэгийн дараа тогтооно',
    'Хөнгөлөлт: баталгаажаагүй',
    'Татвар/НӨАТ: эмнэлгийн тооцоогоор баталгаажна',
    'Эцсийн төлөх дүн: баталгаажаагүй',
    'Төлбөрийн гүйлгээ: энэ аппд баталгаажаагүй',
    'Энэ баримт нь төлбөр төлснийг нотлохгүй.',
  ].join('\n');

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: const ClinicalHeader(),
    body: SafeArea(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 760),
          child: FutureBuilder<PaymentEstimate>(
            future: _estimate,
            builder: (context, snapshot) {
              if (snapshot.connectionState != ConnectionState.done) {
                return const Center(child: CircularProgressIndicator());
              }
              if (snapshot.hasError) {
                return Center(
                  child: Padding(
                    padding: const EdgeInsets.all(24),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        const Icon(
                          Icons.error_outline_rounded,
                          color: AppColors.blue,
                          size: 42,
                        ),
                        const SizedBox(height: 14),
                        const Text(
                          'Үнийн мэдээллийг авах боломжгүй байна. '
                          'Серверийн холболтыг шалгана уу.',
                          textAlign: TextAlign.center,
                        ),
                        const SizedBox(height: 14),
                        OutlinedButton(
                          onPressed: () => setState(_load),
                          child: const Text('Дахин оролдох'),
                        ),
                      ],
                    ),
                  ),
                );
              }
              final q = snapshot.data!;
              return ListView(
                padding: const EdgeInsets.fromLTRB(24, 24, 24, 38),
                children: [
                  const ClinicalPageIntro(
                    eyebrow: 'СУВД ДЕНТ  •  ҮНИЙН МЭДЭЭЛЭЛ',
                    title: 'Төлбөрийн дэлгэрэнгүй',
                    description:
                        'Захиалгад хамаарах үйлчилгээний үнийн мэдээлэл. '
                        'Энэ нь төлбөр төлөх нэхэмжлэх биш.',
                  ),
                  const SizedBox(height: 23),
                  ClinicalInfoPanel(
                    title: 'Үнийн задаргаа',
                    children: [
                      ClinicalDetail('Захиалга', '#${q.appointmentId}'),
                      const Divider(),
                      ClinicalDetail('Үйлчилгээ', q.serviceName),
                      const Divider(),
                      ClinicalDetail('Үндсэн тариф', _estimateText(q)),
                      const Divider(),
                      const ClinicalDetail(
                        'Нэмэлт ажилбар',
                        'Оношилгоо, эмчилгээний явцад тодорхойлно',
                      ),
                      const Divider(),
                      const ClinicalDetail(
                        'Хөнгөлөлт',
                        'Баталгаажсан хөнгөлөлт одоогоор алга',
                      ),
                      const Divider(),
                      const ClinicalDetail(
                        'Татвар, бусад зардал',
                        'Эмнэлгийн эцсийн тооцоогоор баталгаажна',
                      ),
                    ],
                  ),
                  const SizedBox(height: 14),
                  Container(
                    padding: const EdgeInsets.all(20),
                    decoration: BoxDecoration(
                      color: AppColors.sky,
                      borderRadius: BorderRadius.circular(17),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const Text(
                          'ЭЦСИЙН ТӨЛӨХ ДҮН',
                          style: TextStyle(
                            color: AppColors.blue,
                            fontWeight: FontWeight.w800,
                            fontSize: 12,
                          ),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          q.isFinalAmount && q.totalDueMnt != null
                              ? mnt(q.totalDueMnt!)
                              : 'Одоогоор баталгаажаагүй',
                          style: const TextStyle(
                            color: AppColors.navy,
                            fontWeight: FontWeight.w800,
                            fontSize: 22,
                          ),
                        ),
                        const SizedBox(height: 10),
                        Text(
                          q.priceExplanation,
                          style: const TextStyle(
                            color: AppColors.muted,
                            fontSize: 12,
                            height: 1.5,
                          ),
                        ),
                      ],
                    ),
                  ),
                  if (!widget.api.settings.isAcademic) ...[
                    const SizedBox(height: 26),
                    Text(
                      'Төлбөрийн хэрэгсэл',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    const SizedBox(height: 8),
                    const Text(
                      'Арга сонгоход тухайн төлбөрийн шаардлага гарна. '
                      'Серверээр баталгаажаагүй гүйлгээг эндээс хийхгүй.',
                      style: TextStyle(fontSize: 12, color: AppColors.muted),
                    ),
                    const SizedBox(height: 16),
                    ..._methods.map((method) => _methodCard(method)),
                    const SizedBox(height: 13),
                    AnimatedSwitcher(
                      duration: const Duration(milliseconds: 250),
                      child: Container(
                        key: ValueKey(_method),
                        padding: const EdgeInsets.all(16),
                        decoration: BoxDecoration(
                          color: AppColors.sky,
                          borderRadius: BorderRadius.circular(14),
                        ),
                        child: Text(
                          _methods.firstWhere((m) => m.$1 == _method).$4,
                          style: const TextStyle(
                            color: AppColors.ink,
                            fontSize: 13,
                            height: 1.45,
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(height: 19),
                    AnimatedSwitcher(
                      duration: const Duration(milliseconds: 240),
                      child: Card(
                        key: ValueKey('preview-$_method'),
                        child: Padding(
                          padding: const EdgeInsets.all(17),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              const Text(
                                'ҮНИЙН МЭДЭЭЛЛИЙН ТӨСӨЛ',
                                style: TextStyle(
                                  color: AppColors.navy,
                                  fontWeight: FontWeight.w800,
                                  fontSize: 15,
                                ),
                              ),
                              const SizedBox(height: 12),
                              Text(
                                'Арга: ${_methods.firstWhere((m) => m.$1 == _method).$2}',
                                style: const TextStyle(
                                  color: AppColors.blue,
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                              const SizedBox(height: 9),
                              ClinicalDetail('Захиалга', '#${q.appointmentId}'),
                              ClinicalDetail('Үйлчилгээ', q.serviceName),
                              ClinicalDetail('Үнийн хүрээ', _estimateText(q)),
                              const Divider(),
                              const Text(
                                'Гүйлгээний баримт биш • Эцсийн дүн баталгаажаагүй',
                                style: TextStyle(
                                  color: AppColors.muted,
                                  fontSize: 12,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ],
                  if (widget.allowEstimateEmail &&
                      !widget.api.settings.isAcademic) ...[
                    const SizedBox(height: 28),
                    Text(
                      'Баримт авах сонголт',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    const SizedBox(height: 12),
                    SegmentedButton<String>(
                      segments: const [
                        ButtonSegment(
                          value: 'email',
                          label: Text('Имэйл'),
                          icon: Icon(Icons.mail_outline),
                        ),
                        ButtonSegment(
                          value: 'paper',
                          label: Text('Хэвлэмэл'),
                          icon: Icon(Icons.print_outlined),
                        ),
                      ],
                      selected: {_delivery},
                      onSelectionChanged: (values) {
                        setState(() => _delivery = values.first);
                      },
                    ),
                    const SizedBox(height: 12),
                    Text(
                      _delivery == 'email'
                          ? 'Төлбөрийг эмнэлэг баталгаажуулсны дараа '
                                'баримтыг бүртгэлтэй имэйл хаягаар илгээх боломжтой болно.'
                          : 'Төлбөр баталгаажсаны дараа эмнэлгийн кассаас '
                                'хэвлэмэл баримт авах боломжтой.',
                      style: const TextStyle(
                        color: AppColors.muted,
                        fontSize: 13,
                        height: 1.5,
                      ),
                    ),
                    if (_delivery == 'email') ...[
                      const SizedBox(height: 18),
                      FilledButton.icon(
                        onPressed: _sendingEmail ? null : _sendEstimateEmail,
                        icon: _sendingEmail
                            ? const SizedBox(
                                width: 19,
                                height: 19,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                  color: Colors.white,
                                ),
                              )
                            : const Icon(Icons.send_outlined),
                        label: const Text('Үнийн мэдээллийг имэйлээр авах'),
                      ),
                      if (_emailResult != null)
                        Padding(
                          padding: const EdgeInsets.only(top: 10),
                          child: Text(
                            _emailResult!,
                            style: const TextStyle(
                              color: AppColors.blue,
                              fontSize: 13,
                            ),
                          ),
                        ),
                    ],
                  ],
                  const SizedBox(height: 18),
                  OutlinedButton.icon(
                    onPressed: () async {
                      await Clipboard.setData(ClipboardData(text: _preview(q)));
                      if (!context.mounted) return;
                      ScaffoldMessenger.of(context).showSnackBar(
                        const SnackBar(
                          content: Text(
                            'Үнийн мэдээллийн төсөл хууллаа. '
                            'Энэ нь төлсөн баримт биш.',
                          ),
                        ),
                      );
                    },
                    icon: const Icon(Icons.copy_outlined),
                    label: const Text('Үнийн мэдээллийн төслийг хуулах'),
                  ),
                  const SizedBox(height: 16),
                  if (widget.api.settings.isAcademic)
                    const Text(
                      'Сургалтын хувилбар. Төлбөр, баримт болон имэйл илгээхгүй.',
                    )
                  else
                    const Text(
                      'Төлбөр төлөх, э-баримт гаргах, имэйл илгээх үйлдэл '
                      'эмнэлгийн merchant, касс болон серверийн гүйлгээ '
                      'баталгаажуулалтын холболт хийгдсэний дараа идэвхжинэ.',
                      style: TextStyle(
                        color: AppColors.muted,
                        fontSize: 12,
                        height: 1.45,
                      ),
                    ),
                ],
              );
            },
          ),
        ),
      ),
    ),
  );

  Widget _methodCard((String, String, IconData, String) method) {
    final selected = _method == method.$1;
    return Padding(
      padding: const EdgeInsets.only(bottom: 9),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 220),
        curve: Curves.easeInOutCubic,
        decoration: BoxDecoration(
          color: selected ? AppColors.sky : Colors.white,
          borderRadius: BorderRadius.circular(15),
          border: Border.all(
            color: selected ? AppColors.blue : AppColors.line,
            width: selected ? 1.5 : 1,
          ),
        ),
        child: InkWell(
          borderRadius: BorderRadius.circular(15),
          onTap: () => setState(() => _method = method.$1),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
            child: Row(
              children: [
                Icon(method.$3, color: AppColors.blue),
                const SizedBox(width: 14),
                Expanded(
                  child: Text(
                    method.$2,
                    style: const TextStyle(
                      fontWeight: FontWeight.w700,
                      color: AppColors.navy,
                    ),
                  ),
                ),
                Icon(
                  selected
                      ? Icons.radio_button_checked_rounded
                      : Icons.radio_button_off_rounded,
                  color: selected ? AppColors.blue : AppColors.muted,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
