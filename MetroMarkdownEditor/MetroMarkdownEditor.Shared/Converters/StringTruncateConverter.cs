using System;
using Windows.UI.Xaml.Data;

namespace MetroMarkdownEditor.Converters
{
    public class StringTruncateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var text = value as string;
            if (string.IsNullOrEmpty(text)) return value;

            int maxLength = 8;
            if (parameter != null)
            {
                int.TryParse(parameter.ToString(), out maxLength);
            }

            if (text.Length > maxLength)
            {
                return text.Substring(0, maxLength) + "...";
            }
            return text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}