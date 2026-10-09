namespace CanvasForge.Core;

public enum ControlLayoutState { Ready, DifferentMode, Missing }
public static class ControlLayout
{
    // A single unreadable field is not evidence of another palette layout.
    public static ControlLayoutState Inspect(IReadOnlyList<bool> expected,IReadOnlyList<bool> alternative,int distinctTracks)
    {
        if(expected.Count!=3||alternative.Count!=3)throw new ArgumentException("Three controls are required.");
        if(expected.All(x=>x))return ControlLayoutState.Ready;
        return alternative.All(x=>x)&&distinctTracks>=2?ControlLayoutState.DifferentMode:ControlLayoutState.Missing;
    }
    public const string PaletteMismatch="У Pixora вибрана палітра Rust, а керування гри відповідає HEX. Закрий HEX-палітру в Rust або вибери HEX Direct у Pixora й повтори калібрування.";
    public const string HexMismatch="У Pixora вибраний HEX, а керування гри відповідає звичайній палітрі. Відкрий HEX-палітру в Rust або вибери палітру Rust у Pixora й повтори калібрування.";
}
