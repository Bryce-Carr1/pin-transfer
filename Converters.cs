using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PinTransferWPF
{
    public class EditingPlateVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 2) return Visibility.Collapsed;

            var editingPlate = values[0];
            var currentPlate = values[1];

            bool isEditing = Equals(editingPlate, currentPlate);
            bool showEditBox = parameter?.ToString() == "Edit";

            if (showEditBox)
                return isEditing ? Visibility.Visible : Visibility.Collapsed;
            else
                return isEditing ? Visibility.Collapsed : Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Keep the other converters if you're using them elsewhere
    public class EqualityToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Equals(value, parameter) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InverseEqualityToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Equals(value, parameter) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}