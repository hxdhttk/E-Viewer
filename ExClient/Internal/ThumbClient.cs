using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Web.Http;

namespace ExClient.Internal {
    internal static class ThumbClient {
        private static readonly HttpClient _Client = new();

        private static readonly PngEncoder _PngEncoder = new PngEncoder();
        private static readonly Queue<Uri> _KeyCache = new();
        private static readonly Dictionary<Uri, IBuffer> _Cache = new();

        public static Uri FormatThumbUri(string uri) {
            if (uri.IsNullOrWhiteSpace())
                return null;
            return new Uri(uri);
        }

        public static Uri FormatThumbUri(Uri uri) => FormatThumbUri(uri?.ToString());

        public static IAsyncOperation<bool> FetchThumbAsync(Uri source, BitmapImage target, int pageId = -1) {
            if (source is null)
                throw new ArgumentNullException(nameof(source));
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            return AsyncInfo.Run(async token => {
                return await loadThumbAsync(source, target, pageId);
            });
        }

        private static async Task<bool> loadThumbAsync(Uri source, BitmapImage target, int pageId) {
            try {
                if (!TryGetCache(source, out var buf)) {
                    buf = await _Client.GetBufferAsync(source);
                    PushCache(source, buf);
                }
                using (var stream = buf.AsRandomAccessStream()) {
                    if (pageId == -1) {
                        await target.SetSourceAsync(stream);
                    } else {
                        await CropThumbnailAsync(stream, target, pageId);
                    }
                }
            } catch (Exception) {
                return false;
            }
            return true;
        }

        private static async Task CropThumbnailAsync(IRandomAccessStream stream, BitmapImage target, int pageId, int maxThumbnailCount = 20, int thumbnailWidth = 200) {

            using (Image image = await Image.LoadAsync(stream.AsStreamForRead())) {
                int totalThumbnails = image.Width / thumbnailWidth;
                int index = (pageId - 1) % maxThumbnailCount;
                int x = index * thumbnailWidth;

                image.Mutate(ctx => ctx.Crop(
                    new Rectangle(x, 0, thumbnailWidth, image.Height)));

                using (MemoryStream ms = new MemoryStream()) {
                    await image.SaveAsync(ms, _PngEncoder);
                    ms.Position = 0;

                    target.SetSource(ms.AsRandomAccessStream());
                }
            }
        }

        private static void PushCache(Uri uri, IBuffer buffer) {
            lock (_Cache) {
                if (_Cache.ContainsKey(uri)) {
                    _Cache[uri] = buffer;
                } else {
                    lock (_KeyCache) {
                        if (_Cache.Count >= 20) {
                            var key = _KeyCache.Dequeue();
                            _Cache.Remove(key);
                        }
                        _KeyCache.Enqueue(uri);
                        _Cache.Add(uri, buffer);
                    }
                }
            }
        }

        private static bool TryGetCache(Uri uri, out IBuffer buffer) {
            lock (_Cache) {
                if (_Cache.ContainsKey(uri)) {
                    buffer = _Cache[uri];
                    return true;
                }

                buffer = null;
                return false;
            }
        }
    }
}
