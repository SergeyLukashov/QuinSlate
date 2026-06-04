using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace QuinSlate.Ui.Helpers;

/// <summary>
/// Provides helper methods for traversing the visual tree.
/// </summary>
public static class VisualTreeHelpers
{
    /// <summary>
    /// Recursively searches the visual tree of a dependency object for a child of type T with a specific name.
    /// </summary>
    public static T FindVisualChild<T>(DependencyObject obj, string name) where T : DependencyObject
    {
        if (obj == null)
        {
            return null;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T typedChild && child.GetValue(FrameworkElement.NameProperty)?.ToString() == name)
            {
                return typedChild;
            }

            var childOfChild = FindVisualChild<T>(child, name);
            if (childOfChild != null)
            {
                return childOfChild;
            }
        }

        return null;
    }

    /// <summary>
    /// Recursively searches the visual tree of a dependency object for a child of type T.
    /// </summary>
    public static T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        if (obj == null)
        {
            return null;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
            {
                return childOfChild;
            }
        }

        return null;
    }
}
