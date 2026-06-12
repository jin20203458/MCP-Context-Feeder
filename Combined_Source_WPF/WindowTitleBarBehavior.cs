using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CodeMergerUI
{
    public static class WindowTitleBarBehavior
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static readonly DependencyProperty EnableDarkTitleBarProperty =
            DependencyProperty.RegisterAttached(
                "EnableDarkTitleBar",
                typeof(bool),
                typeof(WindowTitleBarBehavior),
                new PropertyMetadata(false, OnEnableDarkTitleBarChanged));

        public static bool GetEnableDarkTitleBar(DependencyObject obj) => (bool)obj.GetValue(EnableDarkTitleBarProperty);
        public static void SetEnableDarkTitleBar(DependencyObject obj, bool value) => obj.SetValue(EnableDarkTitleBarProperty, value);

        private static void OnEnableDarkTitleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Window window && (bool)e.NewValue)
            {
                window.SourceInitialized += (s, ev) => ApplyDarkThemeTitleBar(window);
            }
        }

        public static void ApplyDarkThemeTitleBar(Window window)
        {
            try
            {
                var helper = new WindowInteropHelper(window);
                var hwnd = helper.Handle;

                if (hwnd != IntPtr.Zero)
                {
                    // 1. Force dark mode (prevents flashing white when inactive/unfocused)
                    int useImmersiveDarkMode = 1;
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));

                    // 2. Set title bar color to sidebar background `#141414` -> BGR: 0x00141414
                    int darkColorValue = 0x00141414;
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref darkColorValue, sizeof(int));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set dark title bar color: {ex.Message}");
            }
        }
    }
}
