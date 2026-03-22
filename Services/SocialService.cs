using Supabase;
using StravaIntegration.Models.Entities;

namespace StravaIntegration.Services;

public interface ISocialService
{
    Task<Post> CreatePostAsync(Post newPost);
    Task ToggleLikeAsync(Guid userId, Guid postId);
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
        // Garante que a data de criação é a atual
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
            // Tenta inserir o Like
            await _supabase.From<PostLike>().Insert(likeRecord);
        }
        catch (Postgrest.Exceptions.PostgrestException ex)
        {
            // Se violar a regra UNIQUE, significa que o utilizador já deu like.
            // O comportamento comum numa rede social é remover o like (Toggle).
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
}