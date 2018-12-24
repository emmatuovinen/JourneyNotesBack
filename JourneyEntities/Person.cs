using System;

namespace JourneyEntities
{
    public class Person
    {
        public string PersonId { get; set; }

        public string Nickname { get; set; }

        public string Avatar { get; set; }

        public Person() { }
        public Person(PersonTableEntity entity)
        {
            this.PersonId = entity.PersonId;
            this.Nickname = entity.Nickname;
            this.Avatar = entity.Avatar;
        }
    }

    public class NewPerson
    {
        public string Nickname { get; set; }

        public string Avatar { get; set; }

    }

}
