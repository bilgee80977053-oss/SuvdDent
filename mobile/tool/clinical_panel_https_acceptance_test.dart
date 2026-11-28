import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/io_client.dart';
import 'package:nexora_dental_mobile/live/encounter_treatment_api.dart';
import 'package:nexora_dental_mobile/live/models.dart';
import 'package:nexora_dental_mobile/live/performed_procedure_panel.dart';

const _endpoint = '/api/clinical/appointments/301/performed-procedures';
const _service = 'Synthetic booked service';
const _empty = 'Бүртгэл хараахан байхгүй.';
const _start = 'Ажилбар бүртгэх';
const _confirm = 'Баталгаажуулж бүртгэх';
const _key = '98fcbaca-e637-4dad-a413-7aa83da9c331';

// Create a native HttpClient instead of the widget-test binding's stub.
// The trust store contains only the ephemeral harness certificate.
final class _NativeOverrides extends HttpOverrides {}

// Socket reads run in the real zone captured through tester.runAsync. The
// Flutter state machine still runs in the widget test zone. Observing native
// completion before pumping avoids advancing fake time into an HTTP timeout.
class _LoopbackClient extends http.BaseClient {
  _LoopbackClient(this._client, this._zone, this._origin);

  final http.Client _client;
  final Zone _zone;
  final Uri _origin;
  final methods = <String>[];
  final _completed = <Future<void>>[];

  Future<void> completed(int count) async {
    if (_completed.length != count) {
      throw StateError('Unexpected number of loopback requests.');
    }
    await _completed[count - 1].timeout(const Duration(seconds: 10));
  }

  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) {
    final uri = request.url;
    final path = uri.path;
    final method = request.method;
    final allowed =
        (path == '/api/Auth/login' && method == 'POST') ||
        (path == '/api/Auth/me' && method == 'GET') ||
        (path == _endpoint && (method == 'GET' || method == 'POST'));
    if (uri.scheme != 'https' ||
        uri.host != '127.0.0.1' ||
        uri.port != _origin.port ||
        uri.userInfo.isNotEmpty ||
        uri.hasFragment ||
        request.followRedirects ||
        !allowed) {
      throw StateError('The test transport rejected a non-fixture request.');
    }
    if (path == _endpoint) methods.add(method);
    final response = _zone.run(() async {
      final result = await _client.send(request);
      final bytes = await result.stream.toBytes();
      return http.StreamedResponse(
        Stream.value(bytes),
        result.statusCode,
        headers: result.headers,
        contentLength: bytes.length,
      );
    });
    _completed.add(
      _zone.run(() => response.then<void>((_) {}, onError: (Object _) {})),
    );
    return response;
  }

  @override
  void close() => _zone.run(_client.close);
}

void main() {
  testWidgets('clinician panel confirms and refreshes on real loopback TLS', (
    tester,
  ) async {
    final origin = Uri.parse(Platform.environment['SUVDDENT_LOOPBACK_ORIGIN']!);
    expect(origin.scheme, 'https');
    expect(origin.host, '127.0.0.1');
    expect(origin.hasPort, isTrue);
    expect(origin.path, '/');
    expect(
      origin.hasQuery || origin.hasFragment || origin.userInfo.isNotEmpty,
      isFalse,
    );

    final password =
        Platform.environment['SUVDDENT_LOOPBACK_FIXTURE_PASSWORD']!;
    final trust = SecurityContext(withTrustedRoots: false)
      ..setTrustedCertificatesBytes(
        Platform.environment['SUVDDENT_LOOPBACK_CERT_PEM']!.codeUnits,
      );

    Future<(EncounterTreatmentApi, _LoopbackClient)> login(
      int clinic,
      String syntheticUser,
    ) async {
      final authenticated = await tester.runAsync(() async {
        final socket = _NativeOverrides().createHttpClient(trust);
        socket.findProxy = (_) => 'DIRECT';
        socket.connectionTimeout = const Duration(seconds: 8);
        final bridge = _LoopbackClient(IOClient(socket), Zone.current, origin);
        final api = EncounterTreatmentApi(
          ApiSettings(baseUrl: origin.toString(), clinicId: clinic),
          client: bridge,
        );
        addTearDown(api.close);
        await api.login(syntheticUser, password);
        return (api, bridge);
      });
      return authenticated!;
    }

    final (doctor, ownerWire) = await login(101, 'user13@example.invalid');
    final (foreign, foreignWire) = await login(202, 'user23@example.invalid');
    expect(doctor.authenticated, isTrue);
    expect(foreign.authenticated, isTrue);
    var allocated = 0;
    Future<void> mount(EncounterTreatmentApi api) => tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: PerformedProcedurePanel(
            key: ObjectKey(api),
            api: api,
            appointmentId: 301,
            bookedServiceId: 51,
            bookedServiceLabel: _service,
            requestIdFactory: () {
              allocated++;
              return _key;
            },
          ),
        ),
      ),
    );

    Future<void> flushReceive(_LoopbackClient wire, int count) async {
      await tester.pump();
      await tester.runAsync(() => wire.completed(count));
      await tester.pump();
    }

    await mount(doctor);
    await flushReceive(ownerWire, 3); // Auth, identity, initial history.
    await tester.pumpAndSettle();
    expect(find.text(_empty), findsOneWidget);
    expect(ownerWire.methods, ['GET']);

    await tester.tap(find.text('Нэг шүд'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), '85');
    await tester.tap(find.text(_start));
    await tester.pumpAndSettle();
    expect(find.textContaining('Шүд: 85'), findsOneWidget);
    expect(ownerWire.methods, ['GET']);
    expect(allocated, 0);

    await tester.tap(find.text(_confirm));
    // Finish the dialog route before awaiting the real POST.
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 300));
    await flushReceive(ownerWire, 4);
    await flushReceive(ownerWire, 5); // Automatic audited history refresh.
    await tester.pumpAndSettle();
    expect(ownerWire.methods, ['GET', 'POST', 'GET']);
    expect(allocated, 1);
    expect(find.text('Ажилбар бүртгэгдлээ.'), findsOneWidget);
    expect(find.textContaining('$_service · 85'), findsOneWidget);
    expect(find.text(_empty), findsNothing);
    expect(find.text(_start), findsNothing);

    // A valid token in the other clinic must neither see the signed row nor
    // gain a signing control for this already-selected encounter.
    await mount(foreign);
    await flushReceive(foreignWire, 3);
    await tester.pumpAndSettle();
    expect(foreignWire.methods, ['GET']);
    expect(allocated, 1);
    expect(find.textContaining('хандах эрхгүй'), findsOneWidget);
    expect(find.textContaining('$_service · 85'), findsNothing);
    expect(find.text(_start), findsNothing);
    await tester.pumpWidget(const SizedBox.shrink());
  });
}
