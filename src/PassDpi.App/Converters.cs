using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PassDpi.App;

/// <summary>value.ToString() == parameter → true. ConvertBack (for tab radios) returns the parameter as int when checked.</summary>
public sealed class EqConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        string.Equals(value?.ToString(), p?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        value is true && int.TryParse(p?.ToString(), out var i) ? i : Binding.DoNothing;
}

/// <summary>value.ToString() == parameter → Visible.</summary>
public sealed class EqVisConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        string.Equals(value?.ToString(), p?.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}
