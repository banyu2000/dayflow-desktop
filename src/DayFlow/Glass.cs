using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace DayFlow
{
    public static class Glass
    {
        class Host
        {
            public Canvas Overlay;
            public string Backend = "Pending";
        }
        static readonly Dictionary<Window, Host> hosts = new Dictionary<Window, Host>();
        public static int Confirmations;
        public static Brush Gradient(string first, string last)
        {
            return new LinearGradientBrush((Color)ColorConverter.ConvertFromString(first), (Color)ColorConverter.ConvertFromString(last), new Point(0, 0), new Point(1, 1));
        }
        public static Brush Accent = Gradient("#527FBB", "#449F9C");
        public static Brush Surface = Gradient("#ACFFFFFF", "#75EDF2FF");
        public static Brush Inner = Gradient("#7DFFFFFF", "#50E7F1FF");
        public static DropShadowEffect Shadow(double opacity, double blur, double depth)
        {
            return new DropShadowEffect { Color = Color.FromRgb(69, 91, 139), Opacity = opacity, BlurRadius = blur, ShadowDepth = depth, Direction = 270, RenderingBias = RenderingBias.Quality };
        }
        public static void Configure(Window window)
        {
            window.WindowStyle = WindowStyle.None;
            window.Background = Brushes.Transparent;
            window.AllowsTransparency = false;
            WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(-1), CornerRadius = new CornerRadius(26), UseAeroCaptionButtons = false });
            hosts[window] = new Host();
            window.SourceInitialized += delegate
            {
                hosts[window].Backend = NativeBackdrop.Apply(window);
            };
            window.Loaded += delegate
            {
                if (SystemParameters.ClientAreaAnimation) Motion.Enter((FrameworkElement)window.Content);
            };
            window.Closed += delegate { hosts.Remove(window); Motion.Forget(window); };
        }
        public static string Backend(Window window)
        {
            Host host; return hosts.TryGetValue(window, out host) ? host.Backend : "Unconfigured";
        }
        static Ellipse Glow(string hex, double size, HorizontalAlignment horizontal, VerticalAlignment vertical)
        {
            RadialGradientBrush brush = new RadialGradientBrush();
            Color color = (Color)ColorConverter.ConvertFromString(hex);
            brush.GradientStops.Add(new GradientStop(color, 0)); color.A = 0; brush.GradientStops.Add(new GradientStop(color, 1));
            return new Ellipse { Fill = brush, Width = size, Height = size, HorizontalAlignment = horizontal, VerticalAlignment = vertical, IsHitTestVisible = false, Margin = new Thickness(-110) };
        }
        public static FrameworkElement Wrap(Window window, FrameworkElement content, bool dialog)
        {
            Grid layers = new Grid();
            layers.Children.Add(Glow("#659CEBD5", dialog ? 520 : 760, HorizontalAlignment.Left, VerticalAlignment.Top));
            layers.Children.Add(Glow("#687DAAF6", dialog ? 480 : 820, HorizontalAlignment.Right, VerticalAlignment.Center));
            layers.Children.Add(Glow("#62C2B0EF", dialog ? 430 : 700, HorizontalAlignment.Left, VerticalAlignment.Bottom));
            layers.Children.Add(content);
            Canvas overlay = new Canvas { IsHitTestVisible = false };
            layers.Children.Add(overlay); hosts[window].Overlay = overlay;
            Border shell = new Border
            {
                Background = Gradient(dialog ? "#BAF5FAFF" : "#70F4FCFF", dialog ? "#ACEDF2FC" : "#55E7ECFD"),
                BorderBrush = Gradient("#EBFFFFFF", "#85F7FAFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(25), Child = layers
            };
            shell.SizeChanged += delegate { layers.Clip = new RectangleGeometry(new Rect(0, 0, layers.ActualWidth, layers.ActualHeight), 24, 24); };
            return shell;
        }
        public static void DragArea(FrameworkElement area, Window window)
        {
            area.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                DependencyObject node = e.OriginalSource as DependencyObject;
                while (node != null && node != area)
                {
                    if (node is ButtonBase || node is TextBoxBase || node is ComboBox || node is DatePicker) return;
                    node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
                }
                if (e.ClickCount == 2 && window.Owner == null)
                    window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.LeftButton == MouseButtonState.Pressed) window.DragMove();
                e.Handled = true;
            };
        }
        public static void Confirm(Window window, Rect bounds)
        {
            Host host;
            if (!hosts.TryGetValue(window, out host) || host.Overlay == null || bounds.IsEmpty || !SystemParameters.ClientAreaAnimation) return;
            double width = Math.Min(Math.Max(32, bounds.Width), Math.Max(32, host.Overlay.ActualWidth - bounds.Left - 5));
            Border halo = new Border { Width = width, Height = Math.Max(26, bounds.Height), CornerRadius = new CornerRadius(10), Background = Gradient("#2C9EDBD0", "#2694B7F5"), BorderBrush = Theme.Brush("#8CFFFFFF"), BorderThickness = new Thickness(1), Effect = Shadow(.15, 14, 3), IsHitTestVisible = false };
            Canvas.SetLeft(halo, Math.Max(4, bounds.Left)); Canvas.SetTop(halo, Math.Max(4, bounds.Top));
            host.Overlay.Children.Add(halo);
            Confirmations++;
            TranslateTransform transform = new TranslateTransform(); halo.RenderTransform = transform;
            Motion.To(transform, TranslateTransform.YProperty, -3, 240);
            Motion.To(halo, UIElement.OpacityProperty, 0, 240, null, delegate { host.Overlay.Children.Remove(halo); });
        }
    }
    public static class NativeBackdrop
    {
        [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int State, Flags, Color, Animation; }
        [StructLayout(LayoutKind.Sequential)] struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref Margins margins);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmIsCompositionEnabled(out bool enabled);
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr handle, ref CompositionData data);
        public static string Apply(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            HwndSource source = HwndSource.FromHwnd(handle);
            if (source != null && source.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            try
            {
                bool enabled;
                if (DwmIsCompositionEnabled(out enabled) != 0 || !enabled) return "Composition unavailable";
                Margins frame = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                int frameResult = DwmExtendFrameIntoClientArea(handle, ref frame);
                int light = 0, round = 2, acrylic = 3;
                DwmSetWindowAttribute(handle, 20, ref light, 4);
                DwmSetWindowAttribute(handle, 33, ref round, 4);
                int result = DwmSetWindowAttribute(handle, 38, ref acrylic, 4);
                if (result == 0 && frameResult == 0) return "Windows 11 Desktop Acrylic";
                AccentPolicy policy = new AccentPolicy { State = 4, Flags = 2, Color = unchecked((int)0xB3F4EFEB) };
                IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(policy));
                try
                {
                    Marshal.StructureToPtr(policy, memory, false);
                    CompositionData data = new CompositionData { Attribute = 19, Data = memory, Size = Marshal.SizeOf(policy) };
                    return SetWindowCompositionAttribute(handle, ref data) != 0 ? "Windows 10 Acrylic" : "Tinted glass fallback";
                }
                finally { Marshal.FreeHGlobal(memory); }
            }
            catch (DllNotFoundException) { return "Tinted glass fallback"; }
            catch (EntryPointNotFoundException) { return "Tinted glass fallback"; }
        }
    }
    public static class Motion
    {
        class Anchor { public Rect Rect; public DateTime Time; }
        static readonly Dictionary<Window, Anchor> anchors = new Dictionary<Window, Anchor>();
        public static readonly List<int> Durations = new List<int>();
        public static KeySpline Curve() { return new KeySpline(.22, 1, .36, 1); }
        public static void To(DependencyObject target, DependencyProperty property, double value, int milliseconds, double? from = null, Action completed = null)
        {
            IAnimatable animatable = target as IAnimatable;
            if (animatable == null) return;
            if (milliseconds < 150 || milliseconds > 280) throw new ArgumentOutOfRangeException("milliseconds");
            if (!SystemParameters.ClientAreaAnimation) { target.SetValue(property, value); if (completed != null) completed(); return; }
            Durations.Add(milliseconds);
            DoubleAnimationUsingKeyFrames animation = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)), FillBehavior = FillBehavior.HoldEnd };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from ?? (double)target.GetValue(property), KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)), Curve()));
            animation.Completed += delegate
            {
                target.SetValue(property, value); animatable.BeginAnimation(property, null);
                if (completed != null) completed();
            };
            animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }
        public static void Remember(FrameworkElement source, bool row)
        {
            Window window = Window.GetWindow(source);
            if (window == null || !source.IsVisible || source.ActualWidth <= 0) return;
            FrameworkElement element = row && source.Parent is FrameworkElement ? (FrameworkElement)source.Parent : source;
            try
            {
                Point point = element.TranslatePoint(new Point(), (UIElement)window.Content);
                anchors[window] = new Anchor { Rect = new Rect(point, new Size(element.ActualWidth, element.ActualHeight)), Time = DateTime.UtcNow };
            }
            catch (InvalidOperationException) { }
        }
        public static void Confirm(Window window)
        {
            Anchor anchor;
            if (anchors.TryGetValue(window, out anchor) && (DateTime.UtcNow - anchor.Time).TotalMilliseconds < 2500)
            {
                anchors.Remove(window); Glass.Confirm(window, anchor.Rect);
            }
        }
        public static void Forget(Window window) { anchors.Remove(window); }
        public static void WireButton(Button button)
        {
            TransformGroup group = new TransformGroup(); ScaleTransform scale = new ScaleTransform(1, 1); TranslateTransform move = new TranslateTransform(); group.Children.Add(scale); group.Children.Add(move);
            button.RenderTransformOrigin = new Point(.5, .5); button.RenderTransform = group;
            Action<bool, bool> state = delegate(bool hover, bool pressed)
            {
                if (!button.IsEnabled) return;
                int duration = pressed ? 150 : hover ? 200 : 220;
                To(scale, ScaleTransform.ScaleXProperty, pressed ? .982 : 1, duration); To(scale, ScaleTransform.ScaleYProperty, pressed ? .982 : 1, duration);
                To(move, TranslateTransform.YProperty, pressed ? .8 : hover ? -1.6 : 0, duration);
                button.ApplyTemplate();
                Border bg = button.Template.FindName("bg", button) as Border;
                if (bg != null)
                {
                    DropShadowEffect shadow = bg.Effect as DropShadowEffect;
                    if (shadow == null || shadow.IsFrozen) { shadow = Glass.Shadow(0, 12, 2); bg.Effect = shadow; }
                    To(shadow, DropShadowEffect.OpacityProperty, hover && !pressed ? .15 : 0, duration);
                    To(shadow, DropShadowEffect.BlurRadiusProperty, hover ? 18 : 10, duration);
                    To(shadow, DropShadowEffect.ShadowDepthProperty, pressed ? 1 : hover ? 5 : 2, duration);
                }
            };
            button.MouseEnter += delegate { state(true, false); };
            button.MouseLeave += delegate { state(false, false); };
            button.PreviewMouseLeftButtonDown += delegate { state(true, true); };
            button.PreviewMouseLeftButtonUp += delegate { state(button.IsMouseOver, false); };
            button.LostMouseCapture += delegate { state(button.IsMouseOver, false); };
            button.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Space || e.Key == Key.Enter) state(true, true); };
            button.PreviewKeyUp += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Space || e.Key == Key.Enter) state(button.IsMouseOver, false); };
        }
        public static void Surface(Border card, bool floatOnHover)
        {
            DropShadowEffect shadow = Glass.Shadow(.06, 20, 6);
            UIElement content = card.Child; card.Child = null;
            Thickness padding = card.Padding, border = card.BorderThickness;
            Border plate = new Border { Background = card.Background, BorderBrush = card.BorderBrush, BorderThickness = border, CornerRadius = card.CornerRadius, Effect = shadow, IsHitTestVisible = false };
            Border foreground = new Border { Child = content, Padding = new Thickness(padding.Left + border.Left, padding.Top + border.Top, padding.Right + border.Right, padding.Bottom + border.Bottom) };
            Grid layers = new Grid(); layers.Children.Add(plate); layers.Children.Add(foreground);
            card.Padding = new Thickness(0); card.BorderThickness = new Thickness(0); card.BorderBrush = null; card.Background = Brushes.Transparent; card.Child = layers;
            TranslateTransform move = new TranslateTransform(); card.RenderTransform = move;
            card.MouseEnter += delegate { To(shadow, DropShadowEffect.OpacityProperty, .12, 220); To(shadow, DropShadowEffect.BlurRadiusProperty, 28, 220); if (floatOnHover) To(move, TranslateTransform.YProperty, -2, 220); };
            card.MouseLeave += delegate { To(shadow, DropShadowEffect.OpacityProperty, .06, 220); To(shadow, DropShadowEffect.BlurRadiusProperty, 20, 220); if (floatOnHover) To(move, TranslateTransform.YProperty, 0, 220); };
        }
        public static void Enter(FrameworkElement element)
        {
            TranslateTransform move = element.RenderTransform as TranslateTransform;
            if (move == null) { move = new TranslateTransform(); element.RenderTransform = move; }
            To(move, TranslateTransform.YProperty, 0, 260, 6);
            To(element, UIElement.OpacityProperty, 1, 260, .88);
        }
        public static void Progress(ProgressBar bar, double previous, double next)
        {
            if (Math.Abs(previous - next) < .00001) return;
            bar.Loaded += delegate
            {
                bar.ApplyTemplate();
                FrameworkElement indicator = bar.Template.FindName("PART_Indicator", bar) as FrameworkElement;
                FrameworkElement track = bar.Template.FindName("PART_Track", bar) as FrameworkElement;
                if (indicator != null && track != null) To(indicator, FrameworkElement.WidthProperty, track.ActualWidth * next, 260, track.ActualWidth * previous);
            };
        }
    }
}
