using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Metadata;
using Avalonia.Platform;
using static Avalonia.Controls.Platform.IWin32OptionsTopLevelImpl;

namespace Avalonia.Controls
{
    /// <summary>
    /// Set of Win32 specific properties and events that allow deeper customization of the application per platform.
    /// </summary>
    public static class Win32Properties
    {
        /// <summary>
        /// Defines the <c>NonClientHitTestResult</c> attached property.
        /// </summary>
        public static readonly AttachedProperty<Win32HitTestValue> NonClientHitTestResultProperty =
            AvaloniaProperty.RegisterAttached<Visual, Win32HitTestValue>(
                "NonClientHitTestResult",
                typeof(Win32Properties),
                inherits: true,
                defaultValue: Win32HitTestValue.Client);

        /// <summary>
        /// Defines the <c>WindowCornerPreferenceProperty</c> attached property.
        /// </summary>
        public static readonly AttachedProperty<WindowCornerPreference> WindowCornerPreferenceProperty =
            AvaloniaProperty.RegisterAttached<Window, WindowCornerPreference>(
                "WindowCornerPreference",
                typeof(Win32Properties),
                defaultValue: WindowCornerPreference.Default,
                validate: ValidateWindowCornerPreference);

        /// <summary>
        /// Defines the <c>LayeredWindowOpacityProperty</c> attached property.
        /// </summary>
        /// <remarks>
        /// This is a Windows-only fallback for cases where normal Avalonia transparency is broken
        /// by the compositor. It applies opacity to the whole HWND and is not equivalent to normal
        /// Avalonia per-pixel transparency.
        /// </remarks>
        public static readonly AttachedProperty<double?> LayeredWindowOpacityProperty =
            AvaloniaProperty.RegisterAttached<Window, double?>(
                "LayeredWindowOpacity",
                typeof(Win32Properties),
                defaultValue: null,
                validate: ValidateLayeredWindowOpacity);

        public delegate (uint style, uint exStyle) CustomWindowStylesCallback(uint style, uint exStyle);
        public delegate IntPtr CustomWndProcHookCallback(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled);

        static Win32Properties()
        {
            WindowCornerPreferenceProperty.Changed.AddClassHandler<Window>((window, e) =>
            {
                if (window.PlatformImpl is IWin32OptionsTopLevelImpl toplevelImpl)
                    toplevelImpl.SetWindowCornerPreference(e.GetNewValue<WindowCornerPreference>());
            });

            LayeredWindowOpacityProperty.Changed.AddClassHandler<Window>((window, e) =>
            {
                if (window.PlatformImpl is IWin32OptionsTopLevelImpl toplevelImpl)
                    toplevelImpl.SetLayeredWindowOpacity(e.GetNewValue<double?>());
            });
        }

        /// <summary>
        /// Adds a callback to set the window's style.
        /// </summary>
        /// <param name="topLevel">The window implementation</param>
        /// <param name="callback">The callback</param>
        public static void AddWindowStylesCallback(TopLevel topLevel, CustomWindowStylesCallback? callback)
        {
            if (topLevel.PlatformImpl is IWin32OptionsTopLevelImpl toplevelImpl)
            {
                toplevelImpl.WindowStylesCallback += callback;
            }
        }

        /// <summary>
        /// Removes a callback to set the window's style.
        /// </summary>
        /// <param name="topLevel">The window implementation</param>
        /// <param name="callback">The callback</param>
        public static void RemoveWindowStylesCallback(TopLevel topLevel, CustomWindowStylesCallback? callback)
        {
            if (topLevel.PlatformImpl is IWin32OptionsTopLevelImpl toplevelImpl)
            {
                toplevelImpl.WindowStylesCallback -= callback;
            }
        }

        /// <summary>
        /// Adds a custom callback for the window's WndProc
        /// </summary>
        /// <param name="topLevel">The window</param>
        /// <param name="callback">The callback</param>
        public static void AddWndProcHookCallback(TopLevel topLevel, CustomWndProcHookCallback? callback)
        {
            if (topLevel.PlatformImpl is IWin32OptionsTopLevelImpl toplevelImpl)
            {
                toplevelImpl.WndProcHookCallback += callback;
            }
        }

