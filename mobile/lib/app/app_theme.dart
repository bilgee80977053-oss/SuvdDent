import 'package:flutter/material.dart';

/// Figma: Nexora Dental Clinical Premium (390 × 844 mobile layouts).
abstract final class AppColors {
  static const navy = Color(0xFF102942);
  static const blue = Color(0xFF166CA6);
  static const sky = Color(0xFFE7F4FC);
  static const paper = Color(0xFFF0F5FA);
  static const ink = Color(0xFF17344F);
  static const muted = Color(0xFF50677F);
  static const line = Color(0xFFD5E2EF);
  static const mint = Color(0xFFD8F2EA);
  static const green = Color(0xFF10725F);
  static const heroDark = Color(0xFF0A223C);
  static const heroBlue = Color(0xFF125A87);
  static const heroLight = Color(0xFFCCECFA);

  // Backward-compatible aliases for DEMO and existing feature widgets.
  static const teal = blue;
  static const canvas = paper;
}

abstract final class AppTheme {
  static ThemeData get light {
    final scheme = ColorScheme.fromSeed(
      seedColor: AppColors.blue,
      primary: AppColors.navy,
      secondary: AppColors.blue,
      surface: Colors.white,
    );
    return ThemeData(
      useMaterial3: true,
      fontFamily: 'Noto Sans',
      colorScheme: scheme,
      scaffoldBackgroundColor: AppColors.paper,
      appBarTheme: const AppBarTheme(
        backgroundColor: Colors.white,
        foregroundColor: AppColors.navy,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        centerTitle: false,
      ),
      textTheme: const TextTheme(
        headlineMedium: TextStyle(
          fontSize: 27,
          fontWeight: FontWeight.w800,
          color: AppColors.navy,
          height: 1.22,
        ),
        titleLarge: TextStyle(
          fontSize: 20,
          fontWeight: FontWeight.w700,
          color: AppColors.navy,
        ),
        titleMedium: TextStyle(
          fontSize: 16,
          fontWeight: FontWeight.w700,
          color: AppColors.navy,
        ),
        bodyLarge: TextStyle(fontSize: 16, height: 1.5, color: AppColors.ink),
        bodyMedium: TextStyle(fontSize: 14, height: 1.5, color: AppColors.ink),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: Colors.white,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 16,
          vertical: 18,
        ),
        border: OutlineInputBorder(borderRadius: BorderRadius.circular(16)),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(16),
          borderSide: const BorderSide(color: AppColors.line),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(16),
          borderSide: const BorderSide(color: AppColors.blue, width: 1.5),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: AppColors.navy,
          foregroundColor: Colors.white,
          minimumSize: const Size.fromHeight(52),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(15),
          ),
        ),
      ),
      cardTheme: CardThemeData(
        elevation: 0,
        color: Colors.white,
        margin: EdgeInsets.zero,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
          side: const BorderSide(color: AppColors.line),
        ),
      ),
      dividerTheme: const DividerThemeData(color: AppColors.line),
    );
  }
}
