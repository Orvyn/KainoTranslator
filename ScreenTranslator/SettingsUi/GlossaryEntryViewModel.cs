namespace ScreenTranslator.SettingsUi;

/// <summary>Editable row for the glossary ItemsControl.</summary>
public sealed class GlossaryEntryViewModel
{
    public string SourceTerm { get; set; } = "";
    public string TargetTerm { get; set; } = "";
}
