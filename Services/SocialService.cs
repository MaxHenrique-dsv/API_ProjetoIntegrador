using Supabase;
using StravaIntegration.Models.Entities;

namespace StravaIntegration.Services;

public interface ISocialService
{
    Task<Post> CreatePostAsync(Post newPost);
    Task ToggleLikeAsync(Guid userId, Guid postId);
    Task<PostComment> AddCommentAsync(Guid postId, Guid userId, string content);
    Task<List<PostComment>> GetCommentsAsync(Guid postId);
}

public class SocialService : ISocialService
{
    private readonly Client _supabase;

    public SocialService(Client supabase)
    {
        _supabase = supabase;
    }

    public async Task<Post> CreatePostAsync(Post newPost)
    {
        newPost.CreatedAt = DateTimeOffset.UtcNow;

        var response = await _supabase.From<Post>().Insert(newPost);
        
        return response.Models.First();
    }

    public async Task ToggleLikeAsync(Guid userId, Guid postId)
    {
        var likeRecord = new PostLike
        {
            PostId = postId,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await _supabase.From<PostLike>().Insert(likeRecord);
        }
        catch (Postgrest.Exceptions.PostgrestException ex)
        {
            if (ex.Message.Contains("unique_like_per_user_post"))
            {
                await _supabase.From<PostLike>()
                    .Where(x => x.PostId == postId && x.UserId == userId)
                    .Delete();
                return;
            }
            throw;
        }
    }

public async Task<PostComment> AddCommentAsync(Guid postId, Guid userId, string content)
{
    var comment = new PostComment
    {
        PostId = postId,
        UserId = userId,
        Content = content,
        CreatedAt = DateTimeOffset.UtcNow
    };

    var response = await _supabase.From<PostComment>().Insert(comment);
    return response.Models.First();
}

public async Task<List<PostComment>> GetCommentsAsync(Guid postId)
{
    var response = await _supabase.From<PostComment>()
        .Where(x => x.PostId == postId)
        .Get();

    // Retorna a lista ordenada do comentário mais antigo para o mais recente
    return response.Models.OrderBy(x => x.CreatedAt).ToList();
}
}