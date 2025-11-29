using System;
using MetroMarkdownEditor.ViewModels;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
#if !WINDOWS_PHONE_APP
using Windows.UI.ApplicationSettings;
#endif
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace MetroMarkdownEditor
{
    public sealed partial class App : Application
    {
#if WINDOWS_PHONE_APP
        private TransitionCollection transitions;
#endif

        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            var rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame { CacheSize = 1 };
                Window.Current.Content = rootFrame;
            }

            var locator = Resources["Locator"] as ViewModelLocator;
            if (locator != null)
            {
                await locator.RecentFiles.InitializeAsync();
                locator.Theme.ApplyThemeToRoot();
            }

#if !WINDOWS_PHONE_APP
            SettingsPane.GetForCurrentView().CommandsRequested += OnCommandsRequested;
#endif

            if (rootFrame.Content == null)
            {
#if WINDOWS_PHONE_APP
                if (rootFrame.ContentTransitions != null)
                {
                    transitions = new TransitionCollection();
                    foreach (var c in rootFrame.ContentTransitions)
                    {
                        transitions.Add(c);
                    }
                }

                rootFrame.ContentTransitions = null;
                rootFrame.Navigated += RootFrame_FirstNavigated;
#endif

                if (!rootFrame.Navigate(typeof(MainPage), e.Arguments))
                {
                    throw new Exception("Failed to create initial page");
                }
            }

            Window.Current.Activate();
        }

#if WINDOWS_PHONE_APP
        private void RootFrame_FirstNavigated(object sender, NavigationEventArgs e)
        {
            var rootFrame = sender as Frame;
            rootFrame.ContentTransitions = transitions ?? new TransitionCollection { new NavigationThemeTransition() };
            rootFrame.Navigated -= RootFrame_FirstNavigated;
        }
#endif

#if !WINDOWS_PHONE_APP
        private void OnCommandsRequested(SettingsPane sender, SettingsPaneCommandsRequestedEventArgs args)
        {
            // 我为你生成了一个随机的 GUID：28a24559-0017-4959-9b93-669e20032908
            // 使用 GUID 字符串作为 ID 可以解决这个 FormatException
            args.Request.ApplicationCommands.Add(new SettingsCommand("28a24559-0017-4959-9b93-669e20032908", "Personalization", _ => ShowThemeSettings()));
        }

        private void ShowThemeSettings()
        {
            var flyout = new ThemeSettings();
            var locator = Resources["Locator"] as ViewModelLocator;
            if (locator != null)
            {
                flyout.DataContext = locator.Theme;
            }

            flyout.Show();
        }
#endif

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }

#if WINDOWS_PHONE_APP
        protected override async void OnActivated(IActivatedEventArgs args)
        {
            var locator = Resources["Locator"] as ViewModelLocator;

            var openArgs = args as FileOpenPickerContinuationEventArgs;
            if (openArgs != null && locator != null)
            {
                await locator.Editor.HandleOpenPickerContinuation(openArgs);
            }
            else
            {
                var saveArgs = args as FileSavePickerContinuationEventArgs;
                if (saveArgs != null && locator != null)
                {
                    await locator.Editor.HandleSavePickerContinuation(saveArgs);
                }
            }

            base.OnActivated(args);
        }
#endif
    }
}
