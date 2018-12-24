using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace JourneyEntities
{
    public class PersonTableEntity: TableEntity
    {
        public string PersonId { get; set; }

        public string Nickname { get; set; }

        public string Avatar { get; set; }

        public Person Person { get; set; }

        public PersonTableEntity() { }

        public PersonTableEntity(Person person)
        {
            this.PersonId = person.PersonId;
            this.Nickname = person.Nickname;
            this.Avatar = person.Avatar;
            this.PartitionKey = person.PersonId;
            this.RowKey = "1";
        }


    }
}
