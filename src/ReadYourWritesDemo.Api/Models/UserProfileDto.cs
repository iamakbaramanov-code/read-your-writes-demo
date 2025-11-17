namespace ReadYourWritesDemo.Api.Models;

public record UserProfileDto(
    string Email,
    string Name,
    string? AvatarUrl
);
