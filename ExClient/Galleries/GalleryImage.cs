﻿using ExClient.Internal;
using HtmlAgilityPack;
using Opportunity.MvvmUniverse;
using Opportunity.MvvmUniverse.AsyncHelpers;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Data.Html;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Web.Http;
using static System.Runtime.InteropServices.WindowsRuntime.AsyncInfo;

namespace ExClient.Galleries
{
    [System.Diagnostics.DebuggerDisplay(@"\{PageId = {PageId} State = {State} File = {ImageFile?.Name}\}")]
    public class GalleryImage : ObservableObject
    {
        static GalleryImage()
        {
            DispatcherHelper.BeginInvokeOnUIThread(async () =>
            {
                var info = Windows.Graphics.Display.DisplayInformation.GetForCurrentView();
                thumbWidth = (uint)(100 * info.RawPixelsPerViewPixel);
                DefaultThumb = new BitmapImage();
                using (var stream = await StorageHelper.GetIconOfExtension("jpg"))
                {
                    await DefaultThumb.SetSourceAsync(stream);
                }
            });
        }

        private static uint thumbWidth = 100;

        protected static BitmapImage DefaultThumb
        {
            get; private set;
        }

        internal static IAsyncOperation<GalleryImage> LoadCachedImageAsync(Gallery owner, Models.ImageModel model)
        {
            return Run(async token =>
            {
                var folder = owner.GalleryFolder ?? await owner.GetFolderAsync();
                var imageFile = await folder.TryGetFileAsync(model.FileName);
                if (imageFile == null)
                    return null;
                var img = new GalleryImage(owner, model.PageId, model.ImageKey, null)
                {
                    ImageFile = imageFile,
                    OriginalLoaded = model.OriginalLoaded,
                    Progress = 100,
                    State = ImageLoadingState.Loaded
                };
                return img;
            });
        }

        internal GalleryImage(Gallery owner, int pageId, ulong imageKey, Uri thumb)
        {
            this.Owner = owner;
            this.PageId = pageId;
            this.imageKey = imageKey;
            this.PageUri = new Uri(Client.Current.Uris.RootUri, $"s/{imageKey.TokenToString()}/{Owner.Id}-{PageId}");
            this.thumbUri = thumb;
        }

        private static readonly Regex failTokenMatcher = new Regex(@"return\s+nl\(\s*'(.+?)'\s*\)", RegexOptions.Compiled);

        private IAsyncAction loadImageUri()
        {
            return Run(async token =>
            {
                var loadPageUri = default(Uri);
                if (this.failToken != null)
                    loadPageUri = new Uri(this.PageUri, $"?nl={failToken}");
                else
                    loadPageUri = this.PageUri;
                var loadPage = Client.Current.HttpClient.GetStringAsync(loadPageUri);
                var pageResult = new HtmlDocument();
                pageResult.LoadHtml(await loadPage);

                this.imageUri = new Uri(HtmlUtilities.ConvertToText(pageResult.GetElementbyId("img").GetAttributeValue("src", "")));
                var originalNode = pageResult.GetElementbyId("i7").Descendants("a").FirstOrDefault();
                if (originalNode == null)
                {
                    this.originalImageUri = null;
                }
                else
                {
                    this.originalImageUri = new Uri(HtmlUtilities.ConvertToText(originalNode.GetAttributeValue("href", "")));
                }
                var loadFail = pageResult.GetElementbyId("loadfail").GetAttributeValue("onclick", "");
                this.failToken = failTokenMatcher.Match(loadFail).Groups[1].Value;
            });
        }

        private ImageLoadingState state;

        public ImageLoadingState State
        {
            get => state;
            protected set => Set(ref state, value);
        }

        private Uri thumbUri;

        private readonly WeakReference<ImageSource> thumb = new WeakReference<ImageSource>(null);

        private static HttpClient thumbClient { get; } = new HttpClient();

        private void loadThumb()
        {
            DispatcherHelper.BeginInvokeOnUIThread(async () =>
            {
                var img = new BitmapImage();
                try
                {
                    if (this.imageFile != null)
                    {
                        using (var stream = await this.imageFile.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, thumbWidth * 18 / 10))
                        {
                            await img.SetSourceAsync(stream);
                        }
                    }
                    else if (this.thumbUri != null)
                    {
                        img.DecodePixelType = DecodePixelType.Logical;
                        img.DecodePixelWidth = 100;
                        var buffer = await thumbClient.GetBufferAsync(this.thumbUri);
                        using (var stream = buffer.AsRandomAccessStream())
                        {
                            await img.SetSourceAsync(stream);
                        }
                    }
                    else
                    {
                        img = null;
                    }
                }
                catch (Exception)
                {
                    img = null;
                }
                this.thumb.SetTarget(img);
                if (img != null)
                    RaisePropertyChanged(nameof(Thumb));
            });
        }

        public virtual ImageSource Thumb
        {
            get
            {
                if (this.thumb.TryGetTarget(out var thb))
                    return thb;
                loadThumb();
                return DefaultThumb;
            }
        }

        public Gallery Owner
        {
            get;
        }

        /// <summary>
        /// 1-based Id for image.
        /// </summary>
        public int PageId
        {
            get;
        }

