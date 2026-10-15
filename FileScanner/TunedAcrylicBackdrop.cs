using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace FileScanner;

// Less transparent acrylic than the stock DesktopAcrylicBackdrop, which
// lets too much background through. The tint follows the theme so light mode does not look gray.
internal sealed partial class TunedAcrylicBackdrop : SystemBackdrop
{
    static readonly Color DarkTint = Color.FromArgb(255, 32, 32, 32);
    static readonly Color LightTint = Color.FromArgb(255, 243, 243, 243);

    // Tint weight over what is behind. Higher is less transparent.
    const float TintStrength = 0.55f;

    // How much the background brightness is flattened.
    const float LuminosityStrength = 0.90f;

    readonly AccessibilitySettings _accessibility = new();
    readonly UISettings _uiSettings = new();
    DesktopAcrylicController? _controller;
    SystemBackdropConfiguration? _configuration;
    bool _dark;

    public bool IsDark
    {
        get => _dark;
        set
        {
            _dark = value;
            if (_controller is not null) ApplyTint(_controller);
            if (_configuration is not null) _configuration.Theme = value ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _configuration ??= GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
        _controller ??= new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Base,
            TintOpacity = TintStrength,
            LuminosityOpacity = LuminosityStrength,
        };
        ApplyTint(_controller);
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_configuration);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
    }

    // Called on every theme change of the window content. The base implementation throws ArgumentException ("target")
    // here because this class connects its own DesktopAcrylicController (seen in crash.log, Windows App SDK 2.5.1).
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // Nothing to update: IsDark already sets the theme on _configuration.
    }

    // In high contrast the acrylic is replaced by FallbackColor, so it gets the contrast theme's window background
    // instead of the fixed gray and its text stays readable. When contrast changes, MainWindow.ApplyTheme sets IsDark again, which runs this.
    void ApplyTint(DesktopAcrylicController controller)
    {
        controller.TintColor = (_accessibility.HighContrast, _dark) switch
        {
            (true, _) => _uiSettings.UIElementColor(UIElementType.Window),
            (_, true) => DarkTint,
            _ => LightTint,
        };
        controller.FallbackColor = controller.TintColor;
    }
}
