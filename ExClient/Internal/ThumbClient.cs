using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Web.Http;

namespace ExClient.Internal {
    internal static class ThumbClient {
        private static readonly HttpClient _Client = new();

        private static readonly SemaphoreSlim _CacheLock = new SemaphoreSlim(1, 1);

        private static readonly string _ImageExtension = ".png";
        private static readonly int _MaxCacheSize = 800;
        private static readonly Queue<Uri> _KeyCache = new();
        private static readonly Dictionary<Uri, IBuffer> _Cache = new();
        private static readonly SHA256 _SHA256 = SHA256.Create();

        private static readonly SemaphoreSlim _ProcessingLock = new SemaphoreSlim(
            4,
            Math.Max(4, Environment.ProcessorCount)
        );

        public static Uri FormatThumbUri(string uri) {
            if (uri.IsNullOrWhiteSpace())
                return null;
            return new Uri(uri);
        }

        private static string GetHash(string input) {
            if (input.IsNullOrWhiteSpace())
                return null;
            var bytes = _SHA256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        public static Uri FormatThumbUri(Uri uri) => FormatThumbUri(uri?.ToString());

        public static IAsyncOperation<bool> FetchThumbAsync(
            Uri source,
            BitmapImage target,
            int pageId = -1
        ) {
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
                    await PushCache(source, buf);
                }
                using (var stream = buf.AsRandomAccessStream()) {
                    if (pageId == -1) {
                        await target.SetSourceAsync(stream);
                    } else {
                        await CropThumbnailAsync(source, stream, target, pageId);
                    }
                }
            } catch (Exception) {
                return false;
            }
            return true;
        }

        private static async Task CropThumbnailAsync(
            Uri source,
            IRandomAccessStream stream,
            BitmapImage target,
            int pageId,
            int maxThumbnailCount = 20,
            int thumbnailWidth = 200
        ) {
            try {
                await _ProcessingLock.WaitAsync();
                StorageFile file = null;

                var hashFileName = GetHash(source.AbsoluteUri + pageId) + _ImageExtension;
                file = await ApplicationData.Current.LocalFolder.TryGetFileAsync(hashFileName);
                if (file == null) {
                    using (
                        var image = Mat.FromStream(stream.AsStreamForRead(), ImreadModes.Unchanged)
                    ) {
                        int totalThumbnails = image.Width / thumbnailWidth;
                        int index = (pageId - 1) % maxThumbnailCount;
                        int x = index * thumbnailWidth;

                        using var originalThumb = new Mat(
                            image,
                            new OpenCvSharp.Rect(x, 0, thumbnailWidth, image.Height)
                        );
                        using var croppedImage = TrimTransparentBackground(originalThumb);
                        file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                            hashFileName,
                            CreationCollisionOption.ReplaceExisting
                        );
                        croppedImage.SaveImage(file.Path);
                    }
                }

                using (var thumbStream = await file.OpenAsync(FileAccessMode.Read)) {
                    await target.SetSourceAsync(thumbStream);
                }
            } finally {
                _ProcessingLock.Release();
            }
        }

        public static Mat TrimTransparentBackground(Mat source, byte alphaThreshold = 5) {
            if (source == null || source.Empty()) {
                throw new ArgumentNullException(nameof(source), "源图像不能为空。");
            }

            if (source.Channels() != 4) {
                Console.WriteLine("警告：图像没有Alpha通道，无法按透明度修剪。");
                return source;
            }

            // 提取Alpha通道
            Mat alphaChannel = new Mat();
            Cv2.ExtractChannel(source, alphaChannel, 3);

            // 创建二值掩码，标记非透明区域
            Mat mask = new Mat();
            Cv2.Threshold(alphaChannel, mask, alphaThreshold, 255, ThresholdTypes.Binary);

            // 查找非零点（非透明区域）
            Mat nonZeroPoints = new Mat();
            Cv2.FindNonZero(mask, nonZeroPoints);

            if (nonZeroPoints.Empty()) {
                // 图像完全透明
                return source;
            }

            // 直接从点集计算边界框
            OpenCvSharp.Rect boundingRect = Cv2.BoundingRect(nonZeroPoints);

            // 释放中间过程的Mat对象
            alphaChannel.Dispose();
            mask.Dispose();
            nonZeroPoints.Dispose();

            // 从原始图像中裁剪出目标区域
            return new Mat(source, boundingRect);
        }

        private static async Task PushCache(Uri uri, IBuffer buffer) {
            try {
                await _CacheLock.WaitAsync();
                if (_Cache.ContainsKey(uri)) {
                    _Cache[uri] = buffer;
                } else {
                    if (_Cache.Count >= _MaxCacheSize) {
                        var key = _KeyCache.Dequeue();
                        _Cache.Remove(key);
                    }
                    _KeyCache.Enqueue(uri);
                    _Cache.Add(uri, buffer);
                }
            } finally {
                _CacheLock.Release();
            }
        }

        private static bool TryGetCache(Uri uri, out IBuffer buffer) {
            if (_Cache.ContainsKey(uri)) {
                buffer = _Cache[uri];
                return true;
            }

            buffer = null;
            return false;
        }
    }
}
