import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:nexora_dental_mobile/live/live_flow_controller.dart';
import 'package:nexora_dental_mobile/live/profile_settings_screen.dart';

import 'live_api_test.dart' as fixture;

void main() {
  test('patient profile update sends only editable fields and updates state', () async {
    http.Request? updated;
    final flow = LiveFlowController(fixture.makeApi((r) {
      if (r.url.path == '/api/Patients/me' && r.method == 'PUT') {
        updated = r;
        final payload = jsonDecode(r.body) as Map<String, dynamic>;
        return fixture.response({
          'id': 11,
          ...payload,
        });
      }
      return fixture.standard(r);
    }));
    addTearDown(flow.dispose);

    await flow.login('synthetic@example.invalid', 'synthetic-password');
    await flow.loadProfile();
    await flow.updateProfile(
      fullName: 'Шинэ нэр',
      phone: '99998888',
      dateOfBirth: '2002-02-15',
      address: 'Дархан',
    );

    expect(updated?.method, 'PUT');
    expect(updated?.headers['authorization'], 'Bearer synthetic-test-token');
    expect(jsonDecode(updated!.body), {
      'fullName': 'Шинэ нэр',
      'phone': '99998888',
      'dateOfBirth': '2002-02-15',
      'address': 'Дархан',
    });
    expect(flow.patient?.fullName, 'Шинэ нэр');
    expect(flow.patient?.phone, '99998888');
    expect(flow.notice, 'Профайлын мэдээлэл хадгалагдлаа.');
  });

  test('failed profile PUT does not claim save or overwrite current profile', () async {
    final flow = LiveFlowController(fixture.makeApi((r) {
      if (r.url.path == '/api/Patients/me' && r.method == 'PUT') {
        return fixture.response({'error': 'Өөрчлөлт хадгалагдсангүй'}, 500);
      }
      return fixture.standard(r);
    }));
    addTearDown(flow.dispose);

    await flow.login('synthetic@example.invalid', 'synthetic-password');
    await flow.loadProfile();
    await flow.updateProfile(fullName: 'Алдаатай', phone: '99998888');
    expect(flow.patient?.fullName, 'Тест');
    expect(flow.notice, isNull);
    expect(flow.error, isNotNull);
  });

  testWidgets('profile editor loads current values and can submit changes',
      (tester) async {
    var updates = 0;
    final flow = LiveFlowController(fixture.makeApi((r) {
      if (r.url.path == '/api/Patients/me' && r.method == 'PUT') {
        updates++;
        final payload = jsonDecode(r.body) as Map<String, dynamic>;
        return fixture.response({'id': 11, ...payload});
      }
      return fixture.standard(r);
    }));
    addTearDown(flow.dispose);

    await flow.login('synthetic@example.invalid', 'synthetic-password');
    await flow.loadProfile();

    await tester.pumpWidget(MaterialApp(home: ProfileSettingsScreen(flow: flow)));
    await tester.pumpAndSettle();
    expect(find.text('Профайлын тохиргоо'), findsOneWidget);
    expect(find.text('Тест'), findsOneWidget);

    await tester.enterText(find.byType(TextFormField).at(0), 'Шинэ нэр');
    await tester.enterText(find.byType(TextFormField).at(1), '99998888');
    final save = find.text('Өөрчлөлт хадгалах');
    await tester.ensureVisible(save);
    await tester.pumpAndSettle();
    await tester.tap(save);
    await tester.pumpAndSettle();

    expect(updates, 1);
    expect(flow.patient?.fullName, 'Шинэ нэр');
    expect(find.text('Профайлын мэдээлэл хадгалагдлаа.'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('logging out hides patient details from open settings screen',
      (tester) async {
    final flow = LiveFlowController(fixture.makeApi(fixture.standard));
    addTearDown(flow.dispose);
    await flow.login('synthetic@example.invalid', 'synthetic-password');
    await flow.loadProfile();
    await tester.pumpWidget(MaterialApp(home: ProfileSettingsScreen(flow: flow)));
    await tester.pumpAndSettle();
    flow.logout();
    await tester.pumpAndSettle();
    expect(find.textContaining('Нэвтрэх төлөв өөрчлөгдсөн'), findsOneWidget);
    expect(find.byType(TextFormField), findsNothing);
    await tester.pumpWidget(const SizedBox());
  });
}
