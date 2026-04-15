using PGEmuBackend.DTOs;
using PGEmuBackend.DTOs.ProfileCustomization;

namespace PGEmuBackend.Services;

public interface IProfileCustomizationService
{
    Task<(bool Success, string Message, string? NewUsername)>
        ChangeUsernameAsync(Guid userId, string newUsername);

    Task<(bool Success, string Message, string? NewAvatarUrl)>
        ChangeAvatarAsync(Guid userId, string? newAvatarUrl);

    Task<(bool Success, string Message, string? NewBio)>
        ChangeBioAsync(Guid userId, string? newBio);

    Task<(bool Success, string Message, ProfileCustomizationDTO Profile)>
        GetUserAsync(Guid? UserId, string? username);

    Task<IReadOnlyList<UserSearchResultDTO>>
        SearchUsersBySimilarityAsync(Guid currentUserId, string query, int limit);
}
