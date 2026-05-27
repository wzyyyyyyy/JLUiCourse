using Avalonia;
using Avalonia.Controls;
using System.Collections.Specialized;

namespace iCourse.Behaviors;

public static class AutoScrollToEndBehavior
{
    public static readonly AttachedProperty<INotifyCollectionChanged?> ItemsSourceProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, INotifyCollectionChanged?>(
            "ItemsSource",
            typeof(AutoScrollToEndBehavior));

    public static void SetItemsSource(AvaloniaObject element, INotifyCollectionChanged? value)
    {
        element.SetValue(ItemsSourceProperty, value);
    }

    public static INotifyCollectionChanged? GetItemsSource(AvaloniaObject element)
    {
        return element.GetValue(ItemsSourceProperty);
    }

    static AutoScrollToEndBehavior()
    {
        ItemsSourceProperty.Changed.AddClassHandler<ScrollViewer>((scrollViewer, args) =>
        {
            if (args.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= OnCollectionChanged;
            }

            if (args.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += OnCollectionChanged;
            }

            void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            {
                scrollViewer.ScrollToEnd();
            }
        });
    }
}
