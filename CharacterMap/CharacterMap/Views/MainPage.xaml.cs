﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using CharacterMap.Annotations;
using CharacterMap.Core;
using CharacterMap.ViewModels;
using CharacterMap.Helpers;
using Windows.Storage.Pickers;
using CharacterMap.Services;
using GalaSoft.MvvmLight.Messaging;
using System.Collections.Generic;
using System.Text;
using Windows.UI.Xaml.Markup;
using CharacterMap.Controls;
using CharacterMap.Models;
using Microsoft.Toolkit.Uwp.UI.Controls;
using Windows.UI.ViewManagement;
using Windows.UI.Core.AnimationMetrics;
using CharacterMapCX;
using System.Windows.Input;

namespace CharacterMap.Views
{
    public sealed partial class MainPage : Page, INotifyPropertyChanged, IInAppNotificationPresenter, IPrintPresenter
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public static CoreDispatcher MainDispatcher { get; private set; }

        public MainViewModel ViewModel { get; }

        private Debouncer _fontListDebouncer { get; } = new Debouncer();

        public object ThemeLock { get; } = new object();

        private UISettings _uiSettings { get; }

        private ICommand FilterCommand { get; } 

        public MainPage()
        {
            RequestedTheme = ResourceHelper.AppSettings.UserRequestedTheme;

            InitializeComponent();

            ViewModel = DataContext as MainViewModel;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            NavigationCacheMode = NavigationCacheMode.Enabled;

            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;

            MainDispatcher = Dispatcher;
            Messenger.Default.Register<CollectionsUpdatedMessage>(this, OnCollectionsUpdated);
            Messenger.Default.Register<AppSettingsChangedMessage>(this, OnAppSettingsChanged);
            Messenger.Default.Register<PrintRequestedMessage>(this, m =>
            {
                if (Dispatcher.HasThreadAccess)
                    PrintView.Show(this);
            });

            this.SizeChanged += MainPage_SizeChanged;

            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += (s, e) =>
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Messenger.Default.Send(new AppSettingsChangedMessage(nameof(AppSettings.UserRequestedTheme)));
                });
            };

            FilterCommand = new BasicCommand(OnFilterClick);
        }


        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ViewModel.GroupedFontList):
                    if (ViewModel.IsLoadingFonts)
                        return;

                    if (ViewModel.Settings.UseSelectionAnimations)
                    {
                        Composition.PlayEntrance(LstFontFamily, 66, 100);
                        Composition.PlayEntrance(GroupLabel, 0, 0, 80);
                        if (InlineLabelCount.Visibility == Visibility.Visible)
                            Composition.PlayEntrance(InlineLabelCount, 83, 0, 80);
                    }

                    break;

                case nameof(ViewModel.SelectedFont):
                    if (ViewModel.SelectedFont != null)
                    {
                        LstFontFamily.SelectedItem = ViewModel.SelectedFont;
                        FontMap.PlayFontChanged();
                    }

                    break;

                case nameof(ViewModel.IsLoadingFonts):
                case nameof(ViewModel.IsLoadingFontsFailed):
                    UpdateLoadingStates();

                    break;
            }
        }

        private void OnAppSettingsChanged(AppSettingsChangedMessage msg)
        {
            switch (msg.PropertyName)
            {
                case nameof(AppSettings.UseFontForPreview):
                    OnFontPreviewUpdated();
                    break;
                case nameof(AppSettings.UserRequestedTheme):
                    this.RequestedTheme = ViewModel.Settings.UserRequestedTheme;
                    OnPropertyChanged(nameof(ThemeLock));
                    break;
            }
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Messenger.Default.Register<ImportMessage>(this, OnFontImportRequest);
            Messenger.Default.Register<AppNotificationMessage>(this, OnNotificationMessage);

            ViewModel.FontListCreated -= ViewModel_FontListCreated;
            ViewModel.FontListCreated += ViewModel_FontListCreated;

            UpdateLoadingStates();
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Messenger.Default.Unregister<ImportMessage>(this);
            Messenger.Default.Unregister<AppNotificationMessage>(this);

            ViewModel.FontListCreated -= ViewModel_FontListCreated;
        }

        private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width < 900)
            {
                VisualStateManager.GoToState(this, nameof(CompactViewState), true);
            }
            else if (ViewStates.CurrentState == CompactViewState)
            {
                VisualStateManager.GoToState(this, nameof(DefaultViewState), true);
            }
        }

        private void UpdateLoadingStates()
        {
            TitleBar.TryUpdateMetrics();

            if (ViewModel.IsLoadingFonts && !ViewModel.IsLoadingFontsFailed)
                VisualStateManager.GoToState(this, nameof(FontsLoadingState), true);
            else if (ViewModel.IsLoadingFontsFailed)
                VisualStateManager.GoToState(this, nameof(FontsFailedState), true);
            else
            {
                VisualStateManager.GoToState(this, nameof(FontsLoadedState), false);
                if (ViewModel.Settings.UseSelectionAnimations)
                {
                    Composition.StartStartUpAnimation(
                        new List<FrameworkElement>
                        {
                            OpenFontPaneButton,
                            FontListFilter,
                            OpenFontButton,
                            FontMap.FontTitleBlock,
                            FontMap.SearchBox,
                            BtnSettings
                        },
                        new List<UIElement>
                        {
                            FontsSemanticZoom,
                            FontMap.CharGridHeader,
                            FontMap.SplitterContainer,
                            FontMap.CharGrid,
                            FontMap.PreviewGrid
                        });
                }
            }
        }

        private void ViewModel_FontListCreated(object sender, EventArgs e)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
            {
                await Task.Delay(50);
                LstFontFamily.ScrollIntoView(
                    LstFontFamily.SelectedItem, ScrollIntoViewAlignment.Leading);
            });
        }

        private void TogglePane_Click(object sender, RoutedEventArgs e)
        {
            if (SplitView.DisplayMode == SplitViewDisplayMode.Inline)
            {
                VisualStateManager.GoToState(this, nameof(CollapsedViewState), true);
            }
            else
            {
                VisualStateManager.GoToState(this, nameof(DefaultViewState), true);
            }
        }

        private void BtnSettings_OnClick(object sender, RoutedEventArgs e)
        {
            this.FindName(nameof(SettingsView));
            SettingsView.Show(FontMap.ViewModel.SelectedVariant, ViewModel.SelectedFont);
        }

        private void LayoutRoot_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrlState = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Control);
            if ((ctrlState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
            {
                // Check to see if any basic modals are open first
                if ((SettingsView != null && SettingsView.IsOpen)
                    || (PrintPresenter != null && PrintPresenter.Child != null))
                    return;

                switch (e.Key)
                {
                    case VirtualKey.C:
                        FontMap.TryCopy();
                        break;
                    case VirtualKey.S:
                        if (FontMap.ViewModel.SelectedVariant is FontVariant v)
                            ExportManager.RequestExportFontFile(v);
                            break;
                    case VirtualKey.N:
                        if (ViewModel.SelectedFont is InstalledFont fnt)
                            _ = FontMapView.CreateNewViewForFontAsync(fnt);
                        break;
                    case VirtualKey.P:
                        Messenger.Default.Send(new PrintRequestedMessage());
                        break;
                    case VirtualKey.Delete:
                        if (ViewModel.SelectedFont is InstalledFont font && font.HasImportedFiles)
                            FlyoutHelper.RequestDelete(font);
                        break;
                }
            }
        }

        private void LstFontFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.FirstOrDefault() is InstalledFont font)
            {
                ViewModel.SelectedFont = font;
            }
        }

        private void OpenFontPaneButton_Click(object sender, RoutedEventArgs e)
        {
            SplitView.IsPaneOpen = true;
        }

        void OnCollectionsUpdated(CollectionsUpdatedMessage msg)
        {
            if (ViewModel.InitialLoad.IsCompleted)
            {
                if (Dispatcher.HasThreadAccess)
                    ViewModel.RefreshFontList(ViewModel.SelectedCollection);
                else
                {
                    _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        ViewModel.RefreshFontList(ViewModel.SelectedCollection);
                    });
                }
            }
        }

        private string UpdateFontCountLabel(List<InstalledFont> fontList)
        {
            if (fontList != null)
                return Localization.Get("StatusBarFontCount", fontList.Count);

            return string.Empty;
        }

        private void OnFilterClick(object parameter)
        {
            if (parameter is BasicFontFilter filter)
            {
                if (!FontsSemanticZoom.IsZoomedInViewActive)
                    FontsSemanticZoom.IsZoomedInViewActive = true;

                if (filter == ViewModel.FontListFilter)
                    ViewModel.RefreshFontList();
                else
                    ViewModel.FontListFilter = filter;
            }
        }

        private void MenuFlyout_Opening(object sender, object e)
        {
            // Handles forming the flyout when opening the main FontFilter 
            // drop down menu.
            if (sender is MenuFlyout menu)
            {
                // Reset to default menu
                while (menu.Items.Count > 8)
                    menu.Items.RemoveAt(8);

                // force menu width to match the source button
                foreach (var sep in menu.Items.OfType<MenuFlyoutSeparator>())
                    sep.MinWidth = FontListFilter.ActualWidth;

                // add users collections 
                if (ViewModel.FontCollections.Items.Count > 0)
                {
                    menu.Items.Add(new MenuFlyoutSeparator());
                    foreach (var item in ViewModel.FontCollections.Items)
                    {
                        var m = new MenuFlyoutItem { DataContext = item, Text = item.Name, FontSize = 16 };
                        m.Click += (s, a) =>
                        {
                            if (m.DataContext is UserFontCollection u)
                            {
                                if (!FontsSemanticZoom.IsZoomedInViewActive)
                                    FontsSemanticZoom.IsZoomedInViewActive = true;

                                ViewModel.SelectedCollection = u;
                            }
                        };
                        menu.Items.Add(m);
                    }
                }

                VariableOption.SetVisible(FontFinder.HasVariableFonts);

                if (!FontFinder.HasAppxFonts && !FontFinder.HasRemoteFonts)
                {
                    FontSourceSeperator.Visibility = CloudFontsOption.Visibility = AppxOption.Visibility = Visibility.Collapsed;
                }
                else
                {
                    FontSourceSeperator.Visibility = Visibility.Visible;
                    CloudFontsOption.SetVisible(FontFinder.HasRemoteFonts);
                    AppxOption.SetVisible(FontFinder.HasAppxFonts);
                }

                void SetCommand(MenuFlyoutItemBase b, ICommand c)
                {
                    b.FontSize = 16;
                    if (b is MenuFlyoutSubItem i)
                    {
                        foreach (var child in i.Items)
                            SetCommand(child, c);
                    }
                    else if (b is MenuFlyoutItem m)
                        m.Command = c;
                }

                foreach (var item in menu.Items)
                    SetCommand(item, FilterCommand);
            }
        }

        private void RenameFontCollection_Click(object sender, RoutedEventArgs e)
        {
            _ = (new CreateCollectionDialog(ViewModel.SelectedCollection)).ShowAsync();
        }

        private void DeleteCollection_Click(object sender, RoutedEventArgs e)
        {
            var d = new ContentDialog
            {
                Title = Localization.Get("DigDeleteCollection/Title"),
                IsPrimaryButtonEnabled = true,
                IsSecondaryButtonEnabled = true,
                PrimaryButtonText = Localization.Get("DigDeleteCollection/PrimaryButtonText"),
                SecondaryButtonText = Localization.Get("DigDeleteCollection/SecondaryButtonText"),
            };

            d.PrimaryButtonClick += DigDeleteCollection_PrimaryButtonClick;
            _ = d.ShowAsync();
        }

        private async void DigDeleteCollection_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            string name = ViewModel.SelectedCollection.Name;
            await ViewModel.FontCollections.DeleteCollectionAsync(ViewModel.SelectedCollection);
            ViewModel.RefreshFontList();

            Messenger.Default.Send(new AppNotificationMessage(true, $"\"{name}\" collection deleted"));
        }

        private void OnFontPreviewUpdated()
        {
            if (ViewModel.InitialLoad.IsCompleted)
            {
                _fontListDebouncer.Debounce(16, () =>
                {
                    ViewModel.RefreshFontList(ViewModel.SelectedCollection);
                });
            }
        }

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }

        private async void Grid_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                ViewModel.IsLoadingFonts = true;
                try
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    if (await FontFinder.ImportFontsAsync(items) is FontImportResult result)
                    {
                        if (result.Imported.Count > 0)
                        {
                            ViewModel.RefreshFontList();
                            ViewModel.TrySetSelectionFromImport(result);
                        }

                        ShowImportResult(result);
                    }
                }
                finally
                {
                    ViewModel.IsLoadingFonts = false;
                }
            }
        }

        private async void OpenFont()
        {
            var picker = new FileOpenPicker();
            foreach (var format in FontFinder.SupportedFormats)
                picker.FileTypeFilter.Add(format);

            picker.CommitButtonText = Localization.Get("OpenFontPickerConfirm");
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    ViewModel.IsLoadingFonts = true;

                    if (await FontFinder.LoadFromFileAsync(file) is InstalledFont font)
                    {
                        await FontMapView.CreateNewViewForFontAsync(font, file);
                    }
                }
                finally
                {
                    ViewModel.IsLoadingFonts = false;
                }
            }
        }

        private void OnFontImportRequest(ImportMessage msg)
        {
            _ = CoreApplication.MainView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                ViewModel.IsLoadingFonts = true;
                try
                {
                    if (ViewModel.InitialLoad.IsCompleted)
                    {
                        ViewModel.RefreshFontList();
                    }
                    else
                    {
                        await ViewModel.InitialLoad;
                        await Task.Delay(50);
                    }

                    ViewModel.TrySetSelectionFromImport(msg.Result);
                }
                finally
                {
                    ViewModel.IsLoadingFonts = false;
                    ShowImportResult(msg.Result);
                }
            });
        }

        void ShowImportResult(FontImportResult result)
        {
            if (result.Imported.Count == 1)
                InAppNotificationHelper.ShowNotification(this, Localization.Get("NotificationSingleFontAdded", result.Imported[0].Name), 4000);
            else if (result.Imported.Count > 1)
                InAppNotificationHelper.ShowNotification(this, Localization.Get("NotificationMultipleFontsAdded", result.Imported.Count), 4000);
            else if (result.Invalid.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(Localization.Get("NotificationImportFailed"));
                sb.AppendLine();
                foreach (var i in result.Invalid.Take(5))
                {
                    sb.AppendLine($"{i.Item1.Name}: {i.Item2}");
                }

                if (result.Invalid.Count > 5)
                    sb.Append("…");

                InAppNotificationHelper.ShowNotification(this, sb.ToString().Trim(), 4000);
            }
        }




        /* Printing */

        public Border GetPresenter()
        {
            this.FindName(nameof(PrintPresenter));
            return PrintPresenter;
        }

        public FontMapView GetFontMap() => FontMap;

        public GridLength GetTitleBarHeight() => TitleBar.TemplateSettings.GridHeight;




        /* Notifications */

        public InAppNotification GetNotifier()
        {
            if (NotificationRoot == null)
                this.FindName(nameof(NotificationRoot));

            return DefaultNotification;
        }

        void OnNotificationMessage(AppNotificationMessage msg)
        {
            InAppNotificationHelper.OnMessage(this, msg);
        }




        /* Composition */

        private void Grid_Loading(FrameworkElement sender, object args)
        {
            Composition.SetThemeShadow(sender, 40, PaneRoot);
        }

        private void FontListGrid_Loading(FrameworkElement sender, object args)
        {
            Composition.SetDropInOut(
                CollectionControlBackground,
                CollectionControlItems.Children.Cast<FrameworkElement>().ToList(),
                CollectionControlRow);
        }

        private void LoadingRoot_Loading(FrameworkElement sender, object args)
        {
            if (!ViewModel.Settings.UseSelectionAnimations 
                || !Composition.UISettings.AnimationsEnabled)
                return;

            var v = sender.GetElementVisual();

            Composition.StartCentering(v);

            int duration = 350;
            var ani = v.Compositor.CreateVector3KeyFrameAnimation();
            ani.Target = nameof(v.Scale);
            ani.InsertKeyFrame(1, new System.Numerics.Vector3(1.15f, 1.15f, 0));
            ani.Duration = TimeSpan.FromMilliseconds(duration);

            var op = Composition.CreateFade(v.Compositor, 0, null, duration);

            sender.SetHideAnimation(v.Compositor.CreateAnimationGroup(ani, op));
        }

    }
}
