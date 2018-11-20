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
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class PitstopController : ControllerBase
    {
        // GET: api/Person
        [HttpGet]
        public IEnumerable<string> GetPitstops()
        {
            return new string[] { "value1", "value2" };
        }

        // GET: api/Person/5
        [HttpGet("{id}", Name = "GetPitstop")]
        public string GetPitstop(int id)
        {
            return "value";
        }

        // POST: api/Person
        [HttpPost]
        public void PostPitstop([FromBody] string value)
        {
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
