using System;
using System.Linq;
using System.Configuration;
using System.Threading;
using System.IO;
using Microsoft.WindowsAzure.MediaServices.Client;

namespace MediaService
{
    class Program
    {
        //App.config file values
        private static readonly string MediaServiceAccountName = ConfigurationManager.AppSettings["MediaServicesAccountName"];
        private static readonly string MediaServiceAccountKey = ConfigurationManager.AppSettings["MediaServicesAccountKey"];

        //service context
        private static CloudMediaContext _context;
        private static MediaServicesCredentials _cachedCredentials;

        private const string StandardEncoder = "Media Encoder Standard";
        private const string Bitrate = "H264 Multiple Bitrate 720p";
        private const string AdaptiveBitrate = "Adaptive Bitrate MP4";

        static void Main(string[] args)
        {
            try
            {
                // Create and cache the Media Services credentials in a static class variable.
                _cachedCredentials = new MediaServicesCredentials(MediaServiceAccountName, MediaServiceAccountKey);

                // Used the chached credentials to create CloudMediaContext.
                _context = new CloudMediaContext(_cachedCredentials);

                var asset = UploadFile(@"E:\R&D\media_service\MediaService\death.jpg", AssetCreationOptions.None);

                var encodedAsset = EncodeToAdaptiveBitrateMp4(asset, AssetCreationOptions.None);

                PublishAssetGetUrl(encodedAsset);
            }
            catch (Exception ex)
            {
                // Parse the XML error message in the Media Services response and create a new
                // exception with its content.

                ex = MediaServicesExceptionParser.Parse(ex);

                Console.Error.WriteLine(ex.Message);
            }
            finally
            {
                Console.ReadLine();
            }
        }

        public static IAsset UploadFile(string fileName, AssetCreationOptions options)
        {
            //CreateFromFile creates a new asset into which the specified source file is uploaded.
            var inputAsset = _context.Assets.CreateFromFile(fileName, options,
                (af, p) =>
                    Console.WriteLine("Uploading '{0}' - progress: {1:0.##}%", af.Name, p.Progress));

            Console.WriteLine($"Asset {inputAsset.Id} created");

            return inputAsset;
        }

        /// Prepare a job with a single task to transcode the specified asset
        /// into a multi-bitrate asset.
        public static IAsset EncodeToAdaptiveBitrateMp4(IAsset asset, AssetCreationOptions options)
        {
            
            var job = _context.Jobs.CreateWithSingleTask(
                StandardEncoder,
                Bitrate,
                asset,
                AdaptiveBitrate,
                options);

            Console.WriteLine("Submitting transcoding job...");

            //submit job, wait for completion
            job.Submit();

            job = job.StartExecutionProgressTask(
                j =>
                {
                    Console.WriteLine($"Job state: {j.State}");
                    Console.WriteLine("Job Progress: {0:0.##}%", j.GetOverallProgress());
                },
                CancellationToken.None).Result;

            Console.WriteLine("Transcoding job finished");

            var outputAsset = job.OutputMediaAssets[0];

            return outputAsset;
        }

        /// Publish the output asset by creating an Origin locator for adaptive streaming,
        /// and a SAS locator for progressive download.
        public static void PublishAssetGetUrl(IAsset asset)
        {
            _context.Locators.Create(
                LocatorType.OnDemandOrigin,
                asset,
                AccessPermissions.Read,
                TimeSpan.FromDays(30));

            _context.Locators.Create(
                LocatorType.Sas,
                asset,
                AccessPermissions.Read,
                TimeSpan.FromDays(30)
                );

            var mp4AssetFiles =
                asset.AssetFiles.ToList().Where(af => af.Name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));

            // Get the Smooth Streaming, HLS and MPEG-DASH URLs for adaptive streaming,
            // and the Progressive Download URL.
            var uriSmoothStreaming = asset.GetSmoothStreamingUri();
            var uriHls = asset.GetHlsUri();
            var uriMpegDash = asset.GetMpegDashUri();

            // Get the URls for progressive download for each MP4 file that was generated as a result
            // of encoding.
            var mp4ProgressiveDownloadUris = mp4AssetFiles.Select(af => af.GetSasUri()).ToList();

            Console.WriteLine("Urls for Adaptive streaming:");
            Console.WriteLine(uriSmoothStreaming);
            Console.WriteLine(uriHls);
            Console.WriteLine(uriMpegDash);
            Console.WriteLine();

            Console.WriteLine("Progressive Download Urls");
            foreach (var uri in mp4ProgressiveDownloadUris)
            {
                Console.WriteLine($"{uri}\n");
            }

            Console.WriteLine();

            // Download the output asset to a local folder.
            const string outputFolder = "Downloaded";

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            Console.WriteLine();
            Console.WriteLine("Downloading output asset files to a local folder...");

            asset.DownloadToFolder(
                outputFolder,
                (af, p) =>
                {
                    Console.WriteLine("Downloading '{0}' - Progress: {1:0.##}%", af.Name, p.Progress);
                });

            Console.WriteLine($"Output asset files available at '{Path.GetFullPath(outputFolder)}'.");
        }
    }
}
