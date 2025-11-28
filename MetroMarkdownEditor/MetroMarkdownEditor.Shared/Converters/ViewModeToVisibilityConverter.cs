using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using MetroMarkdownEditor.ViewModels;

namespace MetroMarkdownEditor.Converters
{
    public class ViewModeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var mode = EditorViewMode.Split;
            if (value is EditorViewMode)
            {
                mode = (EditorViewMode)value;
            }
            else
            {
                return Visibility.Visible;
            }

            var target = parameter as string ?? "Editor";
            var isVisible = (target == "Editor" && mode != EditorViewMode.Preview) ||
                            (target == "Preview" && mode != EditorViewMode.Write);

            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
