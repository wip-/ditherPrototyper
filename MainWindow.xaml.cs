using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
//using System.Windows.Shapes;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace ditherPrototyper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                String infoMsg = Window_OnDrop_Sub(e);
                LabelInfo.Content = infoMsg;
            }
            catch (System.Exception ex)
            {
                var st = new StackTrace(ex, true);      // stack trace for the exception with source file information
                var frame = st.GetFrame(0);             // top stack frame
                String sourceMsg = String.Format("{0}({1})", frame.GetFileName(), frame.GetFileLineNumber());
                Console.WriteLine(sourceMsg);
                MessageBox.Show(ex.Message + Environment.NewLine + sourceMsg);
                Debugger.Break();
            }
        }

        private String Window_OnDrop_Sub(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return "Not a file!";

            String[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 1)
                return "Too many files!";

            String filename = files[0];

            if (!File.Exists(filename))
                return "Not a file!";

            FileStream fs = null;
            try
            {
                fs = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException)
            {
                if (fs != null)
                    fs.Close();
                return "File already in use!";
            }


            Bitmap bitmapSource = null;
            try
            {
                bitmapSource = new Bitmap(fs);
            }
            catch (System.Exception ex)
            {
                bitmapSource.Dispose();
                return "Not an image!";
            }

            ImageSource.Source =
                Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapSource.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            int bitmapWidth = bitmapSource.Width;
            int bitmapHeight = bitmapSource.Height;
            Rectangle rect = Rectangle.FromLTRB(0, 0, bitmapWidth, bitmapHeight);

            BitmapData bitmapDataSource = bitmapSource.LockBits(rect,
                ImageLockMode.WriteOnly, bitmapSource.PixelFormat);

            int bitmapStride = bitmapDataSource.Stride;
            int bitmapComponents = GetComponentsNumber(bitmapDataSource.PixelFormat);
            int dataBytesSize = bitmapStride * bitmapHeight;
            

            // First: create "Quantized" image by trunking color precision
            Bitmap bitmapQuantized = new Bitmap(bitmapWidth, bitmapHeight);
            BitmapData bitmapDataQuantized = bitmapQuantized.LockBits(rect,
                ImageLockMode.WriteOnly, /*bitmapSource.PixelFormat*/ System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            byte[] rgbaValuesQuantized = new byte[dataBytesSize];
            for (int y = 0; y < bitmapHeight; y++)
            {
                for (int x = 0; x < bitmapWidth; x++)
                {
                    System.Drawing.Color colorSource = GetColor(bitmapDataSource, x, y, bitmapSource.PixelFormat, bitmapStride, bitmapComponents, bitmapSource);
                    System.Drawing.Color colorQuantized = GetQuantizedColor(colorSource);

                    int indexQuantized = (bitmapStride * y) + (bitmapComponents * x);
                    rgbaValuesQuantized[indexQuantized + 0] = colorQuantized.B;  // B
                    rgbaValuesQuantized[indexQuantized + 1] = colorQuantized.G;  // G
                    rgbaValuesQuantized[indexQuantized + 2] = colorQuantized.R;  // R
                    rgbaValuesQuantized[indexQuantized + 3] = colorQuantized.A;  // A
                }
            }
            Marshal.Copy(rgbaValuesQuantized, 0, bitmapDataQuantized.Scan0, dataBytesSize);           
            ImageQuantized.Source =
                Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapQuantized.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());


            // Second: create "Dithered" image
            Bitmap bitmapDithered = new Bitmap(bitmapWidth, bitmapHeight);
            BitmapData bitmapDataDithered = bitmapDithered.LockBits(rect,
                ImageLockMode.WriteOnly, /*bitmapSource.PixelFormat*/ System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            byte[] rgbaValuesDithered = new byte[dataBytesSize];
            for (int y = 0; y < bitmapHeight; y++)
            {
                for (int x = 0; x < bitmapWidth; x++)
                {
                    System.Drawing.Color colorSource = GetColor(bitmapDataSource, x, y, bitmapSource.PixelFormat, bitmapStride, bitmapComponents, bitmapSource);
                    System.Drawing.Color colorQuantized = GetColor(bitmapDataQuantized, x, y, bitmapQuantized.PixelFormat, bitmapStride, bitmapComponents, bitmapQuantized);

                    System.Drawing.Color colorDithered = colorQuantized;
                    // TODO implement a dithering function !!!


                    int indexDithered = (bitmapStride * y) + (bitmapComponents * x);
                    rgbaValuesDithered[indexDithered + 0] = colorDithered.B;  // B
                    rgbaValuesDithered[indexDithered + 1] = colorDithered.G;  // G
                    rgbaValuesDithered[indexDithered + 2] = colorDithered.R;  // R
                    rgbaValuesDithered[indexDithered + 3] = colorDithered.A;  // A
                }
            }
            Marshal.Copy(rgbaValuesDithered, 0, bitmapDataDithered.Scan0, dataBytesSize);
            ImageDithered.Source =
                Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapDithered.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            bitmapSource.UnlockBits(bitmapDataSource);
            bitmapQuantized.UnlockBits(bitmapDataQuantized);
            bitmapDithered.UnlockBits(bitmapDataDithered);

            return "Drop source image";
        }

        // get closest color in quantized palette
        System.Drawing.Color GetQuantizedColor(System.Drawing.Color colorSource)
        {
            return System.Drawing.Color.FromArgb(
                Range256toRange8(colorSource.A),
                Range256toRange8(colorSource.R),
                Range256toRange8(colorSource.G),
                Range256toRange8(colorSource.B));
        }

        // dumb quantization: artificially replace 255 values precision by 8 values precision 
        byte Range256toRange8(byte b)
        {
            return (byte)(32*(b/32));
        }


        static private System.Drawing.Color GetColor(
            BitmapData bitmapData, int x, int y, System.Drawing.Imaging.PixelFormat pixelFormat, 
            int bitmapStride, int bitmapComponents, Bitmap bitmap)
        {
            if (pixelFormat == System.Drawing.Imaging.PixelFormat.Format8bppIndexed)
            {
                byte index = Marshal.ReadByte(bitmapData.Scan0, (bitmapStride * y) + (bitmapComponents * x));
                return bitmap.Palette.Entries[index];
            }
            else
            {
                System.Drawing.Color color = System.Drawing.Color.FromArgb(
                            Marshal.ReadInt32(bitmapData.Scan0, (bitmapStride * y) + (bitmapComponents * x)));
                return color;
            }
        }

        static private int GetComponentsNumber(System.Drawing.Imaging.PixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case System.Drawing.Imaging.PixelFormat.Format8bppIndexed:
                    return 1;

                case System.Drawing.Imaging.PixelFormat.Format24bppRgb:
                    return 3;

                case System.Drawing.Imaging.PixelFormat.Format32bppArgb:
                    return 4;

                default:
                    Debug.Assert(false);
                    return 0;
            }
        }

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            double vert = ((ScrollViewer)sender).VerticalOffset;
            double hori = ((ScrollViewer)sender).HorizontalOffset;

            foreach (ScrollViewer scrollViewer in new ScrollViewer[]{ScrollViewerSource, ScrollViewerQuantized, ScrollViewerDithered} )
            {
                scrollViewer.ScrollToVerticalOffset(vert);
                scrollViewer.ScrollToHorizontalOffset(hori);
                scrollViewer.UpdateLayout();
            }
        }

        private void SliderZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                ScaleTransform myScaleTransform = new ScaleTransform();
                myScaleTransform.ScaleY = SliderZoom.Value;
                myScaleTransform.ScaleX = SliderZoom.Value;
                if (LabelZoom!=null)
                    LabelZoom.Content = SliderZoom.Value;
                TransformGroup myTransformGroup = new TransformGroup(); 
                myTransformGroup.Children.Add(myScaleTransform); 

                foreach (System.Windows.Controls.Image image in
                    new System.Windows.Controls.Image[] { ImageSource, ImageQuantized, ImageDithered })
                {
                    if (image == null || image.Source == null)
                        continue;
                    image.RenderTransform = myTransformGroup;
                }
            }
            catch (System.Exception ex)
            {
                var st = new StackTrace(ex, true);      // stack trace for the exception with source file information
                var frame = st.GetFrame(0);             // top stack frame
                String sourceMsg = String.Format("{0}({1})", frame.GetFileName(), frame.GetFileLineNumber());
                Console.WriteLine(sourceMsg);
                MessageBox.Show(ex.Message + Environment.NewLine + sourceMsg);
                Debugger.Break();
            }
        }

    }
}
