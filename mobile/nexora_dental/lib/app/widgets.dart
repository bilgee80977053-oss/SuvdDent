import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../core/api.dart';

String safeMessage(Object? error) => error is ApiFailure
    ? error.message
    : 'Тодорхойгүй алдаа гарлаа. Дахин оролдоно уу.';
void showSafeError(BuildContext context, Object error) =>
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(safeMessage(error))));

class AppPage extends StatelessWidget {
  final String title;
  final Widget child;
  final List<Widget>? actions;
  final int? navigationIndex;
  const AppPage({
    super.key,
    required this.title,
    required this.child,
    this.actions,
    this.navigationIndex,
  });
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text(title), actions: actions),
    body: SafeArea(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 720),
          child: child,
        ),
      ),
    ),
    bottomNavigationBar: navigationIndex == null
        ? null
        : NavigationBar(
            selectedIndex: navigationIndex!,
            onDestinationSelected: (index) =>
                context.go(index == 0 ? '/' : '/appointments'),
            destinations: const [
              NavigationDestination(
                icon: Icon(Icons.home_outlined),
                selectedIcon: Icon(Icons.home),
                label: 'Нүүр',
              ),
              NavigationDestination(
                icon: Icon(Icons.event_note_outlined),
                selectedIcon: Icon(Icons.event_note),
                label: 'Миний захиалгууд',
              ),
            ],
          ),
  );
}

class InfoBanner extends StatelessWidget {
  final String text;
  final IconData icon;
  const InfoBanner(this.text, {super.key, this.icon = Icons.info_outline});
  @override
  Widget build(BuildContext context) => Card(
    color: Theme.of(context).colorScheme.secondaryContainer,
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon),
          const SizedBox(width: 12),
          Expanded(child: Text(text)),
        ],
      ),
    ),
  );
}

class EmptyPanel extends StatelessWidget {
  final String message;
  const EmptyPanel(this.message, {super.key});
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.all(32),
    child: Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.event_available_outlined, size: 48),
          const SizedBox(height: 16),
          Text(message, textAlign: TextAlign.center),
        ],
      ),
    ),
  );
}

class ErrorPanel extends StatelessWidget {
  final Object? error;
  final VoidCallback retry;
  const ErrorPanel({super.key, required this.error, required this.retry});
  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(safeMessage(error), textAlign: TextAlign.center),
          const SizedBox(height: 16),
          FilledButton.icon(
            onPressed: retry,
            icon: const Icon(Icons.refresh),
            label: const Text('Дахин оролдох'),
          ),
        ],
      ),
    ),
  );
}

class DataPanel<T> extends StatefulWidget {
  final Future<T> Function() load;
  final Widget Function(T) builder;
  const DataPanel({super.key, required this.load, required this.builder});
  @override
  State<DataPanel<T>> createState() => _DataPanelState<T>();
}

class _DataPanelState<T> extends State<DataPanel<T>> {
  late Future<T> future;
  @override
  void initState() {
    super.initState();
    future = widget.load();
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<T>(
    future: future,
    builder: (context, snapshot) {
      if (snapshot.connectionState != ConnectionState.done) {
        return const Center(child: CircularProgressIndicator());
      }
      if (snapshot.hasError) {
        return ErrorPanel(
          error: snapshot.error,
          retry: () => setState(() => future = widget.load()),
        );
      }
      return widget.builder(snapshot.requireData);
    },
  );
}
