# UI Standard: KPI & Summary Cards

This document defines the official visual and interactive standard for KPI (Key Performance Indicator) and summary cards in the Alpla Portal. These cards provide high-level operational visibility and navigation shortcuts across the application.

## 1. Visual Language: "Industrial Brutalist"

The design follows the project's core "Industrial Brutalist" aesthetic, emphasizing clarity, high-contrast hierarchy, and functional elegance.

- **Borders**: Thick, high-contrast borders (e.g., `border-2 border-slate-900`).
- **Elevation**: Distinct shadows that deepen upon interaction (`shadow-md` -> `shadow-xl`).
- **Watermark Symbols**: Large, low-opacity icons positioned in the bottom-right of the card to provide visual context without cluttering the foreground.
- **Color Palette**: Solid, curated colors (e.g., Alpla Blue, Amber, Rose, Emerald) used to categorize modules and request stages.

## 2. Interaction & Feedback

Cards are designed to be active participants in the user journey:

- **Hover States**:
  - `transform: translateY(-4px)`: Provides immediate spatial feedback.
  - Shadow transition from `md` to `xl`.
  - Icon color shifts/animations if supported.
- **Active State**:
  - Represented by a thick bottom border matching the grouping color.
  - A small, circular indicator dot appears in the top-right to mark the card as the "Current Filter".

## 3. Motion & Animation

Animations are used to reduce cognitive load and enhance perceived responsiveness:

- **Entry**: Smooth `Initial -> Final` state transitions using Framer Motion (restrained slide-up or fade-in).
- **Transitions**: 150ms-300ms durations for hover effects to ensure the UI feels snappy and professional.
- **Hierarchical Feedback**: Animations guide the eye to the active filter state when a card is clicked.

## 4. Responsive Behavior

- **Layout**: Uses CSS Grid with `repeat(auto-fit, minmax(240px, 1fr))` to naturally stack on smaller screens while remaining perfectly aligned in a single row on desktop.
- **Hierarchy Compatibility**: Cards are positioned above the entity table/list, nested within the header area to ensure they don't push critical table controls off-screen on medium viewports.

## 5. Implementation Reference

- **Core Component**: `src/frontend/src/pages/Requests/components/KPICard.tsx`
- **Standard Container**: `src/frontend/src/pages/Requests/components/KPISummary.tsx`
