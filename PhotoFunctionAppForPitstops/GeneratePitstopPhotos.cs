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

namespace PhotoFunctionAppForPitstops
{
    public static class GeneratePitstopPhotos
    {
        const int LargePhotoBiggerSide = 800;
        const int MediumPhotoBiggerSide = 500;
        const int SmallPhotoBiggerSide = 270;

        [FunctionName("PitstopPhotos")]
        public static async void ResizePitstopPhotosAsync([QueueTrigger("journeynotespitstops", Connection = "queueConnection")]string QueueItem, ILogger log, ExecutionContext context)
        {
            log.LogInformation($"Resizing pitstop image: {QueueItem}");
            QueueParam item = QueueParam.FromJson(QueueItem);

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            string storageName = config["Storage"];
            string containerName = config["Container"];

            CloudBlobContainer container = GetBlobReference(storageName, containerName); // method below

            // Resizing the image and naming the images
            string smallImageName = await StoreSmallImage(item.PictureUri, container); // method below
            string mediumStorageImageName = await StoreMediumImage(item.PictureUri, container); // method below
            string originalStorageImageName = await ReplaceLargeStoreImageAsync(item.PictureUri, container); // method below

            // Updating the trip object in the db
            await UpdateDocumentSmallImageUrl(item.Id, smallImageName, config); // method below
            await UpdateDocumentMediumImageUrl(item.Id, mediumStorageImageName, config); // method below
            await UpdateDocumentLargeImageUrl(item.Id, originalStorageImageName, config); // method below

            log.LogInformation($"The image resized and saved. Small image name: {smallImageName}, medium image name: {mediumStorageImageName}, large image name: {originalStorageImageName}");

        }

        private static CloudBlobContainer GetBlobReference(string storage, string containerName)
        {
            var storageAccount = CloudStorageAccount.Parse(storage);
            var blobClient = storageAccount.CreateCloudBlobClient();
            return blobClient.GetContainerReference(containerName);
        }

        private static async Task<string> StoreSmallImage(string blobName, CloudBlobContainer container)
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

        private static async Task<string> StoreMediumImage(string blobName, CloudBlobContainer container)
        {
            CloudBlockBlob pictureBlob = container.GetBlockBlobReference(blobName);
            CloudBlockBlob mediumPictureBlob = container.GetBlockBlobReference(Guid.NewGuid().ToString() + ".jpeg");

            // Adding some metadata (connect to the original image)
            mediumPictureBlob.Metadata.Add("Type", "medium");

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
                    originalImage.Mutate(x => x.Resize(MediumPhotoBiggerSide, MediumPhotoBiggerSide));
                }
                else if (originalImage.Width < originalImage.Height)
                {
                    var newHeight = MediumPhotoBiggerSide;
                    var newWidth = (newHeight * oldWidth) / oldHeight;

                    originalImage.Mutate(x => x.Resize(newWidth, newHeight));
                }
                else
                {
                    var newWidth = MediumPhotoBiggerSide;
                    var newHeight = (newWidth * oldHeight) / oldWidth;

                    originalImage.Mutate(x => x.Resize(newWidth, newHeight));
                }

                MemoryStream memoStream = new MemoryStream();
                originalImage.SaveAsJpeg(memoStream);
                memoStream.Position = 0;

                await mediumPictureBlob.UploadFromStreamAsync(memoStream);
            }
            return mediumPictureBlob.Name;
        }

        private static async Task<string> ReplaceLargeStoreImageAsync(string blobName, CloudBlobContainer container)
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
                    originalImage.Mutate(x => x.Resize(LargePhotoBiggerSide, LargePhotoBiggerSide));
                }
                else if (originalImage.Width < originalImage.Height)
                {
                    var newHeight = LargePhotoBiggerSide;
                    var newWidth = (newHeight * oldWidth) / oldHeight;

                    originalImage.Mutate(x => x.Resize(newWidth, newHeight));
                }
                else
                {
                    var newWidth = LargePhotoBiggerSide;
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
            string endpointUri = conf["CosmosEndpointUri"];
            string key = conf["CosmosPrimaryKey"];
            string databaseName = conf["CosmosDbName"];
            string collectionName = conf["CosmosCollectionPitstop"];

            // using Microsoft.Azure.DocumentDB.Core library
            DocumentClient documentClient = new DocumentClient(new Uri(endpointUri), key);

            // Finding and updating the trip that needs to be updated with the new image:
            var documentUri = UriFactory.CreateDocumentUri(databaseName, collectionName, documentId);
            Pitstop pitstop = await documentClient.ReadDocumentAsync<Pitstop>(documentUri);
            pitstop.PhotoSmallUrl = smallImageUrl;            
            await documentClient.ReplaceDocumentAsync(documentUri, pitstop);
        }

        private static async Task UpdateDocumentMediumImageUrl(string documentId, string mediumImageUrl, IConfiguration conf)
        {
            string endpointUri = conf["CosmosEndpointUri"];
            string key = conf["CosmosPrimaryKey"];
            string databaseName = conf["CosmosDbName"];
            string collectionName = conf["CosmosCollectionPitstop"];

            // using Microsoft.Azure.DocumentDB.Core library
            DocumentClient documentClient = new DocumentClient(new Uri(endpointUri), key);

            // Finding and updating the trip that needs to be updated with the new image:
            var documentUri = UriFactory.CreateDocumentUri(databaseName, collectionName, documentId);
            Pitstop pitstop = await documentClient.ReadDocumentAsync<Pitstop>(documentUri);
            pitstop.PhotoMediumUrl = mediumImageUrl;
            await documentClient.ReplaceDocumentAsync(documentUri, pitstop);
        }

        private static async Task UpdateDocumentLargeImageUrl(string documentId, string largeImageUrl, IConfiguration conf)
        {
            string endpointUri = conf["CosmosEndpointUri"];
            string key = conf["CosmosPrimaryKey"];
            string databaseName = conf["CosmosDbName"];
            string collectionName = conf["CosmosCollectionPitstop"];

            // using Microsoft.Azure.DocumentDB.Core library
            DocumentClient documentClient = new DocumentClient(new Uri(endpointUri), key);

            // Finding and updating the trip that needs to be updated with the new image:
            var documentUri = UriFactory.CreateDocumentUri(databaseName, collectionName, documentId);
            Pitstop pitstop = await documentClient.ReadDocumentAsync<Pitstop>(documentUri);
            pitstop.PhotoLargeUrl = largeImageUrl;
            await documentClient.ReplaceDocumentAsync(documentUri, pitstop);
        }

    }
}
