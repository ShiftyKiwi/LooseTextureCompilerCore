using LooseTextureCompilerCore;
using Lumina.Data.Files;
using OtterTex;
using Penumbra.LTCImport.Dds;
using Penumbra.LTCImport.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;
namespace FFXIVLooseTextureCompiler.ImageProcessing
{
    public static class TexIO
    {
        public static Bitmap DDSToBitmap(string inputFile, bool noAlpha = false)
        {
            using (var scratch = ScratchImage.LoadDDS(inputFile))
            {
                var rgba = scratch.GetRGBA(out var f).ThrowIfError(f);
                byte[] ddsFile = rgba.Pixels[..(f.Meta.Width * f.Meta.Height * f.Meta.Format.BitsPerPixel() / 8)].ToArray();
                var bitmap = RGBAToBitmap(ddsFile, scratch.Meta.Width, scratch.Meta.Height, noAlpha);
                return bitmap;
            }
        }

        public static KeyValuePair<Size, byte[]> DDSToBytes(string inputFile)
        {
            using (var scratch = ScratchImage.LoadDDS(inputFile))
            {
                var rgba = scratch.GetRGBA(out var f).ThrowIfError(f);
                return new KeyValuePair<Size, byte[]>(new Size(f.Meta.Width, f.Meta.Height),
                rgba.Pixels[..(f.Meta.Width * f.Meta.Height * f.Meta.Format.BitsPerPixel() / 8)].ToArray());
            }
        }

        public static KeyValuePair<Size, byte[]> PngToBytes(string inputFile)
        {
            byte[] output = new byte[0];
            using (Bitmap bitmap = new Bitmap(inputFile))
            {
                MemoryStream stream = new MemoryStream();
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;
                PenumbraTextureImporter.PngToTex(stream, out output);
                return TexToBytes(new MemoryStream(output));
            }
        }


        public static Bitmap RGBAToBitmap(byte[] RGBAPixels, int width, int height, bool noAlpha = false)
        {
            Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            Rectangle rect = new Rectangle(0, 0, output.Width, output.Height);
            BitmapData bmpData = output.LockBits(rect, ImageLockMode.ReadWrite, output.PixelFormat);
            IntPtr ptr = bmpData.Scan0;
            for (int i = 0; i < RGBAPixels.Length; i += 4)
            {
                byte R = RGBAPixels[i];
                byte G = RGBAPixels[i + 1];
                byte B = RGBAPixels[i + 2];
                byte A = RGBAPixels[i + 3];

                RGBAPixels[i] = B;
                RGBAPixels[i + 1] = G;
                RGBAPixels[i + 2] = R;
                RGBAPixels[i + 3] = noAlpha ? (byte)255 : A;
            }
            System.Runtime.InteropServices.Marshal.Copy(RGBAPixels, 0, ptr, RGBAPixels.Length);
            output.UnlockBits(bmpData);
            return output;
        }

        //Optimized

        public static byte[] BitmapToRGBA(Bitmap bitmap)
        {
            byte[] RGBAPixels = (byte[])new ImageConverter().ConvertTo(new ImageConverter(), typeof(byte[]));
            for (int i = 0; i < RGBAPixels.Length; i += 4)
            {
                byte B = RGBAPixels[i];
                byte G = RGBAPixels[i + 1];
                byte R = RGBAPixels[i + 2];
                byte A = RGBAPixels[i + 3];

                RGBAPixels[i] = R;
                RGBAPixels[i + 1] = G;
                RGBAPixels[i + 2] = B;
                RGBAPixels[i + 3] = A;
            }
            return RGBAPixels;
        }

        public static byte[] BitmapToRGBA(Stream stream)
        {
            MemoryStream memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            byte[] RGBAPixels = memoryStream.ToArray();
            for (int i = 0; i < RGBAPixels.Length; i += 4)
            {
                byte B = RGBAPixels[i];
                byte G = RGBAPixels[i + 1];
                byte R = RGBAPixels[i + 2];
                byte A = RGBAPixels[i + 3];

                RGBAPixels[i] = R;
                RGBAPixels[i + 1] = G;
                RGBAPixels[i + 2] = B;
                RGBAPixels[i + 3] = A;

            }
            return RGBAPixels;
        }

