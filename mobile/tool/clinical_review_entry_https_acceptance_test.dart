import 'dart:async';
import 'dart:io';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/io_client.dart';
import 'package:nexora_dental_mobile/live/clinician_review_entry.dart';
import 'package:nexora_dental_mobile/live/models.dart';

const _endpoint = '/api/clinical/appointments/301/performed-procedures';
const _service = 'Synthetic booked service';
const _empty = 'Бүртгэл хараахан байхгүй.';
const _start = 'Ажилбар бүртгэх';
const _confirm = 'Баталгаажуулж бүртгэх';
const _assigned = '/api/clinical/appointments/assigned';
const _login = 'Эмчээр нэвтрэх';

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
  final calls = <String>[];
  final signs = <Map<String, Object?>>[];
  bool closed = false;
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
        (path == _assigned && method == 'GET') ||
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
    if (closed) throw StateError('The review transport was closed.');
    calls.add('$method $path');
    if (path == _endpoint && method == 'POST') {
      signs.add(
        jsonDecode((request as http.Request).body) as Map<String, Object?>,
      );
    }
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
  void close() {
    if (closed) return;
    closed = true;
    _zone.run(_client.close);
  }
}

void main() {
  testWidgets('review entry signs a server-selected encounter over real TLS', (
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

    final zone = (await tester.runAsync(() async => Zone.current))!;
    final wires = <_LoopbackClient>[];
    http.Client connect() => zone.run(() {
      final socket = _NativeOverrides().createHttpClient(trust);
      socket.findProxy = (_) => 'DIRECT';
      socket.connectionTimeout = const Duration(seconds: 8);
      final wire = _LoopbackClient(IOClient(socket), zone, origin);
      addTearDown(wire.close);
      wires.add(wire);
      return wire;
    });

    Future<void> mount(int clinic) => tester.pumpWidget(
      MaterialApp(
        home: ClinicianReviewEntry(
          settings: ApiSettings(baseUrl: origin.toString(), clinicId: clinic),
          clientFactory: connect,
          fromUtc: DateTime.utc(2026, 11, 26),
          untilUtc: DateTime.utc(2026, 11, 28),
          enabled: true,
        ),
      ),
    );

    Future<void> complete(_LoopbackClient wire, int count) async {
      await tester.pump();
      await tester.runAsync(() => wire.completed(count));
      await tester.pump();
    }

    Future<_LoopbackClient> login(String syntheticUser) async {
      await tester.enterText(find.byType(TextField).at(0), syntheticUser);
      await tester.enterText(find.byType(TextField).at(1), password);
      await tester.tap(find.text(_login));
      await tester.pump();
      final wire = wires.last;
      await complete(wire, 1);
      await complete(wire, 2);
      await complete(wire, 3);
      await tester.pumpAndSettle();
      return wire;
    }

    await mount(101);
    expect(wires, isEmpty);
    final owner = await login('user13@example.invalid');
    expect(owner.calls, [
      'POST /api/Auth/login',
      'GET /api/Auth/me',
      'GET $_assigned',
    ]);
    expect(find.text('Хуваарилсан үзлэг'), findsOneWidget);
    expect(find.text(_service), findsOneWidget);
    expect(find.text(_start), findsNothing);

    // Selecting the actual response row is the only path into the panel.
    await tester.tap(find.text(_service));
    await tester.pump();
    await complete(owner, 4);
    await tester.pumpAndSettle();
    expect(owner.calls.last, 'GET $_endpoint');
    expect(find.text(_empty), findsOneWidget);

    await tester.tap(find.text('Нэг шүд'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), '85');
    await tester.tap(find.text(_start));
    await tester.pumpAndSettle();
    expect(find.textContaining('Шүд: 85'), findsOneWidget);
    expect(owner.signs, isEmpty);

    await tester.tap(find.text(_confirm));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 300));
    await complete(owner, 5);
    await complete(owner, 6);
    await tester.pumpAndSettle();
    expect(owner.calls.sublist(3), [
      'GET $_endpoint',
      'POST $_endpoint',
      'GET $_endpoint',
    ]);
    expect(owner.signs.single, {
      'requestId': isA<String>(),
      'serviceId': 51,
      'scope': 'SingleTooth',
      'toothCode': '85',
    });
    expect(find.text('Ажилбар бүртгэгдлээ.'), findsOneWidget);
    expect(find.textContaining('$_service · 85'), findsOneWidget);
    expect(find.text(_empty), findsNothing);

    await tester.tap(find.text('Гарах'));
    await tester.pumpAndSettle();
    expect(owner.closed, isTrue);
    expect(find.text(_login), findsOneWidget);
    expect(find.textContaining(_service), findsNothing);
    expect(find.text(_start), findsNothing);
    expect(
      tester.widget<TextField>(find.byType(TextField).at(1)).controller!.text,
      isEmpty,
    );

    // The same host widget reconfigured for a different valid clinic must
    // neither retain the previous result nor open the first clinic's panel.
    await mount(202);
    final foreign = await login('user23@example.invalid');
    expect(foreign.calls, [
      'POST /api/Auth/login',
      'GET /api/Auth/me',
      'GET $_assigned',
    ]);
    expect(find.textContaining('хуваарилсан үзлэг алга'), findsOneWidget);
    expect(find.textContaining(_service), findsNothing);
    expect(find.text(_start), findsNothing);
    expect(foreign.signs, isEmpty);
    await tester.pumpWidget(const SizedBox.shrink());
    expect(foreign.closed, isTrue);
  });
}
