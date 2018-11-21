using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JourneyEntities;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Configuration;

namespace JourneyNotesAPI.Controllers
{
    [EnableCors("MyPolicy")]
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class PitstopsController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly DocumentClient _client;
        private const string _dbName = "JourneyNotesDB";
        private const string _collectionNamePerson = "Person";
        private const string _collectionNameTrip = "Trip";
        private const string _collectionNamePitstop = "Pitstop";

        public PitstopsController(IConfiguration configuration)
        {
            _configuration = configuration;

            var endpointUri =
            _configuration["ConnectionStrings:CosmosDbConnection:EndpointUri"];

            var key =
            _configuration["ConnectionStrings:CosmosDbConnection:PrimaryKey"];

            _client = new DocumentClient(new Uri(endpointUri), key);

            // We have everything in Azure so no need for this:
            //_client.CreateDatabaseIfNotExistsAsync(new Database
            //{
            //    Id = _dbName
            //}).Wait();

            //_client.CreateDocumentCollectionIfNotExistsAsync(
            //UriFactory.CreateDatabaseUri(_dbName),
            //new DocumentCollection { Id = _collectionNameTrip });
        }


        // GET: api/Pitstop
        // No need for this, since you get them from api/trips/5.
        //[HttpGet]
        //public IEnumerable<string> GetPitstops()
        //{
        //    return new string[] { "value1", "value2" };
        //}

        // GET: api/pitstops/5
        [HttpGet("{id}", Name = "GetPitstop")]
        public string GetPitstop(int id)
        {
            // We might need this
            return "value";
        }

        // POST/Pitstop
        [HttpPost]
        public async Task<ActionResult<string>> PostPitstop([FromBody] NewPitstop newPitstop)
        {
            // We need to get the TripId from the http request!

            Pitstop pitstop = new Pitstop();
            var id = newPitstop.TripId;

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Pitstop> query = _client.CreateDocumentQuery<Pitstop>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop),
            $"SELECT * FROM C WHERE C.TripId = {id}", queryOptions);
            var pitstopCount = query.ToList().Count;

            if (pitstopCount == 0)
                pitstopCount = 0;
            else
                pitstopCount = query.ToList().Max(a => a.PitstopId);

            pitstop.PitstopId = pitstopCount + 1;
            pitstop.Title = newPitstop.Title;
            pitstop.Note = newPitstop.Note;
            pitstop.PitstopDate = newPitstop.PitstopDate;
            pitstop.PhotoOriginalUrl = string.Empty; // Remember to update
            pitstop.PhotoLargeUrl = string.Empty;
            pitstop.PhotoMediumUrl = string.Empty;
            pitstop.PhotoSmallUrl = string.Empty; // will be updated when the queue has done it's job.
            pitstop.TripId = newPitstop.TripId;
            pitstop.Latitude = newPitstop.Latitude;
            pitstop.Longitude = newPitstop.Longitude;
            pitstop.Address = newPitstop.Address;

            Document document = await _client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop), pitstop);
            return Ok(document.Id);
        }

        // PUT: api/Person/5
        [HttpPut("{id}")]
        public void PutPitstop(int id, [FromBody] string value)
        {
        }

        // DELETE: api/ApiWithActions/5
        [HttpDelete("{id}")]
        public void DeletePitstop(int id)
        {
        }
    }
}
