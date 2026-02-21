# Visual Reference: Corner Radius Options

This document provides a visual reference for different corner radius configurations in WinUI 3.

## CornerRadius Syntax

```xaml
CornerRadius="TopLeft, TopRight, BottomRight, BottomLeft"
```

## Common Configurations

### Square Corners (All Sides)
```xaml
CornerRadius="0"
CornerRadius="0,0,0,0"
```
```
┌──────────┐
│          │
│          │
│          │
└──────────┘
```

### Rounded Corners (All Sides)
```xaml
CornerRadius="8"
CornerRadius="8,8,8,8"
```
```
╭──────────╮
│          │
│          │
│          │
╰──────────╯
```

### Square Top, Rounded Bottom (CURRENT IMPLEMENTATION) ⭐
```xaml
CornerRadius="0,0,8,8"
```
```
┌──────────┐
│          │
│          │
│          │
╰──────────╯
```
**Use Case**: Teleprompter window positioned at top of screen with floating controls at bottom.

### Rounded Top, Square Bottom
```xaml
CornerRadius="8,8,0,0"
```
```
╭──────────╮
│          │
│          │
│          │
└──────────┘
```
**Use Case**: Dropdown menus, tooltips anchored at bottom.

### Rounded Left, Square Right
```xaml
CornerRadius="8,0,0,8"
```
```
╭──────────┐
│          │
│          │
│          │
╰──────────┘
```
**Use Case**: Side panels attached to right edge.

### Rounded Right, Square Left
```xaml
CornerRadius="0,8,8,0"
```
```
┌──────────╮
│          │
│          │
│          │
└──────────╯
```
**Use Case**: Side panels attached to left edge.

### Diagonal Corners
```xaml
CornerRadius="8,0,8,0"
CornerRadius="0,8,0,8"
```
```
╭──────────┐    ┌──────────╮
│          │    │          │
│          │    │          │
│          │    │          │
└──────────╯    ╰──────────┘
```
**Use Case**: Unique visual designs, card layouts.

## WinPrompter Implementation

### Main Window (RootGrid)
```xaml
<Grid x:Name="RootGrid" Background="Black"
      CornerRadius="0,0,8,8">
```
- **Top corners**: Square (0, 0) - flush with screen top
- **Bottom corners**: Rounded (8, 8) - soft, friendly appearance

### Overlay Panel
```xaml
<Grid x:Name="OverlayPanel"
      CornerRadius="0,0,12,12"
      Background="{ThemeResource AcrylicBackgroundFillColorDefaultBrush}">
```
- **Top corners**: Square (0, 0) - flush with RootGrid
- **Bottom corners**: Rounded (12, 12) - slightly larger radius for visual emphasis

### Design Rationale

The square top + rounded bottom design:
1. **Maximizes screen space** at the top where teleprompter content displays
2. **Provides visual softness** at the bottom where controls are located
3. **Creates visual hierarchy** with the overlay having slightly larger radius (12px vs 8px)
4. **Feels natural** when window is positioned at top of screen

## Adjusting Corner Radius

To change the corner radius, simply modify the value in MainWindow.xaml:

```xaml
<!-- Subtle corners -->
<Grid CornerRadius="0,0,4,4">

<!-- Current (balanced) -->
<Grid CornerRadius="0,0,8,8">

<!-- Prominent corners -->
<Grid CornerRadius="0,0,16,16">
```

**Recommendation**: Keep 8px for main window, 12px for overlay (current values).

## Windows 11 Guidelines

Microsoft's Fluent Design guidelines for Windows 11:
- **Small controls**: 4px radius
- **Medium controls**: 8px radius
- **Large surfaces**: 8-12px radius

The current implementation (8px main, 12px overlay) aligns perfectly with these guidelines.

---

## Additional Resources

- [Microsoft: Corner Radius Guidelines](https://learn.microsoft.com/en-us/windows/apps/design/style/rounded-corner)
- [WinUI 3 Documentation](https://learn.microsoft.com/en-us/windows/apps/winui/)
