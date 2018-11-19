using System;
using System.Collections.Generic;
using System.Text;

namespace JourneyEntities
{
    public class Trip
    {
        public int Id { get; set; }

        public int PersonId { get; set; }

        public string Headline { get; set; }

        public string Description { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public string MainPhotoUrl { get; set; }

        public string MainPhotoSmallUrl { get; set; }


    }
}
