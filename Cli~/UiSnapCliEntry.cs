// Thin entry for UnityCLI `run_script` (this folder is ignored by Unity's importer).
// The real API is Ji2.UiSnapshots.Editor.SnapCli in the package's Editor assembly.
public static class UiSnapCliEntry
{
    public static string Start(string requestJson) => Ji2.UiSnapshots.Editor.SnapCli.Start(requestJson);
    public static string Status() => Ji2.UiSnapshots.Editor.SnapCli.Status();
    public static string Cancel() => Ji2.UiSnapshots.Editor.SnapCli.Cancel();
    public static string Compare(string before, string after) => Ji2.UiSnapshots.Editor.SnapCli.Compare(before, after);
    public static string List() => Ji2.UiSnapshots.Editor.SnapCli.List();
    public static string Validate(string scenarios) => Ji2.UiSnapshots.Editor.SnapCli.Validate(scenarios);
}
