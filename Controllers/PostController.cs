using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StravaIntegration.Models.Entities;
using StravaIntegration.Services;

namespace StravaIntegration.Controllers;

[ApiController]
[Route("api/[controller]")]
// [Authorize] <-- Removido temporariamente para testes
public class PostController : ControllerBase
{
    private readonly ISocialService _socialService;

    public PostController(ISocialService socialService)
    {
        _socialService = socialService;
    }

    // DTO modificado para receber o UserId do frontend
    public class CreatePostRequest
    {
        public Guid UserId { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public string? Caption { get; set; }
        public Guid? ChallengeId { get; set; }
        public long? ActivityId { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> CreatePost([FromBody] CreatePostRequest request)
    {
        // ❌ Removemos a necessidade de extrair do Token
        // var userIdClaim = User.FindFirst("sub")?.Value;
        // if (!Guid.TryParse(userIdClaim, out var userId))
        //     return Unauthorized();

        var newPost = new Post
        {
            UserId = request.UserId, // ✅ Agora pega direto do que o frontend mandou no Body
            ImageUrl = request.ImageUrl,
            Caption = request.Caption,
            ChallengeId = request.ChallengeId,
            ActivityId = request.ActivityId
        };

    var createdPost = await _socialService.CreatePostAsync(newPost);
    return Ok(new 
    {
        id = createdPost.Id,
        userId = createdPost.UserId,
        imageUrl = createdPost.ImageUrl,
        caption = createdPost.Caption,
        challengeId = createdPost.ChallengeId,
        activityId = createdPost.ActivityId,
        createdAt = createdPost.CreatedAt
    });
    }

    [HttpPost("{userId}/{postId}/like")]
    public async Task<IActionResult> ToggleLike(Guid userId, Guid postId)
    {

        await _socialService.ToggleLikeAsync(userId, postId);
        
        return Ok(new { message = "Like atualizado com sucesso." });
    }
    // DTO para receber o comentário
    public class CreateCommentRequest
    {
        public Guid UserId { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    [HttpPost("{postId}/comment")]
    public async Task<IActionResult> AddComment(Guid postId, [FromBody] CreateCommentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "O comentário não pode estar vazio." });

        var comment = await _socialService.AddCommentAsync(postId, request.UserId, request.Content);

        return Ok(new
        {
            id = comment.Id,
            postId = comment.PostId,
            userId = comment.UserId,
            content = comment.Content,
            createdAt = comment.CreatedAt
        });
    }


    [HttpGet("{postId}/comments")]
    public async Task<IActionResult> GetComments(Guid postId)
    {
        var comments = await _socialService.GetCommentsAsync(postId);
        
        var result = comments.Select(c => new
        {
            id = c.Id,
            userId = c.UserId,
            content = c.Content,
            createdAt = c.CreatedAt
        });

        return Ok(result);
    }

    public class DeletePostRequest
    {
        public Guid UserId { get; set; }
    }

    [HttpDelete("{postId}")]
    public async Task<IActionResult> DeletePost(Guid postId, [FromBody] DeletePostRequest request)
    {
        var (success, statusCode, message) = await _socialService.DeletePostAsync(postId, request.UserId);

        if (!success)
        {
            if (statusCode == 404)
                return NotFound(new { error = message });
            
            if (statusCode == 403)
                return StatusCode(403, new { error = message });

            return StatusCode(statusCode, new { error = message });
        }

        return Ok(new { message = message });
    }

    [HttpGet("feed/{userId}")]
    public async Task<IActionResult> GetFeed(Guid userId)
    {
        var feed = await _socialService.GetFeedAsync(userId);
        
        var result = feed.Select(p => new
        {
            id = p.Id,
            userId = p.UserId,
            imageUrl = p.ImageUrl,
            caption = p.Caption,
            challengeId = p.ChallengeId,
            activityId = p.ActivityId,
            clubId = p.ClubId,
            createdAt = p.CreatedAt
        });

        return Ok(result);
    }
}