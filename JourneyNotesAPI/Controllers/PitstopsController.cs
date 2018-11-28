using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using JourneyEntities;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;

namespace JourneyNotesAPI.Controllers
{
    [EnableCors("MyPolicy")]
    [Route("api/[controller]")]
    [ApiController]
    public class PitstopsController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly DocumentClient _client;
        private const string _dbName = "JourneyNotesDB";
        private const string _collectionNamePerson = "Person";
        private const string _collectionNameTrip = "Trip";
        private const string _collectionNamePitstop = "Pitstop";

        // Queue
        private readonly CloudStorageAccount _storageAccount;
        private readonly CloudQueueClient _queueClient;
        private readonly CloudQueue _messageQueue;
        private const string _queueName = "journeynotes";

        // Blob
        private readonly CloudBlobClient _blobClient;
        private readonly CloudBlobContainer _container;
        private const string _containerName = "photos";

        public PitstopsController(IConfiguration configuration)
        {
            _configuration = configuration;
            var accountName = _configuration["ConnectionStrings:StorageConnection:AccountName"];
            var accountKey = _configuration["ConnectionStrings:StorageConnection:AccountKey"];
            _storageAccount = new CloudStorageAccount(new StorageCredentials(accountName, accountKey), true);

            // CosmosDb
            var endpointUri = _configuration["ConnectionStrings:CosmosDbConnection:EndpointUri"];
            var key = _configuration["ConnectionStrings:CosmosDbConnection:PrimaryKey"];
            _client = new DocumentClient(new Uri(endpointUri), key);

            // Queue
            _queueClient = _storageAccount.CreateCloudQueueClient();
            _messageQueue = _queueClient.GetQueueReference(_queueName);

            // Blob
            _blobClient = _storageAccount.CreateCloudBlobClient();
            _container = _blobClient.GetContainerReference(_containerName);

        }

        // We have everything in Azure so no need for this:
        //_client.CreateDatabaseIfNotExistsAsync(new Database
        //{
        //    Id = _dbName
        //}).Wait();

        //_client.CreateDocumentCollectionIfNotExistsAsync(
        //UriFactory.CreateDatabaseUri(_dbName),
        //new DocumentCollection { Id = _collectionNameTrip });

        // GET: api/Pitstop
        // No need for this, since you get them from api/trips/5.
        //[HttpGet]
        //public IEnumerable<string> GetPitstops()
        //{
        //    return new string[] { "value1", "value2" };
        //}

        // GET: api/pitstops/5
        // No need for this, since you get them from api/trips/5.
        //[HttpGet("{id}", Name = "GetPitstop")]
        //public ActionResult<Pitstop> GetPitstop(int id)
        //{
        //    FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
        //    IQueryable<Pitstop> query = _client.CreateDocumentQuery<Pitstop>(
        //    UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
        //    $"SELECT * FROM C WHERE C.PitstopId = {id}", queryOptions);
        //    Pitstop pitstopDetails = query.ToList().FirstOrDefault();

        //    return Ok(pitstopDetails);
        //}

        /// <summary>
        /// Adds a new Pitstop under the user and the chosen Trip
        /// </summary>
        /// <param name="newPitstop"></param>
        /// <returns></returns>
        // POST/Pitstop


