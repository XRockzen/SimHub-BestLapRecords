using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace User.TeamPace
{
    /// <summary>Small shared helpers for the views built in code (one look everywhere).</summary>
    internal static class Ui
    {
        public static readonly Brush Text      = Frozen(Color.FromRgb(0xE8, 0xE9, 0xEC));
        public static readonly Brush Muted     = Frozen(Color.FromArgb(166, 255, 255, 255));
        public static readonly Brush Faint     = Frozen(Color.FromArgb(110, 255, 255, 255));
        public static readonly Brush Panel     = Frozen(Color.FromArgb(14, 255, 255, 255));
        public static readonly Brush HeaderBg  = Frozen(Color.FromArgb(22, 255, 255, 255));
        public static readonly Brush Line      = Frozen(Color.FromArgb(18, 255, 255, 255));
        public static readonly Brush Green     = Frozen(Color.FromRgb(0x66, 0xBB, 0x6A));
        public static readonly Brush Red       = Frozen(Color.FromRgb(0xEF, 0x9A, 0x9A));
        public static readonly Brush Amber     = Frozen(Color.FromRgb(0xFF, 0xC8, 0x57));
        public static readonly Brush Link      = Frozen(Color.FromRgb(0x7F, 0xB6, 0xF0));
        public static readonly Brush Selected  = Frozen(Color.FromArgb(34, 255, 200, 87));
        public static readonly Brush Hover     = Frozen(Color.FromArgb(12, 255, 255, 255));

        public static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static Brush Tint(SolidColorBrush b, byte alpha)
        {
            return Frozen(Color.FromArgb(alpha, b.Color.R, b.Color.G, b.Color.B));
        }

        /// <summary>Lighter version of a driver colour, readable as text on the dark background.</summary>
        public static Brush Light(SolidColorBrush b)
        {
            var c = b.Color;
            return Frozen(Color.FromRgb((byte)(c.R + (255 - c.R) * 0.25), (byte)(c.G + (255 - c.G) * 0.25), (byte)(c.B + (255 - c.B) * 0.25)));
        }

        public static TextBlock T(string text, double size = 13, Brush fg = null, FontWeight? weight = null)
        {
            return new TextBlock
            {
                Text = text ?? "", FontSize = size, Foreground = fg ?? Text,
                FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
        }

        public static Border Badge(string text, string bgHex, double size = 11)
        {
            return new Border
            {
                Background = Frozen((Color)ColorConverter.ConvertFromString(bgHex)),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
            };
        }

        public static Border Card(UIElement child, Thickness? padding = null)
        {
            return new Border { Background = Panel, CornerRadius = new CornerRadius(7), Padding = padding ?? new Thickness(12, 9, 12, 9), Child = child };
        }

        public static Button Button(string text, string tooltip = null)
        {
            var b = new Button { Content = text, ToolTip = tooltip, VerticalAlignment = VerticalAlignment.Center };
            b.SetResourceReference(FrameworkElement.StyleProperty, "NavBtn");
            return b;
        }

        public static Button SmallButton(string text, string tooltip = null)
        {
            var b = new Button { Content = text, ToolTip = tooltip, VerticalAlignment = VerticalAlignment.Center };
            b.SetResourceReference(FrameworkElement.StyleProperty, "SmallBtn");
            return b;
        }

        public static void SetOn(Button b, bool on)
        {
            b.BorderBrush = on ? Frozen(Color.FromRgb(0x21, 0x96, 0xF3)) : Frozen(Color.FromArgb(68, 255, 255, 255));
            b.Background  = on ? Frozen(Color.FromArgb(38, 0x21, 0x96, 0xF3)) : Brushes.Transparent;
        }
    }
}
