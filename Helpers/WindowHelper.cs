using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using System.Runtime.InteropServices;

namespace WinPrompter.Helpers;

public static partial class WindowHelper
{
    public static AppWindow GetAppWindow(Window window)
    {
        var hWnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private static bool _isHandlingResize;

    public static void ConfigureAsFloatingPrompter(Window window, int width = 1400, int height = 300)
    {
        var appWindow = GetAppWindow(window);

        // Extend content into title bar and collapse it
        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        
        // Remove any gray background from title bar by setting all button colors to transparent
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonHoverBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonPressedBackgroundColor = Colors.Transparent;

        // Hide border and title bar
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
            presenter.IsAlwaysOnTop = true;
        }

        PositionTopCenter(appWindow, width, height);

        // Recenter horizontally when resized (with re-entrancy guard)
        appWindow.Changed += (aw, args) =>
        {
            if (!args.DidSizeChange || _isHandlingResize) return;
            _isHandlingResize = true;
            try
            {
                var display = DisplayArea.GetFromWindowId(aw.Id, DisplayAreaFallback.Primary);
                int x = (display.WorkArea.Width - aw.Size.Width) / 2;
                aw.Move(new Windows.Graphics.PointInt32(x, aw.Position.Y));
            }
            catch { }
            finally { _isHandlingResize = false; }
        };
    }

    public static void PositionTopCenter(AppWindow appWindow, int width, int height)
    {
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        int x = (displayArea.WorkArea.Width - width) / 2;
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, 0, width, height));
    }

    public static void ToggleFullscreen(Window window)
    {
        var appWindow = GetAppWindow(window);
        if (appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Default);
            ConfigureAsFloatingPrompter(window);
        }
        else
        {
            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
    }

    public static void SetOpacity(Window window, double opacity)
    {
        var hWnd = WindowNative.GetWindowHandle(window);
        const int GWL_EXSTYLE = -20;
        const nint WS_EX_LAYERED = 0x00080000;

        var style = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, style | WS_EX_LAYERED);
        SetLayeredWindowAttributes(hWnd, 0, (byte)(Math.Clamp(opacity, 0.1, 1.0) * 255), 0x02);
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
}
