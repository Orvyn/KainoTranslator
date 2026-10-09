using System.Windows;
using System.Windows.Controls;
using ScreenTranslator.Localization;

namespace ScreenTranslator.SettingsUi;

/// <summary>Secret (API key) input: hidden behind asterisks, with an eye button to reveal it.</summary>
public partial class SecretTextBox : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SecretTextBox),
        new PropertyMetadata(string.Empty, OnTextChanged));

    private bool _syncing;

    public SecretTextBox()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateTooltip();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (SecretTextBox)d;
        var text = (string)e.NewValue ?? string.Empty;
        box._syncing = true;
        try
        {
            if (box.Masked.Password != text) box.Masked.Password = text;
            if (box.Plain.Text != text) box.Plain.Text = text;
        }
        finally { box._syncing = false; }
    }

    private void OnMaskedChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        Text = Masked.Password;
    }

    private void OnPlainChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        Text = Plain.Text;
    }

    private void OnEyeToggled(object sender, RoutedEventArgs e)
    {
        bool reveal = EyeButton.IsChecked == true;
        Plain.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
        Masked.Visibility = reveal ? Visibility.Collapsed : Visibility.Visible;
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        EyeButton.ToolTip = Loc.S(EyeButton.IsChecked == true ? "Settings.HideKey" : "Settings.ShowKey");
    }
}
