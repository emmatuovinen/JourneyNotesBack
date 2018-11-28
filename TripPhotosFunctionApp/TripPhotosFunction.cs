using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TripPhotosFunctionApp
{
    public static class TripPhotosFunction
    {
        static int SmallPhotoBiggerSide = 270;
        static int BigPhotoBiggerSide = 800;

        [FunctionName("TripPhotosFunction")]
        public static async Task RunAsync([QueueTrigger("tripqueue", Connection = "Storage")]string QueueItem, ILogger log, ExecutionContext context)
        {
            log.LogInformation($"Resizing image: {QueueItem}");
            QueueParam item = QueueParam.FromJson(QueueItem);

            string storageName = "DefaultEndpointsProtocol=https;AccountName=journeynotes;AccountKey=1jxEaxOXJyzg6rfX7WcQ5BOqspV+AQuiMJb8QaHyaG7lH57+09QYsV4fTKSR5kX+E80+eILrcdfD76SwtL7png==;EndpointSuffix=core.windows.net";
            string containerName = "photos";

            CloudBlobContainer container = GetBlobReference(storageName, containerName); // method below

            // Resizing the images
            string smallImageName = await StoreImageAsync(item.PictureUri, container); // method below
            string originalStorageImageName = await ReplaceBigStoreImageAsync(item.PictureUri, container); // method below

            // Updating the trip object in the db
            await UpdateDocumentSmallImageUrl(item.Id, smallImageName); // method below
            await UpdateDocumentBigImageUrl(item.Id, originalStorageImageName); // method below

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

                if ((oldWidth > SmallPhotoBiggerSide) || (oldHeight > SmallPhotoBiggerSide))
                {
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

                if ((oldWidth > BigPhotoBiggerSide) || (oldHeight > BigPhotoBiggerSide))
                {
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
                }

                MemoryStream memoStream = new MemoryStream();
                originalImage.SaveAsJpeg(memoStream);
                memoStream.Position = 0;

                await newSizePictureBlob.UploadFromStreamAsync(memoStream);
            }

            await pictureBlob.DeleteIfExistsAsync();

            return newSizePictureBlob.Name;
        }

        private static async Task UpdateDocumentSmallImageUrl(string documentId, string smallImageUrl)
        {
            string endpointUri = "https://journeynotes.documents.azure.com:443/";
            string key = "8xVQC2IvcmhQE9x1pj9g11h8LfNmX4YiBwHw4wnXG4Ww2qcDMl16AzsJKC503JpB4zLiTI4UBTHVhZTAkxocOg==";
            string databaseName = "JourneyNotesDB";
            string collectionName = "Trip";

            // using Microsoft.Azure.DocumentDB.Core library
            DocumentClient documentClient = new DocumentClient(new Uri(endpointUri), key);

            // Finding and updating the trip that needs to be updated with the new image:
            var documentUri = UriFactory.CreateDocumentUri(databaseName, collectionName, documentId);
            Trip trip = await documentClient.ReadDocumentAsync<Trip>(documentUri);
            trip.MainPhotoSmallUrl = smallImageUrl;
            await documentClient.ReplaceDocumentAsync(documentUri, trip);
        }

        private static async Task UpdateDocumentBigImageUrl(string documentId, string bigImageUrl)
        {
            string endpointUri = "https://journeynotes.documents.azure.com:443/";
            string key = "8xVQC2IvcmhQE9x1pj9g11h8LfNmX4YiBwHw4wnXG4Ww2qcDMl16AzsJKC503JpB4zLiTI4UBTHVhZTAkxocOg==";
            string databaseName = "JourneyNotesDB";
            string collectionName = "Trip";

            // using Microsoft.Azure.DocumentDB.Core library
            DocumentClient documentClient = new DocumentClient(new Uri(endpointUri), key);

            // Finding and updating the trip that needs to be updated with the new image:
            var documentUri = UriFactory.CreateDocumentUri(databaseName, collectionName, documentId);
            Trip trip = await documentClient.ReadDocumentAsync<Trip>(documentUri);
            trip.MainPhotoUrl = bigImageUrl;
            await documentClient.ReplaceDocumentAsync(documentUri, trip);
        }
    }

    public class QueueParam
    {
        public string Id { get; set; }

        public string PictureUri { get; set; }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }

        public static QueueParam FromJson(string json)
        {
            return JsonConvert.DeserializeObject<QueueParam>(json);
        }
    }

    public class Trip
    {
        public int TripId { get; set; }

        public string PersonId { get; set; }

        public string Headline { get; set; }

        public string Description { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public string MainPhotoUrl { get; set; }

        public string MainPhotoSmallUrl { get; set; }

        public string id { get; set; }

        //public IFormFile picture { get; set; }
    }
}
