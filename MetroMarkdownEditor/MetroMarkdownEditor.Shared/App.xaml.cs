using System;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.ViewModels;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
#if !WINDOWS_PHONE_APP
using MetroMarkdownEditor.Windows;
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
            args.Request.ApplicationCommands.Add(new SettingsCommand("28a24559-0017-4959-9b93-669e20032908", "Appearance", _ => ShowThemeSettings()));
            args.Request.ApplicationCommands.Add(new SettingsCommand("0f0593fa-4f39-4f38-8e43-71c81c909f7a", "Markdown", _ => ShowMarkdownSettings()));
            args.Request.ApplicationCommands.Add(new SettingsCommand("276ed2b5-589f-4cb7-ae60-09de6b11f873", "Editor", _ => ShowEditorSettings()));
            args.Request.ApplicationCommands.Add(new SettingsCommand("3ed00ab2-1555-49bd-8a0c-3de3f10fbfa7", "Image", _ => ShowImageSettings()));
            args.Request.ApplicationCommands.Add(new SettingsCommand("2042f0e8-25df-4e31-846f-72a76b4b01c9", "Export", _ => ShowExportSettings()));
            args.Request.ApplicationCommands.Add(new SettingsCommand("c5f5e4f5-71d8-4a0c-bd27-3f58b6c6fbc0", "Auto Save", _ => ShowAutoSaveSettings()));
            args.Request.ApplicationCommands.Add(new SettingsCommand("a1b2c3d4-5e6f-7a8b-9c0d-e1f2a3b4c5d6", "Dev", _ => ShowDevSettings()));
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

        private void ShowAutoSaveSettings()
        {
            var flyout = new AutoSaveSettings();
            flyout.DataContext = Services.AutoSaveService.Instance;
            flyout.Show();
        }

        private void ShowMarkdownSettings()
        {
            var flyout = new MarkdownSettings();
            flyout.Show();
        }

        private void ShowEditorSettings()
        {
            var flyout = new EditorSettings();
            flyout.Show();
        }

        private void ShowImageSettings()
        {
            var flyout = new ImageSettings();
            flyout.Show();
        }

        private void ShowExportSettings()
        {
            var flyout = new ExportSettings();
            flyout.Show();
        }

        private void ShowDevSettings()
        {
            var flyout = new DevSettings();
            flyout.Show();
        }
#endif

        private async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                var frame = Window.Current.Content as Frame;
#if WINDOWS_PHONE_APP
                var editorPage = frame != null ? frame.Content as WindowsPhone.EditorPage : null;
#else
                var editorPage = frame != null ? frame.Content as Windows.EditorPage : null;
#endif
                if (editorPage != null)
                {
                    editorPage.FlushEditorBufferForSuspension();
                }

                var locator = Resources["Locator"] as ViewModelLocator;
                if (locator != null)
                {
                    await locator.Editor.SaveDirtyDocumentsAsync();
                }
            }
            finally
            {
                deferral.Complete();
            }
        }

        /// <summary>
        /// Handles file activation when app is opened via file association (.md, .markdown, .txt)
        /// </summary>
        protected override async void OnFileActivated(FileActivatedEventArgs args)
        {
            base.OnFileActivated(args);

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

                // Handle the activated file
                if (args.Files.Count > 0)
                {
                    var file = args.Files[0] as global::Windows.Storage.StorageFile;
                    if (file != null)
                    {
                        // Store the file for EditorPage to pick up
                        locator.Editor.SetOpenedFile(file);
                        
                        // Navigate directly to EditorPage
#if WINDOWS_PHONE_APP
                        rootFrame.Navigate(typeof(WindowsPhone.EditorPage));
#else
                        rootFrame.Navigate(typeof(Windows.EditorPage));
#endif
                    }
                    else
                    {
                        rootFrame.Navigate(typeof(MainPage));
                    }
                }
                else
                {
                    rootFrame.Navigate(typeof(MainPage));
                }
            }
            else
            {
                rootFrame.Navigate(typeof(MainPage));
            }

            Window.Current.Activate();
        }

#if WINDOWS_PHONE_APP
        // Track if we're picking background image
        public static bool IsPickingBackgroundImage { get; set; }
        
        // Track if we're picking editor file from MainPage
        public static bool IsPickingEditorFile { get; set; }

        protected override async void OnActivated(IActivatedEventArgs args)
        {
            var locator = Resources["Locator"] as ViewModelLocator;
            var rootFrame = Window.Current.Content as Frame;

            var openArgs = args as FileOpenPickerContinuationEventArgs;
            if (openArgs != null && locator != null)
            {
                // Check if we're picking a background image
                if (IsPickingBackgroundImage)
                {
                    IsPickingBackgroundImage = false;
                    if (openArgs.Files != null && openArgs.Files.Count > 0)
                    {
                        await locator.Background.SetBackgroundFromFileAsync(openArgs.Files[0]);
                        // Auto-switch to Custom mode
                        locator.Background.BackgroundMode = MainPageBackgroundMode.Custom;
                    }
                }
                // Check if we're picking an editor file from MainPage
                else if (IsPickingEditorFile)
                {
                    IsPickingEditorFile = false;
                    if (openArgs.Files != null && openArgs.Files.Count > 0)
                    {
                        // Handle the picked file in EditorViewModel and navigate to EditorPage
                        await locator.Editor.HandleOpenPickerContinuation(openArgs);
                        
                        // Navigate to EditorPage
                        if (rootFrame != null)
                        {
                            rootFrame.Navigate(typeof(WindowsPhone.EditorPage));
                        }
                    }
                }
                else
                {
                    await locator.Editor.HandleOpenPickerContinuation(openArgs);
                }
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
