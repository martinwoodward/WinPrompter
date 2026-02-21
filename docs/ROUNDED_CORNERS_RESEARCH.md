# Research: Square Top, Rounded Bottom Corners

This document presents 4 different approaches to make the WinPrompter app appear to have square corners at the top and rounded corners at the bottom.

## Current Implementation

The app currently uses **XAML CornerRadius** on the RootGrid:
```xaml
<Grid x:Name="RootGrid" Background="Black"
      CornerRadius="0,0,8,8"
      ...>
```
This produces square corners at the top (0,0) and rounded corners at the bottom (8,8).

---

## Option 1: XAML CornerRadius (Current Approach) ⭐ RECOMMENDED

### Description
Use the `CornerRadius` property on the root Grid or Border element. The CornerRadius syntax is `TopLeft,TopRight,BottomRight,BottomLeft`.

### Implementation
```xaml
<Grid CornerRadius="0,0,8,8">
    <!-- Content -->
</Grid>
```

Or for the overlay panel:
```xaml
<Grid CornerRadius="0,0,12,12">
    <!-- Overlay content -->
</Grid>
```

### Pros
✅ **Simplest implementation** - Single XAML property, no code required  
✅ **Native WinUI 3 support** - Built-in, well-tested, no interop needed  
✅ **Performant** - Hardware accelerated rendering  
✅ **Works with all controls** - Compatible with all XAML controls and backgrounds  
✅ **Responsive** - Automatically adjusts when window resizes  
✅ **Shadow and effects preserved** - Drop shadows and acrylic effects still work  
✅ **Easy to maintain** - No complex Win32 interop or custom region code  
✅ **Theme-compatible** - Works with light/dark themes and Windows 11 design language  
✅ **Cross-version compatible** - Works on Windows 10 and 11  

### Cons
❌ **Visual only** - Rounds the visual appearance of content, not the window itself  
❌ **Hit-testing quirks** - The window's actual region is still rectangular; clicks outside rounded corners hit window  
❌ **Shadow may extend beyond visual** - Drop shadow might still show rectangular shape in some cases  

### Code Changes Required
**None** - This is already implemented in MainWindow.xaml.

---

## Option 2: DWM Window Corner Preference

### Description
Use the Desktop Window Manager (DWM) API with `DwmSetWindowAttribute` and `DWMWA_WINDOW_CORNER_PREFERENCE` to control window corner rendering. This is the official Windows 11 API for window corner styling.

### Implementation
```csharp
using System.Runtime.InteropServices;

public static class WindowHelper
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
    
    public static void SetWindowCorners(Window window, DWM_WINDOW_CORNER_PREFERENCE preference)
    {
        var hWnd = WindowNative.GetWindowHandle(window);
        int pref = (int)preference;
        DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, Marshal.SizeOf<int>());
    }
}
```

### Pros
✅ **Official Windows 11 API** - Native OS support  
✅ **True window rounding** - Affects actual window region, not just visuals  
✅ **Hit-testing correct** - Clicks outside corners properly miss window  
✅ **Consistent with OS** - Matches Windows 11 design language  
✅ **Simple to implement** - Single API call  

### Cons
❌ **Windows 11 only** - API available only on Windows 11  
❌ **Uniform corners only** - Cannot have different radii per corner (all 4 corners get same treatment)  
❌ **Cannot mix square/rounded** - Cannot have square top + rounded bottom with this API  
❌ **Limited control** - Only 4 preset options (DEFAULT, DONOTROUND, ROUND, ROUNDSMALL)  
❌ **Ignored in some cases** - System ignores when maximized, snapped, or in VM  
❌ **Requires Win32 interop** - P/Invoke code needed  
❌ **Not suitable for this use case** - Cannot achieve asymmetric corner styling  

### Code Changes Required
- Add P/Invoke declarations to WindowHelper.cs
- Call API during window initialization in MainWindow.xaml.cs

### Why Not Recommended for This Use Case
This option **cannot achieve the desired effect** of square top corners with rounded bottom corners because it applies the same style to all four corners simultaneously.

---

## Option 3: Custom Window Region (SetWindowRgn)

### Description
Use Win32 `SetWindowRgn` API to define a custom window region with asymmetric rounded corners. This involves creating a complex region shape using GDI functions.

### Implementation
```csharp
public static class WindowHelper
{
    [DllImport("user32.dll")]
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
    
    public static void SetAsymmetricRoundedRegion(Window window, int width, int height, int bottomRadius)
    {
        var hWnd = WindowNative.GetWindowHandle(window);
        
        // Create a rectangular region for the top portion
        var topRegion = CreateRectRgn(0, 0, width, height - bottomRadius);
        
        // Create a rounded rectangle region for the bottom portion
        var bottomRegion = CreateRoundRectRgn(
            0, height - bottomRadius * 2, 
            width, height, 
            bottomRadius * 2, bottomRadius * 2);
        
        // Combine both regions
        var combinedRegion = CreateRectRgn(0, 0, 0, 0);
        const int RGN_OR = 2;
        CombineRgn(combinedRegion, topRegion, bottomRegion, RGN_OR);
        
        // Apply to window
        SetWindowRgn(hWnd, combinedRegion, true);
        
        // Note: Don't delete regions - they're owned by the window now
    }
}
```

### Pros
✅ **True asymmetric corners** - Can have different corners styled differently  
✅ **Precise hit-testing** - Window boundary matches visual exactly  
✅ **Works on Windows 10/11** - Uses classic Win32 APIs  
✅ **Full control** - Can create any region shape desired  

