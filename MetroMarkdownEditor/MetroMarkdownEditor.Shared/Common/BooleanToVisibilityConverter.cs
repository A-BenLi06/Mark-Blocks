using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace MetroMarkdownEditor.Common // 注意：你可以把这个命名空间改成你项目默认的
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // 如果是 true 则显示，否则隐藏
            return (value is bool && (bool)value) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Visibility && (Visibility)value == Visibility.Visible;
        }
    }
}