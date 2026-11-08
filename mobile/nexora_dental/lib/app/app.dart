import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/session.dart';
import '../features/auth/login_screen.dart';
import '../features/home/home_screen.dart';
import '../features/catalog/catalog_screens.dart';
import '../features/booking/booking_screen.dart';
import '../features/appointments/appointment_screens.dart';

final routerProvider = Provider<GoRouter>((ref) {
  late final GoRouter router;
  ref.listen(sessionProvider, (_, _) => router.refresh());
  int id(GoRouterState state, String key) =>
      int.tryParse(state.pathParameters[key] ?? '') ?? 0;
  router = GoRouter(
    initialLocation: '/',
    redirect: (context, state) {
      final session = ref.read(sessionProvider);
      final path = state.uri.path;
      if (session.isLoading || session.hasError) {
        return path == '/startup' ? null : '/startup';
      }
      if (session.asData?.value == null) {
        return path == '/login' ? null : '/login';
      }
      if (path == '/login' || path == '/startup') {
        return '/';
      }
      return null;
    },
    routes: [
      GoRoute(path: '/startup', builder: (_, _) => const StartupScreen()),
      GoRoute(path: '/login', builder: (_, _) => const LoginScreen()),
      GoRoute(path: '/', builder: (_, _) => const HomeScreen()),
      GoRoute(path: '/clinics', builder: (_, _) => const ClinicsScreen()),
      GoRoute(
        path: '/clinics/:clinicId/doctors',
        builder: (_, s) => DoctorsScreen(id(s, 'clinicId')),
      ),
      GoRoute(
        path: '/clinics/:clinicId/doctors/:doctorId/services',
        builder: (_, s) => ServicesScreen(id(s, 'clinicId'), id(s, 'doctorId')),
      ),
      GoRoute(
        path: '/clinics/:clinicId/doctors/:doctorId/services/:serviceId/book',
        builder: (_, s) => BookingScreen(
          id(s, 'clinicId'),
          id(s, 'doctorId'),
          id(s, 'serviceId'),
        ),
      ),
      GoRoute(
        path: '/appointments',
        builder: (_, _) => const AppointmentsScreen(),
      ),
      GoRoute(
        path: '/appointments/:id/confirmation',
        builder: (_, s) =>
            AppointmentDetailsScreen(id(s, 'id'), confirmation: true),
      ),
      GoRoute(
        path: '/appointments/:id',
        builder: (_, s) => AppointmentDetailsScreen(id(s, 'id')),
      ),
    ],
    errorBuilder: (context, state) => Scaffold(
      body: Center(
        child: FilledButton(
          onPressed: () => context.go('/'),
          child: const Text('Нүүр хуудас руу'),
        ),
      ),
    ),
  );
  ref.onDispose(router.dispose);
  return router;
});

class NexoraApp extends ConsumerWidget {
  const NexoraApp({super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    try {
      ref.watch(apiUrlProvider);
    } catch (_) {
      return const MaterialApp(
        home: Scaffold(
          body: SafeArea(
            child: Center(
              child: Padding(
                padding: EdgeInsets.all(24),
                child: Text(
                  'API_BASE_URL тохиргоонд HTTPS серверийн хаяг оруулна уу. Холболтын тохиргоогоо шалгаарай.',
                ),
              ),
            ),
          ),
        ),
      );
    }
    return MaterialApp.router(
      title: 'Nexora Dental',
      debugShowCheckedModeBanner: false,
      locale: const Locale('mn'),
      supportedLocales: const [Locale('mn'), Locale('en')],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      theme: ThemeData(
        useMaterial3: true,
        colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF087F8C)),
        scaffoldBackgroundColor: const Color(0xFFF5FAFA),
        inputDecorationTheme: const InputDecorationTheme(
          border: OutlineInputBorder(),
        ),
      ),
      routerConfig: ref.watch(routerProvider),
    );
  }
}
