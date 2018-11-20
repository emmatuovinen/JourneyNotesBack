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
        private const string _collectionName = "Trip";

        public CosmosDbController(IConfiguration configuration)
        {
            _configuration = configuration;

            var endpointUri =
            _configuration["ConnectionStrings:CosmosDbConnection:EndpointUri"];

            var key =
            _configuration["ConnectionStrings:CosmosDbConnection:PrimaryKey"];

            _client = new DocumentClient(new Uri(endpointUri), key);
            _client.CreateDatabaseIfNotExistsAsync(new Database
            {
                Id = _dbName
            }).Wait();
            
            _client.CreateDocumentCollectionIfNotExistsAsync(
            UriFactory.CreateDatabaseUri(_dbName),
            new DocumentCollection { Id = _collectionName });
        }

        // POST/trip
        [HttpPost]
        public async Task<ActionResult<string>> PostAsync([FromBody] Trip trip)
        {
            Document document = await _client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(_dbName, _collectionName), trip);
            return Ok(document.Id);
        }

    }
}
