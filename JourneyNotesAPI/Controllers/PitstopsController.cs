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
using Microsoft.WindowsAzure.Storage.Table;

namespace JourneyNotesAPI.Controllers
{
    [EnableCors("MyPolicy")]
    [Route("api/[controller]")]
    [ApiController]
    public class PitstopsController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        // Queue
        private readonly CloudStorageAccount _storageAccount;
        private readonly CloudQueueClient _queueClient;
        private readonly CloudQueue _messageQueue;
        private const string _queueName = "pitstopqueue";

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


        public PitstopsController(IConfiguration configuration)
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

            List<Pitstop> pitstopList = new List<Pitstop>();

            var tripList = new List<TripTableEntity>();

            //TripEntity from trip table
            var tripQuery =  new TableQuery<TripTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, UserID));
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

            //Get pitstops
            List < PitstopTableEntity > pitstopList2 = new List<PitstopTableEntity>();

            TableQuery<PitstopTableEntity> pitstopQuery = new TableQuery<PitstopTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, newPitstop.TripId.ToString() + ";" + UserID));

            TableContinuationToken tokenPitstop = null;

            do
            {
                TableQuerySegment<PitstopTableEntity> resultSegment = await _tablePitstop.ExecuteQuerySegmentedAsync(pitstopQuery, tokenPitstop);
                tokenPitstop = resultSegment.ContinuationToken;

                foreach (PitstopTableEntity entity in resultSegment.Results)
                {
                        pitstopList2.Add(entity);
                }
            } while (tokenPitstop != null);

            //Check if trip exists in trip table (if not return not found)
            if(tripList.Where(a => a.TripId == newPitstop.TripId).Count() != 0)
            {
                var nextPitsop = 1;

                if (pitstopList2.Count != 0)
                    nextPitsop = pitstopList2.Select(a => a.PitstopId).Max() + 1;

                string photoName = await StorePicture(newPitstop.picture);

                Pitstop pitstop = new Pitstop();

                pitstop.PersonId = UserID;
                pitstop.PitstopId = nextPitsop;
                pitstop.Title = newPitstop.Title;
                pitstop.Note = newPitstop.Note;
                pitstop.PitstopDate = newPitstop.PitstopDate;
                if (pitstop.PitstopDate.Year.Equals(0001))
                {
                    pitstop.PitstopDate = DateTime.Now;
                }
                pitstop.PhotoLargeUrl = photoName; // will be replaced with the url to the resized image.
                pitstop.PhotoMediumUrl = string.Empty; // will be updated when the image has been resized.
                pitstop.PhotoSmallUrl = string.Empty; // will be updated when the image has been resized.
                pitstop.TripId = newPitstop.TripId;
                pitstop.pitstopPosition = newPitstop.pitstopPosition;
                pitstop.Address = newPitstop.Address;

                PitstopTableEntity pitstopTable = new PitstopTableEntity(pitstop);

                TableOperation insertOperation = TableOperation.Insert(pitstopTable);

                await _tablePitstop.ExecuteAsync(insertOperation);
            
                await AddQueueItem(new QueueParam { PartitionKey = pitstopTable.PartitionKey, RowKey = pitstopTable.RowKey, PictureUri = photoName });

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

            var pitstopList = new List<PitstopTableEntity>();

            var pitstopQuery = new TableQuery<PitstopTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TripId.ToString() + ";" + UserID));
            TableContinuationToken tokenPitstop = null;
            do
            {
                TableQuerySegment<PitstopTableEntity> resultSegment = await _tablePitstop.ExecuteQuerySegmentedAsync(pitstopQuery, tokenPitstop);
                tokenPitstop = resultSegment.ContinuationToken;

                foreach (PitstopTableEntity entity in resultSegment.Results)
                {
                    if (entity.PitstopId == PitstopId)
                        pitstopList.Add(entity);
                }
            } while (tokenPitstop != null);

            var pitstop = pitstopList.FirstOrDefault();

            //not working...
            if (pitstop != null)
            {
                pitstop.Title = updatedPitstop.Title;
                pitstop.Note = updatedPitstop.Note;
                pitstop.PitstopDate = updatedPitstop.PitstopDate;
                pitstop.pitstopPosition = updatedPitstop.pitstopPosition;
                pitstop.Address = updatedPitstop.Address;

                TableOperation replaceOperation = TableOperation.Replace(pitstop);

                await _tablePitstop.ExecuteAsync(replaceOperation);

                return Ok(pitstop);
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

            List<PitstopTableEntity> pitstopList = new List<PitstopTableEntity>();

            TableQuery<PitstopTableEntity> queryPitstop = new TableQuery<PitstopTableEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TripId.ToString() + ";" + UserID));

            TableContinuationToken tokenPitstop = null;

            do
            {
                TableQuerySegment<PitstopTableEntity> resultSegment = await _tablePitstop.ExecuteQuerySegmentedAsync(queryPitstop, tokenPitstop);
                tokenPitstop = resultSegment.ContinuationToken;

                foreach (PitstopTableEntity entity in resultSegment.Results)
                {
                    if(entity.PitstopId == PitstopId)
                        pitstopList.Add(entity);
                }
            } while (tokenPitstop != null);

            var pitstop = pitstopList.FirstOrDefault();

            if (pitstop != null)
            {
                try
                {
                    string removedPitstop = await TripsController.RemovePitstopImagesFromBlob(pitstop, _container);
                    TableOperation deleteOperation = TableOperation.Delete(pitstop);
                    await _tablePitstop.ExecuteAsync(deleteOperation);
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
