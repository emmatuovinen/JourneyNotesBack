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
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class CosmosDbController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly DocumentClient _client;
        private const string _dbName = "JourneyNotesDB"; 
        private const string _collectionNamePerson = "Person";
        private const string _collectionNameTrip = "Trip";
        private const string _collectionNamePitstop = "Pitstop";

        public CosmosDbController(IConfiguration configuration)
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

        // POST/trip
        [HttpPost]
        public async Task<ActionResult<string>> PostAsync([FromBody] Trip trip)
        {
            Document document = await _client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(_dbName, _collectionNameTrip), trip);
            return Ok(document.Id);
        }

    }
}
