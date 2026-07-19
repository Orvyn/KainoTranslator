namespace ScreenTranslator.SettingsUi;

/// <summary>Wraps an enum value with a human-friendly display label for ComboBox items.</summary>
public sealed class EnumOption<T>
{
    public T Value { get; }
    public string Label { get; }

    public EnumOption(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public override string ToString() => Label;
}
