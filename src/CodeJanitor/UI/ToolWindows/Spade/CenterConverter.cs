using System;
using System.Globalization;

namespace CodeJanitor.UI.ToolWindows.Spade;

/// <summary>
/// CenterConverter is a value converter class that translates a value to or from a center-aligned representation for data binding scenarios.
/// </summary>
public sealed class CenterConverter : System.Windows.Data.IValueConverter
{
    /// <summary>
    /// Divides the input value by 2.0 after casting it to double and returns the result as an object with no side effects or exception handling.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The parameter.</param>
    /// <param name="culture">The culture.</param>
    /// <returns>A object value produced by this method.</returns>

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var result = (double)value / 2.0;

        return result;
    }

    /// <summary>
    /// This ConvertBack method unconditionally throws NotImplementedException when called, indicating the inverse conversion is unsupported and will always fail at runtime.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The parameter.</param>
    /// <param name="culture">The culture.</param>
    /// <returns>A object value produced by this method.</returns>
    /// <exception cref="NotImplementedException">Thrown when method validation or execution fails for this exception type.</exception>

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
