using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JourneyEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace JourneyNotesAPI.Controllers
{
    [EnableCors("MyPolicy")]
    [Route("api/[controller]")]
    [ApiController]
    public class TripsController : ControllerBase
    {
        // HUOM! MUISTA POISTAA TÄMÄ KOVAKOODATTU KÄYTTÄJÄ!!
        int kovakoodattuKayttaja = 70;

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

        /// <summary>
        /// Gets all the trips of the user by user id
        /// </summary>
        /// <param name="userID"></param>
        /// <returns></returns>
        // GET: api/Trips
        [HttpGet]
        public ActionResult<IEnumerable<string>> GetTrips(string userID)
        {
            // Remember to check the safety of this method!

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Trip> query = _client.CreateDocumentQuery<Trip>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM C WHERE C.PersonId = {userID}", queryOptions);
            var tripList = query.ToList();

            return Ok(tripList);
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
        /// Gets the trip and the pitstops under it by trip id
        /// </summary>
        /// <param name="Id"></param>
        /// <returns></returns>
        // GET api/Trips/5
        [HttpGet("{Id}", Name = "GetTripAndPitstops")]
        public ActionResult<string> GetTripAndPitstops(int Id)
        {
            //var person = HttpContext.User;
            var person = kovakoodattuKayttaja;

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Trip> query = _client.CreateDocumentQuery<Trip>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM T WHERE T.TripId = {Id} AND T.PersonId = {person}", queryOptions);
            Trip tripDetails = query.ToList().FirstOrDefault();

            IQueryable<Pitstop> query2 = _client.CreateDocumentQuery<Pitstop>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePitstop),
            $"SELECT * FROM C WHERE C.TripId = {Id} AND C.PersonId = {person}", queryOptions);
            var pitstops = query2.ToList();

            tripDetails.Pitstops = pitstops;

            return Ok(tripDetails);
        }

        /// <summary>
        /// Posts a new trip for a user
        /// </summary>
        /// <param name="newTrip"></param>
        /// <returns></returns>
        // POST: api/trips
        [HttpPost]
        public async Task<ActionResult<string>> PostNewTrip([FromBody] NewTrip newTrip)
        {
            Trip trip = new Trip();

            //var person = HttpContext.User;
            var person = kovakoodattuKayttaja;

            // Determining the tripId number
            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Trip> query = _client.CreateDocumentQuery<Trip>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM C WHERE C.PersonId = {person}", queryOptions);
            var tripCount = query.ToList().Count;

            if (tripCount == 0)
                tripCount = 0;
            else
                tripCount = query.ToList().Max(a => a.TripId);

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
            //var person = HttpContext.User;
            var person = kovakoodattuKayttaja;

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Trip> query = _client.CreateDocumentQuery<Trip>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM C WHERE C.TripId = {id} AND C.PersonId = {person}", queryOptions);
            var trip = query.ToList().FirstOrDefault();

            string documentId = trip.id;

            var documentUri = UriFactory.CreateDocumentUri(_dbName, _collectionNameTrip, documentId);

            Document document = await _client.ReadDocumentAsync(documentUri);

            trip.Headline = editedTrip.Headline;
            trip.Description = editedTrip.Description;
            trip.StartDate = editedTrip.StartDate;
            trip.EndDate = editedTrip.EndDate;
            trip.MainPhotoUrl = string.Empty;  // this needs to be updated! And the picture will be deleted at some point - we will not store huge pics.
            trip.MainPhotoSmallUrl = string.Empty;

            await _client.ReplaceDocumentAsync(document.SelfLink, trip);

            return Ok(document.Id);
        }

        /// <summary>
        /// Deletes a trip by trip id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        // DELETE: api/trip/5
        [HttpDelete("{id}")]
        public async Task<ActionResult<string>> DeleteTrip(int id)
        {
            //var person = HttpContext.User;
            var userID = kovakoodattuKayttaja;

            FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };
            IQueryable<Trip> query = _client.CreateDocumentQuery<Trip>(
            UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip),
            $"SELECT * FROM T WHERE T.TripId = {id} AND T.PersonId = {userID}", queryOptions);
            var trip = query.ToList().FirstOrDefault();

            string DbId = trip.id;

            try
            {
                await _client.DeleteDocumentAsync(UriFactory.CreateDocumentUri(_dbName, _collectionNameTrip, DbId));
                return Ok($"Deleted trip {id}");
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

