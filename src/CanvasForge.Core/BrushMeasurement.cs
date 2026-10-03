namespace CanvasForge.Core;

public readonly record struct BrushMeasurement(int OuterDiameter, int InnerDiameter)
{
    public ScreenPoint SeedOffset { get; init; }
    public int ChangedPixels { get; init; }
    public static BrushMeasurement Read(PixelImage before, PixelImage after, ScreenPoint centre)
    {
        if (before.Width != after.Width || before.Height != after.Height || centre.X < 0 || centre.Y < 0
            || centre.X >= before.Width || centre.Y >= before.Height)
            throw new ArgumentException("Invalid brush measurement images.");
        int w = before.Width, h = before.Height, centreIndex = centre.Y * w + centre.X;
        bool Changed(int i) => RustSlider.Delta(before.Color(i), after.Color(i)) > 12;
        // Rust rasterises tiny brush stamps just beside the click on some signs.
        // The user's Size 1 test changed only four pixels at (-1,+1)..(0,+2).
        // Locate the closest changed pixel in a bounded 4px neighbourhood.
        int seed = -1, distance = int.MaxValue;
        for (int y = Math.Max(0, centre.Y - 4); y <= Math.Min(h - 1, centre.Y + 4); y++)
            for (int x = Math.Max(0, centre.X - 4); x <= Math.Min(w - 1, centre.X + 4); x++)
            {
                int d = (x - centre.X) * (x - centre.X) + (y - centre.Y) * (y - centre.Y);
                if (d < distance && Changed(y * w + x)) { seed = y * w + x; distance = d; }
            }
        if (seed < 0) throw new InvalidOperationException("Не видно тестової крапки біля точки кліку. Очисти Canvas і перевір, що вибрано пензель.");
        // Only the connected painted dot matters; unrelated UI/scene changes do not enlarge it.
        var painted = new bool[w * h];
        var queue = new Queue<int>(); queue.Enqueue(seed); painted[seed] = true;
        int outer = 0, count = 0;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w;
            count++;
            if (x < 2 || y < 2 || x >= w - 2 || y >= h - 2)
                throw new InvalidOperationException("Тестова крапка виходить за область вимірювання. Захопи лише Canvas і повтори калібрування.");
            outer = Math.Max(outer, Math.Max(Math.Abs(x - centre.X), Math.Abs(y - centre.Y)));
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int next = (y + dy) * w + x + dx;
                if (!painted[next] && Changed(next)) { painted[next] = true; queue.Enqueue(next); }
            }
        }
        int inner = 0;
        // Keep radii relative to the commanded click, including any offset.
        // A missing central pixel must never count as solid interior coverage.
        for (int radius = 1; painted[centreIndex] && radius <= outer; radius++)
        {
            if (centre.X - radius < 0 || centre.Y - radius < 0 || centre.X + radius >= w || centre.Y + radius >= h) break;
            bool full = true;
            for (int k = -radius; k <= radius; k++)
                full &= painted[(centre.Y - radius) * w + centre.X + k] && painted[(centre.Y + radius) * w + centre.X + k]
                    && painted[(centre.Y + k) * w + centre.X - radius] && painted[(centre.Y + k) * w + centre.X + radius];
            if (!full) break;
            inner = radius;
        }
        return new(outer * 2 + 1, inner * 2 + 1)
            { SeedOffset = new(seed % w - centre.X, seed / w - centre.Y), ChangedPixels = count };
    }
}
