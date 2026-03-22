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
        // ❌ Removemos a necessidade de extrair do Token
        // var userIdClaim = User.FindFirst("sub")?.Value;
        // if (!Guid.TryParse(userIdClaim, out var userId))
        //     return Unauthorized();

        // ✅ Agora usa o userId que veio direto da URL
        await _socialService.ToggleLikeAsync(userId, postId);
        
        return Ok(new { message = "Like atualizado com sucesso." });
    }
}