### Cons
❌ **Complex implementation** - Requires manual region creation and combination  
❌ **No drop shadow** - Loses native window shadow (must implement custom shadow)  
❌ **No native effects** - Loses acrylic, mica, and other Windows 11 effects  
❌ **Resize complications** - Region must be recreated on every resize  
❌ **Jagged edges** - GDI regions don't support anti-aliasing  
❌ **Poor visual quality** - Rounded corners appear pixelated/aliased  
❌ **No composition effects** - Cannot use modern visual effects  
❌ **Maintenance burden** - More code to maintain and test  
❌ **Potential performance issues** - Region recalculation on resize can be expensive  

### Code Changes Required
- Add P/Invoke declarations to WindowHelper.cs (5+ methods)
- Implement region creation logic (~50 lines of code)
- Hook into window resize events to recreate region
- Handle cleanup of GDI resources

### Why Not Recommended
While this achieves true asymmetric corners, the visual quality is poor and you lose modern Windows 11 effects like shadows and acrylic. The complexity and maintenance burden significantly outweigh the benefits.

---

## Option 4: Layered Window with Composition/Visual Layer

### Description
Use a layered window (`WS_EX_LAYERED`) with Windows.UI.Composition APIs to create a custom-shaped window with high-quality anti-aliased edges and asymmetric corners.

### Implementation
```csharp
public static class WindowHelper
{
    // Configure window as layered
    public static void ConfigureAsLayeredWindow(Window window)
    {
        var hWnd = WindowNative.GetWindowHandle(window);
        const int GWL_EXSTYLE = -20;
        const int WS_EX_LAYERED = 0x00080000;
        
        var style = GetWindowLong(hWnd, GWL_EXSTYLE);
        SetWindowLong(hWnd, GWL_EXSTYLE, style | WS_EX_LAYERED);
    }
    
    // Apply rounded corner clip using Composition
    public static void ApplyAsymmetricRoundedClip(UIElement element)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        
        // Create geometry with rounded bottom, square top
        var pathGeometry = compositor.CreatePathGeometry();
        // ... complex path creation with RoundedRectangleGeometry logic
        
        var geometricClip = compositor.CreateGeometricClip(pathGeometry);
        visual.Clip = geometricClip;
    }
}
```

### Pros
✅ **High visual quality** - Anti-aliased edges, smooth corners  
✅ **Modern approach** - Uses Windows Composition APIs  
✅ **Flexible** - Can create any shape with precise control  
✅ **Performant** - Hardware accelerated  
✅ **Can maintain effects** - Compatible with some visual effects  

### Cons
❌ **Very complex** - Significant code required for path geometry  
❌ **Advanced expertise needed** - Requires deep understanding of Composition APIs  
❌ **Shadow complications** - May need custom shadow implementation  
❌ **Layered window limitations** - Some features may not work (e.g., certain input scenarios)  
❌ **Maintenance burden** - Complex code to maintain  
❌ **Resize handling** - Must update geometry on window resize  
❌ **Testing complexity** - Harder to test and debug  
❌ **Documentation sparse** - Fewer examples available for asymmetric corners  

### Code Changes Required
- Add Composition API usage (~100+ lines)
- Configure window as layered
- Create path geometry for asymmetric corners
- Handle resize events to update clip
- Potentially implement custom shadow

### Why Not Recommended
While this provides high visual quality, the complexity is massive compared to the XAML approach. For a teleprompter app where the functional benefit over XAML CornerRadius is minimal, this is over-engineering.

---

## Recommendation: Option 1 (XAML CornerRadius) ⭐

**Continue using the current XAML CornerRadius approach.**

### Reasoning

1. **Already Implemented**: The app already uses `CornerRadius="0,0,8,8"` on the RootGrid, which produces the exact desired effect.

2. **Optimal Balance**: This approach provides the best balance of:
   - Visual appearance (looks professional)
   - Implementation simplicity (single property)
   - Maintainability (no complex code)
   - Performance (native hardware acceleration)
   - Compatibility (works on Windows 10 and 11)

3. **Practical Benefits**:
   - No risk of breaking existing functionality
   - Works perfectly with the app's acrylic overlay
   - Compatible with opacity changes and fullscreen mode
   - No additional testing burden
   - No Win32 interop complexity

4. **When Other Options Matter**:
   - **Option 2 (DWM)**: Only useful if you needed uniform corners on all sides (not applicable here)
   - **Option 3 (SetWindowRgn)**: Only if you absolutely need true window shape clipping and can sacrifice visual quality
   - **Option 4 (Composition)**: Only if you're building a highly custom, visual-first app where the corner quality is a primary feature

### Minor Enhancement Suggestion

The current implementation could be refined slightly:
- RootGrid: `CornerRadius="0,0,8,8"` (8px bottom radius) ✓ Already set
- OverlayPanel: `CornerRadius="0,0,12,12"` (12px bottom radius) ✓ Already set

The slightly larger radius (12px) on the overlay panel creates a nice visual hierarchy. This is already implemented correctly.

---

## Conclusion

**Status**: ✅ **The desired corner styling is already implemented correctly using XAML CornerRadius.**

**Recommendation**: **Continue using the current approach (Option 1).** Do not implement Options 2, 3, or 4 unless specific requirements emerge that cannot be met with XAML CornerRadius (e.g., need for true window hit-testing outside corners, which is unlikely for a teleprompter).

The XAML approach provides 95% of the visual benefit with 5% of the implementation complexity compared to the alternatives.
