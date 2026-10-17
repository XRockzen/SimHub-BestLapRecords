using System;
using System.Windows;
using System.Windows.Media;

namespace User.TeamPace
{
    /// <summary>
    /// The plugin's icon in SimHub's left menu, drawn in code: a stopwatch whose two hands take
    /// the colours of the first two drivers, one lap time against the other.
    /// </summary>
    internal static class PluginIcon
    {
        private static ImageSource _image;

        public static ImageSource Image
        {
            get
            {
                if (_image == null) _image = Build();
                return _image;
            }
        }

        private static ImageSource Build()
        {
            var white = new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5));
            var blue = new SolidColorBrush(Color.FromRgb(0x37, 0x8A, 0xDD));
            var orange = new SolidColorBrush(Color.FromRgb(0xE0, 0x62, 0x36));
            Func<Brush, double, Pen> pen = (b, w) => new Pen(b, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            var center = new Point(12, 13.5);
            var g = new DrawingGroup();
            // Transparent frame so the image keeps its 24 × 24 box
            g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
            // Case, crown and side button
            g.Children.Add(new GeometryDrawing(null, pen(white, 1.9), new EllipseGeometry(center, 8.6, 8.6)));
            g.Children.Add(new GeometryDrawing(white, null, new RectangleGeometry(new Rect(10, 1.6, 4, 2.1), 0.8, 0.8)));
            g.Children.Add(new GeometryDrawing(null, pen(white, 1.9), new LineGeometry(new Point(12, 3.7), new Point(12, 4.9))));
            g.Children.Add(new GeometryDrawing(null, pen(white, 1.8), new LineGeometry(new Point(18.4, 6.2), new Point(19.6, 5.0))));
            // Quarter ticks
            foreach (var a in new[] { 90.0, 180, 270 })
            {
                double r = a * Math.PI / 180;
                var o = new Point(center.X + Math.Sin(r) * 6.9, center.Y - Math.Cos(r) * 6.9);
                var i = new Point(center.X + Math.Sin(r) * 5.9, center.Y - Math.Cos(r) * 5.9);
                g.Children.Add(new GeometryDrawing(null, pen(white, 1.2), new LineGeometry(i, o)));
            }
            // Two drivers' hands
            g.Children.Add(new GeometryDrawing(null, pen(orange, 2.2), new LineGeometry(center, new Point(center.X + 4.9, center.Y + 2.6))));
            g.Children.Add(new GeometryDrawing(null, pen(blue, 2.2), new LineGeometry(center, new Point(center.X + 1.2, center.Y - 5.6))));
            g.Children.Add(new GeometryDrawing(white, null, new EllipseGeometry(center, 1.45, 1.45)));

            var image = new DrawingImage(g);
            image.Freeze();
            return image;
        }
    }
}
