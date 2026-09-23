using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Serilog;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Bitmap = System.Drawing.Bitmap;
using Graphics = System.Drawing.Graphics;

namespace AnalyseTool.Tools.Underlays
{
    /// <summary>Renders a PDF page or raster image placed in the model to a PNG — the picture a
    /// multimodal model needs to read a drawing that has no vector geometry in Revit.</summary>
    public sealed class UnderlayImageService
    {
        public const int DefaultMaxPixels = 1568; // what vision models downscale to anyway: more is paid for, not seen
        public const int MinMaxPixels = 256;
        public const int MaxMaxPixels = 4096;

        /// <param name="doc">The document.</param>
        /// <param name="id">An ImageInstance id, or an ImageType id.</param>
        /// <param name="maxPixels">Longest edge of the PNG, clamped to 256-4096.</param>
        /// <param name="outputFolder">Where the PNG is also written; defaults to %TEMP%\AnalyseTool\underlays.</param>
        /// <param name="linkInstanceId">The Revit link the image lives in, when it does (GetUnderlays says so).</param>
        public PageImageResult Render(Document doc, long id, int? maxPixels, string? outputFolder = null, long? linkInstanceId = null)
        {
            Document? inDocument = UnderlayReader.GetSourceDocument(doc, linkInstanceId, out string? linkError);
            if (inDocument is null) return new PageImageResult { Id = id, Error = linkError };
            doc = inDocument;

            Element? element = doc.GetElement(new ElementId(id));
            ImageType? type = element switch
            {
                ImageInstance instance => doc.GetElement(instance.GetTypeId()) as ImageType,
                ImageType t => t,
                _ => null,
            };
            if (type is null)
            {
                string what = element switch
                {
                    null => $"No element with id {id}.",
                    ImportInstance => $"Element {id} is a CAD import, which has no raster page — use GetCadGeometry or GetCadLayers.",
                    _ => $"Element {id} is a {element.GetType().Name}, not a PDF/raster image.",
                };
                return new PageImageResult { Id = id, Error = what + " GetUnderlays lists the images and their ids." };
            }

            string path = SafeRead(() => type.Path) ?? string.Empty;
            string name = path.Length > 0 ? Path.GetFileName(path) : type.Name;
            bool isPdf = string.Equals(Path.GetExtension(path.Length > 0 ? path : name), ".pdf", StringComparison.OrdinalIgnoreCase);
            int? page = isPdf && SafeRead<int?>(() => type.PageNumber) is > 0 and var p ? p : null;

            Bitmap? source = SafeRead(() => type.GetImage());
            if (source is null)
                return new PageImageResult
                {
                    Id = id, Name = name, Kind = isPdf ? "pdf" : "image", PageNumber = page,
                    Error = $"Revit has no picture for '{name}': the file is not loaded ({SafeRead<ImageTypeStatus?>(() => type.Status)}). Reload it in Manage Links.",
                };

            using (source)
            {
                int longest = Math.Clamp(maxPixels ?? DefaultMaxPixels, MinMaxPixels, MaxMaxPixels);
                double factor = Math.Min(1.0, (double)longest / Math.Max(source.Width, source.Height));
                int width = Math.Max(1, (int)Math.Round(source.Width * factor));
                int height = Math.Max(1, (int)Math.Round(source.Height * factor));

                byte[] png = EncodePng(source, width, height);
                string? file = TryWrite(png, outputFolder, name, id, page);

                return new PageImageResult
                {
                    Id = id,
                    Name = name,
                    Kind = isPdf ? "pdf" : "image",
                    PageNumber = page,
                    Width = width,
                    Height = height,
                    SourceWidth = source.Width,
                    SourceHeight = source.Height,
                    File = file,
                    Image = new ImageAttachment { MimeType = "image/png", Data = Convert.ToBase64String(png) },
                };
            }
        }

        private static byte[] EncodePng(Bitmap source, int width, int height)
        {
            // Drawn onto white: a PDF page rasterised with a transparent background reads as black in
            // viewers that ignore alpha, and a drawing is black lines on white paper.
            using Bitmap target = new(width, height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(target))
            {
                g.Clear(System.Drawing.Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(source, 0, 0, width, height);
            }
            using MemoryStream stream = new();
            target.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }

        private static string? TryWrite(byte[] png, string? outputFolder, string name, long id, int? page)
        {
            try
            {
                string folder = outputFolder ?? Path.Combine(Path.GetTempPath(), "AnalyseTool", "underlays");
                Directory.CreateDirectory(folder);
                string stem = string.Concat(Path.GetFileNameWithoutExtension(name).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                string file = Path.Combine(folder, $"{stem}-{id}{(page is { } p ? $"-p{p}" : string.Empty)}.png");
                File.WriteAllBytes(file, png);
                return file;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The picture itself is the answer; the file is a convenience for clients that read files.
                Log.Warning(ex, "GetPdfPageAsImage: could not write the PNG for {Name}", name);
                return null;
            }
        }

        private static T SafeRead<T>(Func<T> read)
        {
            try { return read(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return default!; }
            catch (InvalidOperationException) { return default!; }
        }
    }

    /// <summary>What GetPdfPageAsImage answers.</summary>
    public sealed record PageImageResult
    {
        [JsonProperty("id")]
        [System.ComponentModel.Description("The id that was asked for.")]
        public long Id { get; init; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.Description("File name of the image or PDF.")]
        public string? Name { get; init; }

        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.Description("\"pdf\" or \"image\".")]
        public string? Kind { get; init; }

        [JsonProperty("pageNumber", NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.Description("PDF: the page shown.")]
        public int? PageNumber { get; init; }

        [JsonProperty("width")]
        [System.ComponentModel.Description("Width of the returned PNG in pixels.")]
        public int Width { get; init; }

        [JsonProperty("height")]
        [System.ComponentModel.Description("Height of the returned PNG in pixels.")]
        public int Height { get; init; }

        [JsonProperty("sourceWidth")]
        [System.ComponentModel.Description("Width of Revit's raster of the page before downscaling.")]
        public int SourceWidth { get; init; }

        [JsonProperty("sourceHeight")]
        [System.ComponentModel.Description("Height of Revit's raster of the page before downscaling.")]
        public int SourceHeight { get; init; }

        [JsonProperty("file", NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.Description("Path of the PNG written on the Revit machine, for clients that read files.")]
        public string? File { get; init; }

        [JsonProperty("image", NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.Description("The PNG. Over MCP it arrives as an image content block and 'data' is left out of the text.")]
        public ImageAttachment? Image { get; init; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.Description("Set when there is no picture to show (unknown id, a CAD import, an unloaded file).")]
        public string? Error { get; init; }
    }

    /// <summary>An image riding in a command's answer. The MCP server turns a top-level <c>image</c>
    /// property of this shape into an image content block (McpWire.ImageAttachment), so any command —
    /// an extension's too — can hand a picture to a multimodal client by returning one.</summary>
    public sealed record ImageAttachment
    {
        [JsonProperty("mimeType")]
        [System.ComponentModel.Description("\"image/png\".")]
        public string MimeType { get; init; } = "image/png";

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.Description("Base64 of the image. Absent in MCP text and structured content, where the image travels as its own content block.")]
        public string? Data { get; init; }
    }
}
