using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace MediaMetadataEditor.Helpers
{
    public static class SelectedItemsBehavior
    {
        public static readonly DependencyProperty SelectedItemsProperty =
            DependencyProperty.RegisterAttached("SelectedItems", typeof(IList), typeof(SelectedItemsBehavior),
                new PropertyMetadata(null, OnSelectedItemsChanged));

        public static void SetSelectedItems(DependencyObject element, IList value) => element.SetValue(SelectedItemsProperty, value);
        public static IList GetSelectedItems(DependencyObject element) => (IList)element.GetValue(SelectedItemsProperty)!;

        private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ListBox listBox) return;

            listBox.SelectionChanged -= ListBox_SelectionChanged;
            listBox.SelectionChanged += ListBox_SelectionChanged;

            if (e.OldValue is INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= BoundCollectionChanged;

            if (e.NewValue is INotifyCollectionChanged newCollection)
                newCollection.CollectionChanged += BoundCollectionChanged;

            void BoundCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
            {
                listBox.SelectionChanged -= ListBox_SelectionChanged;
                try
                {
                    listBox.SelectedItems.Clear();
                    if (GetSelectedItems(listBox) is IList coll)
                    {
                        foreach (var item in coll) listBox.SelectedItems.Add(item);
                    }
                }
                finally { listBox.SelectionChanged += ListBox_SelectionChanged; }
            }
        }

        private static void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox listBox) return;
            var bound = GetSelectedItems(listBox);
            if (bound == null) return;

            foreach (var item in e.RemovedItems)
                if (bound.Contains(item)) bound.Remove(item);

            foreach (var item in e.AddedItems)
                if (!bound.Contains(item)) bound.Add(item);
        }
    }
}
