using System;
using System.IO;
using System.Threading.Tasks;
using JourneyEntities;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TripPhotoFunctionApp
{
    public static class GenerateTripPhotos
    {
        const int SmallPhotoBiggerSide = 270;
        const int BigPhotoBiggerSide = 800;

        static GenerateTripPhotos()
        {
            string s = $"Starting--- (ctor)";
        }

        [FunctionName("GenerateTripPhotos")]
        public static async void Run([QueueTrigger("tripqueue", Connection = "Connection")]string QueueItem, ILogger log, ExecutionContext context)
        {
            log.LogInformation($"Resizing image: {QueueItem}");
            QueueParam item = QueueParam.FromJson(QueueItem);

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            string storageName = config["Storage"];
            string containerName = config["Container"];

            CloudBlobContainer container = GetBlobReference(storageName, containerName); // method below

            // Resizing the images
            string smallImageName = await StoreImageAsync(item.PictureUri, container); // method below
            string originalStorageImageName = await ReplaceBigStoreImageAsync(item.PictureUri, container); // method below

            // Updating the trip object in the db
            await UpdateDocumentSmallImageUrl(item.Id, smallImageName, config); // method below
            await UpdateDocumentBigImageUrl(item.Id, originalStorageImageName, config); // method below

            log.LogInformation($"The image resized and saved. Small image name: {smallImageName}, resized original image name: {originalStorageImageName}");

        }

        private static CloudBlobContainer GetBlobReference(string storage, string containerName)
        {
            var storageAccount = CloudStorageAccount.Parse(storage);
            var blobClient = storageAccount.CreateCloudBlobClient();
            return blobClient.GetContainerReference(containerName);
        }

        private static async Task<string> StoreImageAsync(string blobName, CloudBlobContainer container)
        {
            CloudBlockBlob pictureBlob = container.GetBlockBlobReference(blobName);
            CloudBlockBlob smallPictureBlob = container.GetBlockBlobReference(Guid.NewGuid().ToString() + ".jpeg");

            // Adding some metadata (connect to the original image)
            smallPictureBlob.Metadata.Add("Type", "small");
            smallPictureBlob.Metadata.Add("Original", blobName);

            // Image resizing
            using (var imageStream = await pictureBlob.OpenReadAsync())
            {
                // Using SixLabors.ImageSharp library here
                Image<Rgba32> originalImage = Image.Load(imageStream);

                originalImage.Size();
                var oldWidth = originalImage.Width;
                var oldHeight = originalImage.Height;

                // checking the ratio + the new size
                if (originalImage.Width == originalImage.Height)
                {
                    originalImage.Mutate(x => x.Resize(SmallPhotoBiggerSide, SmallPhotoBiggerSide));
                }
                else if (originalImage.Width < originalImage.Height)
                {
                    var newHeight = SmallPhotoBiggerSide;
                    var newWidth = (newHeight * oldWidth) / oldHeight;

                    originalImage.Mutate(x => x.Resize(newWidth, newHeight));
                }
                else
                {
                    var newWidth = SmallPhotoBiggerSide;
                    var newHeight = (newWidth * oldHeight) / oldWidth;

                    originalImage.Mutate(x => x.Resize(newWidth, newHeight));
                }

                MemoryStream memoStream = new MemoryStream();
                originalImage.SaveAsJpeg(memoStream);
                memoStream.Position = 0;

                await smallPictureBlob.UploadFromStreamAsync(memoStream);
            }

            return smallPictureBlob.Name;
        }

        private static async Task<string> ReplaceBigStoreImageAsync(string blobName, CloudBlobContainer container)
        {
            CloudBlockBlob pictureBlob = container.GetBlockBlobReference(blobName);
            CloudBlockBlob newSizePictureBlob = container.GetBlockBlobReference(Guid.NewGuid().ToString() + ".jpeg");

            // Adding some metadata (connect to the original image)
            newSizePictureBlob.Metadata.Add("Type", "big");

            // Image resizing
            using (var imageStream = await pictureBlob.OpenReadAsync())
            {
                // Using SixLabors.ImageSharp library here
                Image<Rgba32> originalImage = Image.Load(imageStream);

                originalImage.Size();
                var oldWidth = originalImage.Width;
                var oldHeight = originalImage.Height;

                // checking the ratio + the new size
                if (originalImage.Width == originalImage.Height)
                {
                    originalImage.Mutate(x => x.Resize(BigPhotoBiggerSide, BigPhotoBiggerSide));
                }
                else if (originalImage.Width < originalImage.Height)
                {
                    var newHeight = BigPhotoBiggerSide;
                    var newWidth = (newHeight * oldWidth) / oldHeight;

                    originalImage.Mutate(x => x.Resize(newWidth, newHeight));
                }
                else
                {
                    var newWidth = BigPhotoBiggerSide;
                    var newHeight = (newWidth * oldHeight) / oldWidth;

                    originalImage.Mutate(x => x.Resize(newWidth, newHeight));
                }

                MemoryStream memoStream = new MemoryStream();
                originalImage.SaveAsJpeg(memoStream);
                memoStream.Position = 0;

                await newSizePictureBlob.UploadFromStreamAsync(memoStream);
            }

            await pictureBlob.DeleteIfExistsAsync();

            return newSizePictureBlob.Name;
        }

        private static async Task UpdateDocumentSmallImageUrl(string documentId, string smallImageUrl, IConfiguration conf)
        {
            var endpointUri = conf["CosmosEndpointUri"];
            var key = conf["CosmosPrimaryKey"];
            var databaseName = conf["CosmosDbName"];
            var collectionName = conf["CosmosCollectionTrip"];

            // using Microsoft.Azure.DocumentDB.Core library
            DocumentClient documentClient = new DocumentClient(new Uri(endpointUri), key);

            // Finding and updating the trip that needs to be updated with the new image:
            var documentUri = UriFactory.CreateDocumentUri(databaseName, collectionName, documentId);
            Trip trip = await documentClient.ReadDocumentAsync<Trip>(documentUri);
            trip.MainPhotoSmallUrl = smallImageUrl;
            await documentClient.ReplaceDocumentAsync(documentUri, trip);
        }

        private static async Task UpdateDocumentBigImageUrl(string documentId, string bigImageUrl, IConfiguration conf)
        {
            var endpointUri = conf["CosmosEndpointUri"];
            var key = conf["CosmosPrimaryKey"];
            var databaseName = conf["CosmosDbName"];
            var collectionName = conf["CosmosCollectionTrip"];

            // using Microsoft.Azure.DocumentDB.Core library
            DocumentClient documentClient = new DocumentClient(new Uri(endpointUri), key);

            // Finding and updating the trip that needs to be updated with the new image:
            var documentUri = UriFactory.CreateDocumentUri(databaseName, collectionName, documentId);
            Trip trip = await documentClient.ReadDocumentAsync<Trip>(documentUri);
            trip.MainPhotoUrl = bigImageUrl;
            await documentClient.ReplaceDocumentAsync(documentUri, trip);
        }

    }
}
