
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:nexora_dental/features/auth/login_screen.dart';

void main() {
  testWidgets('Nexora Dental login screen smoke test', (tester) async {
    await tester.pumpWidget(
      const ProviderScope(
        child: MaterialApp(
          home: LoginScreen(),
        ),
      ),
    );

    expect(
      find.byKey(const Key('login.identifier')),
      findsOneWidget,
    );

    expect(
      find.byKey(const Key('login.password')),
      findsOneWidget,
    );

    expect(
      find.byKey(const Key('login.submit')),
      findsOneWidget,
    );
  });
}
