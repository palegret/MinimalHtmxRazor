using MinimalHtmxRazor.Models;

namespace MinimalHtmxRazor.Services;

public sealed class JsonPlaceholderClient(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<List<Post>> GetPostsAsync(CancellationToken ct)
    {
        var posts = await _httpClient.GetFromJsonAsync<List<Post>>("posts", ct);
        return posts ?? [];
    }

    public async Task<Post?> GetPostAsync(int id, CancellationToken ct)
        => await _httpClient.GetFromJsonAsync<Post>($"posts/{id}", ct);

    public async Task<List<Comment>> GetCommentsForPostAsync(int postId, CancellationToken ct)
    {
        // JSONPlaceholder supports filtering comments by postId via querystring
        var comments = await _httpClient.GetFromJsonAsync<List<Comment>>($"comments?postId={postId}", ct);
        return comments ?? [];
    }

    public async Task<PostWithComments?> GetPostWithCommentsAsync(int id, CancellationToken ct)
    {
        var post = await GetPostAsync(id, ct);
        if (post is null) return null;

        var comments = await GetCommentsForPostAsync(id, ct);
        return new PostWithComments {
            Post = post,
            Comments = comments
        };
    }
}
