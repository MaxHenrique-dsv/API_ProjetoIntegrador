using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StravaIntegration.Models.Entities;
using StravaIntegration.Services;

namespace StravaIntegration.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PostController : ControllerBase
{
    private readonly ISocialService _socialService;

    public PostController(ISocialService socialService)
    {
        _socialService = socialService;
    }

    // DTO para receber os dados limpos do Frontend
    public class CreatePostRequest
    {
        public string ImageUrl { get; set; } = string.Empty;
        public string? Caption { get; set; }
        public Guid? ChallengeId { get; set; }
        public long? ActivityId { get; set; } // ID da corrida do Strava
    }

    [HttpPost]
    public async Task<IActionResult> CreatePost([FromBody] CreatePostRequest request)
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var newPost = new Post
        {
            UserId = userId,
            ImageUrl = request.ImageUrl,
            Caption = request.Caption,
            ChallengeId = request.ChallengeId,
            ActivityId = request.ActivityId
        };

        var createdPost = await _socialService.CreatePostAsync(newPost);
        return Ok(createdPost);
    }

    [HttpPost("{postId}/like")]
    public async Task<IActionResult> ToggleLike(Guid postId)
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        await _socialService.ToggleLikeAsync(userId, postId);
        
        return Ok(new { message = "Like atualizado com sucesso." });
    }
}