using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

internal static class Cozy
{
    private static readonly string[] Keys = { "Page", "Surface", "Ink", "Muted", "Accent", "OnAccent",
        "Tint", "Line", "ControlLine", "Hover", "Success", "SuccessTint", "Art", "ArtInk" };
    private static readonly string[] Light = { "#FFF7F2", "#FFFFFF", "#2B2024", "#6E5D63", "#9F3857", "#FFFFFF",
        "#FCE7ED", "#EAD8D9", "#927982", "#F5E9E9", "#25684C", "#E7F6EC", "#FFBC91", "#4B2934" };
    private static readonly string[] Dark = { "#21191E", "#2D2229", "#FFF4F7", "#D9BEC9", "#F2A2BE", "#391522",
        "#432B38", "#594450", "#AB8998", "#3B2B34", "#A6DEBE", "#273F33", "#F2A280", "#4B2934" };

    internal static void Apply(FrameworkElement element, string preference)
    {
        bool dark = preference == "Dark";
        if (preference == "System")
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                dark = key != null && object.Equals(key.GetValue("AppsUseLightTheme"), 0);
        }
        var palette = dark ? Dark : Light;
        for (int i = 0; i < Keys.Length; i++)
            element.Resources[Keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i]));
        if (!SystemParameters.HighContrast) return;
        foreach (string key in new[] { "Page", "Surface", "Tint", "Hover", "SuccessTint", "Art" })
            element.Resources[key] = SystemColors.WindowBrush;
        foreach (string key in new[] { "Ink", "Muted", "Success", "Line", "ControlLine", "ArtInk" })
            element.Resources[key] = SystemColors.WindowTextBrush;
        element.Resources["Accent"] = SystemColors.HighlightBrush;
        element.Resources["OnAccent"] = SystemColors.HighlightTextBrush;
    }

    internal static TextBlock Text(string text, double size, string color, bool bold)
    {
        var label = new TextBlock {
            Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            LineHeight = Math.Ceiling(size * (size >= 24 ? 1.18 : 1.45))
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, color);
        return label;
    }

    internal static TextBlock Label(string text)
    {
        var label = Text(text, 12, "Muted", true);
        label.Margin = new Thickness(0, 0, 0, 7);
        return label;
    }

    internal static Border Card(string background, double radius, double padding)
    {
        var border = new Border { CornerRadius = new CornerRadius(radius), Padding = new Thickness(padding),
            BorderThickness = new Thickness(1), SnapsToDevicePixels = true };
        border.SetResourceReference(Border.BackgroundProperty, background);
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        return border;
    }

    private const string ButtonTemplate = @"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
 <Border x:Name='outline' CornerRadius='13' BorderThickness='2' BorderBrush='Transparent' Padding='2'>
  <Border x:Name='body' CornerRadius='9' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
   BorderThickness='1' Padding='{TemplateBinding Padding}'>
   <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center' RecognizesAccessKey='True' />
  </Border>
 </Border>
 <ControlTemplate.Triggers>
  <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='body' Property='Opacity' Value='0.86'/></Trigger>
  <Trigger Property='IsPressed' Value='True'><Setter TargetName='body' Property='Opacity' Value='0.72'/></Trigger>
  <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='outline' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>
  <Trigger Property='IsEnabled' Value='False'><Setter TargetName='body' Property='Opacity' Value='0.48'/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>";
    private static ControlTemplate template;

    internal static Button Button(string text, Action action, bool primary = false, bool flat = false)
    {
        if (template == null) template = (ControlTemplate)XamlReader.Parse(ButtonTemplate);
        var button = new Button {
            Template = template, Content = text, FontSize = 14, FontWeight = FontWeights.SemiBold,
            MinHeight = 42, Padding = new Thickness(11, 5, 11, 5),
            Cursor = System.Windows.Input.Cursors.Hand, FocusVisualStyle = null
        };
        PaintButton(button, primary, flat);
        button.Click += (sender, e) => action();
        return button;
    }

    internal static void PaintButton(Button button, bool primary, bool flat)
    {
        button.SetResourceReference(Control.ForegroundProperty, primary ? "OnAccent" : "Ink");
        if (flat) { button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; }
        else
        {
            button.SetResourceReference(Control.BackgroundProperty, primary ? "Accent" : "Surface");
            button.SetResourceReference(Control.BorderBrushProperty, primary ? "Accent" : "ControlLine");
        }
    }

    internal static System.Windows.Shapes.Path Path(string data, string color, bool fill, double width = 2)
    {
        var shape = new System.Windows.Shapes.Path { Data = Geometry.Parse(data), StrokeThickness = width,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        shape.SetResourceReference(fill ? Shape.FillProperty : Shape.StrokeProperty, color);
        return shape;
    }

    internal static Viewbox Mascot(double size, bool paused = false, bool done = false)
    {
        var illustration = new Canvas { Width = 160, Height = 150, IsHitTestVisible = false };
        illustration.Children.Add(Path("M80,8 A66,66 0 1 1 79.9,8", done ? "SuccessTint" : "Tint", true));
        illustration.Children.Add(Path("M62,126 A18,13 0 0 0 98,126 Z", "Accent", true));
        illustration.Children.Add(Path("M71,37 C71,26 89,26 89,37 L89,44 L71,44 Z", "Accent", true));
        var bell = Path("M39,105 C50,96 48,88 50,72 C52,53 65,42 80,42 C95,42 108,53 110,72 C112,88 110,96 121,105 C128,111 123,121 115,121 L45,121 C37,121 32,111 39,105 Z", "Art", true);
        bell.StrokeThickness = 2.5;
        bell.SetResourceReference(Shape.StrokeProperty, "ArtInk");
        illustration.Children.Add(bell);
        illustration.Children.Add(Path(paused ? "M61,80 Q66,85 71,80 M89,80 Q94,85 99,80" :
            "M65,77 L65,82 M95,77 L95,82", "ArtInk", false, 3.2));
        illustration.Children.Add(Path(done ? "M72,94 Q80,108 88,94 Z" : "M74,92 Q80,99 86,92", "ArtInk", false, 2.2));
        illustration.Children.Add(Path("M25,42 L25,56 M18,49 L32,49 M127,31 L127,49 M118,40 L136,40 M137,74 L137,82 M133,78 L141,78", "Accent", false, 2.2));
        return new Viewbox { Width = size, Height = size, Child = illustration, IsHitTestVisible = false };
    }

    internal static ContextMenu Options(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        anchor.ContextMenu = menu;
        return menu;
    }

    internal static void Option(ContextMenu menu, string label, Action action)
    {
        var item = new MenuItem { Header = label };
        item.Click += (sender, e) => action();
        menu.Items.Add(item);
    }
}
