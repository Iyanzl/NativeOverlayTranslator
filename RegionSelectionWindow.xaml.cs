using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NativeOverlayTranslator;

public partial class RegionSelectionWindow : Window
{
    private System.Windows.Point _start;
    private bool _dragging;

    public Rect? SelectedRegion { get; private set; }

    public RegionSelectionWindow()
    {
        InitializeComponent();
        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;
    }

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        _dragging = true;
        SelectionRect.Visibility = Visibility.Visible;
        CaptureMouse();
        UpdateSelection(_start);
    }

    private void Window_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        UpdateSelection(e.GetPosition(this));
    }

    private void Window_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        var end = e.GetPosition(this);
        var local = BuildRect(_start, end);
        if (local.Width < 6 || local.Height < 6)
        {
            DialogResult = false;
            return;
        }

        var topLeft = PointToScreen(new System.Windows.Point(local.Left, local.Top));
        var bottomRight = PointToScreen(new System.Windows.Point(local.Right, local.Bottom));
        SelectedRegion = new Rect(
            Math.Min(topLeft.X, bottomRight.X),
            Math.Min(topLeft.Y, bottomRight.Y),
            Math.Abs(bottomRight.X - topLeft.X),
            Math.Abs(bottomRight.Y - topLeft.Y));
        DialogResult = true;
    }

    private void Window_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void UpdateSelection(System.Windows.Point current)
    {
        var rect = BuildRect(_start, current);
        Canvas.SetLeft(SelectionRect, rect.X);
        Canvas.SetTop(SelectionRect, rect.Y);
        SelectionRect.Width = rect.Width;
        SelectionRect.Height = rect.Height;
    }

    private static Rect BuildRect(System.Windows.Point a, System.Windows.Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
}
