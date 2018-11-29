using System;
using System.Collections.Generic;
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
    public class PeopleController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        // CosmosDB
        private readonly DocumentClient _client;
        private const string _dbName = "JourneyNotesDB";
        private const string _collectionNamePerson = "Person";
        private const string _collectionNameTrip = "Trip";
        private const string _collectionNamePitstop = "Pitstop";

        // Queue
        private readonly CloudStorageAccount _storageAccount;
        private readonly CloudQueueClient _queueClient;
        private readonly CloudQueue _messageQueue;
        private const string _queueName = "tripqueue";

        // Blob
        private readonly CloudBlobClient _blobClient;
        private readonly CloudBlobContainer _container;
        private const string _containerName = "photos";

        public PeopleController(IConfiguration configuration)
        {
            _configuration = configuration;

            var accountName = _configuration["ConnectionStrings:StorageConnection:AccountName"];
            var accountKey = _configuration["ConnectionStrings:StorageConnection:AccountKey"];
            _storageAccount = new CloudStorageAccount(new StorageCredentials(accountName, accountKey), true);

            // CosmosDB
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


        /// <summary>
        /// Gets the customer details by CustomerID
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        // GET: api/people/5
        [HttpGet(Name = "GetPerson")]
        public ActionResult<string> GetPerson()
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666";
            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Person> query = _client.CreateDocumentQuery<Person>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePerson),
            $"SELECT * FROM C WHERE C.PersonId = '{UserID}'", queryOptions);
            var person = query.ToList().FirstOrDefault();

            return Ok(person);
        }

        /// <summary>
        /// Allows the user to update their profile (nickname and avatar (if from eg a dropdown list - if not need to add blob feature)
        /// </summary>
        /// <returns></returns>
        //put: api/person
        [HttpPut]
        public async Task<ActionResult<string>> PutPerson([FromBody] NewPerson editperson)
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666";

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Person> query = _client.CreateDocumentQuery<Person>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePerson),
            $"SELECT * FROM C WHERE C.PersonId = '{UserID}'", queryOptions);
            var personDB = query.ToList().FirstOrDefault();

            if (personDB != null)
            {
                string documentId = personDB.id;

                var documentUri = UriFactory.CreateDocumentUri(_dbName, _collectionNamePerson, documentId);

                Document document = await _client.ReadDocumentAsync(documentUri);

                personDB.PersonId = UserID;
                personDB.Nickname = editperson.Nickname;
                personDB.Avatar = editperson.Avatar;

                await _client.ReplaceDocumentAsync(document.SelfLink, personDB);

                return Ok(document.Id);
            }
            return NotFound();
        }

        /// <summary>
        /// Deletes the current users all trips and pitstops and logs them out...
        /// </summary>
        /// <returns></returns>
        //DELETE: api/ApiWithActions/5
        [HttpDelete()]
        public async Task<ActionResult<string>> DeletePerson()
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666";

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Person> query = _client.CreateDocumentQuery<Person>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePerson),
            $"SELECT * FROM C WHERE C.PersonId = '{UserID}'", queryOptions);
            var personDB = query.ToList().FirstOrDefault();

            //to delete a user - first deletes all of users pitstops, then trips and then the user

            if (personDB != null)
            {
                //get all the trips
                FeedOptions queryOptionsTrips = new FeedOptions { MaxItemCount = -1 };
                IQueryable<Trip> queryTrips = _client.CreateDocumentQuery<Trip>(
                UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
                $"SELECT * FROM C WHERE C.PersonId = '{UserID}'", queryOptionsTrips);
                var listOfTrips = queryTrips.ToList();

                foreach (Trip item in listOfTrips)
                {
                    //get all pitstops for the trip to be deleted
                    FeedOptions queryOptionsTripPS = new FeedOptions { MaxItemCount = -1 };
                    IQueryable<Pitstop> queryTripPS = _client.CreateDocumentQuery<Pitstop>(
                    UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop),
                    $"SELECT * FROM C where C.TripId = {item.TripId} AND C.PersonId = '{UserID}'", queryOptionsTripPS);
                    var pitstopList = queryTripPS.ToList();

                    foreach (var pitstop in pitstopList)
                    {
                        string documentId = pitstop.id;

                        try
                        {
                            await _client.DeleteDocumentAsync(
                            UriFactory.CreateDocumentUri(_dbName, _collectionNamePitstop, documentId));

                            // Removing images from the blob storage
                            string removed = await RemovePitstopImagesFromBlob(pitstop, _container);
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

                    //delete the trip
                    FeedOptions queryOptionsTrip = new FeedOptions { MaxItemCount = -1 };
                    IQueryable<Trip> queryTrip = _client.CreateDocumentQuery<Trip>(
                    UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
                    $"SELECT * FROM T WHERE T.TripId = {item.TripId} AND T.PersonId = '{UserID}'", queryOptionsTrip);
                    var trip = queryTrip.ToList().FirstOrDefault();

                    if (trip != null)
                    {
                        try
                        {
                            string TripDbId = trip.id;

                            await _client.DeleteDocumentAsync(UriFactory.CreateDocumentUri(_dbName, _collectionNameTrip, TripDbId));

                            // Removing images from the blob storage
                            string removed = await RemoveTripImagesFromBlob(trip, _container);
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
                }

                try
                {
                    string documentId = personDB.id;
                    await _client.DeleteDocumentAsync(UriFactory.CreateDocumentUri(_dbName, _collectionNamePerson, documentId));
                    return Ok($"Deleted user from Journey Notes");
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

        //Non-action
        private static async Task<string> RemoveTripImagesFromBlob(Trip trip, CloudBlobContainer container)
        {
            string smallImageName = trip.MainPhotoSmallUrl;
            string largeImageName = trip.MainPhotoUrl;
            CloudBlockBlob smallImage = container.GetBlockBlobReference(smallImageName);
            CloudBlockBlob largeImage = container.GetBlockBlobReference(largeImageName);

            using (var deleteStream = await smallImage.OpenReadAsync()) { }
            await smallImage.DeleteIfExistsAsync();

            using (var deleteStream = await largeImage.OpenReadAsync()) { }
            await largeImage.DeleteIfExistsAsync();

            return $"Deleted images {smallImageName} and {largeImageName}";
        }

        private static async Task<string> RemovePitstopImagesFromBlob(Pitstop pitstop, CloudBlobContainer container)
        {
            string smallImageName = pitstop.PhotoSmallUrl;
            string mediumImageName = pitstop.PhotoMediumUrl;
            string largeImageName = pitstop.PhotoLargeUrl;

            CloudBlockBlob smallImage = container.GetBlockBlobReference(smallImageName);
            CloudBlockBlob mediumImage = container.GetBlockBlobReference(mediumImageName);
            CloudBlockBlob largeImage = container.GetBlockBlobReference(largeImageName);

            using (var deleteStream = await smallImage.OpenReadAsync()) { }
            await smallImage.DeleteIfExistsAsync();

            using (var deleteStream = await mediumImage.OpenReadAsync()) { }
            await mediumImage.DeleteIfExistsAsync();

            using (var deleteStream = await largeImage.OpenReadAsync()) { }
            await largeImage.DeleteIfExistsAsync();

            return $"Deleted images {smallImageName}, {mediumImageName} and {largeImageName}";
        }

    }
}

