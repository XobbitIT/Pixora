namespace CanvasForge.Core;

public readonly record struct BrushMeasurement(int OuterDiameter, int InnerDiameter)
{
    public static BrushMeasurement Read(PixelImage before, PixelImage after, ScreenPoint centre)
    {
        if (before.Width != after.Width || before.Height != after.Height || centre.X < 0 || centre.Y < 0
            || centre.X >= before.Width || centre.Y >= before.Height)
            throw new ArgumentException("Invalid brush measurement images.");
        int w = before.Width, h = before.Height, seed = centre.Y * w + centre.X;
        bool Changed(int i) => RustSlider.Delta(before.Color(i), after.Color(i)) > 12;
        if (!Changed(seed)) throw new InvalidOperationException("Не видно тестової крапки в центрі. Очисти Canvas і перевір, що вибрано пензель.");
        // Only the connected painted dot matters; unrelated UI/scene changes do not enlarge it.
        var painted = new bool[w * h];
        var queue = new Queue<int>(); queue.Enqueue(seed); painted[seed] = true;
        int outer = 0;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w;
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
        for (int radius = 1; radius <= outer; radius++)
        {
            if (centre.X - radius < 0 || centre.Y - radius < 0 || centre.X + radius >= w || centre.Y + radius >= h) break;
            bool full = true;
            for (int k = -radius; k <= radius; k++)
                full &= painted[(centre.Y - radius) * w + centre.X + k] && painted[(centre.Y + radius) * w + centre.X + k]
                    && painted[(centre.Y + k) * w + centre.X - radius] && painted[(centre.Y + k) * w + centre.X + radius];
            if (!full) break;
            inner = radius;
        }
        return new(outer * 2 + 1, inner * 2 + 1);
    }
}
