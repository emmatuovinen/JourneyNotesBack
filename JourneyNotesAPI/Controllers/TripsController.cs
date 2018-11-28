using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using JourneyEntities;
using Microsoft.AspNetCore.Authorization;
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
using Newtonsoft.Json.Linq;

namespace JourneyNotesAPI.Controllers
{
    [EnableCors("MyPolicy")]
    [Route("api/[controller]")]
    [ApiController]
    public class TripsController : ControllerBase
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

        public TripsController(IConfiguration configuration)
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
        /// Gets all the trips of the user by the users id (comes as authentication data)
        /// </summary>
        /// <param name="userID"></param>
        /// <returns></returns>
        // GET: api/Trips
        [HttpGet]
        public async Task<ActionResult<IEnumerable<string>>> GetTrips()
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666";
            var triplist = new List<Trip>();

            //Check if user exists
            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Person> query = _client.CreateDocumentQuery<Person>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePerson),
            $"SELECT * FROM C WHERE C.PersonId = '{UserID}'", queryOptions);
            var userCount = query.ToList().Count();

            if (userCount != 0)
            {
                FeedOptions queryOptions2 = new FeedOptions { MaxItemCount = -1 };
                IQueryable<Trip> query2 = _client.CreateDocumentQuery<Trip>(
                UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
                $"SELECT * FROM C WHERE C.PersonId = '{UserID}' Order by C.StartDate", queryOptions2);
                triplist = query2.ToList();
                if (triplist == null)
                    return Ok("[]");
            }
            else
            {
                //Add user to Person-Collection and return an empty triplist
                Person person = new Person
                {
                    PersonId = UserID,
                    Nickname = string.Empty,
                    Avatar = string.Empty
                };
                Document document = await _client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePerson), person);
            }
            return Ok(triplist);
        }

        // GET: api/Trips/5
        // One trip by TripId
        //[HttpGet("{id}", Name = "GetTrip")]
        //public ActionResult<string> GetTrip(string id)
        //{
        //    FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
        //    IQueryable<Trip> query = _client.CreateDocumentQuery<Trip>(
        //    UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
        //    $"SELECT * FROM C WHERE C.TripId = '{id}'", queryOptions);
        //    var tripDetails = query.ToList();

        //    return Ok(tripDetails);
        //}

        /// <summary>
        /// Gets the trip and the pitstops under it with the trip id
        /// </summary>
        /// <param name="Id"></param>
        /// <returns></returns>
        // GET api/Trips/5
        [HttpGet("{Id}", Name = "GetTripAndPitstops")]
        public ActionResult<string> GetTripAndPitstops(int Id)
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666";

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Trip> query = _client.CreateDocumentQuery<Trip>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM T WHERE T.TripId = {Id} AND T.PersonId = '{UserID}'", queryOptions);
            Trip tripDetails = query.ToList().FirstOrDefault();

            if (tripDetails == null)
                return NotFound();

            IQueryable<Pitstop> query2 = _client.CreateDocumentQuery<Pitstop>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop),
            $"SELECT * FROM C WHERE C.TripId = {Id} AND C.PersonId = '{UserID}' Order by C.PitstopDate", queryOptions);
            var pitstops = query2.ToList();


            tripDetails.Pitstops = pitstops;

            return Ok(tripDetails);
        }

        /// <summary>
        /// Posts a new trip for a user (userid comes as authentication id)
        /// </summary>
        /// <param name="newTrip"></param>
        /// <returns></returns>
        // POST: api/trips
        [HttpPost(Name = "PostNewTrip")]
        [Consumes("multipart/form-data"), Authorize]
        public async Task<ActionResult<string>> PostNewTrip(NewTrip newTrip)
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;

            //if (!ModelState.IsValid)
            //{
            //    return BadRequest("Something wrong with the trip details.");
            //}

            Trip trip = new Trip();

            string photoName = await StorePicture(newTrip.picture);

            // Determining the tripId number
            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Trip> query = _client.CreateDocumentQuery<Trip>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM C WHERE C.PersonId = '{UserID}'", queryOptions);
            var tripCount = query.ToList().Count;

            if (tripCount == 0)
                tripCount = 0;
            else
                tripCount = query.ToList().Max(a => a.TripId);

            trip.TripId = tripCount + 1;
            trip.PersonId = UserID;
            trip.Headline = newTrip.Headline;
            trip.Description = newTrip.Description;
            trip.StartDate = newTrip.StartDate;
            trip.EndDate = newTrip.EndDate;
            trip.Position = newTrip.Position;
            trip.MainPhotoUrl = photoName;  // this needs to be updated! And the picture will be deleted at some point - we will not store huge pics.
            trip.MainPhotoSmallUrl = string.Empty;

            Document document = await _client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip), trip);

            QueueParam toQueue = new QueueParam();
            toQueue.Id = document.Id;
            toQueue.PictureUri = photoName;

            await AddQueueItem(toQueue);

            //return Ok(document.Id);
            return Ok($"Trip created, id: {trip.TripId}");
        }

        /// <summary>
        /// Edits a trip by trip id
        /// </summary>
        /// <param name="id"></param>
        /// <param name="editedTrip"></param>
        /// <returns></returns>
        // PUT: api/Trip/5
        [HttpPut("{id}")]
        public async Task<ActionResult<string>> PutTrip(int id, [FromBody] EditedTrip editedTrip)
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666";

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Trip> query = _client.CreateDocumentQuery<Trip>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM C WHERE C.TripId = {id} AND C.PersonId = '{UserID}'", queryOptions);
            var tripList = query.ToList();
            var trip = tripList.FirstOrDefault();

            if (trip != null)
            {
                string documentId = trip.id;

                var documentUri = UriFactory.CreateDocumentUri(_dbName, _collectionNameTrip, documentId);

                Document document = await _client.ReadDocumentAsync(documentUri);
                trip.PersonId = UserID;
                trip.Headline = editedTrip.Headline;
                trip.Description = editedTrip.Description;
                trip.StartDate = editedTrip.StartDate;
                trip.EndDate = editedTrip.EndDate;
                trip.Position = trip.Position;
                trip.MainPhotoUrl = editedTrip.MainPhotoUrl;  // this needs to be updated! And the picture will be deleted at some point - we will not store huge pics.
                trip.MainPhotoSmallUrl = editedTrip.MainPhotoSmallUrl;

                await _client.ReplaceDocumentAsync(document.SelfLink, trip);

                return Ok(document.Id);
            }
            return NotFound();
        }

        /// <summary>
        /// Deletes a trip with the tripID and the pitstops related to that trip 
        /// (user id from authentication data)
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        // DELETE: api/trip/5
        [HttpDelete("{id}")]
        public async Task<ActionResult<string>> DeleteTrip(int id)
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666;

            //get all pitstops for the trip to be deleted
            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Pitstop> query = _client.CreateDocumentQuery<Pitstop>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop),
            $"SELECT * FROM C where C.TripId = {id} AND C.PersonId = '{UserID}'", queryOptions);
            var pitstopList = query.ToList();

            foreach (var pitstop in pitstopList)
            {
                string documentId = pitstop.id;

                try
                {
                    await _client.DeleteDocumentAsync(
                    UriFactory.CreateDocumentUri(_dbName, _collectionNamePitstop, documentId));

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

            FeedOptions queryOptions2 = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Trip> query2 = _client.CreateDocumentQuery<Trip>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM T WHERE T.TripId = {id} AND T.PersonId = '{UserID}'", queryOptions);
            var trip = query2.ToList().FirstOrDefault();

            if (trip != null)
            {
                try
                {
                    string TripDbId = trip.id;

                    await _client.DeleteDocumentAsync(UriFactory.CreateDocumentUri(_dbName, _collectionNameTrip, TripDbId));
                    return Ok($"Deleted trip {id} and all pitstops therein");
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

        // NON ACTIONS
        // ------------------------------------------------------------

        [NonAction]
        private async Task<string> StorePicture(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName);

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

        [NonAction]
        private async Task AddQueueItem(QueueParam queueParam)
        {
            CloudQueueMessage message = new CloudQueueMessage(queueParam.ToJson());
            await _messageQueue.AddMessageAsync(message);

            //catch (Exception exe)
            //{
            //    System.Diagnostics.Trace.WriteLine(exe.StackTrace);
            //}
        }

    }
}

