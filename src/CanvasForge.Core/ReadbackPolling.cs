namespace CanvasForge.Core;

internal static class ReadbackPolling
{
    // Preserve the quick first read. A missing copy gets one second of polling
    // before any new field selection or clipboard marker is emitted. Slower
    // clients can otherwise return the previous Ctrl+C during the next edit.
    internal static double Delay(int poll)=>poll<=6?.025:.05;
}
