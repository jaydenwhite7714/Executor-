using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DrivenTestEngine.App.Infrastructure;

/// <summary>Keeps a log list pinned to the newest entry while enabled.</summary>
public static class AutoScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(AutoScrollBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
        "Handler",
        typeof(object),
        typeof(AutoScrollBehavior),
        new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox || listBox.Items is not INotifyCollectionChanged incc)
        {
            return;
        }

        if (listBox.GetValue(HandlerProperty) is NotifyCollectionChangedEventHandler existing)
        {
            incc.CollectionChanged -= existing;
            listBox.SetValue(HandlerProperty, null);
        }


        if (e.NewValue is not true)
        {
            return;
        }

        NotifyCollectionChangedEventHandler handler = (_, _) => ScrollToEnd(listBox);
        incc.CollectionChanged += handler;
        listBox.SetValue(HandlerProperty, handler);
    }

    private static void ScrollToEnd(ListBox listBox)
    {
        if (!GetIsEnabled(listBox))
        {
            return;
        }

        listBox.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (!GetIsEnabled(listBox) || listBox.Items.Count == 0)
                {
                    return;
                }

                var last = listBox.Items[listBox.Items.Count - 1];
                listBox.ScrollIntoView(last);
            }),
            DispatcherPriority.Background);
    }
}
