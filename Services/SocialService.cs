using Supabase;
using StravaIntegration.Models.Entities;

namespace StravaIntegration.Services;

public interface ISocialService
{
    Task<Post> CreatePostAsync(Post newPost);
    Task ToggleLikeAsync(Guid userId, Guid postId);
    Task<PostComment> AddCommentAsync(Guid postId, Guid userId, string content);
    Task<List<PostComment>> GetCommentsAsync(Guid postId);
    Task<(bool Success, int StatusCode, string Message)> DeletePostAsync(Guid postId, Guid userId);
    Task<List<Post>> GetFeedAsync(Guid userId);
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

public async Task<(bool Success, int StatusCode, string Message)> DeletePostAsync(Guid postId, Guid userId)
{
    var response = await _supabase.From<Post>().Where(x => x.Id == postId).Get();
    var post = response.Models.FirstOrDefault();

    if (post == null)
    {
        return (false, 404, "Post not found.");
    }

    if (post.UserId != userId)
    {
        return (false, 403, "You are not authorized to delete this post.");
    }

    await _supabase.From<Post>().Where(x => x.Id == postId).Delete();
    return (true, 200, "Post deleted successfully.");
}

public async Task<List<Post>> GetFeedAsync(Guid userId)
{
    // 1. Fetch clubs the user is a member of
    var userClubsResp = await _supabase.From<ClubMember>()
        .Select("club_id")
        .Where(x => x.UserId == userId)
        .Get();

    var myClubIds = userClubsResp.Models.Select(c => c.ClubId).ToList();

    List<Guid> myNetworkUserIds = new List<Guid> { userId };
    List<Post> feed = new List<Post>();

    if (myClubIds.Any())
    {
        // 2. Fetch all users in these clubs
        var networkResp = await _supabase.From<ClubMember>()
            .Select("user_id")
            .Filter("club_id", Postgrest.Constants.Operator.In, myClubIds)
            .Get();

        var networkUserIds = networkResp.Models.Select(c => c.UserId).Distinct().ToList();
        myNetworkUserIds.AddRange(networkUserIds);
        myNetworkUserIds = myNetworkUserIds.Distinct().ToList();
    }

    // 3. Fetch Posts where owner is in myNetworkUserIds
    if (myNetworkUserIds.Any())
    {
        var networkPostsResp = await _supabase.From<Post>()
            .Filter("user_id", Postgrest.Constants.Operator.In, myNetworkUserIds)
            .Get();
            
        feed.AddRange(networkPostsResp.Models);
    }

    // 4. Fetch Posts linked specifically to any of myClubIds
    if (myClubIds.Any())
    {
        var clubPostsResp = await _supabase.From<Post>()
            .Filter("club_id", Postgrest.Constants.Operator.In, myClubIds)
            .Get();
            
        feed.AddRange(clubPostsResp.Models);
    }

    // Combine, distinct, and order by date
    return feed.GroupBy(p => p.Id)
               .Select(g => g.First())
               .OrderByDescending(p => p.CreatedAt)
               .ToList();
}
}