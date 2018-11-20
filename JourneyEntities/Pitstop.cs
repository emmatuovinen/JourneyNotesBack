using System;
using System.Collections.Generic;
using System.Text;

namespace JourneyEntities
{
    public class Pitstop
    {
        public int PitstopId { get; set; }

        public string Title { get; set; }

        public string Note { get; set; }

        public DateTime PitstopDate { get; set; }

        public string PhotoLargeUrl { get; set; }

        public string PhotoMediumUrl { get; set; }

        public string PhotoSmallUrl { get; set; }

        public int TripId { get; set; }

        public int Latitude { get; set; }

        public int Longitude { get; set; }

        public string Address { get; set; }
    }
}
