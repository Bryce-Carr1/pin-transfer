using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ViewModels;

namespace PinTransferWPF
{
    public static class TextBoxBehaviors
    {
        public static readonly DependencyProperty SelectAllOnFocusProperty =
            DependencyProperty.RegisterAttached("SelectAllOnFocus", typeof(bool), typeof(TextBoxBehaviors),
                new PropertyMetadata(false, OnSelectAllOnFocusChanged));

        public static bool GetSelectAllOnFocus(DependencyObject obj)
        {
            return (bool)obj.GetValue(SelectAllOnFocusProperty);
        }

        public static void SetSelectAllOnFocus(DependencyObject obj, bool value)
        {
            obj.SetValue(SelectAllOnFocusProperty, value);
        }

        private static void OnSelectAllOnFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox textBox)
            {
                if ((bool)e.NewValue)
                {
                    textBox.IsVisibleChanged += TextBox_IsVisibleChanged;
                    textBox.KeyDown += TextBox_KeyDown;
                    textBox.LostFocus += TextBox_LostFocus;
                }
                else
                {
                    textBox.IsVisibleChanged -= TextBox_IsVisibleChanged;
                    textBox.KeyDown -= TextBox_KeyDown;
                    textBox.LostFocus -= TextBox_LostFocus;
                }
            }
        }

        private static void TextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.Visibility == Visibility.Visible)
            {
                // Delay focus to ensure the TextBox is fully loaded
                textBox.Dispatcher.BeginInvoke(new Action(() =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private static void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                var window = Window.GetWindow(textBox);
                if (window?.DataContext is MainViewModel viewModel)
                {
                    if (e.Key == Key.Enter)
                    {
                        viewModel.ConfirmRenamePlateCommand.Execute(null);
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        viewModel.CancelRenamePlateCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
        }

        private static void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                var window = Window.GetWindow(textBox);
                if (window?.DataContext is MainViewModel viewModel)
                {
                    // Only confirm if the TextBox is still visible (user didn't cancel)
                    if (textBox.Visibility == Visibility.Visible)
                    {
                        viewModel.ConfirmRenamePlateCommand.Execute(null);
                    }
                }
            }
        }
    }
}