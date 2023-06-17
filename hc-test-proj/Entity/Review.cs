using API.Entity.Abstracts;

namespace API.Entity;

public class Review : AbstractEntity
{
    public string Content { get; set; }
    
    public int Rating { get; set; }
    
    public virtual User Author { get; set; }
}