        public static Bitmap TexToBitmap(string path, bool noAlpha = false)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var scratch = PenumbraTexFileParser.Parse(stream);
                var rgba = scratch.GetRGBA(out var f).ThrowIfError(f);
                byte[] RGBAPixels = rgba.Pixels[..(f.Meta.Width * f.Meta.Height * f.Meta.Format.BitsPerPixel() / 8)].ToArray();
                var bitmap = RGBAToBitmap(RGBAPixels, scratch.Meta.Width, scratch.Meta.Height, noAlpha);
                return bitmap;
            }
        }
        public static Bitmap TexToBitmap(Stream stream, bool noAlpha = false)
        {
            var scratch = PenumbraTexFileParser.Parse(stream);
            var rgba = scratch.GetRGBA(out var f).ThrowIfError(f);
            byte[] RGBAPixels = rgba.Pixels[..(f.Meta.Width * f.Meta.Height * f.Meta.Format.BitsPerPixel() / 8)].ToArray();
            var bitmap = RGBAToBitmap(RGBAPixels, scratch.Meta.Width, scratch.Meta.Height, noAlpha);
            return bitmap;
        }

        public static KeyValuePair<Size, byte[]> TexToBytes(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var scratch = PenumbraTexFileParser.Parse(stream);
                var rgba = scratch.GetRGBA(out var f).ThrowIfError(f);
                return new KeyValuePair<Size, byte[]>(new Size(f.Meta.Width, f.Meta.Height),
                rgba.Pixels[..(f.Meta.Width * f.Meta.Height * f.Meta.Format.BitsPerPixel() / 8)].ToArray());
            }
        }

        public static KeyValuePair<Size, byte[]> TexToBytes(Stream stream)
        {
            var scratch = PenumbraTexFileParser.Parse(stream);
            var rgba = scratch.GetRGBA(out var f).ThrowIfError(f);
            return new KeyValuePair<Size, byte[]>(new Size(f.Meta.Width, f.Meta.Height),
            rgba.Pixels[..(f.Meta.Width * f.Meta.Height * f.Meta.Format.BitsPerPixel() / 8)].ToArray());
        }

        public static Bitmap ResolveBitmap(string inputFile, bool noAlpha = false)
        {
            if (string.IsNullOrEmpty(inputFile) || !File.Exists(inputFile))
            {
                var item = new Bitmap(4096, 4096);
                Graphics.FromImage(item).Clear(Color.Transparent);
                return item;
            }
            bool succeeded = false;
            while (!succeeded)
            {
                while (IsFileLocked(inputFile))
                {
                    Thread.Sleep(1000);
                }
                try
                {
                    Bitmap bitmap =
                    inputFile.EndsWith(".tex") ? TexToBitmap(inputFile, noAlpha) :
                    inputFile.EndsWith(".dds") ? DDSToBitmap(inputFile, noAlpha) :
                    inputFile.EndsWith(".ltct") ? OpenImageFromXOR(inputFile, noAlpha) :
                    SafeLoad(inputFile, noAlpha);
                    succeeded = true;
                    return bitmap;
                }
                catch
                {
                    try
                    {
                        Bitmap bitmap =
                        inputFile.EndsWith(".tex") ? TexToBitmap(inputFile, noAlpha) :
                        inputFile.EndsWith(".dds") ? DDSToBitmap(inputFile, noAlpha) :
                        inputFile.EndsWith(".ltct") ? OpenImageFromXOR(inputFile, noAlpha) :
                        SafeLoad(inputFile, noAlpha);
                        succeeded = true;
                        return bitmap;
                    }
                    catch
                    {
                        Thread.Sleep(1000);
                    }
                }
            }
            return new Bitmap(4096, 4096);
        }
        public static Bitmap SafeLoad(string path, bool noAlpha = false)
        {
            while (IsFileLocked(path))
            {
                Thread.Sleep(1000);
            }
            MemoryStream memoryStream = new MemoryStream();
            using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fileStream.CopyTo(memoryStream);
            }
            memoryStream.Position = 0;
            var newImage = SixLabors.ImageSharp.Image.Load<Rgba32>(memoryStream);
            return ImageSharpToBitmap(newImage, noAlpha);
        }
        public static Bitmap Clone(Bitmap bitmap, Rectangle rectangle)
        {
            var newImage = BitmapToImageSharp(bitmap);
            newImage.Mutate(i => i.Crop(new SixLabors.ImageSharp.Rectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height)));
            return ImageSharpToBitmap(newImage.Clone());
        }
        public static Bitmap Resize(Bitmap bitmap, int width, int height)
        {
            var newImage = BitmapToImageSharp(bitmap);
            newImage.Mutate(i => i.Resize(width, height));
            return ImageSharpToBitmap(newImage.Clone());
        }

        public static Image<Rgba32> BitmapToImageSharp(Bitmap bitmap)
        {
            lock (bitmap)
            {
                if (bitmap != null)
                {
                    var newImage = new SixLabors.ImageSharp.Image<Rgba32>(bitmap.Width, bitmap.Height);
                    LockBitmap startingData = new LockBitmap(bitmap);
                    startingData.LockBits();
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            var rgbPixel = startingData.GetPixel(x, y);
                            newImage[x, y] = new Rgba32(rgbPixel.R, rgbPixel.G, rgbPixel.B, rgbPixel.A);
                        }
                    };
                    startingData.UnlockBits();
                    return newImage;
                }
                else
                {
                    return BitmapToImageSharp(new Bitmap(1, 1));
                }
            }
        }
        public static Bitmap ImageSharpToBitmap(Image<Rgba32> newImage, bool noAlpha = false)
        {
            Bitmap canvas = new Bitmap(newImage.Width, newImage.Height, PixelFormat.Format32bppArgb);
            LockBitmap destination = new LockBitmap(canvas);
            destination.LockBits();
            for (int y = 0; y < canvas.Height; y++)
            {
                for (int x = 0; x < canvas.Width; x++)
                {
                    var rgbPixel = newImage[x, y];
                    Color col = Color.FromArgb(noAlpha ? 255 : rgbPixel.A, rgbPixel.R, rgbPixel.G, rgbPixel.B);
                    destination.SetPixel(x, y, col);
                }
            };
            destination.UnlockBits();
            return canvas;
        }
        public static void SaveBitmap(Bitmap bitmap, string path)
        {
            var newImage = BitmapToImageSharp(bitmap);
            var encoder = new SixLabors.ImageSharp.Formats.Png.PngEncoder()
            {
                TransparentColorMode = SixLabors.ImageSharp.Formats.Png.PngTransparentColorMode.Preserve,
                ColorType = SixLabors.ImageSharp.Formats.Png.PngColorType.RgbWithAlpha,
            };
            while (TexIO.IsFileLocked(path))
            {
                Thread.Sleep(100);
            }
            newImage.SaveAsPng(path, encoder);
        }

        public static void SaveBitmap(Bitmap bitmap, Stream stream)
        {
            var newImage = BitmapToImageSharp(bitmap);
            var encoder = new SixLabors.ImageSharp.Formats.Png.PngEncoder()
            {
                TransparentColorMode = SixLabors.ImageSharp.Formats.Png.PngTransparentColorMode.Preserve,
                ColorType = SixLabors.ImageSharp.Formats.Png.PngColorType.RgbWithAlpha,
            };
            newImage.SaveAsPng(stream, encoder);
        }
        public static Bitmap NewBitmap(Bitmap bitmap, bool noAlpha = false)
        {
            Bitmap destinationBitmap = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
            LockBitmap startingData = new LockBitmap(bitmap);
            LockBitmap destination = new LockBitmap(destinationBitmap);
            startingData.LockBits();
            destination.LockBits();
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var rgbPixel = startingData.GetPixel(x, y);
                    destination.SetPixel(x, y, Color.FromArgb(noAlpha ? 255 : rgbPixel.A, rgbPixel.R, rgbPixel.G, rgbPixel.B));
                }
            };
            startingData.UnlockBits();
            destination.UnlockBits();
            return destinationBitmap;
        }
        public static Bitmap NewBitmap(int width, int height)
        {
            Bitmap destinationBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            return destinationBitmap;
        }
        public static byte[] GetTexBytes(string inputFile)
        {
            byte[] data = new byte[0];
            KeyValuePair<Size, byte[]> keyValuePair = ResolveImageBytes(inputFile);
            PenumbraTextureImporter.RgbaBytesToTex(keyValuePair.Value, keyValuePair.Key.Width, keyValuePair.Key.Height, out data);
            return data;
        }
        public static void ObfuscateOrDeobfuscate(byte[] blob)
        {
            for (int i = 0; i < blob.Length; ++i)
            {
                blob[i] ^= 0x2A;
            }
        }

        public static void WriteImageToXOR(Bitmap data, string filename)
        {
            MemoryStream memoryStream = new MemoryStream();
            data.Save(memoryStream, ImageFormat.Png);
            byte[] bytes = memoryStream.ToArray();
            ObfuscateOrDeobfuscate(bytes);
            File.WriteAllBytes(filename, bytes);
        }
        public static void WriteImageToXOR(string input, string filename)
        {
            byte[] bytes = File.ReadAllBytes(input);
            ObfuscateOrDeobfuscate(bytes);
            File.WriteAllBytes(filename, bytes);
        }

        public static Bitmap OpenImageFromXOR(string filename, bool noAlpha = false)
        {
            byte[] file = File.ReadAllBytes(filename);
            ObfuscateOrDeobfuscate(file);
            MemoryStream memoryStream = new MemoryStream(file);
            return ImageSharpToBitmap(SixLabors.ImageSharp.Image.Load<Rgba32>(memoryStream), noAlpha);
        }

        public static void ConvertToLtct(string rootDirectory)
        {
            foreach (string file in Directory.GetFiles(rootDirectory))
            {
                if (file.EndsWith(".tex") || file.EndsWith(".png") || file.EndsWith(".bmp") || file.EndsWith(".dds"))
                {
                    WriteImageToXOR(ResolveBitmap(file), file
                        .Replace(".tex", ".ltct")
                        .Replace(".png", ".ltct")
                        .Replace(".bmp", ".ltct")
                        .Replace(".dds", ".ltct"));
                }
            }
            foreach (string directory in Directory.GetDirectories(rootDirectory))
            {
                ConvertToLtct(directory);
            }
        }

        public static void ConvertPngToLtct(string rootDirectory)
        {
            foreach (string file in Directory.GetFiles(rootDirectory))
            {
                if (file.EndsWith(".png"))
                {
                    WriteImageToXOR(file, file.Replace(".png", ".ltct"));
                }
            }
            foreach (string directory in Directory.GetDirectories(rootDirectory))
            {
                ConvertPngToLtct(directory);
            }
        }
        public static void ConvertLtctToPng(string rootDirectory)
        {
            foreach (string file in Directory.GetFiles(rootDirectory))
            {
                if (file.EndsWith(".ltct"))
                {
                    OpenImageFromXOR(file).Save(file.Replace(".ltct", ".png"));
                }
            }
            foreach (string directory in Directory.GetDirectories(rootDirectory))
            {
                ConvertLtctToPng(directory);
            }
        }

        public static void RunOptiPNG(string rootDirectory)
        {
            foreach (string file in Directory.GetFiles(rootDirectory))
            {
                if (file.EndsWith(".png"))
                {
                    ProcessStartInfo info = new ProcessStartInfo
                    {
                        FileName = Path.Combine(GlobalPathStorage.OriginalBaseDirectory, "optipng.exe"),
                        Arguments = "-clobber " + @"""" + file + @"""",
                        WorkingDirectory = rootDirectory,
                        UseShellExecute = false
                    };
                    info.RedirectStandardOutput = true;
                    Process process = new Process();
                    process.StartInfo = info;
                    process.OutputDataReceived += Process_OutputDataReceived;
                    process.Start();
                }
            }
            foreach (string directory in Directory.GetDirectories(rootDirectory))
            {
                RunOptiPNG(directory);
            }
        }

        private static void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {

        }

        public static KeyValuePair<Size, byte[]> ResolveImageBytes(string inputFile)
        {
            KeyValuePair<Size, byte[]> data = new KeyValuePair<Size, byte[]>(new Size(1, 1), new byte[4]);
            if (!string.IsNullOrEmpty(inputFile) && File.Exists(inputFile))
            {
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                while (IsFileLocked(inputFile))
                {
                    Thread.Sleep(10);
                }
                data = inputFile.EndsWith(".tex") ?
                    TexToBytes(inputFile) : (inputFile.EndsWith(".dds") ?
                    DDSToBytes(inputFile) : PngToBytes(inputFile));
            }
            return data;
        }

        public static bool IsFileLocked(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    using (FileStream stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        stream.Close();
                    }
                }
            }
            catch (IOException)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }

            //file is not locked
            return false;
        }
    }
}
