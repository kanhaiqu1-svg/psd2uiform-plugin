#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    // A range grid on render-space means, not a perceptual identity hash. Neighbour cells
    // exhaustively cover MeanRadius; low-information artwork may deliberately collide.
    internal sealed class PsdImageCandidateIndex
    {
        internal const int MeanRadius = 6;
        private const int CellSize = 16, Side = 16;

        internal sealed class Feature
        {
            internal readonly Color32[] samples = new Color32[Side * Side];
            internal Vector4 mean;

            internal Feature(PsdImagePixels image, Vector4 border, int displaySize)
            {
                if (displaySize < 0)
                {
                    // Physical corners plus palette extrema survive alternative automatic
                    // stretch-band choices. This third index is queried with the two render
                    // views, not a scan after a failed lookup.
                    mean = image.paletteMidpoint;
                    float span = Math.Min(16, Math.Min(image.width, image.height) * .5f);
                    for (int y = 0; y < Side; y++)
                        for (int x = 0; x < Side; x++)
                        {
                            float px = (x % 8 + .5f) * span / 8, py = (y % 8 + .5f) * span / 8;
                            if (x >= 8) px = image.width - span + px;
                            if (y >= 8) py = image.height - span + py;
                            samples[y * Side + x] = image.Sample(px - .5f, py - .5f);
                        }
                    return;
                }
                float w = displaySize == 0 ? image.width : displaySize, h = displaySize == 0 ? image.height : displaySize;
                for (int y = 0; y < Side; y++)
                    for (int x = 0; x < Side; x++)
                    {
                        // Four stratified samples per cell. This is a cheap descriptor, not
                        // an area integral and never evidence sufficient to approve reuse.
                        Vector4 sum = Vector4.zero;
                        for (int sy = 0; sy < 2; sy++)
                            for (int sx = 0; sx < 2; sx++)
                                sum += PsdImageComparer.Vector(PsdImageComparer.SliceSample(image, border,
                                    (x + .25f + .5f * sx) * w / Side, (y + .25f + .5f * sy) * h / Side, w, h));
                        sum *= .25f;
                        samples[y * Side + x] = new Color32((byte)Mathf.RoundToInt(sum.x), (byte)Mathf.RoundToInt(sum.y), (byte)Mathf.RoundToInt(sum.z), (byte)Mathf.RoundToInt(sum.w));
                        mean += sum;
                    }
                mean /= samples.Length;
            }
        }

        private readonly Dictionary<string, Dictionary<int, List<int>>> contracts = new Dictionary<string, Dictionary<int, List<int>>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<int>> content = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<ulong, List<int>>> normalized = new Dictionary<string, Dictionary<ulong, List<int>>>(StringComparer.Ordinal);
        private readonly List<Feature[]> features = new List<Feature[]>();
        private readonly HashSet<int> seen = new HashSet<int>();
        internal int BucketVisits, Candidates;

        internal static Feature[] Describe(PsdImagePixels image, Vector4 border, bool sliced)
            => sliced ? new[] { new Feature(image, border, 256), new Feature(image, border, 512), new Feature(image, border, -1) } : new[] { new Feature(image, Vector4.zero, 0) };

        internal void Add(int id, string contract, string hash, ulong pixelHash, Feature[] views)
        {
            features.Add(views); // IDs are the stable, contiguous master-group indices.
            string key = contract + ":" + hash;
            if (!content.TryGetValue(key, out var exact)) content.Add(key, exact = new List<int>());
            exact.Add(id);
            if (!normalized.TryGetValue(contract, out var pixels)) normalized.Add(contract, pixels = new Dictionary<ulong, List<int>>());
            if (!pixels.TryGetValue(pixelHash, out var samePixels)) pixels.Add(pixelHash, samePixels = new List<int>());
            samePixels.Add(id);
            if (!contracts.TryGetValue(contract, out var grid)) contracts.Add(contract, grid = new Dictionary<int, List<int>>());
            for (int view = 0; view < views.Length; view++)
            {
                int cell = Cell(views[view].mean, view);
                if (!grid.TryGetValue(cell, out var bucket)) grid.Add(cell, bucket = new List<int>());
                bucket.Add(id);
            }
        }

        internal void Query(string contract, string hash, ulong pixelHash, Feature[] views, List<int> result)
        {
            result.Clear(); seen.Clear();
            if (content.TryGetValue(contract + ":" + hash, out var exact))
                for (int i = 0; i < exact.Count; i++) if (seen.Add(exact[i])) result.Add(exact[i]);
            if (normalized.TryGetValue(contract, out var pixels) && pixels.TryGetValue(pixelHash, out var samePixels))
                for (int i = 0; i < samePixels.Count; i++) if (seen.Add(samePixels[i])) result.Add(samePixels[i]);
            if (contracts.TryGetValue(contract, out var grid))
                for (int view = 0; view < views.Length; view++)
                {
                    var mean = views[view].mean;
                    int r = (int)mean.x / CellSize, g = (int)mean.y / CellSize, b = (int)mean.z / CellSize, a = (int)mean.w / CellSize;
                    for (int da = Math.Max(0, a - 1); da <= Math.Min(15, a + 1); da++)
                        for (int db = Math.Max(0, b - 1); db <= Math.Min(15, b + 1); db++)
                            for (int dg = Math.Max(0, g - 1); dg <= Math.Min(15, g + 1); dg++)
                                for (int dr = Math.Max(0, r - 1); dr <= Math.Min(15, r + 1); dr++)
                                {
                                    if (!grid.TryGetValue(dr | dg << 4 | db << 8 | da << 12 | view << 16, out var bucket)) continue;
                                    BucketVisits += bucket.Count;
                                    for (int i = 0; i < bucket.Count; i++)
                                    {
                                        int id = bucket[i];
                                        if (seen.Contains(id) || !Compatible(views[view], features[id][view])) continue;
                                        seen.Add(id); result.Add(id);
                                    }
                                }
                }
            // Preserve the existing master preference, independent of hash/dictionary order.
            result.Sort(); Candidates += result.Count;
        }

        internal static bool Compatible(Feature a, Feature b)
        {
            if (PsdImageComparer.Max(a.mean - b.mean) > MeanRadius) return false;
            int total = 0;
            for (int i = 0; i < a.samples.Length; i++) total += PsdImageComparer.Delta(a.samples[i], b.samples[i]);
            return total <= a.samples.Length * 12;
        }

        private static int Cell(Vector4 mean, int view) => (int)mean.x / CellSize | (int)mean.y / CellSize << 4
            | (int)mean.z / CellSize << 8 | (int)mean.w / CellSize << 12 | view << 16;
    }
}
#endif
