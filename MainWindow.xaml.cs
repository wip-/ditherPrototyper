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
using Microsoft.Win32;

namespace ditherPrototyper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        String ImageSourceFileName;
        Bitmap bitmapQuantized;
        Bitmap bitmapDithered;

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

            ImageSourceFileName = files[0];

            if (!File.Exists(ImageSourceFileName))
                return "Not a file!";

            FileStream fs = null;
            try
            {
                fs = File.Open(ImageSourceFileName, FileMode.Open, FileAccess.Read, FileShare.None);
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
            catch (System.Exception /*ex*/)
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
            

            // Image on center (optional): create "Quantized" image by truncating color precision
            bitmapQuantized = new Bitmap(bitmapWidth, bitmapHeight);
            BitmapData bitmapDataQuantized = bitmapQuantized.LockBits(rect,
                ImageLockMode.WriteOnly, /*bitmapSource.PixelFormat*/ System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            byte[] rgbaValuesQuantized = new byte[dataBytesSize];
            for (int y = 0; y < bitmapHeight; y++)
            {
                for (int x = 0; x < bitmapWidth; x++)
                {
                    System.Drawing.Color colorSource = GetPixelColor(bitmapDataSource, x, y, bitmapSource.PixelFormat, bitmapStride, bitmapComponents, bitmapSource);
                    System.Drawing.Color colorQuantized = GetQuantizedColor(colorSource);

                    SetPixelColorInArray(rgbaValuesQuantized, x, y, colorQuantized, bitmapStride, bitmapComponents);
                }
            }
            Marshal.Copy(rgbaValuesQuantized, 0, bitmapDataQuantized.Scan0, dataBytesSize);           
            ImageQuantized.Source =
                Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapQuantized.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());


            // Image on right: create "Dithered" image
            bitmapDithered = new Bitmap(bitmapWidth, bitmapHeight);
            BitmapData bitmapDataDithered = bitmapDithered.LockBits(rect,
                ImageLockMode.WriteOnly, /*bitmapSource.PixelFormat*/ System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            byte[] rgbaValuesDithered = new byte[dataBytesSize];

            // initialize with values from source image
            for (int y = 0; y < bitmapHeight; y++)
            {
                for (int x = 0; x < bitmapWidth; x++)
                {
                    System.Drawing.Color colorSource = GetPixelColor(bitmapDataSource, x, y, bitmapSource.PixelFormat, bitmapStride, bitmapComponents, bitmapSource);
                    SetPixelColorInArray(rgbaValuesDithered, x, y, colorSource, bitmapStride, bitmapComponents);
                }
            }

            // perform dithering (Floyd Steinberg)
            for (int y = 0; y < bitmapHeight; y++)
            {
                for (int x = 0; x < bitmapWidth; x++)
                {
                    System.Drawing.Color colorSource = GetPixelColor(bitmapDataSource, x, y, bitmapSource.PixelFormat, bitmapStride, bitmapComponents, bitmapSource);
                    System.Drawing.Color colorWithErrorDiffused = GetPixelColorFromArray(rgbaValuesDithered, x, y, bitmapStride, bitmapComponents);
                    System.Drawing.Color colorQuantized = GetQuantizedColor(colorWithErrorDiffused);

                    // Set quantized color to central pixel
                    SetPixelColorInArray(rgbaValuesDithered, x, y, colorQuantized, bitmapStride, bitmapComponents);

                    // Diffuse error to neighbors (they will be quantized during subsequent loops)
                    Int32 errorA = colorSource.A - colorQuantized.A;
                    Int32 errorR = colorSource.R - colorQuantized.R;
                    Int32 errorG = colorSource.G - colorQuantized.G;
                    Int32 errorB = colorSource.B - colorQuantized.B;

                    AddColorErrorToPixelInArray(rgbaValuesDithered, x + 1, y + 0, bitmapStride, bitmapComponents, bitmapWidth, bitmapHeight,
                        errorA, errorR, errorG, errorB, 7f / 16f);
                    AddColorErrorToPixelInArray(rgbaValuesDithered, x - 1, y + 1, bitmapStride, bitmapComponents, bitmapWidth, bitmapHeight,
                        errorA, errorR, errorG, errorB, 3f / 16f);
                    AddColorErrorToPixelInArray(rgbaValuesDithered, x + 0, y + 1, bitmapStride, bitmapComponents, bitmapWidth, bitmapHeight,
                        errorA, errorR, errorG, errorB, 5f / 16f);
                    AddColorErrorToPixelInArray(rgbaValuesDithered, x + 1, y + 1, bitmapStride, bitmapComponents, bitmapWidth, bitmapHeight,
                        errorA, errorR, errorG, errorB, 1f / 16f);
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

        static private System.Drawing.Color GetPixelColor(
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

        static private System.Drawing.Color GetPixelColorFromArray(byte[] pixelsArray, int x, int y, int bitmapStride, int bitmapComponents)
        {
            int indexDithered = (bitmapStride * y) + (bitmapComponents * x);
            return System.Drawing.Color.FromArgb(
                pixelsArray[indexDithered + 3],
                pixelsArray[indexDithered + 2],
                pixelsArray[indexDithered + 1],
                pixelsArray[indexDithered + 0]);
        }

        static private void SetPixelColorInArray(
            byte[] pixelsArray, int x, int y, System.Drawing.Color color, int bitmapStride, int bitmapComponents)
        {
            int indexDithered = (bitmapStride * y) + (bitmapComponents * x);
            pixelsArray[indexDithered + 0] = color.B;  // B
            pixelsArray[indexDithered + 1] = color.G;  // G
            pixelsArray[indexDithered + 2] = color.R;  // R
            pixelsArray[indexDithered + 3] = color.A;  // A
        }

        static private void AddColorErrorToPixelInArray(
            byte[] pixelsArray, int x, int y, int bitmapStride, int bitmapComponents, int bitmapWidth, int bitmapHeigth,
            Int32 errorA, Int32 errorR, Int32 errorG, Int32 errorB, double coefficient)
        {
            if (x < 0 || y < 0 || x >= bitmapWidth || y >= bitmapHeigth)
                return;

            int indexDithered = (bitmapStride * y) + (bitmapComponents * x);
            byte sourceB = pixelsArray[indexDithered + 0];
            byte sourceG = pixelsArray[indexDithered + 1];
            byte sourceR = pixelsArray[indexDithered + 2];
            byte sourceA = pixelsArray[indexDithered + 3];

            double newB = Clamp0_255(sourceB + coefficient * errorB);
            double newG = Clamp0_255(sourceG + coefficient * errorG);
            double newR = Clamp0_255(sourceR + coefficient * errorR);
            double newA = Clamp0_255(sourceA + coefficient * errorA);

            pixelsArray[indexDithered + 0] = Convert.ToByte(newB);  // B
            pixelsArray[indexDithered + 1] = Convert.ToByte(newG);  // G
            pixelsArray[indexDithered + 2] = Convert.ToByte(newR);  // R
            pixelsArray[indexDithered + 3] = Convert.ToByte(newA);  // A
        }

        public static double Clamp0_255(double value)
        {
            return (value < 0f) ? 0f : (value > 255f) ? 255f : value;
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
                    //image.RenderTransform = myTransformGroup;
                    image.LayoutTransform = myTransformGroup;
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

        private void ButtonResetZoom_Click(object sender, RoutedEventArgs e)
        {
            SliderZoom.Value = 1;
        }

        private void ButtonShowSource_Click(object sender, RoutedEventArgs e)
        {
            if (ImageSourceFileName == null)
                return;

            Process.Start("explorer.exe", @"/select,""" + ImageSourceFileName + "\"");
        }

        private void ButtonSaveQuantized_Click(object sender, RoutedEventArgs e)
        {
            if (bitmapQuantized==null)
                return;

            SaveFileDialog dialogSaveFile = new SaveFileDialog();
            dialogSaveFile.Filter = "Supported images|*.png";
            dialogSaveFile.InitialDirectory = Path.GetDirectoryName(ImageSourceFileName);
            dialogSaveFile.FileName = AddToFileName(ImageSourceFileName, "-quantized");

            if ( (bool)dialogSaveFile.ShowDialog() /*== DialogResult.OK*/ )
            {
                Stream saveStream;
                if ((saveStream = dialogSaveFile.OpenFile()) != null)
                {
                    bitmapQuantized.Save(saveStream, ImageFormat.Png);
                    saveStream.Close();
                }
            }
        }

        String AddToFileName(String filename, String addChars)
        {
            return 
                /*Path.GetDirectoryName(filename) +*/
            Path.GetFileNameWithoutExtension(filename) + addChars + Path.GetExtension(filename);
        }

        private void ButtonSaveDithered_Click(object sender, RoutedEventArgs e)
        {
            if (bitmapDithered == null)
                return;

            SaveFileDialog dialogSaveFile = new SaveFileDialog();
            dialogSaveFile.Filter = "Supported images|*.png";
            dialogSaveFile.InitialDirectory = Path.GetDirectoryName(ImageSourceFileName);
            dialogSaveFile.FileName = AddToFileName(ImageSourceFileName, "-dithered");

            if ( (bool)dialogSaveFile.ShowDialog() /*== DialogResult.OK*/)
            {
                Stream saveStream;
                if ((saveStream = dialogSaveFile.OpenFile()) != null)
                {
                    bitmapDithered.Save(saveStream, ImageFormat.Png);
                    saveStream.Close();
                }
            }
        }

    }
}
