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

namespace JourneyNotesAPI.Controllers
{
    [EnableCors("MyPolicy")]
    [Route("api/[controller]")]
    [ApiController]
    public class PeopleController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly DocumentClient _client;
        private const string _dbName = "JourneyNotesDB";
        private const string _collectionNamePerson = "Person";
        private const string _collectionNameTrip = "Trip";
        private const string _collectionNamePitstop = "Pitstop";

        public PeopleController(IConfiguration configuration)
        {
            _configuration = configuration;

            var endpointUri =
            _configuration["ConnectionStrings:CosmosDbConnection:EndpointUri"];

            var key =
            _configuration["ConnectionStrings:CosmosDbConnection:PrimaryKey"];

            _client = new DocumentClient(new Uri(endpointUri), key);
        }

        // GET: api/Person
        //[HttpGet]
        //public IEnumerable<string> GetPersons()
        //{
        //    return new string[] { "valueX", "valueY" };
        //}

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
        /// Adds a new person to the database
        /// </summary>
        /// <param name="newperson"></param>
        /// <returns></returns>
        // POST: api/people
        //[HttpPost]
        //public async Task<ActionResult<string>> PostPerson([FromBody] NewPerson newperson)
        //{
        //    Person person = new Person
        //    {
        //        PersonId = "666",
        //        //PersonId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier).Value,
        //        Nickname = newperson.Nickname,
        //        Avatar = newperson.Avatar,
        //    };
        //    Document document = await _client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNamePerson), person);
        //    return Ok(document.Id);
        //}

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
    }
}
