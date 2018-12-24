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
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;

namespace JourneyNotesAPI.Controllers
{
    [EnableCors("MyPolicy")]
    [Route("api/[controller]")]
    [ApiController]
    public class TripsController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        // Queue
        private readonly CloudStorageAccount _storageAccount;
        private readonly CloudQueueClient _queueClient;
        private readonly CloudQueue _messageQueue;
        private const string _queueName = "tripqueue";

        // Blob
        private readonly CloudBlobClient _blobClient;
        private readonly CloudBlobContainer _container;
        private const string _containerName = "photos";

        //Table
        private readonly CloudTableClient _tableClient;
        private readonly CloudTable _tablePerson;
        private readonly CloudTable _tableTrip;
        private readonly CloudTable _tablePitstop;

        private const string _tableNamePerson = "person";
        private const string _tableNamePitstop = "pitstop";
        private const string _tableNameTrip = "trip";

        public TripsController(IConfiguration configuration)
        {
            _configuration = configuration;

            var accountName = _configuration["ConnectionStrings:StorageConnection:AccountName"];
            var accountKey = _configuration["ConnectionStrings:StorageConnection:AccountKey"];
            _storageAccount = new CloudStorageAccount(new StorageCredentials(accountName, accountKey), true);

            // Queue
            _queueClient = _storageAccount.CreateCloudQueueClient();
            _messageQueue = _queueClient.GetQueueReference(_queueName);

            // Blob
            _blobClient = _storageAccount.CreateCloudBlobClient();
            _container = _blobClient.GetContainerReference(_containerName);

            //Table
            _tableClient = _storageAccount.CreateCloudTableClient();
            _tablePerson = _tableClient.GetTableReference(_tableNamePerson);
            _tablePitstop = _tableClient.GetTableReference(_tableNamePitstop);
            _tableTrip = _tableClient.GetTableReference(_tableNameTrip);
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
            var tripList = new List<TripTableEntity>();

            //Check if user exists
            var userList = new List<PersonTableEntity>();
            var personQuery = new TableQuery<PersonTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, UserID));
            TableContinuationToken tokenPerson = null;
            do
            {
                TableQuerySegment<PersonTableEntity> resultSegment = await _tablePerson.ExecuteQuerySegmentedAsync(personQuery, tokenPerson);
                tokenPerson = resultSegment.ContinuationToken;

                foreach (PersonTableEntity entity in resultSegment.Results)
                {
                    userList.Add(entity);
                }
            } while (tokenPerson != null);

            //if user exits get trips else add user
            if (userList.Count() != 0)
            {
                var tripQuery = new TableQuery<TripTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, UserID));
                TableContinuationToken tokenTrip = null;
                do
                {
                    TableQuerySegment<TripTableEntity> resultSegment = await _tableTrip.ExecuteQuerySegmentedAsync(tripQuery, tokenTrip);
                    tokenTrip = resultSegment.ContinuationToken;

                    foreach (TripTableEntity entity in resultSegment.Results)
                    {
                        tripList.Add(entity);
                    }
                } while (tokenTrip != null);

                if (tripList == null)
                    return Ok("[]");
            }
            else
            {
                //Add user to Person table and return an empty triplist
                Person person = new Person
                {
                    PersonId = UserID,
                    Nickname = string.Empty,
                    Avatar = string.Empty
                };

                PersonTableEntity personTable = new PersonTableEntity(person);

                TableOperation insertOperation = TableOperation.Insert(personTable);

                await _tablePerson.ExecuteAsync(insertOperation);
            }
            return Ok(tripList.OrderByDescending(a => a.StartDate));
        }

        /// <summary>
        /// Gets the trip and the pitstops under it with the trip id
        /// </summary>
        /// <param name="Id"></param>
        /// <returns></returns>
        // GET api/Trips/5
        [HttpGet("{Id}", Name = "GetTripAndPitstops")]
        public async Task<ActionResult<string>> GetTripAndPitstops(int Id)
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666";

            var tripList = new List<TripTableEntity>();

            var tripQuery = new TableQuery<TripTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, UserID));
            TableContinuationToken tokenTrip = null;
            do
            {
                TableQuerySegment<TripTableEntity> resultSegment = await _tableTrip.ExecuteQuerySegmentedAsync(tripQuery, tokenTrip);
                tokenTrip = resultSegment.ContinuationToken;

                foreach (TripTableEntity entity in resultSegment.Results)
                {
                    if (entity.TripId == Id)
                        tripList.Add(entity);
                }
            } while (tokenTrip != null);

            var tripDetails = tripList.FirstOrDefault();

            if (tripDetails == null)
                return NotFound();


            //Get pitstops
            List<Pitstop> pitstopList = new List<Pitstop>();

            TableQuery<PitstopTableEntity> queryPitstop = new TableQuery<PitstopTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, Id.ToString() + ";" + UserID));

            TableContinuationToken tokenPitstop = null;

            do
            {
                TableQuerySegment<PitstopTableEntity> resultSegment = await _tablePitstop.ExecuteQuerySegmentedAsync(queryPitstop, tokenPitstop);
                tokenPitstop = resultSegment.ContinuationToken;

                foreach (PitstopTableEntity entity in resultSegment.Results)
                {
                    Pitstop pitstop =new  Pitstop(entity);
                    pitstopList.Add(pitstop);
                }
            } while (tokenPitstop != null);

            Trip trip = new Trip { TripId = tripDetails.TripId, Description= tripDetails.Description,
                EndDate = tripDetails.EndDate, Headline = tripDetails.Headline, MainPhotoSmallUrl =tripDetails.MainPhotoSmallUrl,
                MainPhotoUrl = tripDetails.MainPhotoUrl, PersonId=tripDetails.PersonId, Pitstops = pitstopList, Position=tripDetails.Position,
                StartDate =tripDetails.StartDate};

            return Ok(trip);
        }

        /// <summary>
        /// Posts a new trip for a user (userid comes as authentication id)
        /// </summary>
        /// <param name="newTrip"></param>
        /// <returns></returns>
        // POST: api/trips
        [HttpPost(Name = "PostNewTrip")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<string>> PostNewTrip(NewTrip newTrip)
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666";

            //if (!ModelState.IsValid)
            //{
            //    return BadRequest("Something wrong with the trip details.");
            //}

            Trip trip = new Trip();

            var tripList = new List<TripTableEntity>();

            string photoName = await StorePicture(newTrip.picture);

            // Determining the tripId number
            var tripQuery = new TableQuery<TripTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, UserID));
            TableContinuationToken tokenTrip = null;
            do
            {
                TableQuerySegment<TripTableEntity> resultSegment = await _tableTrip.ExecuteQuerySegmentedAsync(tripQuery, tokenTrip);
                tokenTrip = resultSegment.ContinuationToken;

                foreach (TripTableEntity entity in resultSegment.Results)
                {
                    tripList.Add(entity);
                }
            } while (tokenTrip != null);

            var tripCount = 0;

            if (tripList.Count() != 0)
                tripCount = tripList.Max(a => a.TripId);

            trip.TripId = tripCount + 1;
            trip.PersonId = UserID;
            trip.Headline = newTrip.Headline;
            trip.Description = newTrip.Description;
            trip.StartDate = newTrip.StartDate;
            trip.EndDate = newTrip.EndDate;
            trip.Position = newTrip.Position;
            trip.MainPhotoUrl = photoName;  // this needs to be updated! And the picture will be deleted at some point - we will not store huge pics.
            trip.MainPhotoSmallUrl = string.Empty;

            TripTableEntity tripTable = new TripTableEntity(trip);

            TableOperation insertOperation = TableOperation.Insert(tripTable);

            await _tableTrip.ExecuteAsync(insertOperation);

            QueueParam toQueue = new QueueParam();
            // toQueue.Id = document.Id;
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

            var tripList = new List<TripTableEntity>();

            var tripQuery = new TableQuery<TripTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, UserID));
            TableContinuationToken tokenTrip = null;
            do
            {
                TableQuerySegment<TripTableEntity> resultSegment = await _tableTrip.ExecuteQuerySegmentedAsync(tripQuery, tokenTrip);
                tokenTrip = resultSegment.ContinuationToken;

                foreach (TripTableEntity entity in resultSegment.Results)
                {
                    if (entity.TripId == id)
                        tripList.Add(entity);
                }
            } while (tokenTrip != null);

            var trip = tripList.FirstOrDefault();

            if (trip != null)
            {
                trip.PersonId = UserID;
                trip.Headline = editedTrip.Headline;
                trip.Description = editedTrip.Description;
                trip.StartDate = editedTrip.StartDate;
                trip.EndDate = editedTrip.EndDate;
                trip.Position = trip.Position;
                trip.MainPhotoUrl = editedTrip.MainPhotoUrl;  // this needs to be updated! And the picture will be deleted at some point - we will not store huge pics.
                trip.MainPhotoSmallUrl = editedTrip.MainPhotoSmallUrl;

                TableOperation replaceOperation = TableOperation.Replace(trip);

                await _tableTrip.ExecuteAsync(replaceOperation);

                return Ok(trip);
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
            //string UserID = "666";

            var tripList = new List<TripTableEntity>();

            var tripQuery = new TableQuery<TripTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, UserID));
            TableContinuationToken tokenTrip = null;
            do
            {
                TableQuerySegment<TripTableEntity> resultSegment = await _tableTrip.ExecuteQuerySegmentedAsync(tripQuery, tokenTrip);
                tokenTrip = resultSegment.ContinuationToken;

                foreach (TripTableEntity entity in resultSegment.Results)
                {
                    if(entity.TripId == id)
                        tripList.Add(entity);
                }
            } while (tokenTrip != null);

            var tripToDelete = tripList.FirstOrDefault();

            if (tripToDelete != null)
            {
                try
                {
                    List<Pitstop> pitstopList = new List<Pitstop>();

                    TableQuery<PitstopTableEntity> queryPitstop = new TableQuery<PitstopTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, tripToDelete.TripId.ToString() + ";" + UserID));

                    TableContinuationToken tokenPitstop = null;

                    do
                    {
                        TableQuerySegment<PitstopTableEntity> resultSegment = await _tablePitstop.ExecuteQuerySegmentedAsync(queryPitstop, tokenPitstop);
                        tokenPitstop = resultSegment.ContinuationToken;

                        foreach (PitstopTableEntity entity in resultSegment.Results)
                        {                         
                            string removedPitstop = await RemovePitstopImagesFromBlob(entity, _container);

                            TableOperation deleteOperation = TableOperation.Delete(entity);

                            await _tablePitstop.ExecuteAsync(deleteOperation);
                        }
                    } while (tokenPitstop != null);

                    // Removing images from the blob storage
                    string removed = await RemoveTripImagesFromBlob(tripToDelete, _container);

                    TableOperation deleteOperationTrip = TableOperation.Delete(tripToDelete);

                    await _tableTrip.ExecuteAsync(deleteOperationTrip);

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

        private static async Task<string> RemoveTripImagesFromBlob(TripTableEntity trip, CloudBlobContainer container)
        {
            string smallImageName = trip.MainPhotoSmallUrl;
            string largeImageName = trip.MainPhotoUrl;
           // CloudBlockBlob smallImage = container.GetBlockBlobReference(smallImageName);
            CloudBlockBlob largeImage = container.GetBlockBlobReference(largeImageName);

            //using (var deleteStream = await smallImage.OpenReadAsync()) { }
            //await smallImage.DeleteIfExistsAsync();

            using (var deleteStream = await largeImage.OpenReadAsync()) { }
            await largeImage.DeleteIfExistsAsync();

            return $"Deleted images {smallImageName} and {largeImageName}";
        }

        public static async Task<string> RemovePitstopImagesFromBlob(PitstopTableEntity pitstop, CloudBlobContainer container)
        {
            string smallImageName = pitstop.PhotoSmallUrl;
            string mediumImageName = pitstop.PhotoMediumUrl;
            string largeImageName = pitstop.PhotoLargeUrl;

           // CloudBlockBlob smallImage = container.GetBlockBlobReference(smallImageName);
            //CloudBlockBlob mediumImage = container.GetBlockBlobReference(mediumImageName);
            CloudBlockBlob largeImage = container.GetBlockBlobReference(largeImageName);

            //using (var deleteStream = await smallImage.OpenReadAsync()) { }
            //await smallImage.DeleteIfExistsAsync();

            //using (var deleteStream = await mediumImage.OpenReadAsync()) { }
            //await mediumImage.DeleteIfExistsAsync();

            using (var deleteStream = await largeImage.OpenReadAsync()) { }
            await largeImage.DeleteIfExistsAsync();

            return $"Deleted images {smallImageName}, {mediumImageName} and {largeImageName}";
        }

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