        /// <summary>
        /// Removes a custom callback for the window's WndProc
        /// </summary>
        /// <param name="topLevel">The window</param>
        /// <param name="callback">The callback</param>
        public static void RemoveWndProcHookCallback(TopLevel topLevel, CustomWndProcHookCallback? callback)
        {
            if (topLevel.PlatformImpl is IWin32OptionsTopLevelImpl toplevelImpl)
            {
                toplevelImpl.WndProcHookCallback -= callback;
            }
        }

        /// <summary>
        /// Sets the value of the <c>NonClientHitTestResult</c> attached property on the specified visual.
        /// </summary>
        /// <param name="obj">The visual.</param>
        /// <param name="value">The value to set.</param>
        public static void SetNonClientHitTestResult(Visual obj, Win32HitTestValue value)
            => obj.SetValue(NonClientHitTestResultProperty, value);

        /// <summary>
        /// Gets the value of the <c>NonClientHitTestResult</c> attached property on the specified visual.
        /// </summary>
        /// <param name="obj">The visual.</param>
        /// <returns>The hit test value for <paramref name="obj"/>.</returns>
        public static Win32HitTestValue GetNonClientHitTestResult(Visual obj)
            => obj.GetValue(NonClientHitTestResultProperty);

        /// <summary>
        /// Sets the value of the <c>WindowCornerPreference</c> attached property on the specified window.
        /// </summary>
        /// <param name="window">The window.</param>
        /// <param name="value">The value to set.</param>
        /// <remarks>
        /// This value is supported starting with Windows 11 Build 22000.
        /// It is ignored on earlier Windows versions.
        /// </remarks>
        public static void SetWindowCornerPreference(Window window, WindowCornerPreference value)
            => window.SetValue(WindowCornerPreferenceProperty, value);

        /// <summary>
        /// Gets the value of the <c>WindowCornerPreference</c> attached property on the specified window.
        /// </summary>
        /// <param name="window">The window.</param>
        /// <returns>The window corner preference for <paramref name="window"/>.</returns>
        public static WindowCornerPreference GetWindowCornerPreference(Window window)
            => window.GetValue(WindowCornerPreferenceProperty);

        /// <summary>
        /// Sets whole-window layered opacity on the specified window.
        /// </summary>
        /// <param name="window">The window.</param>
        /// <param name="value">The opacity from 0 to 1, or <see langword="null"/> to disable it.</param>
        /// <remarks>
        /// This is a Windows-only fallback for cases where normal Avalonia transparency is broken
        /// by the compositor. It applies opacity to the whole HWND and is not equivalent to normal
        /// Avalonia per-pixel transparency. It is ignored on other platforms.
        /// </remarks>
        public static void SetLayeredWindowOpacity(Window window, double? value)
            => window.SetValue(LayeredWindowOpacityProperty, value);

        /// <summary>
        /// Gets whole-window layered opacity for the specified window.
        /// </summary>
        /// <param name="window">The window.</param>
        /// <returns>The opacity from 0 to 1, or <see langword="null"/> when disabled.</returns>
        public static double? GetLayeredWindowOpacity(Window window)
            => window.GetValue(LayeredWindowOpacityProperty);

        private static bool ValidateWindowCornerPreference(WindowCornerPreference preference)
            => preference is >= WindowCornerPreference.Default and <= WindowCornerPreference.RoundSmall;

        private static bool ValidateLayeredWindowOpacity(double? opacity)
            => opacity is null || double.IsFinite(opacity.Value) && opacity.Value is >= 0 and <= 1;

        /// <summary>
        /// Represents a hit test value for a visual.
        /// </summary>
        public enum Win32HitTestValue
        {
            Nowhere = 0,
            Client = 1,
            Caption = 2,
            MinButton = 8,
            MaxButton = 9,
            Left = 10,
            Right = 11,
            Top = 12,
            TopLeft = 13,
            TopRight = 14,
            Bottom = 15,
            BottomLeft = 16,
            BottomRight = 17,
            Close = 20,
        }

        /// <summary>
        /// Represents the rounded corner preference for a window.
        /// </summary>
        public enum WindowCornerPreference
        {
            /// <summary>Let the system decide when to round window corners.</summary>
            Default = 0,

            /// <summary>Never round window corners.</summary>
            DoNotRound = 1,

            /// <summary>Round the corners, if appropriate.</summary>
            Round = 2,

            /// <summary>Round the corners if appropriate, with a small radius.</summary>
            RoundSmall = 3
        }
    }
}
