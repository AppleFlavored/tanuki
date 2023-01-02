using Gtk;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.Gtk;

namespace Tanuki;

public class BrowserWindow : ApplicationWindow
{
    [Builder.Object("UrlEntry")] private readonly Entry _urlEntry = default!;
    [Builder.Object("PageReload")] private readonly ToolButton _reloadPage = default!;
    [Builder.Object("HistoryBack")] private readonly ToolButton _backHistory = default!;
    [Builder.Object("HistoryForward")] private readonly ToolButton _forwardHistory = default!;
    [Builder.Object("PageContainer")] private readonly ScrolledWindow _pageContainer = default!;

    private readonly SKDrawingArea _skiaView = new();

    public BrowserWindow()
        : this(new Builder("BrowserWindow.glade"))
    {
    }

    private BrowserWindow(Builder builder)
        : base(builder.GetObject("BrowserWindow").Handle)
    {
        builder.Autoconnect(this);
        DeleteEvent += OnWindowDeleteEvent;

        _urlEntry.Activated += OnUrlEntryActivated;
        _backHistory.Clicked += OnBackClicked;
        _forwardHistory.Clicked += OnForwardClicked;
        _reloadPage.Clicked += OnReloadClicked;

        _pageContainer.Child = _skiaView;
        _skiaView.PaintSurface += OnPaintSurface;
        _skiaView.Show();
    }

    private static void OnWindowDeleteEvent(object sender, DeleteEventArgs e)
    {
        Application.Quit();
    }

    private void OnBackClicked(object? sender, EventArgs e)
    {
    }

    private void OnForwardClicked(object? sender, EventArgs e)
    {
    }

    private void OnReloadClicked(object? sender, EventArgs e)
    {
    }

    private void OnUrlEntryActivated(object? sender, EventArgs e)
    {
        _pageContainer.GrabFocus();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.White);
    }
}