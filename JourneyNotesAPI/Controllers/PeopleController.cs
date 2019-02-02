using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using JourneyEntities;
using Microsoft.AspNetCore.Cors;
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
    public class PeopleController : ControllerBase
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

        public PeopleController(IConfiguration configuration)
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
        /// Gets the customer details by CustomerID
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        // GET: api/people/5
        [HttpGet(Name = "GetPerson")]
        public async Task<ActionResult<string>> GetPerson()
        {
            string UserID = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value;
            //string UserID = "666";
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

            var person = userList.FirstOrDefault();

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

            var personDB = userList.FirstOrDefault();

            if (personDB != null)
            {
                personDB.PersonId = UserID;
                personDB.Nickname = editperson.Nickname;
                personDB.Avatar = editperson.Avatar;

                TableOperation replaceOperation = TableOperation.Replace(personDB);

                await _tablePerson.ExecuteAsync(replaceOperation);

                return Ok(personDB);
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

            var personDB = userList.FirstOrDefault();

            //to delete a user - first deletes all of users pitstops, then trips and then the user

            if (personDB != null)
            {
                var tripList = new List<TripTableEntity>();

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

                foreach (var tripToDelete in tripList)
                {
                    if (tripToDelete != null)
                    {
                        try
                        {
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

                            TableOperation deleteOperationTrip = TableOperation.Delete(tripToDelete);

                            await _tableTrip.ExecuteAsync(deleteOperationTrip);

                            // Removing images from the blob storage
                            string removed = await RemoveTripImagesFromBlob(tripToDelete, _container);

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
                TableOperation deleteOperationPerson = TableOperation.Delete(personDB);

                await _tablePerson.ExecuteAsync(deleteOperationPerson);

                return Ok("Deleted the account and all Trips and their pitstops");
            }
            return NotFound();
        }

        //Non-action
        private static async Task<string> RemoveTripImagesFromBlob(TripTableEntity trip, CloudBlobContainer container)
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

        private static async Task<string> RemovePitstopImagesFromBlob(PitstopTableEntity pitstop, CloudBlobContainer container)
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

