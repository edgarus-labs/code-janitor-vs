using System.Windows;

namespace CodeJanitor.UI;

/// <summary>
/// A collection of attached properties related to drag and drop behavior.
/// </summary>

public static class DragDropAttachedProperties
{
    /// <summary>
    /// The dependency property definition for the IsBeingDragged attached property.
    /// </summary>

    public static DependencyProperty IsBeingDraggedProperty = DependencyProperty.RegisterAttached(
        "IsBeingDragged", typeof(bool), typeof(DragDropAttachedProperties));

    /// <summary>
    /// Gets the IsBeingDragged value from the specified target.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <returns>The value.</returns>

    public static bool GetIsBeingDragged(UIElement target)
    {
        return (bool)target.GetValue(IsBeingDraggedProperty);
    }

    /// <summary>
    /// Sets the IsBeingDragged value on the specified target.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="value">The value.</param>

    public static void SetIsBeingDragged(UIElement target, bool value)
    {
        target.SetValue(IsBeingDraggedProperty, value);
    }

    /// <summary>
    /// The dependency property definition for the IsDropAboveTarget attached property.
    /// </summary>

    public static DependencyProperty IsDropAboveTargetProperty = DependencyProperty.RegisterAttached(
        "IsDropAboveTarget", typeof(bool), typeof(DragDropAttachedProperties));

    /// <summary>
    /// Gets the IsDropAboveTarget value from the specified target.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <returns>The value.</returns>

    public static bool GetIsDropAboveTarget(UIElement target)
    {
        return (bool)target.GetValue(IsDropAboveTargetProperty);
    }

    /// <summary>
    /// Sets the IsDropAboveTarget value on the specified target.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="value">The value.</param>

    public static void SetIsDropAboveTarget(UIElement target, bool value)
    {
        target.SetValue(IsDropAboveTargetProperty, value);
    }

    /// <summary>
    /// The dependency property definition for the IsDropBelowTarget attached property.
    /// </summary>

    public static DependencyProperty IsDropBelowTargetProperty = DependencyProperty.RegisterAttached(
        "IsDropBelowTarget", typeof(bool), typeof(DragDropAttachedProperties));

    /// <summary>
    /// Gets the IsDropBelowTarget value from the specified target.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <returns>The value.</returns>

    public static bool GetIsDropBelowTarget(UIElement target)
    {
        return (bool)target.GetValue(IsDropBelowTargetProperty);
    }

    /// <summary>
    /// Sets the IsDropBelowTarget value on the specified target.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="value">The value.</param>

    public static void SetIsDropBelowTarget(UIElement target, bool value)
    {
        target.SetValue(IsDropBelowTargetProperty, value);
    }

    /// <summary>
    /// The dependency property definition for the IsDropOnTarget attached property.
    /// </summary>

    public static DependencyProperty IsDropOnTargetProperty = DependencyProperty.RegisterAttached(
        "IsDropOnTarget", typeof(bool), typeof(DragDropAttachedProperties));

    /// <summary>
    /// Gets the IsDropOnTarget value from the specified target.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <returns>The value.</returns>

    public static bool GetIsDropOnTarget(UIElement target)
    {
        return (bool)target.GetValue(IsDropOnTargetProperty);
    }

    /// <summary>
    /// Sets the IsDropOnTarget value on the specified target.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="value">The value.</param>

    public static void SetIsDropOnTarget(UIElement target, bool value)
    {
        target.SetValue(IsDropOnTargetProperty, value);
    }
}
