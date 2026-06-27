using System.Windows;
using System.Windows.Controls;
using WorldWatch.Models;
using WpfColor = System.Windows.Media.Color;
using WinFormsColorDialog = System.Windows.Forms.ColorDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace WorldWatch;

/// <summary>Editor for a single clock. Edits apply live and are persisted by the owner.</summary>
public partial class SettingsWindow : Window
{
    private readonly ClockSettings _settings;
    private readonly Action _onChanged; // apply to overlay + save to disk
    private readonly Action _onDelete;

    // Suppresses change handlers while we populate controls from the model.
    private bool _loading = true;

    private static readonly string[] FontChoices =
    {
        "Consolas", "Cascadia Mono", "Segoe UI", "Arial", "Tahoma",
        "Verdana", "Courier New", "Lucida Console", "Times New Roman",
    };

    public SettingsWindow(ClockSettings settings, Action onChanged, Action onDelete)
    {
        InitializeComponent();
        _settings = settings;
        _onChanged = onChanged;
        _onDelete = onDelete;

        PopulateZones();
        PopulateFonts();

        SizeSlider.Value = settings.FontSize;
        OpacitySlider.Value = settings.Opacity;
        Use24Check.IsChecked = settings.Use24Hour;
        SecondsCheck.IsChecked = settings.ShowSeconds;
        BoldCheck.IsChecked = settings.Bold;
        LockCheck.IsChecked = settings.LockPosition;
        LabelCheck.IsChecked = settings.ShowLabel;
        LabelText.Text = settings.Label;

        _loading = false;
    }

    private void PopulateZones()
    {
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
            ZoneCombo.Items.Add(new ComboBoxItem { Content = tz.DisplayName, Tag = tz.Id });

        foreach (ComboBoxItem item in ZoneCombo.Items)
        {
            if ((string)item.Tag == _settings.TimeZoneId)
            {
                ZoneCombo.SelectedItem = item;
                break;
            }
        }
    }

    private void PopulateFonts()
    {
        foreach (var f in FontChoices)
            FontCombo.Items.Add(f);

        FontCombo.SelectedItem = FontChoices.Contains(_settings.FontFamily)
            ? _settings.FontFamily
            : FontChoices[0];
    }

    private void ZoneCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ZoneCombo.SelectedItem is not ComboBoxItem item)
            return;

        _settings.TimeZoneId = (string)item.Tag;
        _onChanged();
    }

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FontCombo.SelectedItem is not string font)
            return;

        _settings.FontFamily = font;
        _onChanged();
    }

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
            return;

        _settings.FontSize = Math.Round(e.NewValue);
        _onChanged();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
            return;

        _settings.Opacity = e.NewValue;
        _onChanged();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        _settings.Use24Hour = Use24Check.IsChecked == true;
        _settings.ShowSeconds = SecondsCheck.IsChecked == true;
        _settings.Bold = BoldCheck.IsChecked == true;
        _settings.LockPosition = LockCheck.IsChecked == true;
        _settings.ShowLabel = LabelCheck.IsChecked == true;
        _onChanged();
    }

    private void LabelText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
            return;

        _settings.Label = LabelText.Text;
        _onChanged();
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinFormsColorDialog { FullOpen = true };

        var current = ToWpfColor(_settings.ColorHex);
        dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);

        if (dialog.ShowDialog() == WinFormsDialogResult.OK)
        {
            var c = dialog.Color;
            _settings.ColorHex = $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";
            _onChanged();
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Видалити цей годинник?", "WorldWatch",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _onDelete();
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static WpfColor ToWpfColor(string hex)
    {
        try
        {
            return (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return System.Windows.Media.Colors.White;
        }
    }
}
