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
    [Route("api/[controller]")]
    [ApiController]
    public class TripsController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly DocumentClient _client;
        private const string _dbName = "JourneyNotesDB";
        private const string _collectionNamePerson = "Person";
        private const string _collectionNameTrip = "Trip";
        private const string _collectionNamePitstop = "Pitstop";

        public TripsController(IConfiguration configuration)
        {
            _configuration = configuration;

            var endpointUri =
            _configuration["ConnectionStrings:CosmosDbConnection:EndpointUri"];

            var key =
            _configuration["ConnectionStrings:CosmosDbConnection:PrimaryKey"];

            _client = new DocumentClient(new Uri(endpointUri), key);
        }

        // GET: api/Trip
        // All trips of one person
        [HttpGet]
        public ActionResult<IEnumerable<string>> GetTrips(string personID)
        {
            // Remember to check the safety of this method!

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Trip> query = _client.CreateDocumentQuery<Trip>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM C WHERE C.PersonId = {personID}", queryOptions);
            var tripList = query.ToList();

            return Ok(tripList);
        }

        // GET: api/Trip/5
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

        // One trip by TripId + the pitstops under it
        [HttpGet("{Id}", Name = "GetTripAndPitstops")]
        public ActionResult<string> GetTripAndPitstops(int Id)
        {
            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Trip> query = _client.CreateDocumentQuery<Trip>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM C WHERE C.TripId = {Id}", queryOptions);
            Trip tripDetails = query.ToList().FirstOrDefault();

            IQueryable<Pitstop> query2 = _client.CreateDocumentQuery<Pitstop>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop),
            $"SELECT * FROM C WHERE C.TripId = {Id}", queryOptions);
            var pitstops = query2.ToList();

            tripDetails.Pitstops = pitstops;

            return Ok(tripDetails);
        }

        // POST: api/trip
        [HttpPost]
        public async Task<ActionResult<string>> PostAsync([FromBody] NewTrip newTrip)
        {
            Trip trip = new Trip();

            //var person = HttpContext.User;
            var person = 70;

            // Determining the tripId number
            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Trip> query = _client.CreateDocumentQuery<Trip>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM C WHERE C.PersonId = {person}", queryOptions);
            var tripCount = query.ToList().Max(a => a.TripId);

            trip.TripId = tripCount + 1;
            trip.PersonId = person;
            trip.Headline = newTrip.Headline;
            trip.Description = newTrip.Description;
            trip.StartDate = newTrip.StartDate;
            trip.EndDate = newTrip.EndDate;
            trip.MainPhotoUrl = string.Empty;  // this needs to be updated! And the picture will be deleted at some point - we will not store huge pics.
            trip.MainPhotoSmallUrl = string.Empty;
            
            Document document = await _client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip), trip);
            return Ok(document.Id);
        }

        // PUT: api/Trip/5
        [HttpPut("{id}")]
        public void PutTrip(int id, [FromBody] string value)
        {
        }

        // DELETE: api/ApiWithActions/5
        [HttpDelete("{id}")]
        public void DeleteTrip(int id)
        {
        }
    }
}
