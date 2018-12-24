﻿using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace JourneyEntities
{
    public class Trip
    {
        public int TripId { get; set; }

        public string PersonId { get; set; }

        public string Headline { get; set; }

        public string Description { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public string MainPhotoUrl { get; set; }

        public string MainPhotoSmallUrl { get; set; }

        public string Position { get; set; }

        public List<Pitstop> Pitstops { get; set; }
   
        //public IFormFile picture { get; set; }
    }

    public class NewTrip
    {
        public string Headline { get; set; }

        public string Description { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public string Position { get; set; }
        public IFormFile picture { get; set; }
    }

    public class EditedTrip
    {
        public string Headline { get; set; }

        public string Description { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public string MainPhotoUrl { get; set; }

        public string Position { get; set; }

        public string MainPhotoSmallUrl { get; set; }
    }
}
