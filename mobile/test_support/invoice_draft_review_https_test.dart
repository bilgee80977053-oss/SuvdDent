import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/io_client.dart';
import 'package:nexora_dental_mobile/live/dental_api.dart';
import 'package:nexora_dental_mobile/live/models.dart';

class _FixtureTransport extends http.BaseClient {
  _FixtureTransport(this.origin, SecurityContext trust)
    : _client = IOClient(
        HttpClient(context: trust)..findProxy = (_) => 'DIRECT',
      );

  final Uri origin;
  final IOClient _client;
  final List<int> reviewStatuses = [];

  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    if (request.url.origin != origin.origin) {
      throw StateError('Only the synthetic loopback fixture is permitted.');
    }
    request.followRedirects = false;
    final response = await _client.send(request);
    if (request.url.path.endsWith('/invoice-draft-review')) {
      expect(request.headers['Cache-Control'], 'no-store');
      expect(response.headers['cache-control'], contains('no-store'));
      reviewStatuses.add(response.statusCode);
    }
    return response;
  }

  @override
  void close() => _client.close();
}

void main() {
  test(
    'synthetic invoice draft HTTPS owner review and other-patient denial',
    () async {
      final origin = Uri.parse(
        Platform.environment['SUVDDENT_LOOPBACK_ORIGIN']!,
      );
      expect(origin.scheme, 'https');
      expect(origin.host, '127.0.0.1');
      expect(origin.hasPort, isTrue);
      expect(origin.userInfo, isEmpty);
      final trust = SecurityContext(withTrustedRoots: false)
        ..setTrustedCertificatesBytes(
          Platform.environment['SUVDDENT_LOOPBACK_CERT_PEM']!.codeUnits,
        );
      final password =
          Platform.environment['SUVDDENT_LOOPBACK_FIXTURE_PASSWORD']!;
      const appointment = 301;

      Future<_FixtureTransport> check(int clinic, int user, bool owner) async {
        final transport = _FixtureTransport(origin, trust);
        final api = DentalApi(
          ApiSettings(baseUrl: origin.toString(), clinicId: clinic),
          client: transport,
        );
        addTearDown(api.close);
        await api.login('user$user@example.invalid', password);
        final response = await api.readInvoiceDraftReview(appointment);
        if (!owner) {
          expect(response, isNull);
          expect(transport.reviewStatuses, [404]);
          return transport;
        }
        expect(response?.bookingId, appointment);
        final review = response!.breakdown;
        expect(review.payable, isFalse);
        expect(review.lines.map((row) => row.sequence), [1, 2]);
        expect(review.lines.map((row) => review.format(row.charge)), [
          '2.00 XTS',
          '1.00 XTS',
        ]);
        expect(review.lines.map((row) => review.format(row.discount)), [
          '0.15 XTS',
          '0.50 XTS',
        ]);
        expect(review.lines.map((row) => review.format(row.tax)), [
          '0.08 XTS',
          '0.03 XTS',
        ]);
        expect(review.lines.map((row) => review.format(row.total)), [
          '1.93 XTS',
          '0.53 XTS',
        ]);
        expect(review.format(review.subtotal), '3.00 XTS');
        expect(review.format(review.discount), '0.65 XTS');
        expect(review.format(review.tax), '0.11 XTS');
        expect(review.format(review.total), '2.46 XTS');
        expect(transport.reviewStatuses, [200]);
        return transport;
      }

      await check(101, 11, true);
      await check(101, 26, false);
      await check(202, 12, false);
      // Harness verifies the case ran, not just Flutter process exit.
      stdout.writeln('Invoice draft HTTPS acceptance completed.');
    },
  );
}