        public Uri PageUri { get; }

        private IAsyncAction loadImageAction;

        public virtual IAsyncAction LoadImageAsync(bool reload, ConnectionStrategy strategy, bool throwIfFailed)
        {
            var previousAction = this.loadImageAction;
            switch (this.state)
            {
            case ImageLoadingState.Loading:
            case ImageLoadingState.Loaded:
                if (!reload)
                {
                    if (previousAction == null)
                        return AsyncWrapper.CreateCompleted();
                    return PollingAsyncWrapper.Wrap(previousAction, 1500);
                }
                break;
            case ImageLoadingState.Preparing:
                if (previousAction == null)
                    return AsyncWrapper.CreateCompleted();
                return PollingAsyncWrapper.Wrap(previousAction, 1500);
            }
            return this.loadImageAction = Run(async token =>
            {
                IAsyncAction load;
                IAsyncOperationWithProgress<HttpResponseMessage, HttpProgress> imageLoad = null;
                switch (this.state)
                {
                case ImageLoadingState.Waiting:
                case ImageLoadingState.Failed:
                    load = this.loadImageUri();
                    break;
                case ImageLoadingState.Loading:
                case ImageLoadingState.Loaded:
                    if (previousAction?.Status == AsyncStatus.Started)
                        previousAction.Cancel();
                    await this.deleteImageFile();
                    load = this.loadImageUri();
                    break;
                default:
                    return;
                }
                token.Register(() =>
                {
                    load.Cancel();
                    imageLoad?.Cancel();
                });
                this.State = ImageLoadingState.Preparing;
                this.Progress = 0;
                try
                {
                    await load;
                    Uri uri = null;
                    var loadFull = !ConnectionHelper.IsLofiRequired(strategy);
                    if (loadFull)
                    {
                        uri = this.originalImageUri ?? this.imageUri;
                        this.OriginalLoaded = true;
                    }
                    else
                    {
                        uri = this.imageUri;
                        this.OriginalLoaded = (this.originalImageUri == null);
                    }
                    this.State = ImageLoadingState.Loading;
                    imageLoad = Client.Current.HttpClient.GetAsync(uri);
                    imageLoad.Progress = (sender, progress) =>
                    {
                        if (this.State == ImageLoadingState.Loaded)
                        {
                            sender.Cancel();
                            return;
                        }
                        if (progress.TotalBytesToReceive == null || progress.TotalBytesToReceive == 0)
                            this.Progress = 0;
                        else
                        {
                            var pro = (int)(progress.BytesReceived * 100 / ((ulong)progress.TotalBytesToReceive));
                            this.Progress = pro;
                        }
                    };
                    token.ThrowIfCancellationRequested();
                    await this.deleteImageFile();
                    var imageLoadResponse = await imageLoad;
                    if (imageLoadResponse.Content.Headers.ContentType.MediaType == "text/html")
                        throw new InvalidOperationException(HtmlUtilities.ConvertToText(imageLoadResponse.Content.ToString()));
                    token.ThrowIfCancellationRequested();
                    var buffer = await imageLoadResponse.Content.ReadAsBufferAsync();
                    var ext = Path.GetExtension(imageLoadResponse.RequestMessage.RequestUri.LocalPath);
                    var pageId = this.PageId;
                    var folder = this.Owner.GalleryFolder ?? await this.Owner.GetFolderAsync();
                    this.ImageFile = await folder.SaveFileAsync($"{pageId}{ext}", CreationCollisionOption.ReplaceExisting, buffer);
                    using (var db = new Models.GalleryDb())
                    {
                        var gid = this.Owner.Id;
                        var myModel = db.ImageSet.SingleOrDefault(model => model.OwnerId == gid && model.PageId == pageId);
                        if (myModel == null)
                        {
                            db.ImageSet.Add(new Models.ImageModel().Update(this));
                        }
                        else
                        {
                            myModel.Update(this);
                        }
                        db.SaveChanges();
                    }
                    this.State = ImageLoadingState.Loaded;
                }
                catch (TaskCanceledException) { throw; }
                catch (Exception)
                {
                    this.Progress = 100;
                    this.State = ImageLoadingState.Failed;
                    if (throwIfFailed)
                        throw;
                }
            });
        }

        private async Task deleteImageFile()
        {
            var file = this.ImageFile;
            if (file != null)
            {
                this.ImageFile = null;
                await file.DeleteAsync();
            }
        }

        private int progress;

        public int Progress
        {
            get => progress;
            private set => Set(ref progress, value);
        }

        private Uri imageUri;
        private Uri originalImageUri;

        private StorageFile imageFile;

        public StorageFile ImageFile
        {
            get => this.imageFile;
            protected set
            {
                Set(ref this.imageFile, value);
                if (value != null)
                {
                    loadThumb();
                }
            }
        }

        private ulong imageKey;

        public ulong ImageKey
        {
            get => this.imageKey;
            protected set => Set(nameof(PageUri), ref this.imageKey, value);
        }

        private string failToken;

        public bool OriginalLoaded
        {
            get => this.originalLoaded;
            private set => Set(ref this.originalLoaded, value);
        }

        private bool originalLoaded;
    }
}