namespace MinimalHtmxRazor.Models;

public sealed class PostWithComments
{
    public Post Post { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
}
