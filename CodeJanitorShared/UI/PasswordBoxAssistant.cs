using System.Windows;
using System.Windows.Controls;

namespace CodeJanitor.UI;

/// <summary>
/// Enables binding a PasswordBox password in MVVM scenarios.
/// </summary>

public static class PasswordBoxAssistant
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword",
        typeof(string),
        typeof(PasswordBoxAssistant),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    public static readonly DependencyProperty BindPasswordProperty = DependencyProperty.RegisterAttached(
        "BindPassword",
        typeof(bool),
        typeof(PasswordBoxAssistant),
        new PropertyMetadata(false, OnBindPasswordChanged));

    private static readonly DependencyProperty UpdatingPasswordProperty = DependencyProperty.RegisterAttached(
        "UpdatingPassword",
        typeof(bool),
        typeof(PasswordBoxAssistant),
        new PropertyMetadata(false));

    /// <summary>
    /// Returns the string value of the BoundPasswordProperty attached dependency property for the given DependencyObject, with no side effects or exceptions.
    /// </summary>
    /// <param name="obj">The obj.</param>
    /// <returns>A string value produced by this method.</returns>

    public static string GetBoundPassword(DependencyObject obj)
    {
        return (string)obj.GetValue(BoundPasswordProperty);
    }

    /// <summary>
    /// Sets the BoundPasswordProperty dependency property on the given DependencyObject to the specified string value, modifying the object&apos;s property state with no detected exceptions.
    /// </summary>
    /// <param name="obj">The obj.</param>
    /// <param name="value">The value.</param>

    public static void SetBoundPassword(DependencyObject obj, string value)
    {
        obj.SetValue(BoundPasswordProperty, value);
    }

    /// <summary>
    /// Retrieves the current boolean value of the BindPasswordProperty from the specified DependencyObject and returns it as a bool, with no side effects.
    /// </summary>
    /// <param name="obj">The obj.</param>
    /// <returns>A bool value produced by this method.</returns>

    public static bool GetBindPassword(DependencyObject obj)
    {
        return (bool)obj.GetValue(BindPasswordProperty);
    }

    /// <summary>
    /// Sets the BindPasswordProperty attached property to the specified boolean value on the given DependencyObject, updating the object&apos;s property state.
    /// </summary>
    /// <param name="obj">The obj.</param>
    /// <param name="value">The value.</param>

    public static void SetBindPassword(DependencyObject obj, bool value)
    {
        obj.SetValue(BindPasswordProperty, value);
    }

    /// <summary>
    /// Retrieves the current boolean value of the UpdatingPasswordProperty attached dependency property from the specified DependencyObject, with no side effects or exceptions.
    /// </summary>
    /// <param name="obj">The obj.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool GetUpdatingPassword(DependencyObject obj)
    {
        return (bool)obj.GetValue(UpdatingPasswordProperty);
    }

    /// <summary>
    /// Sets the UpdatingPasswordProperty dependency property on the given object to the specified boolean value, potentially triggering property change callbacks or UI updates.
    /// </summary>
    /// <param name="obj">The obj.</param>
    /// <param name="value">The value.</param>

    private static void SetUpdatingPassword(DependencyObject obj, bool value)
    {
        obj.SetValue(UpdatingPasswordProperty, value);
    }

    /// <summary>
    /// This method toggles the PasswordChanged event handler subscription on a PasswordBox when the BindPassword dependency property changes, unsubscribing if the old value was true and subscribing if the new value is true, while safely doing nothing if the target is not a PasswordBox.
    /// </summary>
    /// <param name="d">The d.</param>
    /// <param name="e">The e.</param>

    private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var passwordBox = d as PasswordBox;
        if (passwordBox == null)
        {
            return;
        }

        if ((bool)e.OldValue)
        {
            passwordBox.PasswordChanged -= HandlePasswordChanged;
        }

        if ((bool)e.NewValue)
        {
            passwordBox.PasswordChanged += HandlePasswordChanged;
        }
    }

    /// <summary>
    /// This method updates a PasswordBox&apos;s Password from a dependency property change only when not already updating, temporarily detaching and reattaching the PasswordChanged handler to avoid recursion.
    /// </summary>
    /// <param name="d">The d.</param>
    /// <param name="e">The e.</param>

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var passwordBox = d as PasswordBox;
        if (passwordBox == null)
        {
            return;
        }

        passwordBox.PasswordChanged -= HandlePasswordChanged;
        if (!GetUpdatingPassword(passwordBox))
        {
            passwordBox.Password = e.NewValue == null ? string.Empty : e.NewValue.ToString();
        }

        passwordBox.PasswordChanged += HandlePasswordChanged;
    }

    /// <summary>
    /// Handles a PasswordBox password change by casting the sender, returning if it is not a PasswordBox, then setting the updating flag true, assigning the bound password to the current Password value, and resetting the flag to false, thereby updating the bound property while suppressing re-entrancy.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>

    private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
    {
        var passwordBox = sender as PasswordBox;
        if (passwordBox == null)
        {
            return;
        }

        SetUpdatingPassword(passwordBox, true);
        SetBoundPassword(passwordBox, passwordBox.Password);
        SetUpdatingPassword(passwordBox, false);
    }
}
