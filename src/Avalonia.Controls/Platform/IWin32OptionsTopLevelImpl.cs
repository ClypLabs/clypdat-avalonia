using Avalonia.Metadata;
using Avalonia.Platform;
using static Avalonia.Controls.Win32Properties;

namespace Avalonia.Controls.Platform
{
    [PrivateApi]
    public interface IWin32OptionsTopLevelImpl : ITopLevelImpl
    {
        /// <summary>
        /// Gets or sets a callback to set the window styles. 
        /// </summary>
        public CustomWindowStylesCallback? WindowStylesCallback { get; set; }

        /// <summary>
        /// Gets or sets a custom callback for the window's WndProc
        /// </summary>
        public CustomWndProcHookCallback? WndProcHookCallback { get; set; }

        /// <summary>
        /// Sets a window corner preference for the window.
        /// </summary>
        /// <param name="preference">The value to set.</param>
        public void SetWindowCornerPreference(WindowCornerPreference preference);

        /// <summary>
        /// Sets whole-window layered opacity.
        /// </summary>
        /// <param name="opacity">The opacity, or <see langword="null"/> to disable layered opacity.</param>
        public void SetLayeredWindowOpacity(double? opacity);
    }
}
