#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    // Owns the decoded array. Keep straight RGBA: GPU filtering precedes alpha blending.
    internal sealed class PsdImagePixels
    {
        internal readonly int width, height;
        internal Color32[] pixels;
        internal readonly bool nearest;
        internal readonly ulong pixelHash;
        internal readonly Vector4 paletteMidpoint;
        internal Vector4 border;
        internal static int DecodeCount;

        internal PsdImagePixels(int width, int height, Color32[] ownedPixels, bool nearest = false)
        {
            this.width = width; this.height = height; this.nearest = nearest;
            pixels = ownedPixels;
            ulong hash = 14695981039346656037UL;
            var minimum = new Color32(255,255,255,255); var maximum = new Color32(0,0,0,0);
            unchecked
            {
                hash = (hash ^ (uint)width) * 1099511628211UL;
                hash = (hash ^ (uint)height) * 1099511628211UL;
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    if (p.a != 0)
                    {
                        minimum.r = Math.Min(minimum.r,p.r); minimum.g = Math.Min(minimum.g,p.g); minimum.b = Math.Min(minimum.b,p.b); minimum.a = Math.Min(minimum.a,p.a);
                        maximum.r = Math.Max(maximum.r,p.r); maximum.g = Math.Max(maximum.g,p.g); maximum.b = Math.Max(maximum.b,p.b); maximum.a = Math.Max(maximum.a,p.a);
                    }
                    // Hidden RGB is irrelevant to native pixels. The source array is retained
                    // intact because filtering transparent texels can make that RGB visible.
                    uint rgb = p.a == 0 ? 0u : (uint)(p.r | p.g << 8 | p.b << 16);
                    hash = (hash ^ (rgb | (uint)p.a << 24)) * 1099511628211UL;
                }
            }
            pixelHash = hash;
            paletteMidpoint = maximum.a == 0 ? Vector4.zero : (PsdImageComparer.Vector(minimum) + PsdImageComparer.Vector(maximum)) * .5f;
        }

        internal static PsdImagePixels Read(string path, bool sliced = false, bool nearest = false)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                DecodeCount++;
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false)) throw new InvalidDataException("无法读取图片: " + path);
                return new PsdImagePixels(texture.width, texture.height, texture.GetPixels32(), nearest) {
                    border = sliced ? UGUIParser.CalculateTexture9SliceBorder(texture) : Vector4.zero };
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        internal Color32 Sample(float x, float y)
        {
            x = Mathf.Clamp(x, 0, width - 1); y = Mathf.Clamp(y, 0, height - 1);
            if (nearest) return Visible(pixels[Mathf.FloorToInt(y + .5f) * width + Mathf.FloorToInt(x + .5f)]);
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
            // One rounding after bilinear interpolation, then premultiplication.
            float tx = x - x0, ty = y - y0;
            var a = pixels[y0 * width + x0]; var b = pixels[y0 * width + x1];
            var c = pixels[y1 * width + x0]; var d = pixels[y1 * width + x1];
            float wa = (1 - tx) * (1 - ty), wb = tx * (1 - ty), wc = (1 - tx) * ty, wd = tx * ty;
            float alpha = a.a * wa + b.a * wb + c.a * wc + d.a * wd, opacity = alpha / 255;
            return new Color32((byte)Mathf.RoundToInt((a.r * wa + b.r * wb + c.r * wc + d.r * wd) * opacity),
                (byte)Mathf.RoundToInt((a.g * wa + b.g * wb + c.g * wc + d.g * wd) * opacity),
                (byte)Mathf.RoundToInt((a.b * wa + b.b * wb + c.b * wc + d.b * wd) * opacity), (byte)Mathf.RoundToInt(alpha));
        }

        internal static Color32 Visible(Color32 p) => new Color32((byte)((p.r * p.a + 127) / 255),
            (byte)((p.g * p.a + 127) / 255), (byte)((p.b * p.a + 127) / 255), p.a);
    }

    internal static class PsdImageComparer
    {
        internal enum Match { Different, Exact, EdgeTolerance, Scaled, Sliced }
        private const int Tile = 16, Halo = 5, Stride = Tile + 2 * Halo;
        // Editor export is synchronous on the main thread. Reuse bounded scratch, never a
        // full scaled image. Two source tiles, four filtered tiles, one separable workspace.
        private static readonly Color32[] First = new Color32[Stride * Stride], Second = new Color32[Stride * Stride];
        private static readonly Vector4[] A2 = new Vector4[Stride * Stride], B2 = new Vector4[Stride * Stride],
            A4 = new Vector4[Stride * Stride], B4 = new Vector4[Stride * Stride], Scratch = new Vector4[Stride * Stride];
        private static readonly int[] Kernel2 = { 1, 4, 6, 4, 1 }, Kernel4 = { 1, 8, 28, 56, 70, 56, 28, 8, 1 };
        internal const int ScratchBytes = Stride * Stride * (4 * 2 + 16 * 5);
        internal static int ComparisonCount;

        internal static Match Compare(PsdImagePixels master, PsdImagePixels member, Vector4 masterBorder, Vector4 memberBorder, bool sliced, bool preserveMemberTransform, PsdImageReuseTolerance tolerance)
        {
            ComparisonCount++;
            if (master.nearest != member.nearest) return Match.Different;
            if (sliced)
            {
                // An inferred stretch band is not a designer-authored transform. Reconstruct
                // the member's raster; actual bindings are checked separately by the batch.
                bool equal = preserveMemberTransform ? EquivalentSliceTransforms(master, member, masterBorder, memberBorder, tolerance)
                    : SlicedAt(master, member, masterBorder, Vector4.zero, member.width, member.height, true, tolerance);
                return equal ? Match.Sliced : Match.Different;
            }
            if (master.width == member.width && master.height == member.height)
                return CompareView(master, member, Vector4.zero, Vector4.zero, member.width, member.height, true, tolerance);
            if (master.width < member.width || master.height < member.height
                || Math.Abs(member.width - member.height * (float)master.width / master.height) > 1.5f) return Match.Different;
            return CompareView(master, member, Vector4.zero, Vector4.zero, member.width, member.height, true, tolerance) != Match.Different ? Match.Scaled : Match.Different;
        }

        // Authored borders must preserve their transform beyond the current design size.
        internal static bool EquivalentSliceTransforms(PsdImagePixels a, PsdImagePixels b, Vector4 borderA, Vector4 borderB, PsdImageReuseTolerance tolerance)
        {
            if (!SlicedAt(a, b, borderA, borderB, a.width, a.height, true, tolerance)
                || !SlicedAt(a, b, borderA, borderB, b.width, b.height, true, tolerance)) return false;
            int w = Math.Max(a.width, b.width), h = Math.Max(a.height, b.height);
            int wide = w + Math.Max(8, w / 2), tall = h + Math.Max(8, h / 2);
            return SlicedAt(a, b, borderA, borderB, wide, h, true, tolerance)
                && SlicedAt(a, b, borderA, borderB, w, tall, true, tolerance)
                && SlicedAt(a, b, borderA, borderB, wide, tall, true, tolerance)
                && SlicedAt(a, b, borderA, borderB,
                    Math.Max(1, Mathf.CeilToInt(Mathf.Max(borderA.x + borderA.z, borderB.x + borderB.z) * .75f)),
                    Math.Max(1, Mathf.CeilToInt(Mathf.Max(borderA.y + borderA.w, borderB.y + borderB.w) * .75f)), true, tolerance);
        }

        internal static bool SlicedAt(PsdImagePixels a, PsdImagePixels b, Vector4 borderA, Vector4 borderB, float width, float height, bool fillCenter = true, PsdImageReuseTolerance tolerance = PsdImageReuseTolerance.Visual)
            => CompareView(a, b, borderA, borderB, width, height, fillCenter, tolerance) != Match.Different;

        private static Match CompareView(PsdImagePixels a, PsdImagePixels b, Vector4 borderA, Vector4 borderB, float width, float height, bool fillCenter, PsdImageReuseTolerance tolerance)
        {
            int w = Mathf.CeilToInt(width), h = Mathf.CeilToInt(height);
            bool resampled = width != a.width || height != a.height || width != b.width || height != b.height;
            int quantization = tolerance == PsdImageReuseTolerance.Relaxed ? 4 : 2;
            int minimumEdge = tolerance == PsdImageReuseTolerance.Strict && !resampled ? 8 : 2;
            float biasLimit = tolerance == PsdImageReuseTolerance.Strict ? .5f : tolerance == PsdImageReuseTolerance.Visual ? 1f : 2f;
            float color2 = resampled ? 1.25f : .55f, color4 = resampled ? 1.15f : .5f;
            float gradient2 = resampled ? .75f : .5f, gradient4 = resampled ? .65f : .4f;
            if (tolerance == PsdImageReuseTolerance.Visual) { color2 *= 1.1f; color4 *= 1.1f; }
            if (tolerance == PsdImageReuseTolerance.Relaxed) { color2 *= 1.4f; color4 *= 1.4f; gradient2 *= 1.2f; gradient4 *= 1.2f; }
            bool exact = true;
            Vector4 bias = Vector4.zero;
            for (int top = 0; top < h; top += Tile)
                for (int left = 0; left < w; left += Tile)
                {
                    int rows = Math.Min(Tile, h - top), columns = Math.Min(Tile, w - left);
                    bool detail = false;
                    for (int y = 0; y < rows; y++)
                        for (int x = 0; x < columns; x++)
                        {
                            var p = ViewSample(a, borderA, left + x, top + y, w, h, width, height, fillCenter);
                            var q = ViewSample(b, borderB, left + x, top + y, w, h, width, height, fillCenter);
                            int d = Delta(p, q);
                            if (d == 0) continue;
                            if (a.nearest) return Match.Different;
                            exact = false; bias += Vector(p) - Vector(q);
                            if (d > quantization) detail = true;
                        }
                    if (!detail) continue;
                    for (int y = 0; y < Stride; y++)
                        for (int x = 0; x < Stride; x++)
                        {
                            int px = left + x - Halo, py = top + y - Halo, i = y * Stride + x;
                            // Transparent outside the artwork; clamping here invents opaque
                            // geometry beyond the rectangle and biases edge measurements.
                            First[i] = ViewSample(a, borderA, px, py, w, h, width, height, fillCenter);
                            Second[i] = ViewSample(b, borderB, px, py, w, h, width, height, fillCenter);
                        }
                    bool filtered = false;
                    for (int y = Halo; y < Halo + rows; y++)
                        for (int x = Halo; x < Halo + columns; x++)
                        {
                            int i = y * Stride + x;
                            if (Delta(First[i], Second[i]) <= quantization) continue;
                            // A new mark on a flat area cannot acquire tolerance from blurring.
                            int px = left + x - Halo, py = top + y - Halo;
                            bool coverage = First[i].a != Second[i].a;
                            if (!HasEdge(First, i, px, py, w, h, minimumEdge, coverage) || !HasEdge(Second, i, px, py, w, h, minimumEdge, coverage)) return Match.Different;
                            if (!filtered)
                            {
                                Filter(First, A2, Kernel2, 16); Filter(Second, B2, Kernel2, 16);
                                Filter(First, A4, Kernel4, 256); Filter(Second, B4, Kernel4, 256);
                                filtered = true;
                            }
                            if (!LocalStructure(A2, B2, i, color2, gradient2, quantization)
                                || !LocalStructure(A4, B4, i, color4, gradient4, quantization)) return Match.Different;
                        }
                }
            // Reject systematic tint/opacity drift, including one-level changes over interiors.
            if (Max(bias) > (double)w * h * biasLimit) return Match.Different;
            return exact ? Match.Exact : Match.EdgeTolerance;
        }

        private static Color32 ViewSample(PsdImagePixels image, Vector4 border, int x, int y, int w, int h, float width, float height, bool fillCenter)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return default;
            if (fillCenter && width == image.width && height == image.height) return PsdImagePixels.Visible(image.pixels[y * image.width + x]);
            return SliceSample(image, border, (x + .5f) * width / w, (y + .5f) * height / h, width, height, fillCenter);
        }

        internal static Color32 SliceSample(PsdImagePixels image, Vector4 border, float x, float y, float width, float height, bool fillCenter = true)
        {
            // Image falls back to a simple quad when there is no border; fillCenter then has no effect.
            if (!fillCenter && border != Vector4.zero)
            {
                float sx = border.x + border.z > width ? width / (border.x + border.z) : 1;
                float sy = border.y + border.w > height ? height / (border.y + border.w) : 1;
                if (x >= border.x * sx && x < width - border.z * sx && y >= border.y * sy && y < height - border.w * sy) return default;
            }
            return image.Sample(SlicePosition(x, width, image.width, border.x, border.z), SlicePosition(y, height, image.height, border.y, border.w));
        }

        internal static float SlicePosition(float p, float targetSize, int sourceSize, float leading, float trailing)
        {
            float sum = leading + trailing, scale = sum > targetSize ? targetSize / sum : 1;
            float left = leading * scale, right = trailing * scale;
            if (p < left) return p / scale - .5f;
            if (p >= targetSize - right) return sourceSize - trailing + (p - targetSize + right) / scale - .5f;
            return leading + (p - left) * (sourceSize - sum) / (targetSize - left - right) - .5f;
        }

        private static bool HasEdge(Color32[] pixels, int index, int px, int py, int width, int height, int minimum, bool coverage)
        {
            var p = pixels[index];
            // Only alpha coverage can use the rectangle's outer silhouette. An opaque RGB
            // mark at a corner must still find an edge inside the actual artwork.
            if (coverage && (px == 0 || py == 0 || px == width - 1 || py == height - 1) && p.a >= minimum) return true;
            for (int y = Math.Max(-1, -py); y <= Math.Min(1, height - 1 - py); y++)
                for (int x = Math.Max(-1, -px); x <= Math.Min(1, width - 1 - px); x++)
                    if (Delta(p, pixels[index + y * Stride + x]) >= minimum) return true;
            return false;
        }

        private static void Filter(Color32[] input, Vector4[] output, int[] kernel, float divisor)
        {
            int radius = kernel.Length / 2;
            for (int y = 0; y < Stride; y++)
                for (int x = Halo - 1; x < Stride - Halo + 1; x++)
                {
                    Vector4 sum = Vector4.zero;
                    for (int k = -radius; k <= radius; k++) sum += Vector(input[y * Stride + x + k]) * kernel[k + radius];
                    Scratch[y * Stride + x] = sum / divisor;
                }
            for (int y = Halo - 1; y < Stride - Halo + 1; y++)
                for (int x = Halo - 1; x < Stride - Halo + 1; x++)
                {
                    Vector4 sum = Vector4.zero;
                    for (int k = -radius; k <= radius; k++) sum += Scratch[(y + k) * Stride + x] * kernel[k + radius];
                    output[y * Stride + x] = sum / divisor;
                }
        }

        private static bool LocalStructure(Vector4[] a, Vector4[] b, int i, float colorAllowance, float gradientAllowance, int quantization)
        {
            Vector4 ax = (a[i + 1] - a[i - 1]) * .5f, ay = (a[i + Stride] - a[i - Stride]) * .5f;
            Vector4 bx = (b[i + 1] - b[i - 1]) * .5f, by = (b[i + Stride] - b[i - Stride]) * .5f;
            for (int channel = 0; channel < 4; channel++)
            {
                float strength = Mathf.Max(Mathf.Abs(ax[channel]) + Mathf.Abs(ay[channel]), Mathf.Abs(bx[channel]) + Mathf.Abs(by[channel]));
                if (Mathf.Abs(a[i][channel] - b[i][channel]) > quantization + colorAllowance * strength
                    || Mathf.Abs(ax[channel] - bx[channel]) > quantization + gradientAllowance * strength
                    || Mathf.Abs(ay[channel] - by[channel]) > quantization + gradientAllowance * strength) return false;
            }
            return true;
        }

        // Cropping is lossless representation reduction, never a perceptual operation.
        internal static bool CanCollapse(Color32[] pixels, int width, int height, Vector4 border, bool horizontal)
        {
            int start = Mathf.RoundToInt(horizontal ? border.x : border.y);
            int end = (horizontal ? width : height) - Mathf.RoundToInt(horizontal ? border.z : border.w);
            int cross = horizontal ? height : width;
            for (int c = 0; c < cross; c++)
            {
                var first = pixels[horizontal ? c * width + start : start * width + c];
                for (int p = start + 1; p < end; p++)
                {
                    var next = pixels[horizontal ? c * width + p : p * width + c];
                    if (first.r != next.r || first.g != next.g || first.b != next.b || first.a != next.a) return false;
                }
            }
            return true;
        }

        internal static Vector4 Vector(Color32 p) => new Vector4(p.r, p.g, p.b, p.a);
        internal static float Max(Vector4 v) => Mathf.Max(Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y)), Mathf.Max(Mathf.Abs(v.z), Mathf.Abs(v.w)));
        internal static int Delta(Color32 a, Color32 b) => Math.Max(Math.Max(Math.Abs(a.r - b.r), Math.Abs(a.g - b.g)), Math.Max(Math.Abs(a.b - b.b), Math.Abs(a.a - b.a)));
    }
}
#endif
