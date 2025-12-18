using MetroMarkdownEditor.ViewModels;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace MetroMarkdownEditor.WindowsPhone
{
    public sealed partial class OutlinePage : Page
    {
        public OutlinePage()
        {
            this.InitializeComponent();
            Windows.Phone.UI.Input.HardwareButtons.BackPressed += HardwareButtons_BackPressed;
        }

        private void HardwareButtons_BackPressed(object sender, Windows.Phone.UI.Input.BackPressedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Windows.Phone.UI.Input.HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
            base.OnNavigatedFrom(e);
        }

        private void ListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as OutlineItem;
            if (item != null)
            {
                var vm = DataContext as EditorViewModel;
                if (vm != null)
                {
                    vm.ScrollToLineRequest = item.LineNumber;
                }
                if (Frame.CanGoBack) Frame.GoBack();
            }
        }
    }
}