﻿using ExViewer.Controls;
using ExViewer.ViewModels;
using Opportunity.MvvmUniverse.AsyncHelpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“内容对话框”项模板

namespace ExViewer.Views
{
    public sealed partial class FileSearchDialog : MyContentDialog
    {
        public FileSearchDialog()
        {
            this.InitializeComponent();
            this.picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                CommitButtonText = Strings.Resources.Views.FileSearchDialog.FileOpenPicker.CommitButtonText,
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
                ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail,
                SettingsIdentifier = nameof(FileSearchDialog)
            };
            this.picker.FileTypeFilter.Add(".jpg");
            this.picker.FileTypeFilter.Add(".jpe");
            this.picker.FileTypeFilter.Add(".jpeg");
            this.picker.FileTypeFilter.Add(".jfif");
            this.picker.FileTypeFilter.Add(".gif");
            this.picker.FileTypeFilter.Add(".png");
        }

        private void MyContentDialog_Loading(FrameworkElement sender, object args)
        {
            this.SearchFile = null;
            this.cbCover.IsChecked = false;
            this.cbExp.IsChecked = false;
            this.cbSimilar.IsChecked = true;
        }

        public StorageFile SearchFile
        {
            get => (StorageFile)GetValue(SearchFileProperty); set => SetValue(SearchFileProperty, value);
        }

        // Using a DependencyProperty as the backing store for SearchFile.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SearchFileProperty =
            DependencyProperty.Register("SearchFile", typeof(StorageFile), typeof(FileSearchDialog), new PropertyMetadata(null));

        private readonly Windows.Storage.Pickers.FileOpenPicker picker;

        private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var search = ExClient.Client.Current.SearchAsync(this.SearchFile, this.cbSimilar.IsChecked ?? true, this.cbCover.IsChecked ?? false, this.cbExp.IsChecked ?? false);
            this.SearchFile = null;
            await this.Dispatcher.YieldIdle();
            RootControl.RootController.TrackAsyncAction(search, (s, e) =>
            {
                switch (e)
                {
                case AsyncStatus.Completed:
                    var vm = SearchVM.GetVM(s.GetResults());
                    RootControl.RootController.Frame.Navigate(typeof(SearchPage), vm.SearchQuery.ToString());
                    break;
                case AsyncStatus.Error:
                    RootControl.RootController.SendToast(s.ErrorCode, typeof(SearchPage));
                    break;
                }
                s.Close();
            });
        }

        private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
        }

        private async void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var file = await this.picker.PickSingleFileAsync();
            if (file == null)
                return;
            this.SearchFile = file;
        }

        private IAsyncOperation<StorageFile> check(DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return AsyncWrapper.CreateCompleted(default(StorageFile));
            }
            return AsyncInfo.Run(async token =>
            {
                var deferral = e.GetDeferral();
                try
                {
                    var info = Strings.Resources.Views.FileSearchDialog;
                    var storageitems = await e.DataView.GetStorageItemsAsync();
                    if (storageitems.Count != 1 || !(storageitems[0] is StorageFile file))
                    {
                        e.DragUIOverride.Caption = info.DropWrongFileNumber;
                        return null;
                    }
                    if (!this.picker.FileTypeFilter.Contains(file.FileType.ToLowerInvariant()))
                    {
                        e.DragUIOverride.Caption = info.DropWrongFileType;
                        return null;
                    }
                    e.AcceptedOperation = DataPackageOperation.Copy | DataPackageOperation.Link | DataPackageOperation.Move;
                    e.DragUIOverride.Caption = info.DropHint;
                    e.Handled = true;
                    return file;
                }
                finally
                {
                    deferral.Complete();
                }
            });
        }

        private async void tbFileName_DragEnter(object sender, DragEventArgs e)
        {
            await check(e);
        }

        private async void tbFileName_Drop(object sender, DragEventArgs e)
        {
            this.SearchFile = await check(e);
        }
    }
}
