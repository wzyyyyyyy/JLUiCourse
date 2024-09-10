using HandyControl.Interactivity;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace iCourse.Helpers
{
    public class AutoScrollBehavior : Behavior<ScrollViewer>
    {
        private bool _userIsAtBottom;

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(INotifyCollectionChanged), typeof(AutoScrollBehavior), new PropertyMetadata(null, OnItemsSourceChanged));

        public INotifyCollectionChanged ItemsSource
        {
            get => (INotifyCollectionChanged)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.ScrollChanged += OnScrollChanged;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.ScrollChanged -= OnScrollChanged;
            base.OnDetaching();
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 如果滚动位置接近底部，允许自动滚动
            _userIsAtBottom = AssociatedObject.VerticalOffset >= AssociatedObject.ScrollableHeight - 10;
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= ((AutoScrollBehavior)d).ItemsSource_CollectionChanged;
            }

            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += ((AutoScrollBehavior)d).ItemsSource_CollectionChanged;
            }
        }

        private void ItemsSource_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_userIsAtBottom)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AssociatedObject?.ScrollToEnd();
                });
            }
        }
    }
}
