using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JourneyNotesAPI.Controllers
{
    [EnableCors("MyPolicy")]
    [Route("api/[controller]")]
    [ApiController]
    public class TripController : ControllerBase
    {
        // GET: api/Trip
        [HttpGet]
        public IEnumerable<string> GetTrips()
        {
            return new string[] { "value1", "value2" };
        }

        // GET: api/Trip/5
        [HttpGet("{id}", Name = "GetTrip")]
        public string GetTrip(int id)
        {
            return "value";
        }

        // POST: api/Trip
        [HttpPost]
        public void PostTrip([FromBody] string value)
        {
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
