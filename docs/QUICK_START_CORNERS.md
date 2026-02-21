# Quick Start: Understanding and Modifying Corner Styling

A practical guide for developers working on WinPrompter's corner styling.

## Current Implementation (30 seconds to understand)

### Location
`MainWindow.xaml`, line 12

### Code
```xaml
<Grid x:Name="RootGrid" Background="Black"
      CornerRadius="0,0,8,8"
      ...>
```

### What It Does
- Makes the main window have square corners at top, rounded at bottom
- `CornerRadius="TopLeft, TopRight, BottomRight, BottomLeft"`
- `0,0,8,8` = square top (0,0) + 8px rounded bottom (8,8)

## How to Modify

### Change Bottom Corner Radius
```xaml
<!-- More subtle (4px) -->
<Grid CornerRadius="0,0,4,4">

<!-- Current (8px) -->
<Grid CornerRadius="0,0,8,8">

<!-- More prominent (16px) -->
<Grid CornerRadius="0,0,16,16">
```

### Make All Corners Rounded
```xaml
<Grid CornerRadius="8">
```

### Make All Corners Square
```xaml
<Grid CornerRadius="0">
```

### Rounded Top, Square Bottom (Inverted)
```xaml
<Grid CornerRadius="8,8,0,0">
```

## Elements with Corner Styling

### 1. Main Window (RootGrid)
**File**: MainWindow.xaml, line 12  
**Current**: `CornerRadius="0,0,8,8"`  
**Purpose**: Main window appearance

### 2. Overlay Panel
**File**: MainWindow.xaml, line 32  
**Current**: `CornerRadius="0,0,12,12"`  
**Purpose**: Settings toolbar at bottom (slightly larger radius for emphasis)

### 3. Voice Indicator
**File**: MainWindow.xaml, line 174  
**Current**: `CornerRadius="4"`  
**Purpose**: Small status badge (uniform 4px corners)

### 4. Primary Action Buttons (App.xaml)
**File**: App.xaml, line 42  
**Current**: `CornerRadius="8"`  
**Purpose**: UI controls (uniform 8px corners)

## Design Guidelines

### Windows 11 Fluent Design
- **Small elements** (badges, pills): 4px
- **Medium controls** (buttons, inputs): 8px
- **Large surfaces** (panels, windows): 8-12px

### WinPrompter Design System
- **Main window**: 8px bottom corners (balanced with screen edge)
- **Overlay panel**: 12px bottom corners (visual emphasis)
- **Status indicators**: 4px uniform (subtle)
- **Buttons**: 8px uniform (standard)

## Common Scenarios

### Scenario 1: Increase Bottom Corner Radius
```xaml
<!-- Change from 8px to 12px -->
<Grid x:Name="RootGrid" CornerRadius="0,0,12,12">
```
**Impact**: More prominent rounded appearance at bottom

### Scenario 2: Match Overlay and Window Radius
```xaml
<!-- Make both 12px -->
<Grid x:Name="RootGrid" CornerRadius="0,0,12,12">
<Grid x:Name="OverlayPanel" CornerRadius="0,0,12,12">
```
**Impact**: Consistent visual language

### Scenario 3: Subtle Corners
```xaml
<!-- Reduce to 4px -->
<Grid x:Name="RootGrid" CornerRadius="0,0,4,4">
<Grid x:Name="OverlayPanel" CornerRadius="0,0,4,4">
```
**Impact**: More minimal, less visual emphasis

## Testing Your Changes

### 1. Visual Inspection
```powershell
dotnet run --project WinPrompter.csproj -p:Platform=x64
```
- Look at the window edges
- Check both with and without overlay visible (hover at bottom)
- Test in different window sizes

### 2. Edge Cases to Check
- [ ] Window at default size (1400x300)
- [ ] Window resized to very small
- [ ] Window resized to very large
- [ ] Fullscreen mode (F11)
- [ ] Overlay visible vs hidden
- [ ] Different themes (dark, light, etc.)

### 3. Visual Consistency
Ensure corners look good with:
- [ ] Dark theme
- [ ] Light theme
- [ ] Acrylic overlay enabled
- [ ] Various opacity levels
- [ ] Drop shadow visible

## Troubleshooting

### Corners Don't Appear Rounded
- ✅ Check spelling: `CornerRadius` not `CornerRadii`
- ✅ Verify element has a background (transparent won't show)
- ✅ Check parent element isn't clipping

### Corners Cut Off Content
- ✅ Add padding to child elements
- ✅ Use a Border with CornerRadius around content

### Different Corner Sizes Look Inconsistent
- ✅ Follow the design system (4px, 8px, 12px multiples)
- ✅ Match related elements

## Why Not Use Other Approaches?

Quick reference:

| Question | Answer |
|----------|--------|
| Why not use DWM API? | Cannot do asymmetric corners (all 4 must match) |
| Why not use SetWindowRgn? | Poor visual quality (jagged edges), loses effects |
| Why not use Composition? | Massive complexity with no meaningful benefit |

## When to Revisit This Decision

Consider alternatives if:
1. Click-through outside visual corners becomes required
2. Need pixel-perfect window boundaries for screen capture
3. Building a completely custom-chrome window from scratch

For WinPrompter's use case (teleprompter), these scenarios are extremely unlikely.

## Quick Links

📚 **Full Documentation**:
- [Executive Summary](./EXECUTIVE_SUMMARY.md) - Start here for overview
- [Research Document](./ROUNDED_CORNERS_RESEARCH.md) - Detailed analysis
- [Implementation Guide](./ROUNDED_CORNERS_IMPLEMENTATION.md) - Code samples
- [Visual Reference](./CORNER_RADIUS_VISUAL_REFERENCE.md) - Visual examples
- [Technical Comparison](./CORNER_STYLING_COMPARISON.md) - Decision matrices

---

**TL;DR**: Current implementation is perfect. Use `CornerRadius="0,0,8,8"` in XAML. Done. ✅
