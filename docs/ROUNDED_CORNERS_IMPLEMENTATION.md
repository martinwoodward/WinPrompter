# Implementation Guide: Square Top, Rounded Bottom Corners

This document provides implementation details for each of the 4 approaches researched for achieving square top corners and rounded bottom corners.

## Option 1: XAML CornerRadius (CURRENT & RECOMMENDED)

### Current Implementation in MainWindow.xaml

```xaml
<Grid x:Name="RootGrid" Background="Black"
      CornerRadius="0,0,8,8"
      AllowDrop="True"
      DragOver="RootGrid_DragOver"
      Drop="RootGrid_Drop">
    <!-- Content -->
</Grid>
```

The overlay panel also uses asymmetric corners:
```xaml
<Grid x:Name="OverlayPanel"
      Grid.Row="1"
      Visibility="Collapsed"
      HorizontalAlignment="Stretch"
      Padding="12,8"
      CornerRadius="0,0,12,12"
      Background="{ThemeResource AcrylicBackgroundFillColorDefaultBrush}">
    <!-- Overlay content -->
</Grid>
```

### How It Works
- `CornerRadius` format: `TopLeft,TopRight,BottomRight,BottomLeft`
- `0,0,8,8` means: square top-left (0), square top-right (0), 8px radius bottom-right, 8px radius bottom-left
- The Grid clips its children to the rounded shape
- Works with any WinUI 3 element that supports CornerRadius (Grid, Border, Panel, etc.)

### No Code Changes Needed
This is already fully implemented and working in the current codebase.

---

## Option 2: DWM Window Corner Preference (NOT SUITABLE)

### Why Not Suitable
This API only supports uniform corner styling (all 4 corners the same), making it impossible to achieve square top + rounded bottom.

### Example Implementation (For Reference Only)
If you needed this for uniform corners, here's how it would be implemented:

**Add to WindowHelper.cs:**
```csharp
using System.Runtime.InteropServices;

namespace WinPrompter.Helpers;

public static partial class WindowHelper
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    
    public enum DWM_WINDOW_CORNER_PREFERENCE
    {
        DWMWCP_DEFAULT = 0,      // Let system decide
        DWMWCP_DONOTROUND = 1,   // Never round corners
        DWMWCP_ROUND = 2,        // Round if appropriate
        DWMWCP_ROUNDSMALL = 3    // Round with small radius
    }
    
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, 
        int dwAttribute, 
        ref int pvAttribute, 
        int cbAttribute);
    
    public static void SetWindowCornerPreference(Window window, DWM_WINDOW_CORNER_PREFERENCE preference)
    {
        var hWnd = WindowNative.GetWindowHandle(window);
        int pref = (int)preference;
        DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, Marshal.SizeOf<int>());
    }
}
```

**Usage in MainWindow.xaml.cs:**
```csharp
// After window initialization
WindowHelper.SetWindowCornerPreference(this, WindowHelper.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND);
```

### Limitations
- Only works on Windows 11
- Applies to all 4 corners uniformly
- Cannot achieve asymmetric styling
- System may override in certain window states

---

## Option 3: Custom Window Region (SetWindowRgn)

### Implementation

**Add to WindowHelper.cs:**
```csharp
using System.Runtime.InteropServices;

namespace WinPrompter.Helpers;

public static partial class WindowHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(
        int nLeftRect, int nTopRect, int nRightRect, int nBottomRect,
        int nWidthEllipse, int nHeightEllipse);
    
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(
        int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);
    
    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(
        IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);
    
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
    
    private const int RGN_OR = 2;
    
    public static void SetSquareTopRoundedBottomRegion(Window window, int bottomRadiusPx)
    {
        var appWindow = GetAppWindow(window);
        var hWnd = WindowNative.GetWindowHandle(window);
        int width = appWindow.Size.Width;
        int height = appWindow.Size.Height;
        
        // Create rectangular region for top portion (square corners)
        IntPtr topRegion = CreateRectRgn(0, 0, width, height - bottomRadiusPx);
        
        // Create rounded rectangle region for bottom portion (rounded corners)
        IntPtr bottomRegion = CreateRoundRectRgn(
            0, 
            height - bottomRadiusPx * 2,
            width, 
            height,
            bottomRadiusPx * 2, 
            bottomRadiusPx * 2
        );
        
        // Combine the two regions
        IntPtr combinedRegion = CreateRectRgn(0, 0, 0, 0);
        CombineRgn(combinedRegion, topRegion, bottomRegion, RGN_OR);
        
        // Apply combined region to window
        SetWindowRgn(hWnd, combinedRegion, true);
        
        // Clean up temporary regions (combined region is now owned by window)
        DeleteObject(topRegion);
        DeleteObject(bottomRegion);
        // Don't delete combinedRegion - it's owned by the window
    }
}
```

