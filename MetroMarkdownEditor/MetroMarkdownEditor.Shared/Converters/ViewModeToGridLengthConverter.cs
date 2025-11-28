using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace MetroMarkdownEditor.Converters
{
    public class ViewModeToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var mode = ViewModels.EditorViewMode.Split;
            if (value is ViewModels.EditorViewMode)
            {
                mode = (ViewModels.EditorViewMode)value;
            }
            else
            {
                return new GridLength(1, GridUnitType.Star);
            }

            var target = parameter as string ?? "Editor";
            var shouldShow = (target == "Editor" && mode != ViewModels.EditorViewMode.Preview) ||
                             (target == "Preview" && mode != ViewModels.EditorViewMode.Write);

            return shouldShow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
