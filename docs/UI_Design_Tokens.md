---
project: PoRedoImage
tier: 3
type: registry
last_updated: 2026-06-01
component_library: Radzen Blazor
layout: "Bento Box"
---

# UI Design Tokens

> Shared Radzen component settings, CSS variables, and "Bento Box" layout rules.

---

## 1. CSS Custom Properties

```css
:root {
    /* ─── Color Palette ─── */
    --pri: #512bd4;          /* Primary — .NET purple */
    --pri-light: #6b3ce0;
    --pri-dark: #3a1fa8;
    --sec: #4a9eff;          /* Secondary — info blue */
    --acc: #10a37f;          /* Accent — success green */
    --warn: #f2c811;         /* Warning — storage gold */
    --err: #f38ba8;          /* Error — destructive red */
    --bg: #0f0f23;           /* Background — deep navy */
    --bg-card: #1a1a2e;      /* Card background */
    --bg-surface: #16213e;   /* Surface — slightly lighter */
    --text: #e0e0e0;         /* Primary text */
    --text-muted: #8892b0;   /* Muted text */
    --border: #2a2a4a;       /* Border — subtle divider */

    /* ─── Typography ─── */
    --font-sans: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif;
    --font-mono: 'JetBrains Mono', 'Fira Code', monospace;
    --fs-xs: 0.75rem;   /* 12px */
    --fs-sm: 0.875rem;  /* 14px */
    --fs-base: 1rem;    /* 16px */
    --fs-lg: 1.25rem;   /* 20px */
    --fs-xl: 1.5rem;    /* 24px */
    --fs-2xl: 2rem;     /* 32px */

    /* ─── Spacing ─── */
    --sp-xs: 0.25rem;   /* 4px */
    --sp-sm: 0.5rem;    /* 8px */
    --sp-md: 1rem;      /* 16px */
    --sp-lg: 1.5rem;    /* 24px */
    --sp-xl: 2rem;      /* 32px */

    /* ─── Radius ─── */
    --radius-sm: 4px;
    --radius-md: 8px;
    --radius-lg: 12px;
    --radius-xl: 16px;

    /* ─── Shadows ─── */
    --shadow-sm: 0 1px 3px rgba(0,0,0,0.3);
    --shadow-md: 0 4px 12px rgba(0,0,0,0.4);
    --shadow-lg: 0 8px 24px rgba(0,0,0,0.5);
}
```

---

## 2. Radzen Component Settings

| Component | Key Properties | Usage |
|-----------|---------------|-------|
| **RadzenButton** | `ButtonStyle="Primary"`, `ButtonType="Button"`, `IsBusy` | All actions — upload, generate, save |
| **RadzenProgressBar** | `Value`, `Max="100"`, `ShowValue` | Processing progress (ImageRegen pipeline) |
| **RadzenCard** | `class="rz-p-4"` | Bento Box cell containers |
| **RadzenDialog** | `Title`, `CloseDialogOnOverlayClick` | Prompt drawer, re-roll modal |
| **RadzenNotification** | `Position="NotificationPosition.TopRight"` | Success/error toasts |
| **RadzenTabs** | `SelectedIndex`, `TabChange` | Studio mode selector (ImageRegen / Meme / Bulk) |
| **RadzenDropDown** | `Data`, `Value`, `AllowFiltering` | AI model selector, persona picker |
| **RadzenUpload** | `Url`, `Accept="image/*"`, `AutoUpload` | Image upload panel |
| **RadzenImage** | `Path`, `Style="max-height:400px"` | Original + result display |
| **RadzenListBox** | `Data`, `Multiple`, `Value` | Bulk prompt editor (10 slots) |
| **RadzenText** | `TextStyle="Display"`, `"Body1"`, `"Caption"` | Typography hierarchy |
| **RadzenStack** | `AlignItems`, `JustifyContent`, `Gap` | Flex layout for Bento Box |

---

## 3. Bento Box Layout Rules

The UI uses a **Bento Box** grid layout — a responsive card-based arrangement inspired by Apple's macOS widgets.

### Grid Structure

```
┌─────────────────────────────────────────┐
│  NavMenu (top bar — auth + nav)         │
├──────────────┬──────────────────────────┤
│              │                          │
│  ImageUpload │  ResultsPanel            │
│  Panel       │  (Regen/Meme/Bulk tabs)  │
│  (left col)  │                          │
│              │                          │
├──────────────┼──────────────────────────┤
│  ActiveImage │  MyImagesGallery         │
│  Bar         │  (bottom right)          │
└──────────────┴──────────────────────────┘
```

### Layout Rules

| Rule | Value | Rationale |
|------|-------|-----------|
| **Grid columns** | `1fr 1.5fr` (left : right) | Upload needs less space than results |
| **Min column width** | 320px | Prevents cramped mobile view |
| **Card gap** | `var(--sp-md)` (16px) | Consistent breathing room |
| **Card padding** | `var(--sp-md)` (16px) | Inner content spacing |
| **Card radius** | `var(--radius-lg)` (12px) | Soft, modern feel |
| **Card border** | `1px solid var(--border)` | Subtle definition on dark bg |
| **Image max-height** | 400px | Prevents oversized originals |
| **Progress bar** | Full-width below image | Real-time feedback |
| **Responsive breakpoint** | 768px | Stack columns vertically on mobile |

### Feature Page Pattern

Every feature page (ImageRegen, MemeGen, BulkGenerate, CaptionBattle, StyleDirector) follows the same layout:

1. **FeatureLayout** wrapper (`<FeatureLayout Title="...">`)
2. **ImageUploadPanel** — drag-and-drop zone
3. **ActiveImageBar** — thumbnail of current upload
4. **ResultsPanel** — tabbed output display
5. **ProcessingProgressBar** — pipeline progress

---

## 4. Dark Theme Application

All components render against the deep navy background (`--bg: #0f0f23`). Logic/data nodes in Mermaid diagrams use:
- **Dark Theme** (`#111` background): Logic, compute, error states
- **Light Theme** (`#eee` background): Data, state, storage entities

This mirrors the UI's dark-first approach with light accent cards for data display.