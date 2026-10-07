namespace CanvasForge.Core;

internal static class ReadbackPolling
{
    internal const int Attempts=3;
    // Preserve the quick first read. A missing copy gets one second of polling
    // before any new field selection or clipboard marker is emitted. Slower
    // clients can otherwise return the previous Ctrl+C during the next edit.
    internal const int Polls=24;
    internal static double Delay(int poll)=>poll<=6?.025:.05;
}
