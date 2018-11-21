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
        // HUOM!! MUISTA POISTAA TÄMÄ KOVAKOODATTU KÄYTTÄJÄ !!!
        int kovakoodattuKayttaja = 70;

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

        // POST/Pitstop
        [HttpPost]
        public async Task<ActionResult<string>> PostPitstop([FromBody] NewPitstop newPitstop)
        {
            // We need to get the TripId and the PersonId from the http request!

            Pitstop pitstop = new Pitstop();
            var TripId = newPitstop.TripId;
            //var personId = HttpContext.User;
            var personId = kovakoodattuKayttaja;

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Pitstop> query = _client.CreateDocumentQuery<Pitstop>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop),
            $"SELECT * FROM C WHERE C.TripId = {TripId} AND C.PersonId = {personId}", queryOptions);
            var pitstopCount = query.ToList().Count;

            if (pitstopCount == 0)
                pitstopCount = 0;
            else
                pitstopCount = query.ToList().Max(a => a.PitstopId);

            pitstop.PersonId = personId;
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

            // Updating the List<Pitstop> for the trip
            //FeedOptions queryOptions2 = new FeedOptions { MaxItemCount = -1 };
            //IQueryable<Trip> query2 = _client.CreateDocumentQuery<Trip>(
            //UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            //$"SELECT * FROM T WHERE T.PersonId = {personId} AND T.TripId = {TripId}", queryOptions);
            //var updateTrip = query2.ToList().FirstOrDefault();

            //updateTrip.MainPhotoUrl = "test";
            //updateTrip.Pitstops.Add(pitstop);
            //string documentId = updateTrip.id;

            //var documentUri = UriFactory.CreateDocumentUri(_dbName, _collectionNameTrip, documentId);
            //Document documentTrip = await _client.ReadDocumentAsync(documentUri);
            
            //await _client.ReplaceDocumentAsync(documentTrip.SelfLink, updateTrip);

            Document documentPitstop = await _client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop), pitstop);
            return Ok(documentPitstop.Id);
        }

        // PUT: api/pitstops/5
        [HttpPut("{id}")]
        public async Task<ActionResult<string>> PutPitstop(int id, [FromBody] NewPitstop updatedPitstop)
        {
            //var person = HttpContext.User;
            var person = kovakoodattuKayttaja;

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Pitstop> query = _client.CreateDocumentQuery<Pitstop>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop),
            $"SELECT * FROM C WHERE C.PitstopId = {id} AND C.PersonId = {person}", queryOptions);
            Pitstop pitstop = query.ToList().FirstOrDefault();

            pitstop.Title = updatedPitstop.Title;
            pitstop.Note = updatedPitstop.Note;
            pitstop.PitstopDate = updatedPitstop.PitstopDate;
            pitstop.Latitude = updatedPitstop.Latitude;
            pitstop.Longitude = updatedPitstop.Longitude;
            pitstop.Address = updatedPitstop.Address;

            string documentId = pitstop.id;

            var documentUri = UriFactory.CreateDocumentUri(_dbName, _collectionNamePitstop, documentId);

            Document document = await _client.ReadDocumentAsync(documentUri);
            
            await _client.ReplaceDocumentAsync(document.SelfLink, pitstop);

            return Ok(document.Id);
        }

        // DELETE: api/ApiWithActions/5
        [HttpDelete("{TripId}", Name = "TripId")]
        public async Task<ActionResult<string>> DeletePitstop(int PitstopId, int TripId)
        {
            //var person = HttpContext.User;
            var person = kovakoodattuKayttaja;

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Pitstop> query = _client.CreateDocumentQuery<Pitstop>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop),
            $"SELECT * FROM C WHERE C.PitstopId = {PitstopId} AND C.PersonId = {person}", queryOptions);
            var pitstop = query.ToList().FirstOrDefault();

            string DbId = pitstop.id;

            try
            {
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
            return BadRequest();
        }
    }
}
