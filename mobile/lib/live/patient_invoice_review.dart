import 'package:flutter/material.dart';

typedef InvoiceReviewReader = Future<InvoiceReviewRead?> Function(
  int bookingId,
);

/// Out-of-band booking binding for an injected review source. The breakdown
/// itself deliberately contains no patient, clinic or appointment identifier.
class InvoiceReviewRead {
  final int bookingId;
  final InvoiceReviewBreakdown breakdown;

  const InvoiceReviewRead(this.bookingId, this.breakdown);
}

class _InvoiceReviewUnits {
  static final _digits = RegExp(r'^(0|[1-9][0-9]{0,18})$');
  static final _maximum = BigInt.parse('9223372036854775807');

  static BigInt parse(Object? value) {
    if (value is! String || !_digits.hasMatch(value) || value.trim() != value) {
      throw const FormatException('Invalid review units.');
    }
    final amount = BigInt.parse(value);
    if (amount > _maximum) {
      throw const FormatException('Review units overflow.');
    }
    return amount;
  }

  static String display(BigInt amount, int scale, String currency) {
    final digits = amount.toString().padLeft(scale + 1, '0');
    if (scale == 0) return '$digits $currency';
    final split = digits.length - scale;
    return '${digits.substring(0, split)}.${digits.substring(split)} $currency';
  }
}

class InvoiceReviewLine {
  final int sequence;
  final BigInt charge, discount, tax, total;

  const InvoiceReviewLine._(
    this.sequence,
    this.charge,
    this.discount,
    this.tax,
    this.total,
  );

  factory InvoiceReviewLine._parse(Object? value, int expectedSequence) {
    final fields = _shape(value, {
      'sequence',
      'labelKey',
      'chargeUnits',
      'discountUnits',
      'taxUnits',
      'reviewTotalUnits',
    });
    if (fields['sequence'] is! int ||
        fields['sequence'] != expectedSequence ||
        fields['labelKey'] != 'invoice.review.charge') {
      throw const FormatException('Invalid review line.');
    }
    final charge = _InvoiceReviewUnits.parse(fields['chargeUnits']);
    final discount = _InvoiceReviewUnits.parse(fields['discountUnits']);
    final tax = _InvoiceReviewUnits.parse(fields['taxUnits']);
    final total = _InvoiceReviewUnits.parse(fields['reviewTotalUnits']);
    if (discount > charge || charge - discount + tax != total) {
      throw const FormatException('Mismatched review line total.');
    }
    return InvoiceReviewLine._(expectedSequence, charge, discount, tax, total);
  }
}

Map<String, dynamic> _shape(Object? value, Set<String> keys) {
  if (value is! Map<String, dynamic> ||
      value.length != keys.length ||
      !value.keys.every(keys.contains)) {
    throw const FormatException('Invalid review structure.');
  }
  return value;
}

/// Decoder for #44's internal projection, not an API client or a payable
/// invoice. Only exact canonical unit strings and a fixed label are accepted.
class InvoiceReviewBreakdown {
  final String currency;
  final int scale;
  final BigInt subtotal, discount, tax, total;
  final List<InvoiceReviewLine> lines;
  bool get payable => false;

  const InvoiceReviewBreakdown._(
    this.currency,
    this.scale,
    this.subtotal,
    this.discount,
    this.tax,
    this.total,
    this.lines,
  );