        [HttpPost("{TripId}")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<string>> PostPitstop(NewPitstop newPitstop)
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666";

            //Check if tripID exisists in Trips...
            FeedOptions queryOptionsT = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Pitstop> queryT = _client.CreateDocumentQuery<Pitstop>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM C WHERE C.TripId = {newPitstop.TripId} AND C.PersonId = '{UserID}'", queryOptionsT);
            var Trip = queryT.ToList().Count;

            string photoName = await StorePicture(newPitstop.picture);

            if (Trip != 0)
            {
                // We need to get the TripId from the http request!
                Pitstop pitstop = new Pitstop();
                FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
                IQueryable<Pitstop> query = _client.CreateDocumentQuery<Pitstop>(
                UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop),
                $"SELECT * FROM C WHERE C.TripId = {newPitstop.TripId} AND C.PersonId = '{UserID}'", queryOptions);
                var pitstopCount = query.ToList().Count;

                if (pitstopCount == 0)
                    pitstopCount = 0;
                else
                    pitstopCount = query.ToList().Max(a => a.PitstopId);

                pitstop.PersonId = UserID;
                pitstop.PitstopId = pitstopCount + 1;
                pitstop.Title = newPitstop.Title;
                pitstop.Note = newPitstop.Note;
                pitstop.PitstopDate = newPitstop.PitstopDate;
                pitstop.PhotoLargeUrl = photoName; // will be replaced with the url to the resized image.
                pitstop.PhotoMediumUrl = string.Empty; // will be updated when the image has been resized.
                pitstop.PhotoSmallUrl = string.Empty; // will be updated when the image has been resized.
                pitstop.TripId = newPitstop.TripId;
                pitstop.pitstopPosition = newPitstop.pitstopPosition;
                pitstop.Address = newPitstop.Address;

                Document documentPitstop = await _client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop), pitstop);

                await AddQueueItem(new QueueParam { Id = documentPitstop.Id, PictureUri = photoName });

                //return Ok(documentPitstop.Id);
                return Ok($"Pitstop created under trip {pitstop.TripId}, id: {pitstop.PitstopId}");
            }
            return NotFound();
        }

        /// <summary>
        /// Updates a certain pitstop by PitstopId
        /// </summary>
        /// <param name="TripId"></param>
        /// <param name="PitstopId"></param>
        /// <param name="updatedPitstop"></param>
        /// <returns></returns>
        // PUT: api/pitstops/5
        [HttpPut("{TripId}/{PitstopId}")]
        public async Task<ActionResult<string>> PutPitstop([FromRoute] int TripId, [FromRoute] int PitstopId, [FromBody] NewPitstop updatedPitstop)
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666";

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Pitstop> query = _client.CreateDocumentQuery<Pitstop>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop),
            $"SELECT * FROM C WHERE C.PitstopId = {PitstopId} AND C.TripId = {TripId} AND C.PersonId = '{UserID}'", queryOptions);
            Pitstop pitstop = query.ToList().FirstOrDefault();

            if (pitstop != null)
            {
                pitstop.Title = updatedPitstop.Title;
                pitstop.Note = updatedPitstop.Note;
                pitstop.PitstopDate = updatedPitstop.PitstopDate;
                pitstop.pitstopPosition = updatedPitstop.pitstopPosition;
                pitstop.Address = updatedPitstop.Address;

                string documentId = pitstop.id;

                var documentUri = UriFactory.CreateDocumentUri(_dbName, _collectionNamePitstop, documentId);

                Document document = await _client.ReadDocumentAsync(documentUri);

                await _client.ReplaceDocumentAsync(document.SelfLink, pitstop);

                return Ok(document.Id);
            }
            return NotFound();
        }

        /// <summary>
        /// Deletes a certain Pitstop by PitstopId
        /// </summary>
        /// <param name="PitstopId"></param>
        /// <param name="TripId"></param>
        /// <returns></returns>
        // DELETE: api/ApiWithActions/5
        //[HttpDelete("{TripId}", Name = "TripId")]
        [HttpDelete("{TripId}/{PitstopId}")]
        public async Task<ActionResult<string>> DeletePitstop([FromRoute] int TripId, int PitstopId)
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666";

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Pitstop> query = _client.CreateDocumentQuery<Pitstop>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop),
            //$"SELECT * FROM C WHERE C.PitstopId = {PitstopId} AND C.PersonId = {person}", queryOptions);
            $"SELECT * FROM C where C.TripId = {TripId} AND C.PersonId = '{UserID}' AND C.PitstopId = {PitstopId}", queryOptions);
            var pitstop = query.ToList().FirstOrDefault();

            if (pitstop != null)
            {
                try
                {
                    string DbId = pitstop.id;
                    await _client.DeleteDocumentAsync(
                     UriFactory.CreateDocumentUri(_dbName, _collectionNamePitstop, DbId));
                    return Ok($"Deleted pitstop {PitstopId}");
                }
                catch (DocumentClientException de)
                {
                    switch (de.StatusCode.Value)
                    {
                        case System.Net.HttpStatusCode.NotFound:
                            return NotFound();
                    }
                }
            }
            return NotFound();
        }

        [NonAction]
        private async Task<string> StorePicture(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName);

            try
            {
                CloudBlockBlob blockBlob = _container.GetBlockBlobReference(Guid.NewGuid().ToString() + ext);
                blockBlob.Metadata.Add("FileName", file.FileName);
                if (file.Length > 0)
                {
                    using (var fileStream = file.OpenReadStream())
                    {
                        await blockBlob.UploadFromStreamAsync(fileStream);
                    }
                }
                return blockBlob.Name;

            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine(e.StackTrace);
                return null;
            }

        }

        [NonAction]
        private async Task AddQueueItem(QueueParam queueParam)
        {
            CloudQueueMessage message = new CloudQueueMessage(queueParam.ToJson());
            await _messageQueue.AddMessageAsync(message);
        }
    }
}
