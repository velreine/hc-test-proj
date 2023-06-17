using API.Entity.Abstracts;

namespace API.Entity;

public class User : AbstractEntity
{
    public string Username { get; set; }

    public string Password { get; set; }

    public virtual List<Review>? Reviews { get; set; }
}