  factory InvoiceReviewBreakdown.fromJson(Object? value) {
    final fields = _shape(value, {
      'status',
      'payable',
      'currency',
      'scale',
      'subtotalUnits',
      'discountUnits',
      'taxUnits',
      'reviewTotalUnits',
      'lines',
    });
    final currency = fields['currency'];
    final scale = fields['scale'];
    final data = fields['lines'];
    if (fields['status'] != 'draftReview' ||
        fields['payable'] != false ||
        currency is! String ||
        currency.length != 3 ||
        !RegExp(r'^[A-Z]{3}$').hasMatch(currency) ||
        scale is! int ||
        scale < 0 ||
        scale > 28 ||
        data is! List ||
        data.isEmpty ||
        data.length > 100) {
      throw const FormatException('Invalid nonpayable review.');
    }
    final subtotal = _InvoiceReviewUnits.parse(fields['subtotalUnits']);
    final discount = _InvoiceReviewUnits.parse(fields['discountUnits']);
    final tax = _InvoiceReviewUnits.parse(fields['taxUnits']);
    final total = _InvoiceReviewUnits.parse(fields['reviewTotalUnits']);
    final lines = List<InvoiceReviewLine>.unmodifiable([
      for (var i = 0; i < data.length; i++)
        InvoiceReviewLine._parse(data[i], i + 1),
    ]);
    BigInt sum(BigInt Function(InvoiceReviewLine) unit) =>
        lines.fold(BigInt.zero, (running, line) => running + unit(line));
    if (discount > subtotal ||
        subtotal - discount + tax != total ||
        sum((row) => row.charge) != subtotal ||
        sum((row) => row.discount) != discount ||
        sum((row) => row.tax) != tax ||
        sum((row) => row.total) != total) {
      throw const FormatException('Mismatched review total.');
    }
    return InvoiceReviewBreakdown._(
      currency,
      scale,
      subtotal,
      discount,
      tax,
      total,
      lines,
    );
  }

  String format(BigInt amount) =>
      _InvoiceReviewUnits.display(amount, scale, currency);
}

class PatientInvoiceReviewPanel extends StatefulWidget {
  const PatientInvoiceReviewPanel({
    super.key,
    required this.bookingId,
    required this.reader,
  });

  final int bookingId;
  final InvoiceReviewReader reader;

  @override
  State<PatientInvoiceReviewPanel> createState() =>
      _PatientInvoiceReviewPanelState();
}

class _PatientInvoiceReviewPanelState extends State<PatientInvoiceReviewPanel> {
  late Future<InvoiceReviewRead?> _response;

  @override
  void initState() {
    super.initState();
    _read();
  }

  @override
  void didUpdateWidget(PatientInvoiceReviewPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.bookingId != oldWidget.bookingId ||
        widget.reader != oldWidget.reader) {
      _read();
    }
  }

  void _read() {
    final id = widget.bookingId;
    final reader = widget.reader;
    _response = Future.sync(() => reader(id));
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<InvoiceReviewRead?>(
    key: ObjectKey(_response),
    future: _response,
    builder: (context, snapshot) {
      final read = snapshot.data;
      final review =
          snapshot.connectionState == ConnectionState.done &&
              !snapshot.hasError &&
              read?.bookingId == widget.bookingId
          ? read?.breakdown
          : null;
      return Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Semantics(
                container: true,
                header: true,
                child: const Text(
                  'Төлөх боломжгүй ноорог',
                  style: TextStyle(fontWeight: FontWeight.bold),
                ),
              ),
              const Text(
                'Зөвхөн туршилтын задаргаа. Энэ нь төлбөр төлөх нэхэмжлэх эсвэл төлбөрийн баримт биш.',
              ),
              if (snapshot.connectionState != ConnectionState.done)
                Semantics(
                  container: true,
                  liveRegion: true,
                  child: const Text('Нооргийг ачаалж байна…'),
                )
              else if (review == null)
                Semantics(
                  container: true,
                  liveRegion: true,
                  child: const Text(
                    'Энэ захиалгын задаргааг харуулах боломжгүй.',
                  ),
                )
              else ...[
                for (final row in review.lines) ...[
                  const Divider(),
                  Text('Тооцооны мөр ${row.sequence}'),
                  Text('Дүн: ${review.format(row.charge)}'),
                  Text('Хөнгөлөлт: ${review.format(row.discount)}'),
                  Text('Татвар: ${review.format(row.tax)}'),
                  Text('Мөрийн нийлбэр: ${review.format(row.total)}'),
                ],
                const Divider(),
                Text('Дэд нийлбэр: ${review.format(review.subtotal)}'),
                Text('Нийт хөнгөлөлт: ${review.format(review.discount)}'),
                Text('Нийт татвар: ${review.format(review.tax)}'),
                Semantics(
                  container: true,
                  child: Text(
                    'Нооргийн нийлбэр: ${review.format(review.total)}',
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
