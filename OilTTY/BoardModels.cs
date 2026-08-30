internal sealed record ApiEnvelope<T>(
    bool Success,
    T? Data,
    int StatusCode,
    string? Message,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

internal sealed record AuthUser(
    int Id,
    string UserName,
    string DisplayName,
    string Role);

internal sealed record MachineAuthSession(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    AuthUser User,
    string TokenType);

internal sealed record CsrfTokenDto(string CsrfToken);

internal sealed record BoardData(
    BoardSnapshot Board,
    IReadOnlyDictionary<int, CardTypeDefinition> CardTypes,
    IReadOnlyDictionary<int, SlickDefinition> Slicks,
    IReadOnlyList<CardTag> Tags,
    IReadOnlyList<BoardMember> Members);

internal sealed record BoardSummary(
    int Id,
    string Name,
    string Description);

internal sealed record BoardMember(
    int UserId,
    string UserName,
    string DisplayName,
    string? ProfileImageRelativePath,
    string Role,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

internal sealed record BoardSnapshot(
    int Id,
    string Name,
    string Description,
    bool SlickCohesionModeEnabled,
    string? CurrentUserRole,
    IReadOnlyList<BoardColumn> Columns);

internal sealed record BoardColumn(
    int Id,
    string Title,
    string SortKey,
    IReadOnlyList<BoardCard> Cards);

internal sealed record BoardCard(
    int Id,
    int BoardColumnId,
    int CardTypeId,
    string CardTypeName,
    string? CardTypeEmoji,
    string Title,
    string Description,
    string SortKey,
    IReadOnlyList<CardTag> Tags,
    IReadOnlyList<string> TagNames,
    DateTime CardCreatedUtc,
    DateTime CardUpdatedUtc,
    int? AssignedUserId,
    string? AssignedUserDisplayName,
    string? AssignedUserImageRelativePath,
    int? SlickId,
    string? SlickName,
    string? ExternalUrl);

internal sealed record CardMove(
    int CardId,
    int BoardColumnId,
    int? PositionAfterCardId);

internal sealed record CardTag(
    int Id,
    string Name,
    string StyleName,
    string StylePropertiesJson,
    string? Emoji);

internal sealed record CardTypeDefinition(
    int Id,
    string Name,
    string? Emoji,
    string StyleName,
    string StylePropertiesJson,
    bool IsSystem = false);

internal sealed record SlickDefinition(
    int Id,
    string Name,
    string StyleName,
    string StylePropertiesJson);