**Usage in MainWindow.xaml.cs:**
```csharp
public MainWindow()
{
    this.InitializeComponent();
    
    // ... existing initialization ...
    
    // Configure window
    WindowHelper.ConfigureAsFloatingPrompter(this);
    
    // Apply custom region (8px rounded bottom corners)
    WindowHelper.SetSquareTopRoundedBottomRegion(this, 8);
    
    // Re-apply region on resize
    var appWindow = WindowHelper.GetAppWindow(this);
    appWindow.Changed += (_, args) =>
    {
        if (args.DidSizeChange)
        {
            WindowHelper.SetSquareTopRoundedBottomRegion(this, 8);
        }
    };
}
```

### Tradeoffs
- ✅ True window shape clipping
- ✅ Correct hit-testing
- ❌ Poor visual quality (aliased edges)
- ❌ Loses drop shadow
- ❌ Must recalculate on resize
- ❌ Complex to maintain

---

## Option 4: Layered Window with Composition

### Implementation Overview

This approach is highly complex and requires:

1. **Configure window as layered**
2. **Use Composition APIs** to create custom geometry
3. **Create CompositionPathGeometry** with rounded corners
4. **Apply as clip** to root visual

### Pseudo-Implementation

**Add to WindowHelper.cs:**
```csharp
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
using Windows.Graphics;

namespace WinPrompter.Helpers;

public static partial class WindowHelper
{
    public static void ApplyCompositionRoundedClip(UIElement element, float topRadius, float bottomRadius)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        
        // Create a rounded rectangle geometry clip
        // Note: CompositionRoundedRectangleGeometry only supports uniform corners
        // For asymmetric corners, need CompositionPathGeometry with manual path creation
        
        var pathBuilder = new CompositionPathBuilder(compositor);
        
        // This would require building the path manually:
        // 1. Start at top-left corner
        // 2. Line to top-right corner (no arc)
        // 3. Line down right side
        // 4. Arc for bottom-right corner
        // 5. Line across bottom
        // 6. Arc for bottom-left corner
        // 7. Line up left side to close
        
        // This is complex and requires significant geometry code
        // See: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositionpathgeometry
    }
}
```

**Note**: Full implementation would require ~150+ lines of path geometry code.

### Tradeoffs
- ✅ Excellent visual quality
- ✅ Anti-aliased edges
- ✅ Modern API
- ❌ Extremely complex to implement
- ❌ Significant maintenance burden
- ❌ Overkill for this use case

---

## Testing Different Options

If you want to test different corner radius values:

### In MainWindow.xaml
```xaml
<!-- Try different values -->
<Grid CornerRadius="0,0,8,8">   <!-- 8px bottom radius -->
<Grid CornerRadius="0,0,12,12"> <!-- 12px bottom radius -->
<Grid CornerRadius="0,0,16,16"> <!-- 16px bottom radius -->
```

### Consistency
Make sure all elements that should match use the same corner radius:
- RootGrid: `CornerRadius="0,0,8,8"`
- OverlayPanel: `CornerRadius="0,0,12,12"` (slightly larger for emphasis)

---

## Summary

For the WinPrompter teleprompter application:

| Option | Complexity | Visual Quality | Maintenance | Suitable? |
|--------|-----------|----------------|-------------|-----------|
| **1. XAML CornerRadius** | ⭐ Simple | ⭐⭐⭐⭐ Excellent | ⭐⭐⭐⭐⭐ Easy | ✅ **YES** |
| 2. DWM API | ⭐⭐ Moderate | ⭐⭐⭐⭐ Excellent | ⭐⭐⭐ Moderate | ❌ NO (uniform only) |
| 3. SetWindowRgn | ⭐⭐⭐ Complex | ⭐⭐ Poor | ⭐⭐ Difficult | ❌ NO (poor quality) |
| 4. Composition | ⭐⭐⭐⭐⭐ Very Complex | ⭐⭐⭐⭐⭐ Excellent | ⭐ Very Difficult | ❌ NO (overkill) |

**Final Recommendation**: Continue using Option 1 (XAML CornerRadius) - already correctly implemented.
